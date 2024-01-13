using Amazon.S3;
using Amazon.S3.Model;
using System.Linq;
using System.Threading.Tasks;
using Metrist.Core;
using Amazon;
using System;

namespace Metrist.Monitors.S3
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string Region { get; set; }
        public string AwsAccessKeyId { get; set; }
        public string AwsSecretAccessKey { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly string _bucketPrefix;
        private readonly string _bucketName;
        private readonly string _objectName;
        private readonly RegionEndpoint _region;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", config.AwsAccessKeyId);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", config.AwsSecretAccessKey);

            // For now, we really don't need to make this settable.
            _bucketPrefix = $"metrist-mon-";
            _bucketName = _bucketPrefix + Guid.NewGuid().ToString();
            _objectName = Guid.NewGuid().ToString();
            _region = RegionEndpoint.GetBySystemName(_config.Region);
        }

        public async Task Cleanup(Logger logger)
        {
            try
            {
                using var client = new AmazonS3Client(_region);
                var responseTask = client.ListBucketsAsync(new ListBucketsRequest());
                responseTask.Wait();

                var bucketsToDelete = responseTask.Result.Buckets.Where(o => o.BucketName.StartsWith(_bucketPrefix) && (DateTime.UtcNow - o.CreationDate).TotalMinutes > 30).ToList();
                foreach (var bucket in bucketsToDelete)
                {
                    logger($"Terminating bucket with name {bucket.BucketName}.");
                    try
                    {
                        await AttemptDeleteAsync(bucket.BucketName);
                    }
                    catch (Exception ex)
                    {
                        logger($"METRIST_MONITOR_ERROR - Error deleting bucket {bucket.BucketName}. Normally this is due to eventual consistency on ListBucketsAsync. Error was: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger($"Error when trying to cleanup orphaned buckets. {ex}");
            }
        }

        public double PutBucket(Logger logger)
        {
            using var client = new AmazonS3Client(_region);
            var putRequest = new PutBucketRequest
            {
                BucketName = _bucketName,
                UseClientRegion = true
            };
            var (time, ignored) = Timed(() => client.PutBucketAsync(putRequest));
            return time;
        }

        public double DeleteBucket(Logger logger)
        {
            Action deletionAttempt = () => AttemptDeleteAsync(_bucketName).Wait();
            Func<Exception, bool> shouldRetry = (ex) =>
            {
                return ex.Message.Contains($"Please try again.") ||
                    ex.Message.Contains("The specified bucket does not exist");
            };
            return TimedWithRetries(deletionAttempt, shouldRetry);
        }

        private async Task AttemptDeleteAsync(string bucketName)
        {
            using var client = new AmazonS3Client(_region);

            // Need to use bucket's actual region for delete request
            var bucketRegion = await client.GetBucketLocationAsync(new GetBucketLocationRequest{
                BucketName = bucketName
            });

            // Delete any remaining objects in the bucket before deleting the bucket
            ListObjectsRequest listRequest = new ListObjectsRequest
            {
                BucketName = bucketName
            };

            ListObjectsResponse listResponse;
            do
            {
                // Get a list of objects
                listResponse = await client.ListObjectsAsync(listRequest);
                foreach (S3Object obj in listResponse.S3Objects)
                {
                    // Delete each object
                    await client.DeleteObjectAsync(new DeleteObjectRequest
                    {
                        BucketName = obj.BucketName,
                        Key = obj.Key
                    });
                }

                // Set the marker property
                listRequest.Marker = listResponse.NextMarker;
            } while (listResponse.IsTruncated);

            await client.DeleteBucketAsync(new DeleteBucketRequest
            {
                BucketName = bucketName,
                BucketRegion = bucketRegion.Location
            });
        }

        public double PutObject(Logger logger)
        {
            using var client = new AmazonS3Client(_region);
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = _objectName,
                ContentBody = $"Metrist Test Object : {_bucketName} : {DateTime.Now}"
            };
            var (time, ignored) = Timed(() => client.PutObjectAsync(request));
            return time;
        }

        public double GetObject(Logger logger)
        {
            using var client = new AmazonS3Client(_region);
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = _objectName
            };
            var (time, ignored) = Timed(() => client.GetObjectAsync(request));
            return time;
        }

        public double DeleteObject(Logger logger) {
            using var client = new AmazonS3Client(_region);
            var request = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = _objectName
            };
            var (time, ignored) = Timed(() => client.DeleteObjectAsync(request));
            return time;
        }
    }
}
