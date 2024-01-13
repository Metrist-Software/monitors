using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using Metrist.Core;

namespace Metrist.Webhooks
{
    /**
     *  This is a helper class that supports steps in monitors that want to poll SQS for
     *  new messages to (usually) check whether a webhook call successfully originated
     *  from the monitored system. It assumes that this is running in an environment that
     *  has things setup so that the .NET AWS SDK has rights to talk to SQS.
     */
    public class SQSChecker
    {
        private readonly string _queueUrl;
        private readonly AmazonSQSClient _sqsClient;
        private Func<Message, bool> _matcher;
        private Logger _logger;

        public SQSChecker(string queueUrl, string serviceUrl)
        {
            var sqsConfig = new AmazonSQSConfig();
            sqsConfig.ServiceURL = serviceUrl;
            _sqsClient = new AmazonSQSClient(sqsConfig);
            _queueUrl = queueUrl;

            // Hopefully sensible defaults :)
            _matcher = (_) => true;
            _logger = s => Console.WriteLine(s);
        }
        public SQSChecker() : this(Environment.GetEnvironmentVariable("ORCHESTRATOR_REGION")) {}

        public SQSChecker(string region) : this(Environment.GetEnvironmentVariable("QUEUE_URL"), ServiceURL(region)) { }

        public static string ServiceURL(string region)
        {
            return $"https://sqs.{region}.amazonaws.com";
        }

        public SQSChecker WithMatcher(Func<Message, bool> matcher)
        {
            _matcher = matcher;
            return this;
        }
        public SQSChecker WithLogger(Logger logger)
        {
            _logger = logger;
            return this;
        }

        /**
         *  Wait until the matching function returns true, retrying all the time until the set
         *  elapsed time has expired. It will return the total time taken, in milliseconds.
         */
        public double WaitForMatch()
        {
            var task = StartTask();
            task.Wait();
            return task.Result;
        }

        public double WaitForDrainAfterAtLeastOneMessage()
        {
            var task = StartDrainTask();
            task.Wait();
            return task.Result;
        }

        private async Task<double> StartTask()
        {
            var watch = Stopwatch.StartNew();
            var isFound = false;
            while (!isFound)
            {
                var receiveMessageRequest = new ReceiveMessageRequest
                {
                    QueueUrl = _queueUrl
                };
                var receiveMessageResponse = await _sqsClient.ReceiveMessageAsync(receiveMessageRequest);
                foreach (var message in receiveMessageResponse.Messages)
                {
                    _logger($"Webhook SQS message: {message.Body}");
                    var deleteMessageRequest = new DeleteMessageRequest
                    {
                        QueueUrl = _queueUrl,
                        ReceiptHandle = message.ReceiptHandle
                    };
                    await _sqsClient.DeleteMessageAsync(deleteMessageRequest);
                    isFound = _matcher(message);
                }
                await Task.Delay(5);
            }
            return watch.ElapsedMilliseconds;
        }

        private async Task<double> StartDrainTask()
        {
            var watch = Stopwatch.StartNew();
            var receiveMessageRequest = new ReceiveMessageRequest
            {
                QueueUrl = _queueUrl
            };

            var hasReadAtLeastOneMessage = false;
            while (true)
            {
                var receiveMessageResponse = await _sqsClient.ReceiveMessageAsync(receiveMessageRequest);
                foreach (var message in receiveMessageResponse.Messages)
                {
                    hasReadAtLeastOneMessage = true;
                    _logger($"Webhook SQS message: {message.Body}");
                    var deleteMessageRequest = new DeleteMessageRequest
                    {
                        QueueUrl = _queueUrl,
                        ReceiptHandle = message.ReceiptHandle
                    };
                    await _sqsClient.DeleteMessageAsync(deleteMessageRequest);
                }

                if (receiveMessageResponse.Messages.Count == 0 &&
                    hasReadAtLeastOneMessage)
                {
                    break;
                }
            }
            return watch.ElapsedMilliseconds;
        }
    }
}
