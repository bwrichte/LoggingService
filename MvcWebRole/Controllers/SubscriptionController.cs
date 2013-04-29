using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.RetryPolicies;
using Microsoft.WindowsAzure.Storage.Table;
using MvcWebRole.Models;
using System.Diagnostics;
using Microsoft.WindowsAzure.Storage.Queue;

namespace LoggingServiceControllers
{
    public class SubscriptionController : Controller
    {
        private CloudTable subscribersTable;
        private CloudQueue subscribeQueue;

        public SubscriptionController()
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

            var tableClient = storageAccount.CreateCloudTableClient();
            subscribersTable = tableClient.GetTableReference("Subscribers");

            var queueClient = storageAccount.CreateCloudQueueClient();
            subscribeQueue = queueClient.GetQueueReference("subscribequeue");
        }

        private Subscription FindRow(string partitionKey, string rowKey)
        {
            var retrieveOperation = TableOperation.Retrieve<Subscription>(partitionKey, rowKey);
            var retrievedResult = subscribersTable.Execute(retrieveOperation);
            
            var subscriberList = retrievedResult.Result as Subscription;

            return subscriberList;
        }

        //
        // GET: /Subscription/
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

            List<Subscription> subscribers;
            try
            {
                var query = new TableQuery<Subscription>().Select(new string[] { 
                    "PartitionKey", 
                    "RowKey", 
                    "Description", 
                    "Verified"
                });

                subscribers = subscribersTable.ExecuteQuery(query, reqOptions).ToList();
            }
            catch (StorageException se)
            {
                ViewBag.errorMessage = "Timeout error, try again.";
                Trace.TraceError(se.Message);
                return View("Error: " + se.Message);
            }

            return View(subscribers);
        }

        //
        // GET: /Subscription/Subscribe
        //
        [RequireHttps]
        public ActionResult Subscribe()
        {
            return View();
        }

        //
        // GET: /Subscription/Verify
        //
        [RequireHttps]
        public ActionResult Verify(string appName, string logName, string apiKey)
        {
            Subscription subscriber = FindRow(appName, logName);
            if (subscriber == null)
            {
                ModelState.AddModelError(string.Empty, "No such subscription exists.");
                return View("Verification", model:null);
            }
            if (!subscriber.APIKey.Equals(apiKey, StringComparison.InvariantCulture))
            {
                ModelState.AddModelError(string.Empty, "Invalid APIKey, Cannot Verify.");
                return View("Verification", model:null);
            }

            return View("Verification", model: subscriber);
        }

        [HttpPost]
        [RequireHttps]
        [ValidateAntiForgeryToken]
        public ActionResult Verify(string ApplicationName, string LogName, string APIKey, string action)
        {
            Subscription subscriber = null;
            if (ModelState.IsValid)
            {
                subscriber = FindRow(ApplicationName, LogName);
                if (subscriber == null)
                {
                    ModelState.AddModelError(string.Empty, "No such subscription exists.");
                    subscriber = new Subscription()
                    {
                        ApplicationName = ApplicationName,
                        LogName = LogName
                    };
                    return View(subscriber);
                }

                if (!subscriber.APIKey.Equals(APIKey, StringComparison.InvariantCulture))
                {
                    ModelState.AddModelError(string.Empty, "Invalid ID, Cannot Verify.");
                    return View("Verification", model: null);
                }

                if (action.Equals("Confirm", StringComparison.InvariantCultureIgnoreCase))
                {
                    subscriber.Verified = true;
                    var replaceOperation = TableOperation.Replace(subscriber);
                    subscribersTable.Execute(replaceOperation);
                    return View("Verification", model: subscriber);
                }
                else
                {
                    subscriber.Verified = null;
                    var deleteOperation = TableOperation.Delete(subscriber);
                    subscribersTable.Execute(deleteOperation);
                    return View("Verification", model: subscriber);
                }
            }
            subscriber = new Subscription()
            {
                ApplicationName = ApplicationName,
                LogName = LogName,
                APIKey = APIKey
            };
            return View("Verification", model: subscriber);
        }

        //
        // POST: /Subscription/Subscribe
        // TODO: Validate that this does not already exist, because if it does, we get conflict
        //
        [HttpPost]
        [RequireHttps]
        [ValidateAntiForgeryToken]
        public ActionResult Subscribe(Subscription subscriber)
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

                    return View("Confirmation", model:subscriber);
                }
                else if (!prevSubscriber.EmailAddress.Equals(subscriber.EmailAddress, StringComparison.InvariantCultureIgnoreCase)
                         || prevSubscriber.Verified == true)
                {
                    ModelState.AddModelError(string.Empty, "You attempted to subscribe an already subscribed App with that Log Name");
                }
                else
                {
                    AddToSubscriberQueue(prevSubscriber);
                    return View("Reconfirmation", model: prevSubscriber);
                }
            }

            return View(subscriber);
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

        //
        // TODO: Add Edit, Delete
        //
    }
}