using System;
using System.Data;
using System.Linq;
using System.Data.SqlClient;
using System.Collections.Generic;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.Sql.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Metrist.Core;

namespace Metrist.Monitors.AzureSQL
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

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly Region _azureRegion;

        private ISqlServer _sqlServer;
        private ISqlDatabase _sqlDb;
        private SqlConnection _conn;

        private readonly Dictionary<string, Region> _regionMap = new Dictionary<string, Region>
        {
            ["us-west-1"] = Region.USWest,
            ["us-west-2"] = Region.USWest2,
            ["us-east-1"] = Region.USEast,
            ["us-east-2"] = Region.USEast2,
            ["ca-central-1"] = Region.CanadaCentral
        };

        private const string DB_SERVER_PREFIX = "azuresqlmonitorserver";
        private const string DB_NAME_PREFIX = "azuresqlmonitordb";
        private const string DB_RG_PREFIX = "azuresqlmonitorrg";
        private const string CREATED_AT_TAG_NAME = "createdat";
        private const string DB_USERNAME = "username";
        private const string DB_PASSWORD = "password123#@!";

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

        public double CreateSqlServer(Logger logger)
        {
            var azure = ConfigureSDK();

            var serverName = SdkContext.RandomResourceName(DB_SERVER_PREFIX, 10);
            var rgName = SdkContext.RandomResourceName(DB_RG_PREFIX, 10);

            var rg = azure
                .ResourceGroups
                .Define(rgName)
                .WithRegion(_azureRegion)
                .WithTag(CREATED_AT_TAG_NAME, DateTime.UtcNow.ToString("O"))
                .Create();

            logger($"Resource group {rgName} created");

            var (time, sqlServer) = Timed(() => azure.SqlServers
                .Define(serverName)
                .WithRegion(_azureRegion)
                .WithExistingResourceGroup(rgName)
                .WithAdministratorLogin(DB_USERNAME)
                .WithAdministratorPassword(DB_PASSWORD)
                .WithNewFirewallRule("0.0.0.0", "255.255.255.255")
                .Create()
            );

            logger($"Server ${serverName} created");

            _sqlServer = sqlServer;

            return time;
        }

        public void CreateDatabase(Logger logger)
        {
            if (_sqlServer == null) throw new Exception("Server instance not set");

            var dbName = SdkContext.RandomResourceName(DB_NAME_PREFIX, 10);

            _sqlDb = _sqlServer.Databases
                .Define(dbName)
                .WithBasicEdition(SqlDatabaseBasicStorage.Max1Gb)
                .Create();
        }

        public double CreateTable(Logger logger)
        {
            EnsureConnection();

            using var cmd = new SqlCommand("CREATE TABLE monitor_data (id int NOT NULL, name varchar(20) NOT NULL)", _conn);
            var (time, _) = Timed(() => cmd.ExecuteNonQuery());

            return time;
        }

        public double InsertItem(Logger logger)
        {
            EnsureConnection();

            using var cmd = new SqlCommand("INSERT INTO monitor_data (id, name) VALUES (1, 'data')", _conn);

            var (time, _) = Timed(() => cmd.ExecuteNonQuery());

            return time;
        }

        public double GetItem(Logger logger)
        {
            EnsureConnection();

            using var cmd = new SqlCommand("SELECT * FROM monitor_data WHERE id = 1", _conn);

            var time = Timed(() =>
            {
                var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    var name = reader.GetValue(1);
                    logger($"Got document with name {name}");
                }
                reader.Close();
            });

            return time;
        }

        public double DeleteItem(Logger logger)
        {
            EnsureConnection();

            using var cmd = new SqlCommand("DELETE FROM monitor_data WHERE id = 1", _conn);

            var (time, _) = Timed(() => cmd.ExecuteNonQuery());

            return time;
        }

        public void DeleteDatabase(Logger logger)
        {
            _sqlServer.Databases.Delete(_sqlDb.Name);
        }

        public double DeleteServer(Logger logger)
        {
            var azure = ConfigureSDK();

            var time = Timed(() => azure.SqlServers.DeleteById(_sqlServer.Id));

            azure.ResourceGroups.BeginDeleteByName(_sqlServer.ResourceGroupName);

            return time;
        }

        public void TearDown(Logger logger)
        {
            try
            {
                _conn?.Dispose();
                if (_sqlServer != null)
                {
                    logger("Server still exists in teardown. Manually deleting resource group");
                    var azure = ConfigureSDK();
                    azure.ResourceGroups.BeginDeleteByName(_sqlServer.ResourceGroupName);
                }
            }
            catch (Exception ex)
            {
                logger($"METRIST_MONITOR_ERROR - Error when doing final cleanup for monitor run. {ex}");
            }
        }

        public void Cleanup(Logger logger)
        {
            logger("Running cleanup");
            try
            {
                var azure = ConfigureSDK();

                foreach (var rg in azure.ResourceGroups.List().Where(o => o.Name.StartsWith(DB_RG_PREFIX)).ToList())
                {
                    if (rg.Tags == null) continue;
                    logger($"Cleanup up resource group {rg.Name}");

                    var createdAt = rg.Tags.GetValueOrDefault(CREATED_AT_TAG_NAME);
                    if (createdAt == null || (DateTime.UtcNow - DateTime.Parse(createdAt)).TotalMinutes > 30)
                    {
                        azure.ResourceGroups.BeginDeleteByName(rg.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                logger($"METRIST_MONITOR_ERROR - Error when trying to cleanup orphaned instances. {ex}");
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

            return Azure
                .Configure()
                .Authenticate(credentials)
                .WithSubscription(_config.SubscriptionID);
        }

        private void EnsureConnection()
        {
            if (_conn != null) return;

            var connectionString = $"Server=tcp:{_sqlServer.Name}.database.windows.net;Database={_sqlDb.Name};User ID={DB_USERNAME}@{_sqlServer.Name};Password={DB_PASSWORD};Trusted_Connection=False;Encrypt=True;";

            _conn = new SqlConnection(connectionString);
            _conn.Open();
        }
    }
}
