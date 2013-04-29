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
using Microsoft.WindowsAzure.Storage.Queue;

namespace MvcWebRole.Controllers.Apis
{
    public class SubscriptionsController : ApiController
    {
        private CloudTable subscribersTable;
        private CloudQueue subscribeQueue;

        public SubscriptionsController()
        {
            var storageAccount = Microsoft.WindowsAzure.Storage.CloudStorageAccount.Parse(
                RoleEnvironment.GetConfigurationSettingValue(
                    "StorageConnectionString"
                ));

            var tableClient = storageAccount.CreateCloudTableClient();
            subscribersTable = tableClient.GetTableReference("Subscribers");

            var queueClient = storageAccount.CreateCloudQueueClient();
            subscribeQueue = queueClient.GetQueueReference("subscribequeue");
        }

        [NonAction]
        private Subscription FindRow(string partitionKey, string rowKey)
        {
            var retrieveOperation = TableOperation.Retrieve<Subscription>(partitionKey, rowKey);
            var retrievedResult = subscribersTable.Execute(retrieveOperation);

            var subscriber = retrievedResult.Result as Subscription;

            return subscriber;
        }

        [HttpGet]
        public HttpResponseMessage Subscribers()
        {
            TableRequestOptions reqOptions = new TableRequestOptions()
            {
                MaximumExecutionTime = TimeSpan.FromSeconds(1.5),
                RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(3), 3)
            };

