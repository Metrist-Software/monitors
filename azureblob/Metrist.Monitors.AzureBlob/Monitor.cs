using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Azure.Storage.Blobs;
using Metrist.Core;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.Storage.Fluent;

namespace Metrist.Monitors.AzureBlob
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public MonitorConfig() {}
        public MonitorConfig(BaseMonitorConfig baseCfg) : base(baseCfg) {}
        //Might need this for Azure
        public string ClientID { get; set; }
        public string ClientSecret { get; set; }
        public string TenantID { get; set; }
        public string SubscriptionID { get; set; }
        public string Region { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly Region _azureRegion;
        private IStorageAccount _storageAccount;
        private BlobContainerClient _containerClient;

        private const string STORAGE_RG_PREFIX = "storagerg";
        private const string STORAGE_NAME_PREFIX = "monitorstorage";
        private const string BLOB_CONTAINER_PREFIX = "storagecontainer";
        private const string CREATED_AT_TAG_NAME = "createdat";
        private const string BLOB_NAME = "monitorblob";
        public Monitor(MonitorConfig config) : base(config)
        {
            var envRegion = System.Environment.GetEnvironmentVariable("ORCHESTRATOR_REGION");
            if (envRegion != null)
            {
                _azureRegion = Region.Create(envRegion);
            }
            else
            {
                _azureRegion = Region.Create(config.Region);
            }
            _config = config;
        }

        public void Cleanup(Logger logger)
        {
            try
            {
                var azure = ConfigureSDK();
                foreach (var rg in azure.ResourceGroups.List().Where(o => o.Name.StartsWith(STORAGE_RG_PREFIX)).ToList())
                {
                    //No tags old resource group
                    if (rg.Tags == null)
                    {
                        continue;
                    }
                    var createdAt = rg.Tags.GetValueOrDefault(CREATED_AT_TAG_NAME);
                    if (createdAt == null || (DateTime.UtcNow - DateTime.Parse(createdAt)).TotalMinutes > 30)
                    {
                        //Kill the whole resource group with the storage account in it.
                        azure.ResourceGroups.BeginDeleteByName(rg.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                logger($"METRIST_MONITOR_ERROR - Error when trying to cleanup orphaned instances. {ex}");
            }
        }

        public double CreateStorageAccount(Logger logger)
        {
            var azure = ConfigureSDK();

            string storageName = SdkContext.RandomResourceName(STORAGE_NAME_PREFIX, 10);
            string rgName = SdkContext.RandomResourceName(STORAGE_RG_PREFIX, 10);

            var rg = azure
                .ResourceGroups
                .Define(rgName)
                .WithRegion(_azureRegion)
                .WithTag(CREATED_AT_TAG_NAME, DateTime.UtcNow.ToString("O"))
                .Create();

            var (time, storageAccount) = Timed(() => azure.StorageAccounts
                .Define(storageName)
                .WithRegion(_azureRegion)
                .WithExistingResourceGroup(rgName)
                .WithAccessFromAllNetworks()
                .WithBlobStorageAccountKind()
                .WithAccessTier(Microsoft.Azure.Management.Storage.Fluent.Models.AccessTier.Cool)
                .WithoutBlobEncryption()
                .WithTag(CREATED_AT_TAG_NAME, DateTime.UtcNow.ToString("O"))
                .Create());

            _storageAccount = storageAccount;

            return time;
        }

        public double CreateContainer(Logger logger)
        {
            var connString = $"DefaultEndpointsProtocol=https;AccountName={_storageAccount.Name};AccountKey={_storageAccount.GetKeys().Where(o => o.KeyName == "key1").First().Value};EndpointSuffix=core.windows.net";
            BlobServiceClient blobServiceClient = new BlobServiceClient(connString);
            string containerName = SdkContext.RandomResourceName(BLOB_CONTAINER_PREFIX, 10);
            var (time, containerClient) = Timed(() => blobServiceClient.CreateBlobContainerAsync(containerName));
            _containerClient = containerClient;
            return time;
        }

        public double AddBlob(Logger logger)
        {
            using var memoryStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("These are the bytes for my blob"));
            var (time, response) = Timed(() => _containerClient.UploadBlobAsync(BLOB_NAME, memoryStream));
            return time;
        }

        public double GetBlob(Logger logger)
        {
            BlobClient blobClient = _containerClient.GetBlobClient(BLOB_NAME);
            var (time, response) = Timed(() => blobClient.DownloadAsync());
            return time;
        }

        public double DeleteBlob(Logger logger)
        {
            var azure = ConfigureSDK();
            var (time, response) = Timed(() => _containerClient.DeleteBlobAsync(BLOB_NAME));
            return time;
        }

        public void TearDown(Logger logger)
        {
            try
            {
                if (_storageAccount != null)
                {
                    var azure = ConfigureSDK();
                    azure.ResourceGroups.BeginDeleteByName(_storageAccount.ResourceGroupName);
                }
            }
            catch (Exception ex)
            {
                logger($"METRIST_MONITOR_ERROR - Error when doing final cleanup for monitor run. {ex}");
            }
        }

        private IAzure ConfigureSDK()
        {
            var credentials = SdkContext.AzureCredentialsFactory
                .FromServicePrincipal(_config.ClientID,
                    _config.ClientSecret,
                    _config.TenantID,
                    AzureEnvironment.AzureGlobalCloud);

            return Microsoft.Azure.Management.Fluent.Azure
                .Configure()
                .Authenticate(credentials)
                .WithSubscription(_config.SubscriptionID);
        }
    }
}
