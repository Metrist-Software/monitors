using Metrist.Core;
using System.Net.Http;
using System.Net;
using System;

namespace Metrist.Monitors.Avalara
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string Url { get; set; } = "https://rest.avatax.com/api/v2/utilities/ping";
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

        public double Ping(Logger logger)
        {
            HttpResponseMessage response = null;
            double time;

            try
            {
                (time, response) = Timed(() => _client.GetAsync(
                    _config.Url
                ));

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception($"Unexpected status code of {response.StatusCode}");
                }

                return time;
            }
            finally
            {
                response?.Dispose();
            }
        }
    }
}