            try
            {
                var query = new TableQuery<Subscription>().Select(new string[] { 
                    "PartitionKey", 
                    "RowKey", 
                    "Description", 
                    "Verified",
                    "VerificationsSent"
                });
                IEnumerable<SubscriptionDTO> subscriptions =
                    subscribersTable.ExecuteQuery(query, reqOptions).Select(x => new SubscriptionDTO(x));
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new
                    {
                        Success = true,
                        Subscriptions = subscriptions
                    },
                    Configuration.Formatters.JsonFormatter
                    );
            }
            catch (StorageException)
            {
                throw new HttpResponseException(Request.CreateErrorResponse(
                    HttpStatusCode.ServiceUnavailable, 
                    "Timeout error, try again."
                    ));
            }
        }

        [HttpGet]
        public HttpResponseMessage Apps()
        {
            TableRequestOptions reqOptions = new TableRequestOptions()
            {
                MaximumExecutionTime = TimeSpan.FromSeconds(1.5),
                RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(3), 3)
            };

            try
            {
                var query = new TableQuery<Subscription>().Select(new string[] { "PartitionKey" });

                List<Subscription> subscribers = 
                    subscribersTable.ExecuteQuery(query, reqOptions).ToList();
                IEnumerable<string> apps =
                    subscribers.Select(s => s.ApplicationName).Distinct();
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new
                    {
                        Success = true,
                        Apps = apps
                    },
                    Configuration.Formatters.JsonFormatter
                    );
            }
            catch (StorageException)
            {
                throw new HttpResponseException(Request.CreateErrorResponse(
                    HttpStatusCode.ServiceUnavailable,
                    "Timeout error, try again."
                    ));
            }
        }

        [HttpGet]
        public HttpResponseMessage Logs(string appName)
        {
            TableRequestOptions reqOptions = new TableRequestOptions()
            {
                MaximumExecutionTime = TimeSpan.FromSeconds(1.5),
                RetryPolicy = new LinearRetry(TimeSpan.FromSeconds(3), 3)
            };

            try
            {
                var query = new TableQuery<Subscription>().Where(
                    TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, appName)
                    );
                IEnumerable<string> logs = 
                    subscribersTable.ExecuteQuery(query, reqOptions).ToList().Select(s => s.LogName);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new
                    {
                        Success = true,
                        Logs = logs
                    },
                    Configuration.Formatters.JsonFormatter
                    );
            }
            catch (StorageException)
            {
                throw new HttpResponseException(Request.CreateErrorResponse(
                    HttpStatusCode.ServiceUnavailable,
                    "Timeout error, try again."
                    ));
            }
        }

        [HttpPost]
        [System.Web.Mvc.RequireHttps]
        public HttpResponseMessage Subscribe(Subscription subscriber)
        {
            if (ModelState.IsValid)
            {
                Subscription prevSubscriber = FindRow(subscriber.ApplicationName, subscriber.LogName);
                if (prevSubscriber == null)
                {
                    subscriber.APIKey = Guid.NewGuid().ToString();
                    subscriber.Verified = false;
                    subscriber.VerificationsSent = 0;
                    var insertOperation = TableOperation.Insert(subscriber);
                    subscribersTable.Execute(insertOperation);

                    AddToSubscriberQueue(subscriber);

                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        { 
                            Success = true, 
                            Message = "Added to Subscriber Queue",
                            Subscription = new SubscriptionDTO(subscriber, true) 
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }
                else if (!prevSubscriber.EmailAddress.Equals(subscriber.EmailAddress, StringComparison.InvariantCultureIgnoreCase)
                         || prevSubscriber.Verified == true)
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new
                        {
                            Success = false,
                            Error = "You attempted to subscribe an already subscribed App with that Log Name",
                            Subscription = new SubscriptionDTO(subscriber, true)
                        },
                        Configuration.Formatters.JsonFormatter
                        );
                }
                else
                {
                    AddToSubscriberQueue(prevSubscriber);
                    return Request.CreateResponse(
                        HttpStatusCode.Accepted,
                        new
                        {
                            Success = true,
                            Message = "Re-added to subscriber queue",
                            Subscription = new SubscriptionDTO(prevSubscriber, true)
                        },
                        Configuration.Formatters.JsonFormatter
                        );
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
                    Subscription = new SubscriptionDTO(subscriber, true)
                },
                Configuration.Formatters.JsonFormatter
                );
        }

        [HttpPost]
        [System.Web.Mvc.RequireHttps]
        public HttpResponseMessage Verify(string appName, string logName, string apiKey, string action)
        {
            Subscription subscriber = null;
            if (ModelState.IsValid)
            {
                subscriber = FindRow(appName, logName);
                if (subscriber == null)
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new { Success = false, Error = "No such subscription exists" },
                        Configuration.Formatters.JsonFormatter
                        );
                }

                if (!subscriber.APIKey.Equals(apiKey, StringComparison.InvariantCulture))
                {
                    return Request.CreateResponse(
                        HttpStatusCode.Unauthorized,
                        new { Success = false, Error = "Invalid APIKey" },
                        Configuration.Formatters.JsonFormatter
                        );
                }

                if (subscriber.Verified == true)
                {
                    if (action.Equals("Confirm", StringComparison.InvariantCultureIgnoreCase))
                    {
                        return Request.CreateResponse(
                            HttpStatusCode.OK,
                            new { Success = true, Message = "Already verified previously" },
                            Configuration.Formatters.JsonFormatter
                            );
                    }
                }

                if (action.Equals("Confirm", StringComparison.InvariantCultureIgnoreCase))
                {
                    subscriber.Verified = true;
                    var replaceOperation = TableOperation.Replace(subscriber);
                    subscribersTable.Execute(replaceOperation);
                    return Request.CreateResponse(
                        HttpStatusCode.OK, 
                        new { Success = true, Message = "Verified" },
                        Configuration.Formatters.JsonFormatter
                        );
                }
                else
                {
                    subscriber.Verified = null;
                    var deleteOperation = TableOperation.Delete(subscriber);
                    subscribersTable.Execute(deleteOperation);
                    return Request.CreateResponse(
                        HttpStatusCode.OK, 
                        new { Success = true, Message = "Canceled Subscription" },
                        Configuration.Formatters.JsonFormatter
                        );
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
        [System.Web.Mvc.RequireHttps]
        public HttpResponseMessage Verify(VerifyDTO verify)
        {
            if (ModelState.IsValid)
            {
                return Verify(verify.ApplicationName, verify.LogName, verify.APIKey, verify.Action);
            }
            else
            {
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
                        Verify = verify
                    },
                    Configuration.Formatters.JsonFormatter
                    );
            }
        }

        private void AddToSubscriberQueue(Subscription subscriber)
        {
            // Create the queue message.
            string queueMessageString =
                subscriber.ApplicationName + "," +
                subscriber.LogName;
            var queueMessage = new CloudQueueMessage(queueMessageString);
            subscribeQueue.AddMessage(queueMessage);
        }
    }
}
