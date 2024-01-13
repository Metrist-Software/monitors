using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Management.CosmosDB.Fluent;
using Microsoft.Azure.Management.CosmosDB.Fluent.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Newtonsoft.Json;
using Metrist.Core;

namespace Metrist.Monitors.AzureDB
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public MonitorConfig() { }
        public MonitorConfig(BaseMonitorConfig baseCfg) : base(baseCfg) { }

        public string ClientID { get; set; }
        public string ClientSecret { get; set; }
        public string TenantID { get; set; }
        public string SubscriptionID { get; set; }
        public string Region { get; set; }
    }

    public class MonitorItem
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly Region _azureRegion;

        private ICosmosDBAccount _dbAccount;

        private CosmosClient _client;
        private string _itemId;

        private const string COSMOS_NAME_PREFIX = "cosmosmonitordb";
        private const string COSMOS_RG_PREFIX = "cosmosrg";
        private const string CREATED_AT_TAG_NAME = "createdat";
        private const string DEFAULT_DATABASE_NAME = "monitordb";
        private const string DEFAULT_CONTAINER_NAME = "monitorcontainer";

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
            logger("Running cleanup");
            try
            {
                var azure = ConfigureSDK();

                foreach (var rg in azure.ResourceGroups.List().Where(o => o.Name.StartsWith(COSMOS_RG_PREFIX)).ToList())
                {
                    logger($"Cleanup up resource group {rg.Name}");
                    //No tags old resource group
                    if (rg.Tags == null)
                    {
                        continue;
                    }
                    var createdAt = rg.Tags.GetValueOrDefault(CREATED_AT_TAG_NAME);
                    if (createdAt == null || (DateTime.UtcNow - DateTime.Parse(createdAt)).TotalMinutes > 30)
                    {
                        System.Console.WriteLine(rg.Name);
                        //Kill the whole resource group with the account in it.
                        azure.ResourceGroups.BeginDeleteByName(rg.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                logger($"METRIST_MONITOR_ERROR - Error when trying to cleanup orphaned instances. {ex}");
            }
        }

        public double CreateCosmosAccount(Logger logger)
        {
            var azure = ConfigureSDK();

            string dbName = SdkContext.RandomResourceName(COSMOS_NAME_PREFIX, 10);
            string rgName = SdkContext.RandomResourceName(COSMOS_RG_PREFIX, 10);

            var rg = azure
                .ResourceGroups
                .Define(rgName)
                .WithRegion(_azureRegion)
                .WithTag(CREATED_AT_TAG_NAME, DateTime.UtcNow.ToString("O"))
                .Create();

            var (time, dbAccount) = Timed(() => azure.CosmosDBAccounts
                .Define(dbName)
                .WithRegion(_azureRegion)
                .WithExistingResourceGroup(rgName)
                .WithDataModelSql()
                .WithStrongConsistency()
                .WithTag(CREATED_AT_TAG_NAME, DateTime.UtcNow.ToString("O"))
                .Create()
            );

            _dbAccount = dbAccount;

            return time;
        }

        public void CreateDatabase(Logger logger)
        {
            _dbAccount.Update()
                .DefineNewSqlDatabase(GetDatabaseName())
                .Attach()
                .Apply();
        }

        public void CreateContainer(Logger logger)
        {
            _dbAccount.Update()
                .UpdateSqlDatabase(GetDatabaseName())
                .DefineNewSqlContainer(GetContainerName())
                .WithThroughput(400)
                .WithPartitionKey(PartitionKind.Hash, null)
                .WithPartitionKeyPath("/id")
                .Attach()
                .Parent()
                .Apply();
        }

        public double InsertItem(Logger logger)
        {
            EnsureClient();
            var container = _client.GetContainer(GetDatabaseName(), GetContainerName());

            _itemId = Guid.NewGuid().ToString();

            var (time, itemResponse) = Timed(async () =>
                await container.CreateItemAsync(new MonitorItem { Id = _itemId, CreatedAt = DateTime.UtcNow }, new PartitionKey(_itemId))
            );

            return time;
        }

        public void GetItem(Logger logger)
        {
            EnsureClient();
            var container = _client.GetContainer(GetDatabaseName(), GetContainerName());

            var item = container.GetItemLinqQueryable<MonitorItem>(true)
                .Where(item => item.Id == _itemId)
                .AsEnumerable()
                .FirstOrDefault();
        }

        public async Task DeleteItem(Logger logger)
        {
            EnsureClient();
            var container = _client.GetContainer(GetDatabaseName(), GetContainerName());

            await container.DeleteItemAsync<MonitorItem>(_itemId, new PartitionKey(_itemId));
        }

        public void DeleteContainer(Logger logger)
        {
            _dbAccount
                .Update()
                .UpdateSqlDatabase(GetDatabaseName())
                .WithoutSqlContainer(GetContainerName())
                .Parent()
                .Apply();
        }

        public void DeleteDatabase(Logger logger)
        {
            _dbAccount
                .Update()
                .WithoutSqlDatabase(GetDatabaseName())
                .Apply();
        }

        private void EnsureClient()
        {
            if (_client == null)
            {
                _client = new CosmosClient(GetConnectionString());
            }
        }

        public void TearDown(Logger logger)
        {
            try
            {
                if (_dbAccount != null)
                {
                    var azure = ConfigureSDK();
                    azure.ResourceGroups.BeginDeleteByName(_dbAccount.ResourceGroupName);
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
                .FromServicePrincipal(
                    _config.ClientID,
                    _config.ClientSecret,
                    _config.TenantID,
                    AzureEnvironment.AzureGlobalCloud
                );

            return Microsoft.Azure.Management.Fluent.Azure
                .Configure()
                .Authenticate(credentials)
                .WithSubscription(_config.SubscriptionID);
        }

        private string GetConnectionString()
        {
            return _dbAccount
                .ListConnectionStrings()
                .ConnectionStrings
                .First(entry => entry.Description == "Primary SQL Connection String")
                .ConnectionString;
        }

        private string GetDatabaseName() => DEFAULT_DATABASE_NAME;

        private string GetContainerName() => DEFAULT_CONTAINER_NAME;
    }
}
