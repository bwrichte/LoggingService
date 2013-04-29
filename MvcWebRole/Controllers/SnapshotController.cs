using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using MvcWebRole.Models;
using MvcWebRole.SharedSrc;
using Microsoft.WindowsAzure.Storage.RetryPolicies;

namespace MvcWebRole.Controllers
{
    public class SnapshotController : Controller
    {
        private CloudTable subscribersTable;
        private CloudTable snapshotsTable;

        public SnapshotController()
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
            subscribersTable = tableClient.GetTableReference("Subscribers");
            snapshotsTable = tableClient.GetTableReference("Snapshots");
        }

        private Subscription FindSubscription(string appName, string logName)
        {
            var retrieveOperation = TableOperation.Retrieve<Subscription>(appName, logName);
            var retrievedResult = subscribersTable.Execute(retrieveOperation);
            var subscriber = retrievedResult.Result as Subscription;
            return subscriber;
        }

        private Snapshot FindSnapshot(string appName, string logName)
        {
            DateTime tb = Utils.NextOperationTB;
            string partitionKey = tb.Ticks.ToString();
            string rowKey = string.Format(
                "{0}_{1}_{2}",
                appName,
                logName,
                tb.Ticks
                );
            var retrieveOperation = TableOperation.Retrieve<Snapshot>(partitionKey, rowKey);
            var retrievedResult = snapshotsTable.Execute(retrieveOperation);
            var snapshot = retrievedResult.Result as Snapshot;
            return snapshot;
        }

        //
        // GET: /Snapshot/
        //
        // Note: This way of handling may not scale and may need to use continuation tokens later
        //
        public ActionResult Index()
        {
            TableRequestOptions reqOptions = new TableRequestOptions()
            {
                MaximumExecutionTime = TimeSpan.FromSeconds(10),
                RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(3), 3)
            };

            var query = new TableQuery<Snapshot>().Where(
                TableQuery.GenerateFilterCondition(
                    "PartitionKey", 
                    QueryComparisons.LessThanOrEqual, 
                    Utils.NextOperationTB.Ticks.ToString()
                ));
            var snapshots = snapshotsTable.ExecuteQuery(query, reqOptions).ToList();
            return View(snapshots);
        }

        //
        // GET: /Snapshot/Log
        //
        [RequireHttps]
        public ActionResult Log()
        {
            return View();
        }

        [HttpPost]
        [RequireHttps]
        [ValidateAntiForgeryToken]
        public ActionResult Log(Snapshot snapshot)
        {
            if (ModelState.IsValid)
            {
                Subscription subscriber = FindSubscription(snapshot.ApplicationName, snapshot.LogName);
                if (subscriber == null)
                {
                    ModelState.AddModelError(string.Empty, "Such a log has not been subscribed");
                }
                else if (!subscriber.APIKey.Equals(snapshot.APIKey, StringComparison.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError(string.Empty, "Invalid API Key");
                }
                else if (subscriber.Verified == false)
                {
                    ModelState.AddModelError(string.Empty, "Not currently verified");
                }
                else
                {
                    Snapshot existing = FindSnapshot(snapshot.ApplicationName, snapshot.LogName);
                    if (existing != null)
                    {
                        if (existing.Status != "Pending")
                        {
                            ModelState.AddModelError(string.Empty, "Snapshot cannot be edited because it isn't in Pending status");
                        }
                        else
                        {
                            existing.SnapshotValue = snapshot.SnapshotValue;
                            var replaceOperation = TableOperation.Replace(existing);
                            snapshotsTable.Execute(replaceOperation);
                            return RedirectToAction("Index");
                        }
                    }
                    else
                    {
                        snapshot.APIKey = null; //  No need to store this after verification
                        var insertOperation = TableOperation.Insert(snapshot);
                        snapshotsTable.Execute(insertOperation);
                        return RedirectToAction("Index");
                    }
                }
            }

            return View(snapshot);
        }
    }
}
