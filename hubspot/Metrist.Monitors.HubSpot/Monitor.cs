using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Metrist.Core;

namespace Metrist.Monitors.HubSpot
{

    public class MonitorConfig : BaseMonitorConfig
    {
        public string ApiKey { get; set; }
    }

    public class Monitor: BaseMonitor
    {
        private readonly MonitorConfig _config;
        private HttpClient _client;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }

        public async Task GetContacts(Logger logger)
        {
            var response = await _client.GetAsync($"https://api.hubapi.com/contacts/v1/lists/all/contacts/all");
            response.EnsureSuccessStatusCode();

            logger("Got contacts");
        }
    }
}
