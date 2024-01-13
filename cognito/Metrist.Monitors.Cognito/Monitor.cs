using System;
using Metrist.Core;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using System.Threading.Tasks;

namespace Metrist.Monitors.Cognito
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string UserPool { get; set; }
        public string AwsAccessKeyId { get; set; }
        public string AwsSecretAccessKey { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly AmazonCognitoIdentityProviderClient _client;
        private readonly string _uniqueId;
        private readonly string _tempPass;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", config.AwsAccessKeyId);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", config.AwsSecretAccessKey);

            _client = new AmazonCognitoIdentityProviderClient();
            _uniqueId = Guid.NewGuid().ToString();
            _tempPass = Guid.NewGuid().ToString();
        }

        public async Task CreateUser(Logger logger)
        {
            var request = new AdminCreateUserRequest()
            {
                Username = _uniqueId,
                TemporaryPassword = _tempPass,
                UserPoolId = _config.UserPool
            };
            await _client.AdminCreateUserAsync(request);
        }

        public async Task DeleteUser(Logger logger)
        {
            var request = new AdminDeleteUserRequest()
            {
                Username = _uniqueId,
                UserPoolId = _config.UserPool
            };
            await _client.AdminDeleteUserAsync(request);
        }

        public async Task Cleanup(Logger logger)
        {
            var cutOff = DateTime.Now.Subtract(new System.TimeSpan(1, 0, 0));
            logger($"Cleanup: removing up all users created before {cutOff}.");
            var request = new ListUsersRequest()
            {
                UserPoolId = _config.UserPool
            };
            var response = await _client.ListUsersAsync(request);
            logger($"Cleanup found {response.Users.Count} users in the monitor's pool {_config.UserPool}.");
            foreach (var user in response.Users)
            {
                logger($"Found user: {user.Username}, created {user.UserCreateDate}.");
                if (user.UserCreateDate < cutOff)
                {
                    logger($"Deleting user {user.Username}");
                    var deleteRequest = new AdminDeleteUserRequest()
                    {
                        Username = user.Username,
                        UserPoolId = _config.UserPool
                    };
                    await _client.AdminDeleteUserAsync(deleteRequest);
                }
            }
            logger("Cleanup completed successfully.");
        }
    }
}
