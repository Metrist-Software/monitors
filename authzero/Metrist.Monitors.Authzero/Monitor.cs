using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Metrist.Core;
using Newtonsoft.Json;

namespace Metrist.Monitors.Authzero
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string Domain {get; set;}
        public string ClientId {get; set;}
        public string ClientSecret {get; set;}
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private HttpClient _client;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            _client = new HttpClient();
        }

        public async Task GetAccessToken(Logger logger)
        {
            var body = JsonConvert.SerializeObject(new {
                client_id     = _config.ClientId,
                client_secret = _config.ClientSecret,
                audience      = $"https://{_config.Domain}/api/v2/",
                grant_type    = "client_credentials"
            });

            var response = await _client.PostAsync(
                $"https://{_config.Domain}/oauth/token",
                new StringContent(body, Encoding.UTF8, "application/json")
            );

            string responseBody = response.Content.ReadAsStringAsync().Result;
            response.EnsureSuccessStatusCode();

            var responseData = JsonConvert.DeserializeObject<IDictionary<string, string>>(responseBody);
            // client is now authenticated for future steps
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                responseData["token_type"],
                responseData["access_token"]
            );

            logger("Got access token");
        }

        public async Task GetBranding(Logger logger)
        {
            var response = await _client.GetAsync($"https://{_config.Domain}/api/v2/branding");
            response.EnsureSuccessStatusCode();

            logger("Got branding");
        }
    }
}
