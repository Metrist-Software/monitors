using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.Internal;
using Amazon.Runtime.Internal.Auth;
using Amazon.Runtime.Internal.Util;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Amazon.SecurityToken.Model.Internal.MarshallTransformations;
using Amazon.Util;
using Metrist.Core;
using k8s;
using k8s.Autorest;
using k8s.KubeConfigModels;
using k8s.Models;
using Logger = Metrist.Core.Logger;

namespace Metrist.Monitors.AWSEKS
{

    public class MonitorConfig : BaseMonitorConfig
    {
        public MonitorConfig() { }
        public string AWSAccessKeyID { get; set; }
        public string AWSSecretAccessKey { get; set; }
        public string AWSRegion { get; set; }
        public string ClusterName { get; set; }
        public string ClusterServerAddress { get; set; }
        public string ClusterCertificateAuthorityData { get; set; }
        public MonitorConfig(BaseMonitorConfig baseCfg) : base(baseCfg) { }
    }

    public class Monitor : BaseMonitor
    {

        private const string STSServiceName = "sts";
        private const string HTTPGet = "GET";
        private const string HTTPS = "https";
        private const string K8sHeader = "x-k8s-aws-id";
        private const string NAMESPACE = "default";

        private readonly MonitorConfig _config;
        private readonly Kubernetes _kubernetes;
        private readonly string _runId;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            _runId = Guid.NewGuid().ToString().ToLower();
            _kubernetes = new Kubernetes(buildClientConfig());
        }

