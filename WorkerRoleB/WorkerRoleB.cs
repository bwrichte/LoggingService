using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using CoinbaseSharp.API;
using CoinbaseSharp.Authentication;
using CoinbaseSharp.DataTypes;
using CoinbaseSharp.Resources;
using MvcWebRole.Models;
using MvcWebRole.SharedSrc;
using TamperEvidentLogs;
using TamperEvidentLogs.Aggregators;

namespace WorkerRoleB
{
    public class WorkerRoleB : RoleEntryPoint
    {
        private CloudQueue snapshotsQueue;
        private CloudBlobContainer proofsContainer;
        private CloudTable snapshotsTable;
        private CloudTable proofsArchiveTable;

        private volatile bool onStopCalled = false;
        private volatile bool returnedFromRunMethod = false;

        public override void Run()
        {
            Trace.TraceInformation("WorkerRoleB start of Run()");

            List<CloudQueueMessage> msgs = new List<CloudQueueMessage>();

            while (true)
            {
                DateTime nextTB = Utils.NextOperationTB;

                try
                {
                    // If OnStop has been called, return to do a graceful shutdown.
                    if (onStopCalled)
                    {
                        Trace.TraceInformation("onStopCalled WorkerRoleB");
                        returnedFromRunMethod = true;
                        return;
                    }

                    msgs.Clear();
                    int curSize;
                    do
                    {
                        curSize = msgs.Count;
                        // Retrieve and process a new message from the send-email-to-list queue.
                        msgs.AddRange(snapshotsQueue.GetMessages(32));
                    } while ((msgs.Count - curSize) >= 32 && msgs.Count < 1000);

                    // If this becomes a problem, have it call GetRoleInstance()
                    Trace.TraceInformation("WorkerB Grabbed {0} messages off of the queue.", msgs.Count);
                    
                    if (msgs.Count > 0)
                    {
                        ProcessQueueMessages(msgs);
                    }

                    // If retrieved less than maximum, wait for 10 full minutes
                    if (msgs.Count < 1000)
                    {
                        Trace.TraceInformation("Processed less than maximum, going to sleep until next time block.");

                        // Sleep until next time block (or if we have already gone over, continue onward)
                        long ticks = nextTB.Ticks - DateTime.Now.Ticks;
                        if (ticks < 0)
                        {
                            continue;
                        }
                        TimeSpan sleep = TimeSpan.FromTicks(ticks).Add(TimeSpan.FromSeconds(5));
                        System.Threading.Thread.Sleep(sleep);
                    }
                    else // TODO: Tune this, but perhaps we should still sleep a bit
                    {
                        Trace.TraceInformation("Processed maximum, skipping sleep to check again.");
                    }
                }
                catch (Exception ex)
                {
                    string err = ex.Message;
                    if (ex.InnerException != null)
                    {
                        err += " Inner Exception: " + ex.InnerException.Message;
                    }
                    if (msgs.Count > 0)
                    {
                        err += " Last queue message retrieved: " + msgs.Last().AsString;
                    }
                    Trace.TraceError(err);

                    // Don't fill up Trace storage if we have a bug in either process loop.
                    System.Threading.Thread.Sleep(1000 * 60);
                }
            }
        }

        private int GetRoleInstance()
        {
            string instanceId = RoleEnvironment.CurrentRoleInstance.Id;
            int instanceIndex = -3;
            int.TryParse(instanceId.Substring(instanceId.LastIndexOf("_") + 1), out instanceIndex);

            // The instanceIndex of the first instance is 0. 
            return instanceIndex;
        }

        private void SaveBlob(string blobName, string proof)
        {
            // Retrieve reference to a blob.
            var blob = proofsContainer.GetBlockBlobReference(blobName);
            // Create the blob or overwrite the existing blob by uploading a local file.
            using (StreamWriter writer = new StreamWriter(blob.OpenWrite()))
            {
                writer.Write(proof);
            }
        }

