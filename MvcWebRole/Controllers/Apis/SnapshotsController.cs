using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Web.Http;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using Microsoft.WindowsAzure.Storage.Table;
using MvcWebRole.Models;
using MvcWebRole.Models.DTOs;
using MvcWebRole.SharedSrc;

namespace MvcWebRole.Controllers.Apis
{
    public class SnapshotsController : ApiController
    {
        private CloudTable subscribersTable;
        private CloudTable snapshotsTable;

        public SnapshotsController()
        {
            var storageAccount = Microsoft.WindowsAzure.Storage.CloudStorageAccount.Parse(
                RoleEnvironment.GetConfigurationSettingValue(
                    "StorageConnectionString"
                ));

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

        [HttpGet]
        public HttpResponseMessage Latest()
        {
            var query = new TableQuery<Snapshot>().Where(
                TableQuery.GenerateFilterCondition(
                    "PartitionKey",
                    QueryComparisons.LessThanOrEqual,
                    Utils.NextOperationTB.Ticks.ToString()
                ));
            IEnumerable<SnapshotDTO> snapshots = 
                snapshotsTable.ExecuteQuery(query).Select(x => new SnapshotDTO(x));
            return Request.CreateResponse(
                HttpStatusCode.OK,
                new
                {
                    Success = true,
                    Snapshots = snapshots
                },
                Configuration.Formatters.JsonFormatter
                );
        }

        [HttpPost]
        [System.Web.Mvc.RequireHttps]
        public HttpResponseMessage Log(Snapshot snapshot)
        {
            if (ModelState.IsValid)
            {
                Subscription subscriber = FindSubscription(snapshot.ApplicationName, snapshot.LogName);
                if (subscriber == null)
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = false,
                            Error = "Such a log has not been subscribed"
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }
                else if (!subscriber.APIKey.Equals(snapshot.APIKey, StringComparison.OrdinalIgnoreCase))
                {
                    return Request.CreateResponse(
                        HttpStatusCode.Unauthorized,
                        new
                        {
                            Success = false,
                            Error = "Invalid API Key"
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }
                else if (subscriber.Verified == false)
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = false,
                            Error = "Not verified"
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }
                else 
                {
                    Snapshot existing = FindSnapshot(snapshot.ApplicationName, snapshot.LogName);
                    if (existing != null)
                    {
                        if (existing.Status != "Pending")
                        {
                            return Request.CreateResponse(
                                HttpStatusCode.OK,
                                new
                                {
                                    Success = false,
                                    Error = string.Format(
                                        "Snapshot cannot be edited in {0} status",
                                        existing.Status
                                        )
                                },
                                Configuration.Formatters.JsonFormatter
                                );
                        }
                        else
                        {
                            existing.SnapshotValue = snapshot.SnapshotValue;
                            var replaceOperation = TableOperation.Replace(existing);
                            snapshotsTable.Execute(replaceOperation);
                            return Request.CreateResponse(
                                HttpStatusCode.OK,
                                new
                                {
                                    Success = true,
                                    Message = "Updated existing Snapshot, Logging is still Pending",
                                    Snapshot = new SnapshotDTO(snapshot),
                                },
                                Configuration.Formatters.JsonFormatter
                                );
                        }
                    }
                    else 
                    {
                        snapshot.APIKey = null;
                        var insertOperation = TableOperation.Insert(snapshot);
                        snapshotsTable.Execute(insertOperation);
                        return Request.CreateResponse(
                            HttpStatusCode.OK, 
                            new
                            {
                                Success = true,
                                Message = "Created Snapshot, Logging is Pending",
                                Snapshot = new SnapshotDTO(snapshot)
                            },
                            Configuration.Formatters.JsonFormatter
                            );
                    }
                }
            }

            List<string> errorList = ModelState.Values
                .SelectMany(m => m.Errors)
                .Select(e => e.ErrorMessage)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            return Request.CreateResponse(
                HttpStatusCode.OK,
                new
                { 
                    Success = false, 
                    Errors = errorList,
                    Snapshot = new SnapshotDTO(snapshot)
                },
                Configuration.Formatters.JsonFormatter
                );
        }
    }
}
