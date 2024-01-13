using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Metrist.Core;
using k8s;
using k8s.Models;
using Microsoft.Azure.Management.ContainerService.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;

namespace Metrist.Monitors.AzureAKS
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
        private IKubernetesCluster _invokedResource;
        private readonly Region _azureRegion;
        private Kubernetes _kubernetesClient;
        private string _runId;
        private readonly static string NAMESPACE = "default";
        private const string RG_PREFIX = "k8rg";
        private const string CREATED_AT_TAG_NAME = "createdat";
        private const string RESOURCE_PREFIX = "k8";
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
            _runId = Guid.NewGuid().ToString().ToLower();
        }

        public void Cleanup(Logger logger)
        {
            try
            {
                var azure = ConfigureAzureSDK();
                foreach (var rg in azure.ResourceGroups.List().Where(o => o.Name.StartsWith(RG_PREFIX)).ToList())
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
                logger($"METRIST_MONITOR_ERROR - Error when trying to cleanup orphaned AKS Clusters. {ex}");
            }
        }

        public double CreateCluster(Logger logger)
        {
            logger("Starting aks cluster creation.");

            var azure = ConfigureAzureSDK();

            string rgName = SdkContext.RandomResourceName(RG_PREFIX, 10);
            string resourceName = SdkContext.RandomResourceName(RESOURCE_PREFIX, 10);
            var keygen = new SshKeyGenerator.SshKeyGenerator(2048);
            var publicKey = keygen.ToRfcPublicKey("canmonuser@gmail.com");


            var rg = azure
                .ResourceGroups
                .Define(rgName)
                .WithRegion(_azureRegion)
                .WithTag(CREATED_AT_TAG_NAME, DateTime.UtcNow.ToString("O"))
                .Create();

            var (time, kubernetesCluster) =
            Timed(() =>
                {
                    var task = azure.KubernetesClusters.Define(resourceName)
                        .WithRegion(_azureRegion)
                        .WithExistingResourceGroup(rgName)
                        .WithLatestVersion()
                        .WithRootUsername("canaryuser")
                        .WithSshKey(publicKey)
                        .WithServicePrincipalClientId(_config.ClientID)
                        .WithServicePrincipalSecret(_config.ClientSecret)
                        .DefineAgentPool("ap")
                            .WithVirtualMachineSize(Microsoft.Azure.Management.ContainerService.Fluent.Models.ContainerServiceVMSizeTypes.StandardA2V2)
                            .WithAgentPoolVirtualMachineCount(1)
                            .WithAgentPoolModeName("System")
                            .Attach()
                        .WithDnsPrefix("azurek8-ap")
                        .WithTag(CREATED_AT_TAG_NAME, DateTime.UtcNow.ToString("O"))
                        .CreateAsync();

                    return task.Result;
                }
            );

            _invokedResource = kubernetesCluster;

            logger($"Cluster creation complete. {_invokedResource.ResourceGroupName}:{_invokedResource.Name}");
            return time;
        }

        public async Task UpdateCluster(Logger logger)
        {
            if (_invokedResource == null)
            {
                throw new Exception("Cannot update cluster as the creation failed.");
            }

            await _invokedResource.Update()
                .WithAgentPoolVirtualMachineCount(2)
                .ApplyAsync();
        }

        public void CreateDeployment(Logger logger)
        {
            if (_invokedResource == null)
            {
                throw new Exception("Cannot create deployment as the creation failed.");
            }

            using MemoryStream ms = new MemoryStream(_invokedResource.AdminKubeConfigContent);
            _kubernetesClient = new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigFile(ms));

            var container = new V1Container
            {
                Name = "azurek8-monitor-test",
                Image = "nginx",
                ImagePullPolicy = "Always",
                Ports = new List<V1ContainerPort>
                {
                    new V1ContainerPort(containerPort: 80)
                },
                Resources = new V1ResourceRequirements
                {
                    Limits = new Dictionary<string,ResourceQuantity>
                    {
                        {"cpu", new ResourceQuantity("100m")},
                        {"memory", new ResourceQuantity("128Mi")}
                    },
                    Requests = new Dictionary<string,ResourceQuantity>
                    {
                        {"cpu", new ResourceQuantity("50m")},
                        {"memory", new ResourceQuantity("64Mi")}
                    }
                },
                LivenessProbe = new V1Probe
                {
                    HttpGet = new V1HTTPGetAction
                    {
                        Port = 80,
                        Path = "/"
                    },
                    InitialDelaySeconds = 1,
                    PeriodSeconds = 1
                }
            };
            var template = new V1PodTemplateSpec
            {
                Metadata = new V1ObjectMeta(labels: RunIdLabels),
                Spec = new V1PodSpec(new List<V1Container> {container})
            };
            var spec = new V1DeploymentSpec
            {
                Replicas = 1,
                Template = template,
                Selector = new V1LabelSelector(matchLabels: RunIdLabels)
            };
            var deployment = new V1Deployment
            {
                ApiVersion = "apps/v1",
                Kind = "Deployment",
                Metadata = new V1ObjectMeta(name: RunIdName, labels: RunIdLabels),
                Spec = spec
            };
            try
            {
                _kubernetesClient.CreateNamespacedDeployment(deployment, NAMESPACE);
            }
            catch (Microsoft.Rest.HttpOperationException e)
            {
                Console.WriteLine("Rest exception, server body: " + e.Response.Content);
                throw e;
            }

            // wait for deployment to finish
            while (true)
            {
                var list = _kubernetesClient.ListNamespacedPod(NAMESPACE,
                                                     labelSelector: RunIdSelector);
                var allHealthy = true;
                logger($"Checking {list.Items.Count} pods...");
                foreach (var pod in list.Items)
                {
                    if (pod.Status.Phase != "Running")
                    {
                        logger($"Pod phase is {pod.Status.Phase}, not done yet.");
                        allHealthy = false;
                    }
                }
                if (list.Items.Count > 0 && allHealthy)
                {
                    logger($"Pod healthy, deployment done");
                    return;
                }
                logger("Sleeping 100ms before retry");
                Sleep();
            }
        }

        public void RemoveDeployment(Logger logger)
        {
            if (_invokedResource == null)
            {
                throw new Exception("Cannot remove deployment as the creation failed.");
            }

            try
            {
                _kubernetesClient.DeleteNamespacedDeployment(RunIdName, NAMESPACE);
            }
            catch (Microsoft.Rest.HttpOperationException e)
            {
                Console.WriteLine("Rest exception, server body: " + e.Response.Content);
                throw e;
            }

            // wait for delete to finish
            while (true)
            {
                var list = _kubernetesClient.ListNamespacedPod(NAMESPACE,
                                                     labelSelector: RunIdSelector);
                var allGone = true;
                logger($"Checking {list.Items.Count} pods...");
                foreach (var pod in list.Items)
                {
                    if (pod.Status.Phase != "Succeeded")
                    {
                        logger($"Pod phase is {pod.Status.Phase}, not done yet.");
                        allGone = false;
                    }
                }
                if (list.Items.Count == 0 || allGone)
                {
                    logger($"Deployment delete completed");
                    return;
                }
                logger("Sleeping a second before retry");
                Sleep();
            }
        }

        private IAzure ConfigureAzureSDK()
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

        public void TearDown(Logger logger)
        {
            try
            {
                if (_invokedResource != null)
                {
                    var azure = ConfigureAzureSDK();
                    azure.ResourceGroups.BeginDeleteByName(_invokedResource.ResourceGroupName);
                }
            }
            catch (Exception ex)
            {
                logger($"METRIST_MONITOR_ERROR - Error when doing final cleanup for monitor run. {ex}");
            }
        }

        private void Sleep() => Task.Delay(100).Wait();

        private string RunIdName => $"azurek8-monitor-{_runId}";
        private Dictionary<string, string> RunIdLabels =>
            new Dictionary<string, string> {{ "azurek8-monitor-run", _runId }};
        private string RunIdSelector => $"azurek8-monitor-run={_runId}";
    }
}
