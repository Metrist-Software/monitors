using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Metrist.Core;

namespace Metrist.Monitors.AzureDevOpsArtifacts
{
    public class MonitorConfig : BaseMonitorConfig
    {
    }

    public class Monitor : BaseMonitor
    {
        // Note: Mostly a duplicate of NPM monitor but this is so simple, hardly worth refactoring.

        private const string BASE_URL = "https://pkgs.dev.azure.com/metrist-public-us-central/Public/_packaging/public/npm/registry/hello_world";
        private string PKG_URL = $"{BASE_URL}/-/hello_world-1.0.0.tgz";
        private const string SHA1_SUM = "mZ8r/wuLM3SaBKEsnxMGwMyPHw0=";

        private HttpClient _client;

        public Monitor(MonitorConfig config) : base(config)
        {
            // Yes this sets this "globally" but in this case globally is for this specific monitor.
            ServicePointManager.DnsRefreshTimeout = 0;

            _client = new HttpClient();
        }

        public double Ping(Logger logger)
        {
            // Get package metadata
            var (time, response) = Timed(() => _client.GetAsync(BASE_URL));
            response.EnsureSuccessStatusCode();

            logger("Got package metadata");

            return time;
        }

        public double DownloadPackage(Logger logger)
        {
            var (time, response) = Timed(() => _client.GetAsync(PKG_URL));
            response.EnsureSuccessStatusCode();

            using var responseBody = response.Content.ReadAsStreamAsync().Result;

            var sha = SHA1.Create();
            byte[] checksum = sha.ComputeHash(responseBody);
            var digest = Convert.ToBase64String(checksum);
            bool isValid = SHA1_SUM.Equals(digest);

            logger($"Downloaded package from {PKG_URL} and checksum verified {isValid}");

            return time;
        }
    }
}
