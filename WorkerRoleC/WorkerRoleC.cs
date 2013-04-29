using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading;
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

namespace WorkerRoleC
{
    public class WorkerRoleC : RoleEntryPoint
    {
        private static Amount MINIMUM_TOTAL_BALANCE_AMOUNT = new Amount() { AmountValue = 0.5m, Currency = "BTC" };

        private CloudQueue subscribeQueue;
        private CloudTable subscribersTable;

        private volatile bool onStopCalled = false;
        private volatile bool returnedFromRunMethod = false;

        public override void Run()
        {
            Trace.TraceInformation("WorkerRoleC start of Run()");
            CloudQueueMessage msg = null;
            bool messageFound = false;
            DateTime nextBalanceCheck = DateTime.Now;
            while (true)
            {
                try
                {
                    if (onStopCalled)
                    {
                        Trace.TraceInformation("onStopCalled WorkerRoleC");
                        returnedFromRunMethod = true;
                        return;
                    }

                    if (DateTime.Now > nextBalanceCheck)
                    {
                        CheckBalanceStatus();
                        nextBalanceCheck = Utils.NextOperationTB;
                    }

                    // Retrieve and process a new message from the subscribe queue.
                    msg = subscribeQueue.GetMessage();
                    if (msg != null)
                    {
                        ProcessSubscribeQueueMessage(msg);
                        messageFound = true;
                    }
                    else
                    {
                        messageFound = false;
                    }

                    if (!messageFound)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(30));
                    }
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Caught exception {0}.", ex.Message);
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }
             }
        }

        private void ProcessSubscribeQueueMessage(CloudQueueMessage msg)
        {
            if (msg.DequeueCount > 5)
            {
                Trace.TraceError(
                    "Deleting poison subscribe message: message {0} WorkerC.",
                    msg.ToString()
                    );
                subscribeQueue.DeleteMessage(msg);
            }

            string[] subscriberParts = msg.AsString.Split(new char[] { ',' });
            string partitionKey = subscriberParts[0];
            string rowKey = subscriberParts[1];

            var retrieveOperation = TableOperation.Retrieve<Subscription>(partitionKey, rowKey);
            var retrievedResult = subscribersTable.Execute(retrieveOperation);
            var subscriber = retrievedResult.Result as Subscription;

            if (subscriber.Verified == true)
            {
                Trace.TraceWarning(
                    "Deleting message where subscriber App {0} with Log {1} is already verified.",
                    partitionKey,
                    rowKey
                    );
                subscribeQueue.DeleteMessage(msg);
                return;
            }
            else if (subscriber.VerificationsSent >= 3)
            {
                Trace.TraceWarning(
                    "Deleting message where subscriber App {0} with Log {1} has had {2} verification emails sent.",
                    partitionKey,
                    rowKey,
                    subscriber.VerificationsSent
                    );
                subscribeQueue.DeleteMessage(msg);
                return;
            }

            // Send email
            SendEmail(subscriber);
            
            // Increment count
            subscriber.VerificationsSent++;
            var replaceOperation = TableOperation.Replace(subscriber);
            subscribersTable.Execute(replaceOperation);

            subscribeQueue.DeleteMessage(msg);
        }

        private static void CheckBalanceStatus()
        {
            APIKey key1 = new APIKey(
                RoleEnvironment.GetConfigurationSettingValue("CoinbaseAccountKey1")
                );
            APIKey key2 = new APIKey(
                RoleEnvironment.GetConfigurationSettingValue("CoinbaseAccountKey2")
                );

            User user1 = API.GetUser(key1);
            if (!user1.IsValid)
            {
                Trace.TraceError(
                    "Unable to get information for {0}: {1}",
                    key1,
                    user1
                    );
                return;
            }
            User user2 = API.GetUser(key2);
            if (!user2.IsValid)
            {
                Trace.TraceError(
                    "Unable to get information for {0}: {1}",
                    key2,
                    user2
                    );
                return;
            }

            if (user1.Balance.Currency != "BTC")
            {
                user1.Balance = API.GetBalance(key1);
                if (!user1.Balance.IsValid)
                {
                    Trace.TraceError(
                        "Unable to convert user1's balance to BTC: {0}",
                        user1.Balance
                        );
                    return;
                }
            }

            if (user2.Balance.Currency != "BTC")
            {
                user2.Balance = API.GetBalance(key2);
                if (!user2.Balance.IsValid)
                {
                    Trace.TraceError(
                        "Unable to convert user2's balance to BTC: {0}",
                        user2.Balance
                        );
                    return;
                }
            }

            if (user1.Balance.AmountValue + user2.Balance.AmountValue < MINIMUM_TOTAL_BALANCE_AMOUNT.AmountValue)
            {
                SendEmail(user1);
                SendEmail(user2);
            }
        }

        private static void SendEmail(Subscription subscriber)
        {
            string serviceName = RoleEnvironment.GetConfigurationSettingValue("ServiceName");
            var fromAddress = new MailAddress(
                RoleEnvironment.GetConfigurationSettingValue("ServiceGmailAddress") + "@gmail.com",
                serviceName
                );
            var toAddress = new MailAddress(subscriber.EmailAddress, subscriber.ApplicationName);
            string fromPassword = RoleEnvironment.GetConfigurationSettingValue("ServiceGmailPassword");
            string subject = String.Format(
                "Subscribe to {0}",
                serviceName
                );

            string subscribeURL = RoleEnvironment.GetConfigurationSettingValue("LoggingServiceURL") +
                "/Subscription/Verify?appName=" + subscriber.ApplicationName + "&logName=" + subscriber.LogName +
                "&apiKey=" + subscriber.APIKey;

            string body = String.Format(
                "<p>Click the link below to subscribe {0}'s Log <code>{1}</code> to {2}. " +
                "If you don't confirm your subscription, you won't be subscribed to the service.</p>" +
                "<a href=\"{3}\">Confirm Subscription</a>" +
                "<p>With each subscription comes an API Key, which functions as a password for all actions involving an application-specific log. " +
                "Thus for all requests requiring authentication (such as adding a snapshot to the logging service, editing a snapshot, or unsubscribing), this unique key " +
                "<strong><em>MUST</em></strong> be provided. If you lose this key, there is no way currently for you to recover or reset it." +
                "<p>Your API Key: <pre>{4}</pre></p>",
                subscriber.ApplicationName,
                subscriber.LogName,
                serviceName, 
                subscribeURL,
                subscriber.APIKey
                );

            var smtp = new SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Credentials = new NetworkCredential(fromAddress.Address, fromPassword),
                Timeout = 20000
            };
            using (var message = new MailMessage(fromAddress, toAddress)
                   {
                       Subject = subject,
                       Body = body,
                       IsBodyHtml = true
                   }
                  )
            {
                smtp.Send(message);
            }
        }

        private static void SendEmail(User user)
        {
            string serviceName = RoleEnvironment.GetConfigurationSettingValue("ServiceName");
            var fromAddress = new MailAddress(
                RoleEnvironment.GetConfigurationSettingValue("ServiceGmailAddress") + "@gmail.com",
                serviceName
                );
            var toAddress = new MailAddress(user.Email, user.Name);
            string fromPassword = RoleEnvironment.GetConfigurationSettingValue("ServiceGmailPassword");

            string subject = string.Format("[LetMeLogThatForYou] IMPORTANT: Coinbase Account Balance Critically Low!!");

            string body = string.Format(
                "<p>The Coinbase User Account {0} current balance is: {1}</p>" +
                "<p>This is below the minimum balance {2} requried by the application.</p>",
                user,
                user.Balance,
                MINIMUM_TOTAL_BALANCE_AMOUNT
                );

            var smtp = new SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Credentials = new NetworkCredential(fromAddress.Address, fromPassword),
                Timeout = 20000
            };
            using (var message = new MailMessage(fromAddress, toAddress)
            {
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            }
                  )
            {
                smtp.Send(message);
            }
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
            Trace.TraceInformation("Initializing storage account in WorkerC");
            var storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue(
                "StorageConnectionString"
                ));

            // Initialize queue storage 
            Trace.TraceInformation("Creating queue client in WorkerC");
            var queueClient = storageAccount.CreateCloudQueueClient();
            subscribeQueue = queueClient.GetQueueReference("subscribequeue");

            // Initialize table storage
            Trace.TraceInformation("Creating table client in WorkerC");
            var tableClient = storageAccount.CreateCloudTableClient();
            subscribersTable = tableClient.GetTableReference("Subscribers");

            Trace.TraceInformation("WorkerC: Creating blob container, queue, tables, if they don't exist.");
            subscribeQueue.CreateIfNotExists();
            subscribersTable.CreateIfNotExists();

            return base.OnStart();
        }
    }
}
