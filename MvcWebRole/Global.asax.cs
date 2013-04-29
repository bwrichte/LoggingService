using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace MvcWebRole
{
    // Note: For instructions on enabling IIS6 or IIS7 classic mode, 
    // visit http://go.microsoft.com/?LinkId=9394801

    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();

            WebApiConfig.Register(GlobalConfiguration.Configuration);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
            AuthConfig.RegisterAuth();

            //
            // Verify that all the tables, queues, and blob containers used in this application
            // exist, and create any that do not already exist.
            //
            CreateTablesQueuesBlobContainers();
        }

        private static void CreateTablesQueuesBlobContainers()
        {
            var storageAccount = CloudStorageAccount.Parse(RoleEnvironment.GetConfigurationSettingValue("StorageConnectionString"));
            /*
             * If this is running in a Windows Azure Web Site (not a Cloud Service),
             * use the Web.config file: 
             *     var storageAccount = CloudStorageAccount.Parse(
             *         ConfigurationManager.ConnectionStrings["StorageConnectionString"].ConnectionString
             *         );
             */

            var tableClient = storageAccount.CreateCloudTableClient();
            var blobClient = storageAccount.CreateCloudBlobClient();
            var queueClient = storageAccount.CreateCloudQueueClient();

            //
            // Create tables, queues, and blob containers from clients
            //
            var subscriberTable = tableClient.GetTableReference("Subscribers");
            subscriberTable.CreateIfNotExists();

            var snapshotTable = tableClient.GetTableReference("Snapshots");
            snapshotTable.CreateIfNotExists();

            var proofsArchiveTable = tableClient.GetTableReference("ProofsArchive");
            proofsArchiveTable.CreateIfNotExists();
            
            var blobContainer = blobClient.GetContainerReference("proofscontainer");
            blobContainer.CreateIfNotExists();
            blobContainer.SetPermissions(new BlobContainerPermissions
            {
                PublicAccess = BlobContainerPublicAccessType.Blob
            });

            var snapshotQueue = queueClient.GetQueueReference("snapshotsqueue");
            snapshotQueue.CreateIfNotExists();

            var subscribeQueue = queueClient.GetQueueReference("subscribequeue");
            subscribeQueue.CreateIfNotExists();
        }
    }
}