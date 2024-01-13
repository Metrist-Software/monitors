using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Metrist.Core;
using DnsClient;
using Logger = Metrist.Core.Logger;

namespace Metrist.Monitors.CloudFlare
{
    public class MonitorConfig : BaseMonitorConfig
    {
      public string CdnCheckUrl { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly HttpClient _httpClient = new HttpClient();

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
        }

        public async Task Ping(Logger logger)
        {
            using var response = await _httpClient.GetAsync("https://1.1.1.1/favicon.ico");
            response.EnsureSuccessStatusCode();
        }

        public async Task DNSLookup(Logger logger)
        {
            var endpoint = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53);
            var dnsClient = new LookupClient(new LookupClientOptions(new NameServer(endpoint)) {
                UseCache = false,
                ContinueOnDnsError = false,
                ContinueOnEmptyResponse = false
            });
            var result = await dnsClient.QueryAsync("app.metrist.io", QueryType.A);

            if (result.HasError || result.Answers?.Count == 0)
            {
                throw new Exception("Error resolving app.metrist.io using 1.1.1.1");
            }
        }

        public async Task CDN(Logger logger)
        {
            string urlToCheck = string.IsNullOrEmpty(_config.CdnCheckUrl) ? "https://www.cloudflare.com" : _config.CdnCheckUrl;
            using var response = await _httpClient.GetAsync(urlToCheck);
            var hasCloudFlareCDNHeader = response.Headers.Contains("cf-cache-status");
            if (!hasCloudFlareCDNHeader)
            {
                throw new Exception($"Cloudflare CDN not active on {urlToCheck}");
            }
        }
    }
}
