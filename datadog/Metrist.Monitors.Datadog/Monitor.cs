using System.Threading;
using System.Text;
using System;
using System.Net.Http;
using Metrist.Core;
using Newtonsoft.Json;

namespace Metrist.Monitors.Datadog
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string EnvironmentTagName { get; set; }
        public string ApiKey { get; set; }
        public string AppKey { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly HttpClient _client;
        private string _eventId;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("DD-API-KEY", _config.ApiKey);
            _client.DefaultRequestHeaders.Add("DD-APPLICATION-KEY", _config.AppKey);
        }

        public double SubmitEvent(Logger logger)
        {
            var content = JsonConvert.SerializeObject(new {
                aggregation_key = _config.EnvironmentTagName,
                date_happened = (int)DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalSeconds,
                host = "Metrist",
                text = "This is a monitoring event",
                title = $"Monitoring event {_config.EnvironmentTagName}"
            });

            var (time, res) = Timed(() => _client.PostAsync(
                "https://Application.datadoghq.com/api/v1/events",
                new StringContent(content, Encoding.UTF8, "application/json")
            ));

            res.EnsureSuccessStatusCode();

            var resContent = res.Content.ReadAsStringAsync().Result;
            dynamic response = JsonConvert.DeserializeObject(resContent);
            _eventId = response["event"].id.ToString();

            return time;
        }

        public double GetEvent(Logger logger)
        {
            Thread.Sleep(10000);

            return TimedWithRetries(
                () => {
                    var res = _client.GetAsync(
                        $"https://api.datadoghq.com/api/v1/events/{_eventId}"
                    ).Result;
                    res.EnsureSuccessStatusCode();
                },
                ex => ex.Message.Contains("404"),
                msg => logger($"GetEvent: {msg}"),
                30
            );
        }
    }
}
