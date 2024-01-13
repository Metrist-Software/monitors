using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Metrist.Core;

namespace Metrist.Monitors.EC2
{
    public class MonitorConfig : BaseMonitorConfig
    {
       public string AmiID { get; set; }
       public string PersistentInstanceId { get; set; }
       public string Region { get; set; }
       public string AwsAccessKeyId { get; set; }
       public string AwsSecretAccessKey { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly RegionEndpoint _region;
        private List<string> _instanceIds;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            _region = RegionEndpoint.GetBySystemName(_config.Region);
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", config.AwsAccessKeyId);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", config.AwsSecretAccessKey);
        }

        public void Cleanup(Logger logger)
        {
            try
            {
                using var client = new AmazonEC2Client(_region);
                var responseTask = client.DescribeInstancesAsync(new DescribeInstancesRequest
                {
                    Filters = new List<Filter>()
                    {
                        new Filter
                        {
                            Name = "image-id",
                            Values = new List<string>() { _config.AmiID }
                        }
                    }
                });

                List<string> instanceIds = new List<string>();

                var runningInstances =
                    responseTask.Result.Reservations
                    .SelectMany(x => x.Instances)
                    .Where(
                      y => y.State.Name == InstanceStateName.Running
                      && (DateTime.UtcNow - y.LaunchTime).TotalMinutes > 2
                      && !y.Tags.Any(t => t.Key.ToLower() == "persistent")
                      && y.Tags.Any(t => t.Key.ToLower() == "createdby" && t.Value.ToLower() == "metrist")
                    )
                    .Select(o => o.InstanceId)
                    .ToList();

                if (runningInstances.Count > 0)
                {
                    foreach (var ri in runningInstances)
                    {
                        logger($"Terminating orphaned instance with id {ri}.");
                    }
                    AttemptTerminate(runningInstances);
                }
            } catch (Exception ex)
            {
                logger($"Error when trying to cleanup orphaned instances. {ex}");
            }
        }

        public double RunInstance(Logger logger)
        {
            using var client = new AmazonEC2Client(_region);

            var runRequest = new RunInstancesRequest
            {
                ImageId = _config.AmiID,
                InstanceType = InstanceType.T2Nano,
                MinCount = 1,
                MaxCount = 1,
                TagSpecifications = new List<TagSpecification> {
                  new TagSpecification {
                    ResourceType = ResourceType.Instance,
                    Tags = new List<Tag> {
                      new Tag("createdby", "metrist")
                    }
                  }
                }
            };

            var (time, runResponse) = Timed(() => client.RunInstancesAsync(runRequest));

            _instanceIds = runResponse.Reservation.Instances.Select(i => i.InstanceId).ToList();

            return time;
        }

        private static Regex instanceNotFound = new Regex("The instance ID 'i-[0-9a-f]+' does not exist",
                                                          RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public double TerminateInstance(Logger logger)
        {
            Action terminationAtempt = () => AttemptTerminate(_instanceIds);
            Func<Exception, bool> shouldRetry = (ex) => instanceNotFound.IsMatch(ex.Message);

            // Too often, we hit this before AWS had propagated the instance name. No rush, just
            // wait a bit.
            Thread.Sleep(5000);

            return TimedWithRetries(terminationAtempt, shouldRetry, msg => logger($"TerminateInstance: {msg}"));
        }

        private void AttemptTerminate(List<string> instanceIds)
        {
            using var client = new AmazonEC2Client(RegionEndpoint.GetBySystemName(_config.Region));
            var terminateRequest = new TerminateInstancesRequest
            {
                InstanceIds = instanceIds
            };

            client.TerminateInstancesAsync(terminateRequest).Wait();
        }

        public double DescribePersistentInstance(Logger logger)
        {
            using var client = new AmazonEC2Client(_region);
            var request = new DescribeInstancesRequest
            {
                Filters = new List<Filter>()
                {
                    new Filter
                    {
                        Name = "instance-id",
                        Values = new List<string>() { _config.PersistentInstanceId }
                    }
                }
            };
            var (time, runningInstances) = Timed(() => client.DescribeInstancesAsync(request));
            if (runningInstances.Reservations
                .SelectMany(x => x.Instances)
                .Where(y => y.State.Name == InstanceStateName.Running)
                .Select(o => o.InstanceId)
                .ToList()
                .Count() > 0) return time;
            else
                throw new Exception($"Persistent instance with ID {_config.PersistentInstanceId} cannot be found!");
        }
    }
}
