using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using Metrist.Core;
using System.Net.Http;
using System.Linq;

namespace Metrist.Monitors.Trello
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string AppKey { get; set; }
        public string UserToken { get; set; }
        public string ListId {get; set; }
    }

    public class Monitor: BaseMonitor
    {
        private readonly MonitorConfig _config;
        private HttpClient _client;
        private string _createdCardId;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            _client = new HttpClient();
        }

        public async Task CreateCard(Logger logger)
        {
            var response = await _client.PostAsync(
                $"https://api.trello.com/1/cards?key={_config.AppKey}&token={_config.UserToken}&name=test&idList={_config.ListId}",
                null // no body
            );
            response.EnsureSuccessStatusCode();

            var responseBody = response.Content.ReadAsStringAsync().Result;
            var responseData = JsonConvert.DeserializeObject<JObject>(responseBody);
            _createdCardId = responseData["id"].Value<string>();

            logger($"Created card {_createdCardId}");
        }

        public async Task DeleteCard(Logger logger)
        {
            var response = await _client.DeleteAsync(
                $"https://api.trello.com/1/cards/{_createdCardId}?key={_config.AppKey}&token={_config.UserToken}"
            );
            response.EnsureSuccessStatusCode();

            logger($"Deleted card {_createdCardId}");
        }

        public async Task Cleanup(Logger logger)
        {
            logger("Cleanup started");

            var lookback = DateTime.UtcNow.AddMinutes(-30);

            var response = await _client.GetAsync(
              $"https://api.trello.com/1/lists/{_config.ListId}/cards?key={_config.AppKey}&token={_config.UserToken}"
            );
            response.EnsureSuccessStatusCode();

            var responseBody = response.Content.ReadAsStringAsync().Result;
            var cards = JsonConvert.DeserializeObject<JArray>(responseBody).ToList();
            cards
                .Where(card => card["dateLastActivity"].Value<DateTime>() < lookback)
                .ToList()
                .ForEach(async card =>
                {
                    var id = card["id"].Value<string>();
                    logger($"Deleting card with ID {id}");
                    var url = $"https://api.trello.com/1/cards/{id}?key={_config.AppKey}&token={_config.UserToken}";
                    await _client.DeleteAsync(url);

                });

            logger("Cleanup complete");
        }

    }
}
