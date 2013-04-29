using BlockchainSharp.API;
using BlockchainSharp.Resources;
using CoinbaseSharp.API;
using CoinbaseSharp.Authentication;
using CoinbaseSharp.Resources;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Web.Http;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using Microsoft.WindowsAzure.Storage.Table;
using MvcWebRole.Models;
using MvcWebRole.Models.DTOs;
using MvcWebRole.SharedSrc;

namespace MvcWebRole.Controllers.Apis
{
    public class ProofsController : ApiController
    {
        private CloudTable subscribersTable;
        private CloudTable proofsArchiveTable;
        private CloudBlobContainer blobContainer;

        public ProofsController()
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

        private List<Subscription> FindSubscriptions(string appName)
        {
            TableRequestOptions reqOptions = new TableRequestOptions()
            {
                MaximumExecutionTime = TimeSpan.FromSeconds(10),
                RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(3), 3)
            };

            var query = new TableQuery<Subscription>().Where(
                TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, appName)
                );

            return subscribersTable.ExecuteQuery(query, reqOptions).ToList();
        }

        private Subscription FindSubscription(string appName, string logName)
        {
            var retrieveOperation = TableOperation.Retrieve<Subscription>(appName, logName);
            var retrievedResult = subscribersTable.Execute(retrieveOperation);
            var subscriber = retrievedResult.Result as Subscription;
            return subscriber;
        }

        [HttpGet]
        public HttpResponseMessage GetProofs(string appName, string logName)
        {
            if (string.IsNullOrWhiteSpace(appName))
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new
                    {
                        Success = false,
                        Error = "Must provide an Application Name to search for Proofs"
                    },
                    Configuration.Formatters.JsonFormatter
                    );
            }

            if (string.IsNullOrWhiteSpace(logName))
            {
                List<Subscription> subscriptions = FindSubscriptions(appName);

                if (subscriptions.Count == 0)
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = false,
                            Error = string.Format("{0} has no Subscriptions", appName)
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }

                bool foundVerified = false;
                foreach (Subscription subscription in subscriptions)
                {
                    if (subscription.Verified == true)
                    {
                        foundVerified = true;
                        break;
                    }
                }
                if (!foundVerified)
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = false,
                            Error = string.Format("{0} has no Verified Subscriptions", appName)
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }
            }
            else
            {
                Subscription subscriber = FindSubscription(appName, logName);

                if (subscriber == null)
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = false,
                            Error = "Subscriber does not exist"
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }
                else if (subscriber.Verified != true)
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = false,
                            Error = "Subscriber is not verified"
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }
            }

            TableRequestOptions reqOptions = new TableRequestOptions()
            {
                MaximumExecutionTime = TimeSpan.FromSeconds(10),
                RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(3), 3)
            };

            List<Proof> proofs;
            try
            {
                TableQuery<Proof> query;

                if (string.IsNullOrWhiteSpace(logName))
                {
                    query = new TableQuery<Proof>().Where(
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, appName)
                        );
                }
                else
                {
                    Snapshot snapshot = new Snapshot()
                    {
                        ApplicationName = appName,
                        LogName = logName,
                        TimeBlock = Utils.NextOperationTB
                    };

                    query = new TableQuery<Proof>().Where(
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
                }

                proofs = proofsArchiveTable.ExecuteQuery(query, reqOptions).ToList();
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new
                    {
                        Success = true,
                        Proofs = proofs.Select(x => new ProofDTO(x))
                    },
                    Configuration.Formatters.JsonFormatter
                    );
            }
            catch (StorageException se)
            {
                Trace.TraceError(se.Message);
                throw new HttpResponseException(Request.CreateErrorResponse(
                    HttpStatusCode.ServiceUnavailable,
                    "Timeout error, try again."
                    ));
            }
        }

        [HttpPost]
        public HttpResponseMessage Query(ProofQueryDTO query)
        {
            if (ModelState.IsValid)
            {
                if (string.IsNullOrWhiteSpace(query.ApplicationName))
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = false,
                            Error = "Must provide an Application Name to search for Proofs"
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }

                if (string.IsNullOrWhiteSpace(query.LogName))
                {
                    if (query.TimeBlock.Ticks > 0)
                    {
                        return Request.CreateResponse(
                            HttpStatusCode.OK,
                            new
                            {
                                Success = false,
                                Error = "Cannot query for Specific TimeBlock without Specific Log"
                            },
                            Configuration.Formatters.JsonFormatter
                            );
                    }

                    List<Subscription> subscriptions = FindSubscriptions(query.ApplicationName);

                    if (subscriptions.Count == 0)
                    {
                        return Request.CreateResponse(
                            HttpStatusCode.OK,
                            new
                            {
                                Success = false,
                                Error = string.Format("{0} has no Subscriptions", query.ApplicationName)
                            },
                            Configuration.Formatters.JsonFormatter
                            );
                    }

                    bool foundVerified = false;
                    foreach (Subscription subscription in subscriptions)
                    {
                        if (subscription.Verified == true)
                        {
                            foundVerified = true;
                            break;
                        }
                    }
                    if (!foundVerified)
                    {
                        return Request.CreateResponse(
                            HttpStatusCode.OK,
                            new
                            {
                                Success = false,
                                Error = string.Format("{0} has no Verified Subscriptions", query.ApplicationName)
                            },
                            Configuration.Formatters.JsonFormatter
                            );
                    }
                }
                else
                {
                    Subscription subscriber = FindSubscription(query.ApplicationName, query.LogName);

                    if (subscriber == null)
                    {
                        return Request.CreateResponse(
                            HttpStatusCode.OK,
                            new
                            {
                                Success = false,
                                Error = "Subscriber does not exist"
                            },
                            Configuration.Formatters.JsonFormatter
                            );
                    }
                    else if (subscriber.Verified != true)
                    {
                        return Request.CreateResponse(
                            HttpStatusCode.OK,
                            new
                            {
                                Success = false,
                                Error = "Subscriber is not verified"
                            },
                            Configuration.Formatters.JsonFormatter
                            );
                    }
                }

                TableRequestOptions reqOptions = new TableRequestOptions()
                {
                    MaximumExecutionTime = TimeSpan.FromSeconds(10),
                    RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(3), 3)
                };

                List<Proof> proofs;
                try
                {
                    TableQuery<Proof> tableQuery;

                    if (string.IsNullOrWhiteSpace(query.LogName))
                    {
                        // Guaranteed from above that TB is null
                        tableQuery = new TableQuery<Proof>().Where(
                            TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, query.ApplicationName)
                            );
                    }
                    else
                    {
                        if (query.TimeBlock.Ticks <= 0)
                        {
                            Snapshot snapshot = new Snapshot()
                            {
                                ApplicationName = query.ApplicationName,
                                LogName = query.LogName,
                                TimeBlock = Utils.NextOperationTB
                            };
                            tableQuery = new TableQuery<Proof>().Where(
                                TableQuery.CombineFilters(
                                    TableQuery.CombineFilters(
                                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, query.ApplicationName),
                                        TableOperators.And,
                                        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.LessThanOrEqual, snapshot.RowKey)
                                        ),
                                    TableOperators.And,
                                    TableQuery.GenerateFilterCondition("LogName", QueryComparisons.Equal, query.LogName)
                                    )
                                );
                        }
                        else
                        {
                            Snapshot snapshot = new Snapshot()
                            {
                                ApplicationName = query.ApplicationName,
                                LogName = query.LogName,
                                TimeBlock = query.TimeBlock
                            };

                            string queryComparison;
                            if (query.QueryComparison.Equals("Equal", StringComparison.InvariantCultureIgnoreCase))
                            {
                                queryComparison = QueryComparisons.Equal;
                            }
                            else if (query.QueryComparison.Equals("LessThan", StringComparison.InvariantCultureIgnoreCase))
                            {
                                queryComparison = QueryComparisons.LessThan;
                            }
                            else if (query.QueryComparison.Equals("GreaterThan", StringComparison.InvariantCultureIgnoreCase))
                            {
                                queryComparison = QueryComparisons.GreaterThan;
                            }
                            else if (query.QueryComparison.Equals("LessThanOrEqual", StringComparison.InvariantCultureIgnoreCase))
                            {
                                queryComparison = QueryComparisons.LessThanOrEqual;
                            }
                            else if (query.QueryComparison.Equals("GreaterThanOrEqual", StringComparison.InvariantCultureIgnoreCase))
                            {
                                queryComparison = QueryComparisons.GreaterThanOrEqual;
                            }
                            else
                            {
                                queryComparison = QueryComparisons.LessThanOrEqual;
                            }

                            tableQuery = new TableQuery<Proof>().Where(
                                TableQuery.CombineFilters(
                                    TableQuery.CombineFilters(
                                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, query.ApplicationName),
                                        TableOperators.And,
                                        TableQuery.GenerateFilterCondition("RowKey", queryComparison, snapshot.RowKey)
                                        ),
                                    TableOperators.And,
                                    TableQuery.GenerateFilterCondition("LogName", QueryComparisons.Equal, query.LogName)
                                    )
                                );
                        }
                    }

                    proofs = proofsArchiveTable.ExecuteQuery(tableQuery, reqOptions).ToList();
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = true,
                            Proofs = proofs.Select(x => new ProofDTO(x))
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }
                catch (StorageException se)
                {
                    Trace.TraceError(se.Message);
                    throw new HttpResponseException(Request.CreateErrorResponse(
                        HttpStatusCode.ServiceUnavailable,
                        "Timeout error, try again."
                        ));
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
                    Errors = errorList
                },
                Configuration.Formatters.JsonFormatter
                );
        }

        [HttpPost]
        public HttpResponseMessage Audit(ProofQueryDTO query)
        {
            if (ModelState.IsValid)
            {
                // Validate Query
                if (string.IsNullOrWhiteSpace(query.ApplicationName))
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = false,
                            Error = "Must provide an Application Name to Audit"
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }
                else if (string.IsNullOrWhiteSpace(query.LogName))
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = false,
                            Error = "Must provide a Log Name to Audit"
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }
                else if (query.TimeBlock.Ticks <= 0)
                {
                    query.TimeBlock = DateTime.Now;
                }

                
                Subscription subscriber = FindSubscription(query.ApplicationName, query.LogName);

                if (subscriber == null)
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = false,
                            Error = "Subscriber does not exist"
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }
                else if (subscriber.Verified != true)
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = false,
                            Error = "Subscriber is not verified"
                        },
                        Configuration.Formatters.JsonFormatter
                        );
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
                        ApplicationName = query.ApplicationName,
                        LogName = query.LogName,
                        TimeBlock = query.TimeBlock
                    };

                    var tableQuery = new TableQuery<Proof>().Where(
                        TableQuery.CombineFilters(
                            TableQuery.CombineFilters(
                                TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, query.ApplicationName),
                                TableOperators.And,
                                TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.LessThanOrEqual, snapshot.RowKey)
                                ),
                            TableOperators.And,
                            TableQuery.GenerateFilterCondition("LogName", QueryComparisons.Equal, query.LogName)
                            )
                        );

                    proofs = proofsArchiveTable.ExecuteQuery(tableQuery, reqOptions).ToList();
                    if (proofs.Count == 0)
                    {
                        return Request.CreateResponse(
                            HttpStatusCode.OK,
                            new
                            {
                                Success = false,
                                Error = string.Format("No proofs as of {0}", query.TimeBlock.ToString())
                            },
                            Configuration.Formatters.JsonFormatter
                            );
                    }

                    Proof proofToAudit = null;
                    long maxTicks = 0;
                    foreach (Proof proof in proofs)
                    {
                        long ticks = DateTime.Parse(proof.TimeBlock).Ticks;
                        if (ticks > maxTicks)
                        {
                            maxTicks = ticks;
                            proofToAudit = proof;
                        }
                    }

                    //
                    // TODO: Make API Call
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

                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = true,
                            Proof = new ProofDTO(proofToAudit)
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }
                catch (StorageException se)
                {
                    Trace.TraceError(se.Message);
                    throw new HttpResponseException(Request.CreateErrorResponse(
                        HttpStatusCode.ServiceUnavailable,
                        "Timeout error, try again."
                        ));
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
                    Errors = errorList
                },
                Configuration.Formatters.JsonFormatter
                );
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
