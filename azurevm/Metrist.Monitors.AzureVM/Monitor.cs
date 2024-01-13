using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Metrist.Core;
using Microsoft.Azure.Management.Compute.Fluent;
using Microsoft.Azure.Management.Compute.Fluent.Models;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;

namespace Metrist.Monitors.AzureVM
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public MonitorConfig() { }
        public MonitorConfig(BaseMonitorConfig baseCfg) : base(baseCfg) { }

        //Might need this for Azure
        public string ClientID { get; set; }
        public string ClientSecret { get; set; }
        public string TenantID { get; set; }
        public string SubscriptionID { get; set; }
        public string Region { get; set; }

        public string PersistentInstanceResourceGroup { get; set; }
        public string PersistentInstanceName { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private IVirtualMachine _invokedMachine;
        private readonly Region _azureRegion;

        private readonly Dictionary<string, Region> _regionMap = new Dictionary<string, Region>
        {
            ["us-west-1"] = Region.USWest,
            ["us-west-2"] = Region.USWest2,
            ["us-east-1"] = Region.USEast,
            ["us-east-2"] = Region.USEast2,
            ["ca-central-1"] = Region.CanadaCentral
        };

        private const string VM_RG_PREFIX = "vmrg";
        private const string CREATED_AT_TAG_NAME = "createdat";
        private const string VM_PREFIX = "vm";
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
                foreach (var rg in azure.ResourceGroups.List().Where(o => o.Name.StartsWith(VM_RG_PREFIX)).ToList())
                {
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

        public double CreateInstance(Logger logger)
        {
            var azure = ConfigureSDK();

            string rgName = SdkContext.RandomResourceName(VM_RG_PREFIX, 10);
            string linuxVmName1 = SdkContext.RandomResourceName(VM_PREFIX, 10);

            var rg = azure
                .ResourceGroups
                .Define(rgName)
                .WithRegion(_azureRegion)
                .WithTag(CREATED_AT_TAG_NAME, DateTime.UtcNow.ToString("O"))
                .Create();

            var (time, invokedMachine) = Timed(() =>
            {
                var task = azure.VirtualMachines.Define(linuxVmName1)
                    .WithRegion(_azureRegion)
                    .WithExistingResourceGroup(rgName)
                    .WithNewPrimaryNetwork("10.0.0.0/28")
                    .WithPrimaryPrivateIPAddressDynamic()
                    .WithoutPrimaryPublicIPAddress()
                    .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer16_04_Lts)
                    .WithRootUsername(Guid.NewGuid().ToString())
                    .WithRootPassword(Guid.NewGuid().ToString())
                    .WithUnmanagedDisks()
                    .WithSize(VirtualMachineSizeTypes.Parse("Standard_B1s"))
                    .WithTag(CREATED_AT_TAG_NAME, DateTime.UtcNow.ToString("O"))
                    .CreateAsync();

                return task.Result;
            });

            _invokedMachine = invokedMachine;

            return time;
        }

        public double RunInstance(Logger logger)
        {
            if (_invokedMachine == null)
            {
                throw new Exception("Cannot run instance as the creation failed.");
            }
            var azure = ConfigureSDK();

            return Timed(() => _invokedMachine.StartAsync());
        }

        public double TerminateInstance(Logger logger)
        {
            if (_invokedMachine == null)
            {
                throw new Exception("Cannot terminate instance as the creation failed.");
            }
            var azure = ConfigureSDK();
            var time = Timed(() => _invokedMachine.PowerOff());
            return time;
        }

        public double DescribePersistentInstance(Logger logger)
        {
            var azure = ConfigureSDK();

            var (time, vm) = Timed(() => azure
                .VirtualMachines
                .GetByResourceGroup(_config.PersistentInstanceResourceGroup, _config.PersistentInstanceName));

            if (vm.PowerState == PowerState.Running) return time;

            throw new Exception($"Persistent instance {_config.PersistentInstanceResourceGroup}/{_config.PersistentInstanceName} is not currently running");
        }

        public void TearDown(Logger logger)
        {
            try
            {
                if (_invokedMachine != null)
                {
                    var azure = ConfigureSDK();
                    azure.ResourceGroups.BeginDeleteByName(_invokedMachine.ResourceGroupName);
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
