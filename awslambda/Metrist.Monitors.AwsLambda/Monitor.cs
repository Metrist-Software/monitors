using System;
using System.Threading.Tasks;
using Amazon;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Metrist.Core;
using Metrist.Webhooks;

namespace Metrist.Monitors.AwsLambda
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string TestFunctionArn { get; set; }
        public string Region { get; set; }
        public string QueueUrl { get; set; }
        public string AwsAccessKeyId { get; set; }
        public string AwsSecretAccessKey { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly string _uniqueId;
        private readonly AmazonLambdaClient _lambda;
        private readonly SQSChecker _sqsChecker;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            System.Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", config.AwsAccessKeyId);
            System.Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", config.AwsSecretAccessKey);

            _uniqueId = Guid.NewGuid().ToString();
            _lambda = new AmazonLambdaClient(region: RegionEndpoint.GetBySystemName(config.Region));
            _sqsChecker = new SQSChecker(config.QueueUrl, SQSChecker.ServiceURL(config.Region));
        }

        public async Task<double> TriggerLambdaAndWaitForResponse(Logger logger)
        {
            var ir = new InvokeRequest()
            {
                FunctionName = _config.TestFunctionArn,
                InvocationType = "Event",
                Payload = "{\"id\": \"" + _uniqueId + "\"}"
            };
            await _lambda.InvokeAsync(ir);

            return _sqsChecker
                .WithLogger(logger)
                .WithMatcher(message => message.Body.Contains(_uniqueId))
                .WaitForMatch();
        }
    }
}
