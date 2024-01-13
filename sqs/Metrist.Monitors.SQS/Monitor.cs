using System;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using Metrist.Core;
using Metrist.Webhooks;

namespace Metrist.Monitors.SQS
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string Region { get; set; }
        public string QueueUrl { get; set; }
        public string AwsAccessKeyId { get; set; }
        public string AwsSecretAccessKey { get; set; }        
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly string _messageBody;
        private readonly AmazonSQSClient _sqsClient;
        private readonly SQSChecker _sqsChecker;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", config.AwsAccessKeyId);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", config.AwsSecretAccessKey);            

            _messageBody = Guid.NewGuid().ToString();
            var sqsConfig = new AmazonSQSConfig();
            sqsConfig.ServiceURL = $"https://sqs.{_config.Region}.amazonaws.com";
            _sqsClient = new AmazonSQSClient(sqsConfig);
            _sqsChecker = new SQSChecker(_config.QueueUrl, sqsConfig.ServiceURL);
        }

        public double ReadMessage(Logger logger)
        {
            return _sqsChecker
                .WithLogger(logger)
                .WithMatcher(message => message.Body.Contains(_messageBody))
                .WaitForMatch();
        }

        public async Task WriteMessage(Logger logger)
        {
            var send = new SendMessageRequest
            {
                QueueUrl = _config.QueueUrl,
                MessageBody = _messageBody
            };
            await _sqsClient.SendMessageAsync(send);
        }
    }
}
