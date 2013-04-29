using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using MvcWebRole.Models;
using MvcWebRole.SharedSrc;

namespace WorkerRoleA
{
    public class WorkerRoleA : RoleEntryPoint
    {
        private CloudQueue snapshotsQueue;
        private CloudTable snapshotsTable;

        private volatile bool onStopCalled = false;
        private volatile bool returnedFromRunMethod = false;

        public override void Run()
        {
            Trace.TraceInformation("WorkerRoleA entering Run()");
            
            while (true)
            {
                try
                {                    
                    // If OnStop has been called, return to do a graceful shutdown.
                    if (onStopCalled == true)
                    {
                        Trace.TraceInformation("WorkerRoleA onStopCalled");
                        returnedFromRunMethod = true;
                        return;
                    }

                    DateTime tb = Utils.PrevOperationTB;

                    // Retrieve all snapshots that were logged for the previous time block or earlier
                    // and are in Pending or Queuing status.
                    var query = new TableQuery<Snapshot>().Where(
                        TableQuery.GenerateFilterCondition(
                            "PartitionKey",
                            QueryComparisons.LessThanOrEqual,
                            tb.Ticks.ToString()
                        ));
                    List<Snapshot> snapshotsToProcess = snapshotsTable.ExecuteQuery(query).ToList();
                    
                    // Process each snapshot (queue snapshots to be logged).
                    foreach (Snapshot snapshotToProcess in snapshotsToProcess)
                    {
                        //
                        // If the snapshot is already in Queuing status,
                        // set flag to indicate this is a restart.
                        //
                        string restartFlag = "0";
                        if (snapshotToProcess.Status == "Queuing")
                        {
                            restartFlag = "1";
                        }

                        //
                        // If the snapshot is in Pending status, change it to Queuing.
                        //
                        if (snapshotToProcess.Status == "Pending")
                        {
                            snapshotToProcess.Status = "Queuing";
                            TableOperation replaceOperation = TableOperation.Replace(snapshotToProcess);
                            snapshotsTable.Execute(replaceOperation);
                        }

                        //
                        // If the message is in Queuing status, process it and change it to Processing status
                        //
                        if (snapshotToProcess.Status == "Queuing")
                        {
                            ProcessSnapshot(snapshotToProcess, restartFlag);

                            snapshotToProcess.Status = "Processing";
                            TableOperation replaceOperation = TableOperation.Replace(snapshotToProcess);
                            snapshotsTable.Execute(replaceOperation);
                        }
                        else if (snapshotToProcess.Status == "Processing")
                        {
                            // TODO: Figure out if we need to do anything here!
                            // Definitely in queue, should be taken care of
                            // CheckAndArchiveIfComplete(snapshotToProcess);
                        }
                        else // Status must be "Complete"
                        {
                            // TODO: Handle this
                            // Either haven't deleted yet, or crashed right before it was able to delete
                            // Archive(snapshotToProcess)
                        }
                    }

                    // Sleep until next time block
                    long sleepTicks = Utils.NextOperationTB.Ticks - DateTime.Now.Ticks;
                    if (sleepTicks > 0)
                    {
                        System.Threading.Thread.Sleep(TimeSpan.FromTicks(sleepTicks));
                    }
                }
                catch (Exception ex)
                {
                    string err = ex.Message;
                    if (ex.InnerException != null)
                    {
                        err += " Inner Exception: " + ex.InnerException.Message;
                    }
                    Trace.TraceError(err);
                }
            }
        }

        private void ProcessSnapshot(Snapshot snapshotToProcess, string restartFlag)
        {
            Trace.TraceInformation("ProcessMessage Beginning PK: "
                + snapshotToProcess.PartitionKey);

            // When we add the this snapshot to the queue, we may be adding it twice if
            // the worker goes down down after adding previously before marking it as
            // processed and then restarted.  In this case the restartFlag would be marked.

            // Create the queue message.
            string queueMessageString =
                snapshotToProcess.PartitionKey + "," +
                snapshotToProcess.RowKey + "," +
                restartFlag;
            var queueMessage = new CloudQueueMessage(queueMessageString);
            snapshotsQueue.AddMessage(queueMessage);

            Trace.TraceInformation("ProcessMessage end PK: "
                + snapshotToProcess.PartitionKey);
        }

        private void ConfigureDiagnostics()
        {
            DiagnosticMonitorConfiguration config = DiagnosticMonitor.GetDefaultInitialConfiguration();
            config.ConfigurationChangePollInterval = TimeSpan.FromMinutes(1d);
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
            Trace.TraceInformation("Initializing storage account in WorkerA");
            var storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue(
                "StorageConnectionString"
                ));

            var queueClient = storageAccount.CreateCloudQueueClient();
            snapshotsQueue = queueClient.GetQueueReference("snapshotsqueue");
            
            var tableClient = storageAccount.CreateCloudTableClient();
            snapshotsTable = tableClient.GetTableReference("Snapshots");

            // Create if not exists for queue, blob container, SentEmail table. 
            snapshotsQueue.CreateIfNotExists();
            snapshotsTable.CreateIfNotExists();

            return base.OnStart();
        }
    }
}
