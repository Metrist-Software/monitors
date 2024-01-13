using System.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Metrist.Core;

namespace Metrist.Monitors.Fastly
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public String RequestUri { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly HttpClient _client;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            _client = new HttpClient();
        }

        public async Task PurgeCache(Logger logger)
        {
            await SendAsync("PURGE", new Uri(_config.RequestUri));
        }

        public async Task GetNonCachedFile(Logger logger)
        {
            await SendAsync("GET", new Uri(_config.RequestUri));
        }

        public async Task GetCachedFile(Logger logger)
        {
            var res = await SendAsync("GET", new Uri(_config.RequestUri));

            if (res.Headers.GetValues("X-Cache").FirstOrDefault() == "MISS")
            {
                throw new Exception("Did not get expected cache hit");
            }
        }

        private async Task<HttpResponseMessage> SendAsync(string method, Uri requestUri)
        {
            var request = new HttpRequestMessage
            {
                Method = new HttpMethod(method),
                RequestUri = requestUri
            };

            request.Headers.Add("Accept", "application/json");

            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return response;
        }
    }
}
