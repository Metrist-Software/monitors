using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Metrist.Core;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.CloudFront;
using Amazon.Runtime;

namespace Metrist.Monitors.AWSCloudfront
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public MonitorConfig() { }
        public MonitorConfig(BaseMonitorConfig baseCfg) : base(baseCfg) { }
        public string DistributionId { get; set; }
        public string DistributionDomainName { get; set; }
        public string BucketName { get; set; }
        public string AWSAccessKeyID { get; set; }
        public string AWSSecretAccessKey { get; set; }
        public string AWSRegion { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly AmazonS3Client _s3Client;
        private readonly AmazonCloudFrontClient _cfClient;
        private readonly HttpClient _httpClient;
        private readonly String _workingS3Key;

        private const String NewFileContent = "Hello World";
        private const String UpdatedFileContent = "Hello World (Updated)";

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(_config.AWSAccessKeyID, _config.AWSSecretAccessKey);
            var region = Amazon.RegionEndpoint.GetBySystemName(_config.AWSRegion);
            _s3Client = new AmazonS3Client(awsCredentials, region);
            _cfClient = new AmazonCloudFrontClient(awsCredentials, region);
            _workingS3Key = $"content/mutable/do-not-use-{Guid.NewGuid().ToString()}.txt";
            _httpClient = new HttpClient() { BaseAddress = new System.Uri($"https://{_config.DistributionDomainName}") };
        }


        public async Task GetCachedFile(Logger logger)
        {
            var response = await _httpClient.GetAsync("content/immutable/cached.txt");
            response.EnsureSuccessStatusCode();
        }


        public async Task PublishFile(Logger logger)
        {
            PutObjectRequest request = new Amazon.S3.Model.PutObjectRequest()
            {
                BucketName = _config.BucketName,
                Key = _workingS3Key,
                ContentBody = NewFileContent,
            };
            // CF caches the origin header. By setting this to 1 day we can validate that the
            //  cached file is purged when we call the invalidation command & getting the new version of the file
            request.Headers["Cache-control"] = "max-age=86400";
            EnsureAWSResponseCode(await _s3Client.PutObjectAsync(request));

        }

        public double GetNewFile(Logger logger)
        {
            return TimedWithRetries(async () =>
            {
                var response = await _httpClient.GetAsync(_workingS3Key);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
                var content = await response.Content.ReadAsStringAsync();
                throw new SimpleHttpResponseException(response.StatusCode, content);
            },
            (Exception ex) =>
            {
                return ex is SimpleHttpResponseException && ((SimpleHttpResponseException)ex).StatusCode == HttpStatusCode.NotFound;
            },
            logger);
        }

        public async Task UpdateFile(Logger logger)
        {
            EnsureAWSResponseCode(await _s3Client.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest()
            {
                BucketName = _config.BucketName,
                Key = _workingS3Key,
                ContentBody = UpdatedFileContent
            }));
        }

        public async Task PurgeFile(Logger logger)
        {
            EnsureAWSResponseCode(await _cfClient.CreateInvalidationAsync(new Amazon.CloudFront.Model.CreateInvalidationRequest()
            {
                DistributionId = _config.DistributionId,
                InvalidationBatch = new Amazon.CloudFront.Model.InvalidationBatch()
                {
                    CallerReference = Guid.NewGuid().ToString(),
                    Paths = new Amazon.CloudFront.Model.Paths()
                    {
                        Items = new List<string>() { $"/{_workingS3Key}" },
                        Quantity = 1
                    }
                }
            }));
        }

        public double GetUpdatedFile(Logger logger)
        {
            return TimedWithRetries(async () =>
            {
                var response = await _httpClient.GetAsync(_workingS3Key);
                var content = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode && content == UpdatedFileContent)
                {
                    return;
                }
                throw new SimpleHttpResponseException(response.StatusCode, content);
            },
            (Exception ex) =>
            {
                if (ex is SimpleHttpResponseException)
                {
                    var responseException = ((SimpleHttpResponseException)ex);
                    // Retry if the resource is not found
                    //  or the content inside it is not the updated one
                    return responseException.StatusCode == HttpStatusCode.NotFound || responseException.Message != UpdatedFileContent;
                }

                return false;
            },
            logger);
        }

        public async Task DeleteFile(Logger logger)
        {
            var deleteObjectRequest = new DeleteObjectRequest
            {
                BucketName = _config.BucketName,
                Key = _workingS3Key
            };

            EnsureAWSResponseCode(await _s3Client.DeleteObjectAsync(deleteObjectRequest));
        }

        public double WaitForDeletionPropagation(Logger logger)
        {
            var tryAction = async () =>
            {
                var response = await _httpClient.GetAsync(_workingS3Key);
                if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return;
                }
                logger($"Object {_workingS3Key} not yet deleted, throwing error to force retry");
                var content = await response.Content.ReadAsStringAsync();
                throw new SimpleHttpResponseException(response.StatusCode, content);
            };
            var shouldRetry = (Exception ex) =>
            {
                // We retry if the object is still there.
                return ex is SimpleHttpResponseException && ((SimpleHttpResponseException)ex).StatusCode == HttpStatusCode.OK;
            };

            return TimedWithRetries(tryAction, shouldRetry, logger);
        }

        public async Task Cleanup(Logger logger)
        {
            var cutOff = DateTime.Now.Subtract(new System.TimeSpan(0, 1, 0, 0));
            logger($"Starting cleanup of old S3 objects in bucket {_config.BucketName}, cutoff time is {cutOff}");
            try
            {
                var response = await _s3Client.ListObjectsAsync(_config.BucketName, "content/mutable");
                logger($"Bucket contains {response.S3Objects.Count} entries (1000 max)");
                foreach (var s3Object in response.S3Objects)
                {
                    if (s3Object.LastModified < cutOff)
                    {
                        logger($"Deleting object with key {s3Object.Key}");
                        await _s3Client.DeleteObjectAsync(s3Object.BucketName, s3Object.Key);
                    }
                    else
                    {
                        logger($"Object with key {s3Object.Key} too young, not deleting");
                    }

                }
            }
            catch (Exception ex)
            {
                logger($"Error during cleanup, giving up. Exception: {ex}");
            }
        }

        private void EnsureAWSResponseCode(AmazonWebServiceResponse response)
        {
            var intCode = (int)response.HttpStatusCode;
            if (!(intCode >= 200 && intCode <=299))
            {
                throw new SimpleHttpResponseException(response.HttpStatusCode, $"AWS Status code was {response.HttpStatusCode}({(int)response.HttpStatusCode}");
            }
        }
    }

    public class SimpleHttpResponseException : Exception
    {
        public HttpStatusCode StatusCode { get; private set; }

        public SimpleHttpResponseException(HttpStatusCode statusCode, string content) : base(content)
        {
            StatusCode = statusCode;
        }
    }
}