        public void CreateDeployment(Logger logger)
        {
            var container = new V1Container
            {
                Name = "awseks-monitor-test",
                Image = "nginx",
                ImagePullPolicy = "Always",
                Ports = new List<V1ContainerPort>
                {
                    new V1ContainerPort(containerPort: 80)
                },
                Resources = new V1ResourceRequirements
                {
                    Limits = new Dictionary<string, ResourceQuantity>
                    {
                        {"cpu", new ResourceQuantity("100m")},
                        {"memory", new ResourceQuantity("128Mi")}
                    },
                    Requests = new Dictionary<string, ResourceQuantity>
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
                Spec = new V1PodSpec(new List<V1Container> { container })
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

            _kubernetes.CreateNamespacedDeployment(deployment, NAMESPACE);

            // wait for deployment to finish
            while (true)
            {
                var list = _kubernetes.ListNamespacedPod(NAMESPACE, labelSelector: RunIdSelector);
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

        public void Cleanup(Logger logger)
        {
            logger("Running cleanup");
            var cutOff = DateTime.UtcNow.AddMinutes(-60);

            foreach (var item in _kubernetes.ListNamespacedDeployment(NAMESPACE).Items)
            {
                if (item.CreationTimestamp() == null || cutOff < item.CreationTimestamp())
                {
                    continue;
                }
                logger($"Deleting {item.Name()} deployment");
                _kubernetes.DeleteNamespacedDeployment(item.Name(), NAMESPACE);
                logger($"Successfully deleted {item.Name()} deployment");
            }

            foreach (var item in _kubernetes.ListNamespacedPod(NAMESPACE).Items)
            {
                if (item.CreationTimestamp() == null || cutOff < item.CreationTimestamp())
                {
                    continue;
                }

                logger($"Deleting {item.Name()} pod");
                _kubernetes.DeleteNamespacedPod(item.Name(), NAMESPACE);
                logger($"Successfully deleted {item.Name()} pod");
            }

            logger("Cleanup finished");
        }

        public void RemoveDeployment(Logger logger)
        {
            try
            {
                _kubernetes.DeleteNamespacedDeployment(RunIdName, NAMESPACE);
            }
            catch (HttpOperationException e)
            {
                Console.WriteLine("Rest exception, server body: " + e.Response.Content);
                throw e;
            }

            // wait for delete to finish
            while (true)
            {
                var list = _kubernetes.ListNamespacedPod(NAMESPACE,
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


        private KubernetesClientConfiguration buildClientConfig()
        {
            var config = KubernetesClientConfiguration.BuildConfigFromConfigObject(new K8SConfiguration()
            {
                Kind = "Config",
                ApiVersion = "v1",
                CurrentContext = "aws",
                Users = new List<User>() {
                    new User() {
                        Name = "aws",
                        UserCredentials = new UserCredentials() {
                            Token = generateToken()
                        }
                    }
                },
                Clusters = new List<Cluster> {
                    new Cluster() {
                        Name = "kubernetes",
                        ClusterEndpoint = new ClusterEndpoint() {
                           Server = _config.ClusterServerAddress,
                           CertificateAuthorityData =  _config.ClusterCertificateAuthorityData
                        },
                    }
                },
                Contexts = new List<Context>() {
                    new Context() {
                        Name = "aws",
                        ContextDetails = new ContextDetails() {
                            Cluster = "kubernetes",
                            User = "aws"
                        }
                    }
                }
            });
            config.SkipTlsVerify = true;
            return config;
        }

        // Creates a bearer token being used by the kubernetes client. See https://github.com/kubernetes-sigs/aws-iam-authenticator#api-authorization-from-outside-a-cluster
        // To generate token we create a `GetCallerIdentityRequest` and sign it with the aws signer
        private string generateToken()
        {
            IRequest request = GetCallerIdentityRequestMarshaller.Instance.Marshall(new GetCallerIdentityRequest());
            request.UseQueryString = true;
            request.HttpMethod = HTTPGet;
            request.OverrideSigningServiceName = STSServiceName;
            request.Parameters.Add(HeaderKeys.XAmzExpires, "60");
            request.Headers.Add(K8sHeader, _config.ClusterName);
            #pragma warning disable CS0618 // the endpoint methods are apparently obsoleted but no clue/pointer/docs to the new official way.
            request.Endpoint = new UriBuilder(HTTPS, RegionEndpoint.GetBySystemName(_config.AWSRegion).GetEndpointForService(STSServiceName).Hostname).Uri;

            var stsConfig = new AmazonSecurityTokenServiceConfig
            {
                AuthenticationRegion = _config.AWSRegion,
            };

            AWS4SigningResult signingResult = new AWS4PreSignedUrlSigner().SignRequest(request, stsConfig, new RequestMetrics(), _config.AWSAccessKeyID, _config.AWSSecretAccessKey);

            // We could've used `signingResult.ForQueryParameters` here but we need to URLEncode some parts of the query param.
            //  we can use `ForQueryParameters` when this issue is closed https://github.com/aws/aws-sdk-net/issues/1953
            var authParams = new StringBuilder()
                    .AppendFormat("{0}={1}", HeaderKeys.XAmzAlgorithm, AWS4Signer.AWS4AlgorithmTag)
                    .AppendFormat("&{0}={1}", HeaderKeys.XAmzCredential,
                            AWSSDKUtils.UrlEncode(string.Format(CultureInfo.InvariantCulture, "{0}/{1}", signingResult.AccessKeyId, signingResult.Scope),
                            false))
                    .AppendFormat("&{0}={1}", HeaderKeys.XAmzDateHeader, signingResult.ISO8601DateTime)
                    .AppendFormat("&{0}={1}", HeaderKeys.XAmzSignedHeadersHeader, AWSSDKUtils.UrlEncode(signingResult.SignedHeaders, false))
                    .AppendFormat("&{0}={1}", HeaderKeys.XAmzSignature, signingResult.Signature)
                    .ToString();

            var url = AmazonServiceClient.ComposeUrl(request).AbsoluteUri + "&" + authParams;
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(url)).Replace("=", "");
            return $"k8s-aws-v1.{token}";
        }

        // All naming/selector stuff together so we get it right+consistent.
        private string RunIdName => $"eks-monitor-{_runId}";
        private Dictionary<string, string> RunIdLabels =>
            new Dictionary<string, string> { { "eks-monitor-run", _runId } };
        private string RunIdSelector => $"eks-monitor-run={_runId}";

        private void Sleep() => Task.Delay(100).Wait();
    }
}
