using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Metrist.Core;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

namespace Metrist.Monitors.Jira
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string ApiToken { get; set; }
        public string Url { get; set; }
        public string ProjectKey {get ; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private HttpClient _client;

        private string _issueNumber;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _config.ApiToken);
        }

        public double CreateIssue(Logger logger)
        {
            var body = JsonConvert.SerializeObject(new {
                fields = new {
                    project = new { id = "10000" },
                    issuetype = new { id = "10002" },
                    summary = "CANARY TESTING"
                },
            });

            var (time, response) = Timed(() => _client.PostAsync(
                $"{_config.Url}/issue",
                new StringContent(body, Encoding.UTF8, "application/json")
            ));
            response.EnsureSuccessStatusCode();

            var responseBody = response.Content.ReadAsStringAsync().Result;
            var issue = JsonConvert.DeserializeObject<JObject>(responseBody);
            _issueNumber = issue["key"].Value<string>();

            logger($"Created issue {_issueNumber}");

            return time;
        }

        public double DeleteIssue(Logger logger)
        {
            Action deletionAttempt = () =>
            {
                var response = _client.DeleteAsync($"{_config.Url}/issue/{_issueNumber}").Result;
                response.EnsureSuccessStatusCode();
            };

            Func<Exception, bool> shouldRetry = (ex) => ex.Message.Contains("404 (Not Found)");
            double time = TimedWithRetries(deletionAttempt, shouldRetry);

            logger($"Deleted issue {_issueNumber}");

            return time;
        }

        public async Task Cleanup(Logger logger)
        {
            var lookback = DateTime.UtcNow.AddMinutes(-15);
            var jql = $"project = {_config.ProjectKey} AND created < \"{lookback.ToString("yyyy-MM-dd HH:mm")}\"";
            var url = $"{_config.Url}/search?jql={jql}&maxResults=100";
            var result = await _client.GetAsync(url);
            var responseJson = result.Content.ReadAsStringAsync().Result;
            var response = JsonConvert.DeserializeObject<JObject>(responseJson);
            var keys = response["issues"]
                .Value<JArray>()
                .Select(i => i["key"].Value<string>())
                .ToList();

            keys.ForEach(async key =>
            {
                logger($"Deleting ticket with key {key}");
                await _client.DeleteAsync($"{_config.Url}/issue/{key}");
            });
        }
    }
}
