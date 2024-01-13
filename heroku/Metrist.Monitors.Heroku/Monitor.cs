using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Metrist.Core;
using Metrist.Webhooks;

namespace Metrist.Monitors.Heroku
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public MonitorConfig() {}
        public MonitorConfig(BaseMonitorConfig baseCfg) : base(baseCfg) {}

        public string ApiKey { get; set; }
        public string ApiKeyUser { get; set; }
        public string AppName { get; set; }
        public string Region { get; set; }
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
            _client.DefaultRequestHeaders.Add("Accept", "application/vnd.heroku+json; version=3");
        }

        public async Task AppPing(Logger logger)
        {
            var response = await _client.GetAsync($"https://{_config.AppName}.herokuapp.com");
            response.EnsureSuccessStatusCode();

            logger("Performed app health check (ping)");
        }

        public async Task ConfigUpdate(Logger logger)
        {
            var body = JsonSerializer.Serialize(new {
                CHANGE_KEY = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds()
            });

            var response = await _client.PatchAsync(
                $"https://api.heroku.com/apps/{_config.AppName}/config-vars",
                new StringContent(body, Encoding.UTF8, "application/json")
            );
            response.EnsureSuccessStatusCode();

            logger("Updated app configuration");
        }
    }
}
