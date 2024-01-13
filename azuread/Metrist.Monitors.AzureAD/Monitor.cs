using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Metrist.Core;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;
using Logger = Metrist.Core.Logger;

namespace Metrist.Monitors.AzureAD
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public MonitorConfig() {}
        public MonitorConfig(BaseMonitorConfig baseCfg) : base(baseCfg) {}
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string TenantId { get; set; }
        public string Domain { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        const string USER_MONITOR_SUFFIX = "-MONITORUSER";
        private readonly MonitorConfig _config;
        private ClientCredentialProvider _auth;
        private string _userId;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            _userId = null;
            _auth = null;
        }

        public async Task Cleanup(Logger logger)
        {
            try
            {
                var app = GetClientApplication();
                var auth = new ClientCredentialProvider(app);
                var graphClient = new GraphServiceClient(auth);

                var users = await graphClient.Users
                .Request()
                .Select(u => new {
                    u.Id,
                    u.DisplayName,
                    u.CreatedDateTime
                })
                .GetAsync();

                foreach(var user in users)
                {
                    if (
                        user.CreatedDateTime != null
                        && (DateTime.UtcNow - user.CreatedDateTime.Value).TotalMinutes > 30
                        && user.DisplayName.EndsWith(USER_MONITOR_SUFFIX)
                    )
                    {
                        logger($"Deleting stale user Id: {user.Id} DisplayName: {user.DisplayName}");
                        try
                        {
                            await graphClient.Users[user.Id].Request().DeleteAsync();
                        }
                        catch (Exception ex)
                        {
                            logger($"METRIST_MONITOR_ERROR - Error deleting stale AD user {user.Id}. Error was: {ex}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger($"Error when trying to cleanup orphaned users. {ex}");
            }
        }

        private IConfidentialClientApplication GetClientApplication()
        {
            var app = ConfidentialClientApplicationBuilder
                                                .Create(_config.ClientId)
                                                .WithTenantId(_config.TenantId)
                                                .WithClientSecret(_config.ClientSecret)
                                                .Build();

            return app;
        }

        public async Task Authenticate(Logger logger)
        {
            var app = GetClientApplication();

            // This forces an explicit authenticate for this timing
            var result = await app.AcquireTokenForClient(new string[] { "https://graph.microsoft.com/.default" })
                            .ExecuteAsync();

            _auth = new ClientCredentialProvider(app);
        }

        public async Task WriteUser(Logger logger)
        {
            var graphClient = new GraphServiceClient(_auth);
            var newUserGuid = Guid.NewGuid();

            var user = new User
            {
                DisplayName = $"{newUserGuid}{USER_MONITOR_SUFFIX}",
                PasswordProfile = new PasswordProfile() { Password = Guid.NewGuid().ToString() },
                AccountEnabled = false,
                UserPrincipalName = $"{newUserGuid}{USER_MONITOR_SUFFIX}@{_config.Domain}",
                MailNickname = $"{newUserGuid}{USER_MONITOR_SUFFIX}"
            };

            var newUser = await graphClient.Users
                .Request()
                .AddAsync(user);

            _userId = newUser.Id;
        }


        public double ReadUser(Logger logger)
        {
            // Too often, we hit this before Azure has propagated the user. No rush, just
            // wait a bit.
            Thread.Sleep(30000);

            return Timed(() =>
            {
                var graphClient = new GraphServiceClient(_auth);
                graphClient.Users[_userId].Request().GetAsync().Wait();
            });
        }

        private static Regex instanceNotFound = new Regex("Unable to read the company information from the directory",
                                                          RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public double DeleteUser(Logger logger)
        {
            Action deletionAttempt = () => AttemptDelete(_userId);
            Func<Exception, bool> shouldRetry = (ex) => instanceNotFound.IsMatch(ex.Message);

            return TimedWithRetries(deletionAttempt, shouldRetry, msg => logger($"DeleteUser: {msg}"));
        }

        private void AttemptDelete(string userId)
        {
            var graphClient = new GraphServiceClient(_auth);
            graphClient.Users[_userId].Request().DeleteAsync().Wait();
        }
    }
}
