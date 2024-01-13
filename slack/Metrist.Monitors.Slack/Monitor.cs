using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Metrist.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Metrist.Monitors.Slack
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string Channel { get; set; }
        public string ApiToken { get; set; }
        public string Tag { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private HttpClient _client;
        private readonly string _postUrl = "https://slack.com/api/chat.postMessage";
        private readonly string _readUrl = "https://slack.com/api/conversations.history";

        private string _postedChannelId;
        private string _postedTs;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiToken);
        }

        public async Task PostMessage(Logger logger)
        {
            var body = JsonConvert.SerializeObject(new {
                channel = _config.Channel,
                text    = $"Hello from Slack monitor running in {_config.Tag} {_config.InstanceIdentifier}. The clock says {DateTime.UtcNow.ToString("o")}"
            });

            var response = await _client.PostAsync(
                _postUrl,
                new StringContent(body, Encoding.UTF8, "application/json")
            );
            response.EnsureSuccessStatusCode();

            var responseBody = response.Content.ReadAsStringAsync().Result;
            var responseData = JObject.Parse(responseBody);

            if (responseData["ok"].Value<bool>())
            {
                _postedChannelId = responseData["channel"].Value<string>();
                _postedTs = responseData["ts"].Value<string>();
                logger($"Posted Slack message to {_config.Channel}");
            }
            else
            {
                throw new Exception($"Error writing test message. Error is: {responseData["error"].Value<string>()}");
            }
        }

        public async Task ReadMessage(Logger logger)
        {
            var response = await _client.GetAsync(
                _readUrl + $"?channel={_postedChannelId}&latest={_postedTs}&limit=1&inclusive=true"
            );
            response.EnsureSuccessStatusCode();

            var responseBody = response.Content.ReadAsStringAsync().Result;
            var responseData = JObject.Parse(responseBody);

            if (responseData["ok"].Value<bool>() && responseBody.Contains(_postedTs))
            {
                logger($"Retrieved message with TS {_postedTs} from channel with id {_postedChannelId}");
            }
            else
            {
                throw new Exception($"Error retrieving posted message with TS {_postedTs} from channel with id {_postedChannelId}. Error is: {responseData["error"].Value<string>()}");
            }            
        }
    }
}
