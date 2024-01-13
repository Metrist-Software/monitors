using System;
using System.Net.Http;
using System.Threading.Tasks;
using Metrist.Core;

namespace Metrist.Monitors.AzureFncs
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string TestFunctionUrl { get; set; }
        public string TestFunctionCode { get; set; }
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

        public async Task<double> RunHttpTrigger(Logger logger)
        {
            var uniqueId = Guid.NewGuid().ToString();
            var (time, response) = Timed(() => _client.GetAsync($"{_config.TestFunctionUrl}?code={_config.TestFunctionCode}&id={uniqueId}"));

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();

            if (content != uniqueId) {
                throw new Exception($"Invalid response from Azure Function. Expected {uniqueId}, got {content} ");
            }

            return time;
        }
    }
}
