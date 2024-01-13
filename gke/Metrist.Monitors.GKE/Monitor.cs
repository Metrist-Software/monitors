using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Base62;
using k8s;
using k8s.Models;
using k8s.KubeConfigModels;
using Google.Apis.Auth.OAuth2;
using Metrist.Core;
using k8s.Autorest;

namespace Metrist.Monitors.GKE
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string Region { get; set; }
        public string Base64Keyfile { get; set; }
        public string ClusterServer { get; set; }
        public string ClusterCertAuthData { get; set; }
        public string Namespace {get; set; }
    }

    // Run something
    // kubectl create deployment kubernetes-bootcamp --image=gcr.io/google-samples/kubernetes-bootcamp:v1
    // Expose
    // kubectl expose deployment/kubernetes-bootcamp --type="NodePort" --port 8080
    // kubectl describe services/kubernetes-bootcamp
    // Gives a NodeJS server that will simple return "Hello Kubernetes bootcamp!" plus some data.
    //
    // Deployment example: https://stackoverflow.com/questions/56054344/how-to-create-k8s-deployment-using-kubernetes-client-in-c
    // (dotnet lib does not have a lot of examples, but others may. Download the works ;-))
    public class Monitor : BaseMonitor
    {
        private Kubernetes _client;
        private readonly MonitorConfig _config;
        private string _runId;
        private readonly static string DEFAULT_NAMESPACE = "default";

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            if (string.IsNullOrEmpty(_config.Namespace))
            {
              _config.Namespace = DEFAULT_NAMESPACE;
            }

            _client = CreateClient(config);

            Environment.SetEnvironmentVariable("HOME", "/tmp");

            _runId = makeRunId();
        }

        public void CreateDeployment(Logger logger)
        {
            var container = new V1Container
            {
                Name = "gke-monitor-test",
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
                _client.CreateNamespacedDeployment(deployment, _config.Namespace);
            }
            catch (HttpOperationException e)
            {
                logger("Rest exception, server body: " + e.Response.Content);
                throw e;
            }

            // wait for deployment to finish
            while (true)
            {
                var list = _client.ListNamespacedPod(_config.Namespace,
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
                logger("Sleeping a second before retry");
                Sleep();
            }
        }

        public void RemoveDeployment(Logger logger)
        {
            try
            {
                _client.DeleteNamespacedDeployment(RunIdName, _config.Namespace);
            }
            catch (HttpOperationException e)
            {
                logger("Rest exception, server body: " + e.Response.Content);
                throw e;
            }

            // wait for delete to finish
            while (true)
            {
                var list = _client.ListNamespacedPod(_config.Namespace,
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

        public void Cleanup(Logger logger)
        {
            logger("Running cleanup");
            var cutOff = DateTime.UtcNow.AddMinutes(-60);

            foreach (var item in _client.ListNamespacedDeployment(_config.Namespace).Items)
            {
                if (item.CreationTimestamp() == null || cutOff < item.CreationTimestamp())
                {
                    continue;
                }
                logger($"Deleting {item.Name()} deployment");
                _client.DeleteNamespacedDeployment(item.Name(), _config.Namespace);
                logger($"Successfully deleted {item.Name()} deployment");
            }

            foreach (var item in _client.ListNamespacedPod(_config.Namespace).Items)
            {
                if (item.CreationTimestamp() == null || cutOff < item.CreationTimestamp())
                {
                    continue;
                }

                logger($"Deleting {item.Name()} pod");
                _client.DeleteNamespacedPod(item.Name(), _config.Namespace);
                logger($"Successfully deleted {item.Name()} pod");
            }

            logger("Cleanup finished");
        }

        private void Sleep() => Task.Delay(1000).Wait();

        // All naming/selector stuff together so we get it right+consistent.
        private string RunIdName => $"gke-monitor-{_runId}";
        private Dictionary<string, string> RunIdLabels =>
            new Dictionary<string, string> {{ "gke-monitor-run", _runId }};
        private string RunIdSelector => $"gke-monitor-run={_runId}";

        // A UUID would work but I don't know what K8s limits are, so
        // something shorter and "good enough". Should also be a bit more readable
        // in the Google console.

        Random rnd = new Random();
        private static readonly int RUN_ID_BYTES = 4;
        private string makeRunId()
        {
            var buffer = new byte[RUN_ID_BYTES];
            var randPart = new Span<byte>(buffer, 0, RUN_ID_BYTES);
            rnd.NextBytes(randPart);

            return buffer.ToBase62().ToLower();
        }


        private static Stream StreamFromString(string s)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        private static Kubernetes CreateClient(MonitorConfig config)
        {

            var decoder = new System.Text.UTF8Encoding();
            var credsJson = decoder.GetString(Convert.FromBase64String(config.Base64Keyfile));

            // Get an access token. This is basically what kubectl does as well. Thanks
            // to locked down properties in creds, we have to supply scopes in a somewhat
            // round-about way. We can only supply the scopes through an initializer and
            // we can only parse the key with "fromstream"
            var serviceCreds = ServiceAccountCredential.FromServiceAccountData(StreamFromString(credsJson));
            var initializer = new ServiceAccountCredential.Initializer(serviceCreds.Id)
            {
                ProjectId = serviceCreds.ProjectId,
                Scopes = new List<string>
                {
                    "https://www.googleapis.com/auth/cloud-platform",
                    "https://www.googleapis.com/auth/userinfo.email"
                },
                Key = serviceCreds.Key,
                KeyId = serviceCreds.KeyId
            };
            serviceCreds = new ServiceAccountCredential(initializer);
            var token = serviceCreds.GetAccessTokenForRequestAsync().Result;

            var k8sConfig = new K8SConfiguration
            {
                ApiVersion = "v1",
                Kind = "Config",
                Clusters = new List<Cluster>
                {
                    new Cluster
                    {
                        Name = "cm-gcp-gke-monitor",
                        ClusterEndpoint = new ClusterEndpoint
                        {
                            CertificateAuthorityData = config.ClusterCertAuthData,
                            Server = config.ClusterServer
                        }
                    }
                },
                Users = new List<User>
                {
                    new User
                    {
                        Name = "gcp-gke-monitor",
                        UserCredentials = new UserCredentials
                        {
                            Token = token
                        }
                    }
                },
                Contexts = new List<Context>
                {
                    new Context
                    {
                        Name = "cm-gcp-gke-monitor-gcp-gke-monitor",
                        ContextDetails = new ContextDetails
                        {
                            Cluster = "cm-gcp-gke-monitor",
                            User = "gcp-gke-monitor"
                        }
                    }
                },
                CurrentContext = "cm-gcp-gke-monitor-gcp-gke-monitor"
            };

            return new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigObject(k8sConfig));
        }
    }
}
