using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Metrist.Core;

namespace Metrist.Monitors.EasyPost
{
    public class MonitorConfig : BaseMonitorConfig {
        public string TestAPIKey { get; set; }
        public string ProdAPIKey { get; set; }
    }
    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly string _url = "https://api.easypost.com/v2/addresses/";
        private HttpClient _client;

        public Monitor(MonitorConfig config) : base(config) {
            _config = config;

            _client = new HttpClient();
        }

        public async Task GetAddresses(string apiKey)
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            var response = await _client.GetAsync(_url);
            response.EnsureSuccessStatusCode();
        }

        public async Task GetAddressesTest(Logger logger)
        {
            await GetAddresses(_config.TestAPIKey);

            logger("Got addresses with test API key");
        }

        public async Task GetAddressesProd(Logger logger)
        {
            await GetAddresses(_config.ProdAPIKey);

            logger("Got addresses with prod API key");
        }

        public async Task VerifyInvalidAddress(Logger logger)
        {
            var body = new List<KeyValuePair<string, string>> {
                new KeyValuePair<string, string>("verify[]",         "delivery"),
                new KeyValuePair<string, string>("address[street1]", "This Street Don't Exist"),
                new KeyValuePair<string, string>("address[city]",    "Fran Sancisco"),
                new KeyValuePair<string, string>("address[state]",   "OR"),
                new KeyValuePair<string, string>("address[zip]",     "90210"),
                new KeyValuePair<string, string>("address[country]", "US")
            };

            var request = new HttpRequestMessage(HttpMethod.Post, _url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ProdAPIKey);
            request.Content = new FormUrlEncodedContent(body);

            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseBody = response.Content.ReadAsStringAsync().Result;
            if (!responseBody.Contains("\"verifications\":{\"delivery\":{\"success\":false"))
            {
                throw new Exception($"Unexpected response \"{responseBody}\"");
            }

            logger("Verified invalid address with prod API key");
        }
    }
}