        private void ProcessQueueMessages(List<CloudQueueMessage> msgs)
        {
            Trace.TraceInformation("WorkerB (RoleInstance {0}: ProcessQueueMessages start", GetRoleInstance());

            HashTree hashTree = new HashTree(new SHA256Aggregator());
            List<Tuple<CloudQueueMessage, Snapshot>> goodMsgs = new List<Tuple<CloudQueueMessage, Snapshot>>();
            foreach (CloudQueueMessage msg in msgs)
            {
                // Log and delete if this is a "poison" queue message (repeatedly processed
                // and always causes an error that prevents processing from completing).
                // Production applications should move the "poison" message to a "dead message"
                // queue for analysis rather than deleting the message.           
                if (msg.DequeueCount > 5)
                {
                    Trace.TraceError(
                        "Deleting poison message: message {0} Role Instance {1}.",
                        msg.ToString(), 
                        GetRoleInstance()
                        );
                    snapshotsQueue.DeleteMessage(msg);
                    continue;
                }

                // Parse summarized snapshot retrieved from queue.
                string[] snapshotParts = msg.AsString.Split(new char[] { ',' });
                string partitionKey = snapshotParts[0];
                string rowKey = snapshotParts[1];
                string restartFlag = snapshotParts[2];

                var retrieveOperation = TableOperation.Retrieve<Snapshot>(partitionKey, rowKey);
                var retrievedResult = snapshotsTable.Execute(retrieveOperation);
                var snapshot = retrievedResult.Result as Snapshot;

                if (snapshot == null)
                {
                    Trace.TraceError("WorkerB: Snapshot does not exist for RK {0}", rowKey);
                    continue;
                }

                // If this is a restart, verify that the shapshot has not already been logged
                if (restartFlag == "1")
                {
                    /*if (snapshot.Status == "Complete")
                    {
                    }*/
                }

                // Add snapshot value into history tree
                hashTree.Append(snapshot.SnapshotValue);
                goodMsgs.Add(new Tuple<CloudQueueMessage, Snapshot>(msg, snapshot));
            }

            // Snapshot commitment into bitcoin
            Transaction transaction = SnapshotCommitmentIntoBitcoin(
                hashTree.RootCommitmentString
                );

            if (transaction == null)
            {
                Trace.TraceError("Snapshot into Bitcoin failed, unable to continue.");
                return;
            }

            for (int i = 0; i < goodMsgs.Count; ++i)
            {
                Snapshot snapshot = goodMsgs[i].Item2;
                string proofText = hashTree.GenerateMembershipProof(i).ToString();

                // Save proof
                string proofName = snapshot.RowKey + ".proof";
                SaveBlob(proofName, proofText);

                var proof = new Proof
                {
                    ApplicationName = snapshot.ApplicationName, // Sets partition key
                    RowKey = snapshot.RowKey,
                    LogName = snapshot.LogName,
                    SnapshotValue = snapshot.SnapshotValue,
                    ProofBlobName = proofName,
                    CoinbaseTransactionID = transaction.ID
                };

                // Save in archive
                var upsertOperation = TableOperation.InsertOrReplace(proof);
                proofsArchiveTable.Execute(upsertOperation);

                // TODO: Decide if need to mark as complete

                var deleteOperation = TableOperation.Delete(snapshot);
                snapshotsTable.Execute(deleteOperation);

                // Delete the associated queue message
                snapshotsQueue.DeleteMessage(goodMsgs[i].Item1);
            }

            Trace.TraceInformation("WorkerB (RoleInstance {0}: ProcessQueueMessages complete",
               GetRoleInstance()
               );
        }

