using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Amazon.Kinesis;
using Amazon.Kinesis.Model;
using Metrist.Core;

namespace Metrist.Monitors.Kinesis
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string StreamName { get; set; }
        public string Region { get; set; }
        public string AwsAccessKeyId { get; set; }
        public string AwsSecretAccessKey { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private string _uniqueId;
        private readonly MonitorConfig _config;
        private readonly AmazonKinesisClient _kinesisClient;
        private DateTime _horizonTimestamp;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", config.AwsAccessKeyId);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", config.AwsSecretAccessKey);

            AmazonKinesisConfig kcc = new AmazonKinesisConfig();
            kcc.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(config.Region);
            _kinesisClient = new AmazonKinesisClient(kcc);
            _uniqueId = Guid.NewGuid().ToString();
            _horizonTimestamp = DateTime.UtcNow.AddSeconds(-1); // just to be sure.
        }

        public async Task<double> ReadFromStream(Logger logger)
        {
            DescribeStreamRequest describeRequest = new DescribeStreamRequest();
            describeRequest.StreamName = _config.StreamName;

            DescribeStreamResponse describeResponse = await _kinesisClient.DescribeStreamAsync(describeRequest);
            List<Shard> shards = describeResponse.StreamDescription.Shards;

            Action readAttempt = () => TryRead(shards, logger).Wait();
            Func<Exception, bool> shouldRetry = ex => ex.Message.Contains("Unique ID Not Found");
            return TimedWithRetries(readAttempt, shouldRetry);
        }

        private async Task TryRead(List<Shard> shards, Logger logger)
        {
            // We have one shard, but this is probably the cleanest way to
            // get the shard id we need.
            foreach (Shard shard in shards)
            {
                GetShardIteratorRequest iteratorRequest = new GetShardIteratorRequest();
                iteratorRequest.StreamName = _config.StreamName;
                iteratorRequest.ShardId = shard.ShardId;
                iteratorRequest.ShardIteratorType = ShardIteratorType.AT_TIMESTAMP;
                iteratorRequest.Timestamp = _horizonTimestamp;

                GetShardIteratorResponse iteratorResponse = await _kinesisClient.GetShardIteratorAsync(iteratorRequest);
                string iteratorId = iteratorResponse.ShardIterator;

                while (!string.IsNullOrEmpty(iteratorId))
                {
                    GetRecordsRequest getRequest = new GetRecordsRequest();
                    getRequest.Limit = 1000;
                    getRequest.ShardIterator = iteratorId;

                    GetRecordsResponse getResponse = await _kinesisClient.GetRecordsAsync(getRequest);
                    string nextIterator = getResponse.NextShardIterator;
                    List<Record> records = getResponse.Records;

                    if (records.Count > 0)
                    {
                        logger($"Received {records.Count} records.");
                        foreach (Record record in records)
                        {
                            string message = Encoding.UTF8.GetString(record.Data.ToArray());
                            logger($"Message is '{message}', looking for '{_uniqueId}'");
                            if (message.Contains(_uniqueId))
                            {
                                return;
                            }
                        }
                    }
                    iteratorId = nextIterator;
                }
            }
            logger("Unique ID not found in messages, throwing exception to force retry");
            throw new Exception("Unique ID Not Found"); // forces a retry
        }

        public async Task WriteToStream(Logger logger)
        {
            var request = new PutRecordRequest();
            request.StreamName = _config.StreamName;
            var message = new UTF8Encoding().GetBytes(_uniqueId);
            using MemoryStream memoryStream = new MemoryStream(message);
            request.Data = memoryStream;
            request.PartitionKey = _uniqueId;
            await _kinesisClient.PutRecordAsync(request);
        }
    }
}
