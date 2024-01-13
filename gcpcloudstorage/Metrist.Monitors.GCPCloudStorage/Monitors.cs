using System;
using System.IO;
using System.Text;
using Metrist.Core;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Storage.v1.Data;
using Google.Cloud.Storage.V1;

namespace Metrist.Monitors.GCPCloudStorage
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public MonitorConfig() { }
        public string PrivateKey { get; set; }
        public string ProjectId { get; set; }
        public string Region { get; set; }
        public MonitorConfig(BaseMonitorConfig src) : base(src) { }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly StorageClient _storageClient;
        private readonly string _bucketName;
        private readonly string _bucketPrefix;
        private readonly string _objectName;
        private readonly string _region;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            var saCredStream = new MemoryStream(Convert.FromBase64String(config.PrivateKey));
            _storageClient = StorageClient.Create(GoogleCredential.FromStream(saCredStream));
            _bucketPrefix = "monitor-";
            _bucketName = _bucketPrefix + Guid.NewGuid().ToString();
            _objectName = Guid.NewGuid().ToString();
            _region = config.Region ?? System.Environment.GetEnvironmentVariable("ORCHESTRATOR_REGION");
            if (_region == null)
            {
                throw new ArgumentNullException("ORCHESTRATOR_REGION must be set!");
            }
        }

        public void CreateBucket(Logger logger)
        {
            _storageClient.CreateBucket(_config.ProjectId, new Bucket()
            {
                Name = _bucketName,
                Location = _region
            });
        }

        public void UploadObject(Logger logger)
        {
            _storageClient.UploadObject(_bucketName, _objectName, "text/html", new MemoryStream(Encoding.UTF8.GetBytes("Hello world")));
        }

        public void GetObject(Logger logger)
        {
            _storageClient.GetObject(_bucketName, _objectName);
        }

        public void DeleteObject(Logger logger)
        {
            _storageClient.DeleteObject(_bucketName, _objectName);
        }

        public void DeleteBucket(Logger logger)
        {
            _storageClient.DeleteBucket(_bucketName);
        }

        public void Cleanup(Logger logger)
        {
            var cutOff = DateTime.Now.Subtract(new System.TimeSpan(0, 1, 0, 0));
            var maxDeletions = 5;
            var deletionCount = 0;
            logger($"Cleanup: cleaning up all buckets created before {cutOff}");
            try
            {
                var buckets = _storageClient.ListBuckets(_config.ProjectId);
                foreach (var bucket in buckets)
                {
                    if(!bucket.Name.StartsWith(_bucketPrefix)) {
                        continue;
                    }

                    logger($"Cleanup, have bucket: {bucket.Name}, created {bucket.TimeCreated}");
                    if (bucket.TimeCreated < cutOff)
                    {
                        logger("Too old, deleting");
                        _storageClient.DeleteBucket(bucket.Name, new DeleteBucketOptions()
                        {
                           DeleteObjects = true
                        });
                        deletionCount++;
                    }

                    // Bucket deletions can be expensive, so if we have a large catch-up to do let's spread the pain a bit.
                    if (deletionCount >= maxDeletions)
                    {
                        logger($"Only deleting {maxDeletions} buckets in one cleanup round, ending cleanup run");
                        return;
                    }
                }

            }
            catch (Exception e)
            {
                logger($"Got exception {e.Message} during cleanup, ending cleanup run.");
            }
        }
    }
}
