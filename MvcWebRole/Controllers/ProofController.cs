using BlockchainSharp.API;
using BlockchainSharp.Resources;
using CoinbaseSharp.API;
using CoinbaseSharp.Authentication;
using CoinbaseSharp.Resources;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using Microsoft.WindowsAzure.Storage.Table;
using MvcWebRole.Controllers.Apis;
using MvcWebRole.Models;
using MvcWebRole.Models.DTOs;
using MvcWebRole.SharedSrc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace MvcWebRole.Controllers
{
    public class ProofController : Controller
    {
        private CloudTable subscribersTable;
        private CloudTable proofsArchiveTable;
        private CloudBlobContainer blobContainer;

        public ProofController()
        {
            var storageAccount = Microsoft.WindowsAzure.Storage.CloudStorageAccount.Parse(
                RoleEnvironment.GetConfigurationSettingValue(
                    "StorageConnectionString"
                ));

            //
            // If this is running in a Windows Azure Web Site (not a Cloud Service) use the Web.config file:
            //    var storageAccount = Microsoft.WindowsAzure.Storage.CloudStorageAccount.Parse(
            //        ConfigurationManager.ConnectionStrings["StorageConnectionString"].ConnectionString
            //        );
            //

            // Get context object for working with tables and a reference to the blob container.
            var tableClient = storageAccount.CreateCloudTableClient();
            var blobClient = storageAccount.CreateCloudBlobClient();
            subscribersTable = tableClient.GetTableReference("Subscribers");
            proofsArchiveTable = tableClient.GetTableReference("ProofsArchive");
            blobContainer = blobClient.GetContainerReference("proofscontainer");
        }

        private Subscription FindSubscription(string appName, string logName)
        {
            var retrieveOperation = TableOperation.Retrieve<Subscription>(appName, logName);
            var retrievedResult = subscribersTable.Execute(retrieveOperation);
            var subscriber = retrievedResult.Result as Subscription;
            return subscriber;
        }

        //
        // GET: /Proof/
        //
        public ActionResult Index()
        {
            return View();
        }

        //
        // GET: /Proof/appName=X&?logName=Y
        //
        public ActionResult GetProofs(string appName, string logName)
        {
            ViewData["BlobURL"] = blobContainer.Uri.AbsoluteUri;
            ViewBag.ApplicationName = appName;
            ViewBag.LogName = logName;
            ViewBag.LatestBlockNumber = BlockchainAPI.GetLatestBlock().BlockHeight;
            if (appName == null || logName == null)
            {
                return View();
            }

            Subscription subscriber = FindSubscription(appName, logName);

            if (subscriber == null)
            {
                ViewBag.errorMessage = "Subscriber does not exist.";
                ModelState.AddModelError(string.Empty, "Subscriber does not exist.");
                return View("Proofs", model: new List<Proof>());
            }
            else if (subscriber.Verified != true)
            {
                ViewBag.errorMessage = "Subscriber is not currently verified, no proofs exist.";
                ModelState.AddModelError(string.Empty, "Subscriber is not currently verified, no proofs exist.");
                return View("Proofs", model: new List<Proof>());
            }

            TableRequestOptions reqOptions = new TableRequestOptions()
            {
                MaximumExecutionTime = TimeSpan.FromSeconds(10),
                RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(3), 3)
            };

            List<Proof> proofs;
            try
            {
                Snapshot snapshot = new Snapshot()
                {
                    ApplicationName = appName,
                    LogName = logName,
                    TimeBlock = Utils.NextOperationTB
                };

                var query = new TableQuery<Proof>().Where(
                    TableQuery.CombineFilters(
                        TableQuery.CombineFilters(
                            TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, appName),
                            TableOperators.And,
                            TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.LessThanOrEqual, snapshot.RowKey)
                            ),
                        TableOperators.And,
                        TableQuery.GenerateFilterCondition("LogName", QueryComparisons.Equal, logName)
                        )
                    );

                proofs = proofsArchiveTable.ExecuteQuery(query, reqOptions).ToList();
                return View("Proofs", model:proofs);
            }
            catch (StorageException se)
            {
                ViewBag.errorMessage = "Timeout error, try again.";
                Trace.TraceError(se.Message);
                return View("Error: " + se.Message);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Audit(Proof proof)
        {
            ViewData["BlobURL"] = blobContainer.Uri.AbsoluteUri;
            ViewBag.ApplicationName = proof.ApplicationName;
            ViewBag.LogName = proof.LogName;
            ViewBag.LatestBlockNumber = BlockchainAPI.GetLatestBlock().BlockHeight;
            if (ModelState.IsValid)
            {
                DateTime dt;
                DateTime.TryParse(proof.TimeBlock, out dt);
                // Validate Query
                if (string.IsNullOrWhiteSpace(proof.ApplicationName))
                {
                    return View("Proofs", model:new List<Proof>());
                }
                else if (string.IsNullOrWhiteSpace(proof.LogName))
                {
                    return View("Proofs", model:new List<Proof>());
                }
                else if (dt.Ticks <= 0)
                {
                    dt = DateTime.Now;
                }

                Subscription subscriber = FindSubscription(proof.ApplicationName, proof.LogName);

                if (subscriber == null)
                {
                    return View("Proofs", model: new List<Proof>());
                }
                else if (subscriber.Verified != true)
                {
                    return View("Proofs", model: new List<Proof>());
                }

                TableRequestOptions reqOptions = new TableRequestOptions()
                {
                    MaximumExecutionTime = TimeSpan.FromSeconds(10),
                    RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(3), 3)
                };

                List<Proof> proofs;
                try
                {
                    Snapshot snapshot = new Snapshot()
                    {
                        ApplicationName = proof.ApplicationName,
                        LogName = proof.LogName,
                        TimeBlock = dt
                    };

                    var tableQuery = new TableQuery<Proof>().Where(
                        TableQuery.CombineFilters(
                            TableQuery.CombineFilters(
                                TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, proof.ApplicationName),
                                TableOperators.And,
                                TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.LessThanOrEqual, snapshot.RowKey)
                                ),
                            TableOperators.And,
                            TableQuery.GenerateFilterCondition("LogName", QueryComparisons.Equal, proof.LogName)
                            )
                        );

                    proofs = proofsArchiveTable.ExecuteQuery(tableQuery, reqOptions).ToList();
                    if (proofs.Count == 0)
                    {
                        return View("Proofs", model: new List<Proof>());
                    }

                    Proof proofToAudit = null;
                    long maxTicks = 0;
                    foreach (Proof singleProof in proofs)
                    {
                        long ticks = DateTime.Parse(singleProof.TimeBlock).Ticks;
                        if (ticks > maxTicks)
                        {
                            maxTicks = ticks;
                            proofToAudit = singleProof;
                        }
                    }

                    //
                    // TODO: Make API Call, Update Database!
                    //
                    if (proofToAudit.BitcoinBlockNumber == null
                        || proofToAudit.BitcoinTransactionHash == null)
                    {
                        if (PopulateBitcoinInformation(proofToAudit))
                        {
                            var replaceOperation = TableOperation.Replace(proofToAudit);
                            proofsArchiveTable.Execute(replaceOperation);
                        }
                    }

                    return View("Proofs", model: new List<Proof>() { proofToAudit });
                }
                catch (StorageException se)
                {
                    ViewBag.errorMessage = "Timeout error, try again.";
                    Trace.TraceError(se.Message);
                    return View("Error: " + se.Message);
                }
            }
            
            return View("Proofs", model: new List<Proof>());
        }

        [NonAction]
        private bool PopulateBitcoinInformation(Proof proof)
        {
            APIKey apiKey = new APIKey(RoleEnvironment.GetConfigurationSettingValue(
                "CoinbaseAccountKey1"
                ));

            Transaction coinbaseTransaction = API.GetTransaction(proof.CoinbaseTransactionID, apiKey);

            if (coinbaseTransaction.Hash != null)
            {
                proof.BitcoinTransactionHash = coinbaseTransaction.Hash;
                BitcoinTransaction bitcoinTransaction =
                    BlockchainAPI.GetTransaction(coinbaseTransaction.Hash);
                if (bitcoinTransaction.BlockHeight > 0)
                {
                    proof.BitcoinBlockNumber = bitcoinTransaction.BlockHeight;
                    return true;
                }
            }

            return false;
        }
    }
}
