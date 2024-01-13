using System.Diagnostics;
using Metrist.Core;
using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using System.Net.Http.Headers;

namespace Metrist.Monitors.PagerDuty
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public MonitorConfig() { }
        public MonitorConfig(BaseMonitorConfig baseCfg) : base(baseCfg) { }

        public string RoutingKey { get; set; }
        public string ApiToken { get; set; }
        public string ServiceId { get; set; }

        public WaitForWebhook WaitForWebhookDelegate { get; set; }
    }

    public class Monitor: BaseMonitor
    {
        public static readonly string EventsUrl = "https://events.pagerduty.com/v2/enqueue";
        public static readonly string IncidentsUrl = "https://api.pagerduty.com/incidents/?statuses[]=triggered&limit=100";

        private readonly MonitorConfig _config;
        private readonly HttpClient _client;
        private readonly string _dedupKey;
        private DateTime _createIncidentTime;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            _client = new HttpClient();
            _dedupKey = Guid.NewGuid().ToString();
        }

        public async Task CreateIncident(Logger logger)
        {
            _createIncidentTime = DateTime.UtcNow;
            await SendEvent("trigger", logger);
        }

        public async Task ResolveIncident(Logger logger) => await SendEvent("resolve", logger);

        private async Task SendEvent(string eventAction, Logger logger)
        {
            var body = System.Text.Json.JsonSerializer.Serialize(
                new Event
                {
                    RoutingKey = _config.RoutingKey,
                    EventAction = eventAction,
                    DedupKey = _dedupKey,
                    Payload = new Payload
                    {
                        Summary = _dedupKey,
                        Severity = "critical",
                        Source = "canary-service-us-west-2"
                    }
                }
            );

            var response = await _client.PostAsync(
                EventsUrl,
                new StringContent(body, Encoding.UTF8, "application/json")
            );
            response.EnsureSuccessStatusCode();

            logger($"Sent event with dedup key {_dedupKey}");
        }

        public async Task<double> CheckForIncident(Logger logger)
        {
            var watch = Stopwatch.StartNew();

            var isFound = false;
            while (!isFound)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, IncidentsUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Token", $"token={_config.ApiToken}");
                request.Headers.Add("Accept", "application/vnd.pagerduty+json;version=2");

                var response = await _client.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseBody = response.Content.ReadAsStringAsync().Result;
                isFound = responseBody.Contains(_dedupKey);

                if (isFound)
                {
                    logger($"Found incident with dedup key {_dedupKey}");
                }
                else
                {
                    await Task.Delay(100);
                }
            }

            return watch.ElapsedMilliseconds;
        }

        public double ReceiveWebhook(Logger logger)
        {
            return TimeWebhook(_config.WaitForWebhookDelegate, _dedupKey, _createIncidentTime);
        }

        protected async Task Cleanup(Logger logger)
        {
            try
            {
                logger("Cleanup started");

                var cleanup = new Cleanup(logger, _config);
                await cleanup.Run(DateTime.UtcNow.AddMinutes(-15));
                await cleanup.CheckForDisabledWebhooks();

                logger("Cleanup complete");
            }
            catch (Exception ex)
            {
                logger($"Error when trying to cleanup orphaned incidents. {ex}");
            }
        }
    }
}
