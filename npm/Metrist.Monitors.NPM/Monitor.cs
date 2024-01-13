using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Metrist.Core;

namespace Metrist.Monitors.NPM
{
    public class MonitorConfig : BaseMonitorConfig
    {
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        // TODO: Create a small, purpose-built npm package and use that
        // See https://registry.npmjs.org/vue-debounce-provider
        private const string _downloadPackageUrl = "https://registry.npmjs.org/vue-debounce-provider/-/vue-debounce-provider-1.0.15.tgz";
        private const string _integrity = "sha512-gYXvFX6xVQQ3MSLjWK/vnm/PJnDLUEC4yBqMRQESBWyBEVv1Fqj1EcBsmxrMh9lQXoUZMzykWLp9O7k9DhjfOQ==";

        private HttpClient _client;

        public Monitor(MonitorConfig config) : base(config)
        {
            // Yes this sets this "globally" but in this case globally is for this specific monitor.
            ServicePointManager.DnsRefreshTimeout = 0;
            _config = config;

            _client = new HttpClient();
        }

        public double Ping(Logger logger)
        {
            // Get package metadata
            var (time, response) = Timed(() => _client.GetAsync("https://registry.npmjs.org/vue-debounce-provider"));
            response.EnsureSuccessStatusCode();

            logger("Got package metadata");

            return time;
        }

        public double DownloadPackage(Logger logger)
        {
            var (time, response) = Timed(() => _client.GetAsync(_downloadPackageUrl));
            response.EnsureSuccessStatusCode();

            using var responseBody = response.Content.ReadAsStreamAsync().Result;

            var sha = SHA512.Create();
            byte[] checksum = sha.ComputeHash(responseBody);
            var digest = Convert.ToBase64String(checksum);
            bool isValid = _integrity.Split('-')[1].Equals(digest);

            logger($"Downloaded package from {_downloadPackageUrl} and checksum verified {isValid}");

            return time;
        }
    }
}
