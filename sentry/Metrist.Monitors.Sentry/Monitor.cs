using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using Metrist.Core;
using Sentry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using Metrist.Webhooks;
using System.Text.RegularExpressions;

namespace Metrist.Monitors.Sentry
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string IngestUrl { get; set; }
        public string ApiToken { get; set; }
        public string OrganizationSlug { get; set; }
        public string ProjectSlug { get; set; }
        public WaitForWebhook WaitForWebhookDelegate { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly string _dedupKey;
        private HttpClient _client;
        private string _issueId; // aka: groupId
        private DateTime _captureEventTime;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiToken);
            _dedupKey = Guid.NewGuid().ToString();
        }

        public async Task CaptureEvent(Logger logger)
        {
            SentryId eventId;

            SentryOptions options = new SentryOptions {
                Dsn = _config.IngestUrl,
                BeforeSend = @event =>
                {
                    @event.SetFingerprint(new [] { _dedupKey });
                    return @event;
                }
            };

            _captureEventTime = DateTime.UtcNow;

            using (SentrySdk.Init(options))
            {
                // eventId = SentrySdk.CaptureException(new Exception(_dedupKey));
                eventId = SentrySdk.CaptureMessage(_dedupKey);

                // Sentry creates a queue of events, flush it to ensure event is sent
                await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
            }


            logger($"Captured event {eventId} using dedupKey {_dedupKey}");
        }

        public double WaitForIssue(Logger logger)
        {
            return TimeWebhook(_config.WaitForWebhookDelegate, _dedupKey, _captureEventTime, (obj) =>
            {
                _issueId = obj["data"]["issue"]["id"].Value<string>();
            });
        }

        public double ResolveIssue(Logger logger)
        {
            var body = JsonConvert.SerializeObject(new { status = "resolved" });

            var (time, response) = Timed(() => _client.PutAsync(
                $"https://sentry.io/api/0/issues/{_issueId}/",
                new StringContent(body, Encoding.UTF8, "application/json")
            ));

            response.EnsureSuccessStatusCode();

            logger($"Resolved issue {_issueId} using dedupKey {_dedupKey}");

            return time;
        }

        private static Regex instanceNotFound = new Regex("Response status code does not indicate success: 404 ",
                                                          RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public double DeleteIssue(Logger logger)
        {
            logger($"Attempting delete of issue {_issueId}");
            Action deletionAttempt = () => AttemptDelete(_issueId, logger);
            Func<Exception, bool> shouldRetry = (ex) => instanceNotFound.IsMatch(ex.Message);

            return TimedWithRetries(deletionAttempt, shouldRetry, msg => logger($"DeleteIssue: {msg}"), 10000);
        }

        private void AttemptDelete(string issueId, Logger logger)
        {
            var response = _client.DeleteAsync($"https://sentry.io/api/0/issues/{_issueId}/").Result;
            var deleteResponse = response.Content.ReadAsStringAsync();
            logger($"Delete response was: {deleteResponse}");
            response.EnsureSuccessStatusCode();
            logger($"Deleted issue {_issueId} using dedupKey {_dedupKey}");
        }

        public async Task Cleanup(Logger logger)
        {
            try
            {
                logger("Cleanup started");

                // use '?query=' to get all issues, otherwise defaults to is:unresolved
                var listResponse = await _client.GetAsync($"https://sentry.io/api/0/projects/{_config.OrganizationSlug}/{_config.ProjectSlug}/issues/?query=&statsPeriod=");
                listResponse.EnsureSuccessStatusCode();

                var raw = await listResponse.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<JArray>(raw);
                var issueIds = data.Select((issue) => issue["id"].Value<string>()).ToArray();

                if (issueIds.Length > 0) {
                    logger($"Deleting issues {String.Join(' ', issueIds)}");

                    // make querystring id=123&id=456&id=789
                    var wrappedIds = issueIds.Select((id) => $"id={id}");
                    var joinedIds = String.Join('&', wrappedIds);

                    var deleteResponse = await _client.DeleteAsync($"https://sentry.io/api/0/projects/{_config.OrganizationSlug}/{_config.ProjectSlug}/issues/?{joinedIds}");
                    deleteResponse.EnsureSuccessStatusCode();
                }

                logger("Cleanup complete");
            }
            catch (Exception ex)
            {
                logger($"Error when trying to cleanup orphaned issues. {ex}");
            }
        }
    }
}