        private Transaction SnapshotCommitmentIntoBitcoin(string commitment)
        {
            APIKey key1 = new APIKey(
                RoleEnvironment.GetConfigurationSettingValue("CoinbaseAccountKey1")
                );
            APIKey key2 = new APIKey(
                RoleEnvironment.GetConfigurationSettingValue("CoinbaseAccountKey2")
                );

            Amount balance1 = API.GetBalance(key1);

            if (!balance1.IsValid)
            {
                Trace.TraceError(
                    "Unable to get balance for {0}: {1}",
                    key1,
                    balance1
                    );
                return null;
            }

            Amount balance2 = API.GetBalance(key2);
            if (!balance2.IsValid)
            {
                Trace.TraceError(
                    "Unable to get balance for {0}: {1}",
                    key2,
                    balance2
                    );
                return null;
            }

            if (balance1.AmountValue < balance2.AmountValue)
            {
                APIKey tempKey = key1;
                Amount tempBalance = balance1;
                key1 = key2;
                balance1 = balance2;
                key2 = tempKey;
                balance2 = tempBalance;
            }

            if (balance1.AmountValue < API.MINIMUM_PAYMENT_AMOUNT)
            {
                Trace.TraceError(
                    "Insufficient Funds ({0}) to send miniumum payment of {1} BTC",
                    balance1,
                    API.MINIMUM_PAYMENT_AMOUNT
                    );
                return null;
            }

            User user2 = API.GetUser(key2);
            if (!user2.IsValid)
            {
                Trace.TraceError(
                    "Unable to get User Information for {0}: {1}",
                    key2,
                    user2
                    );
                return null;
            }

            SendMoneyTransaction sendMoney = new SendMoneyTransaction()
            {
                Amount = new Amount() { AmountValue = API.MINIMUM_PAYMENT_AMOUNT, Currency = "BTC" },
                ToAddr = user2.Email,
                Notes = commitment
            };

            Transaction transaction = API.SendMoney(sendMoney, key1);

            if (!transaction.IsValid)
            {
                Trace.TraceError(
                    "Unable to complete SendMoney Transaction: {0}",
                    transaction
                    );
                return null;
            }

            return transaction;
        }

        private void ConfigureDiagnostics()
        {
            DiagnosticMonitorConfiguration config = DiagnosticMonitor.GetDefaultInitialConfiguration();
            config.Logs.BufferQuotaInMB = 500;
            config.Logs.ScheduledTransferLogLevelFilter = LogLevel.Verbose;
            config.Logs.ScheduledTransferPeriod = TimeSpan.FromMinutes(1d);

            DiagnosticMonitor.Start(
                "Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString",
                config
                );
        }

        public override void OnStop()
        {
            onStopCalled = true;
            while (!returnedFromRunMethod)
            {
                System.Threading.Thread.Sleep(1000);
            }
        }

        public override bool OnStart()
        {
            ServicePointManager.DefaultConnectionLimit = Environment.ProcessorCount;

            ConfigureDiagnostics();

            // Read storage account configuration settings
            Trace.TraceInformation("Initializing storage account in WorkerB");
            var storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue(
                "StorageConnectionString"
                ));

            // Initialize queue storage 
            Trace.TraceInformation("Creating queue client in WorkerB");
            var queueClient = storageAccount.CreateCloudQueueClient();
            snapshotsQueue = queueClient.GetQueueReference("snapshotsqueue");

            // Initialize blob storage
            Trace.TraceInformation("Creating blob client in WorkerB");
            var blobClient = storageAccount.CreateCloudBlobClient();
            proofsContainer = blobClient.GetContainerReference("proofscontainer");

            // Initialize table storage
            Trace.TraceInformation("Creating table client in WorkerB");
            var tableClient = storageAccount.CreateCloudTableClient();
            snapshotsTable = tableClient.GetTableReference("Snapshots");
            proofsArchiveTable = tableClient.GetTableReference("ProofsArchive");

            Trace.TraceInformation("WorkerB: Creating blob container, queue, tables, if they don't exist.");
            proofsContainer.CreateIfNotExists();
            proofsContainer.SetPermissions(new BlobContainerPermissions
            {
                PublicAccess = BlobContainerPublicAccessType.Blob
            });

            snapshotsQueue.CreateIfNotExists();
            snapshotsTable.CreateIfNotExists();
            proofsArchiveTable.CreateIfNotExists();

            return base.OnStart();
        }
    }
}
