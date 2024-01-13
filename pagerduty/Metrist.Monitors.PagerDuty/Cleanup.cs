using Metrist.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Metrist.Monitors.PagerDuty
{
    public class Cleanup
    {
        public readonly string BaseURL = "https://api.pagerduty.com";

        private readonly MonitorConfig _config;
        private readonly HttpClient _client;
        private readonly Logger _logger;

        public Cleanup(Logger logger, MonitorConfig config)
        {
            _config = config;
            _logger = logger;
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", $"token={_config.ApiToken}");
            _client.DefaultRequestHeaders.Add("Accept", "application/vnd.pagerduty+json;version=2");
            _client.DefaultRequestHeaders.Add("From", "ryan@canarymonitor.com"); // TODO needed? If so make variable.
        }

        public async Task Run(DateTime lookback)
        {
            var url = $"{BaseURL}/incidents?statuses[]=triggered&until={lookback.ToString("yyyy-MM-ddTHH:mm:ssZ")}";
            var responseJson = await _client.GetAsync(url);

            var response = JsonConvert.DeserializeObject<JObject>(await responseJson.Content.ReadAsStringAsync());
            var incidents = response["incidents"].Value<JArray>();
            var ids = incidents.ToList().Select(i => i["id"].Value<string>());

            ids.ToList().ForEach(id => ResolveIncident(id));
        }

        private void ResolveIncident(string id)
        {
            dynamic requestJson = new {
                incident = new {
                    type = "incident_reference",
                    status = "resolved"
                }
            };

            var body = JsonConvert.SerializeObject(requestJson);
            var requestBody = new StringContent(body, Encoding.UTF8, "application/json");

            _logger($"Resolving incident with ID {id}");
            _client.PutAsync($"{BaseURL}/incidents/{id}", requestBody).Wait();
        }

        public async Task CheckForDisabledWebhooks()
        {
            _logger($"Checking for disabled webhooks on service with ID {_config.ServiceId}");
            var responseBody = await _client.GetAsync($"{BaseURL}/extensions?include[]=temporarily_disabled");
            var responseJson = JsonConvert.DeserializeObject<JObject>(await responseBody.Content.ReadAsStringAsync());

            // This API doesn't offer filtering by service, so we'll do that here.

            var urls = responseJson["extensions"]
                .Value<JArray>()
                .Where(e => e["temporarily_disabled"].Value<bool>())
                .SelectMany(d => d["extension_objects"].Value<JArray>())
                .Where(o => o["id"].Value<string>() == _config.ServiceId)
                .Select(o => o["html_url"])
                .Distinct();

            if (urls.Count() == 0) return;

            var urlsMessage = string.Join(", ", urls);
            var message = $"PagerDuty webhooks are temporarily disabled for: {urlsMessage}.";
            if (_logger != null)
            {
                _logger(message);
            }
        }
    }
}
