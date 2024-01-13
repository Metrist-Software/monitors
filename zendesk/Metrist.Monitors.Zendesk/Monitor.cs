using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using Metrist.Core;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Metrist.Monitors.Zendesk
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string ApiToken { get; set; }
        public string Subdomain { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private HttpClient _client;
        private string _ticketId;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _config.ApiToken);
        }

        public async Task GetUsers(Logger logger)
        {
            var response = await _client.GetAsync($"https://{_config.Subdomain}.zendesk.com/api/v2/users.json");
            response.EnsureSuccessStatusCode();

            logger("Got users");
        }

        public double CreateTicket(Logger logger)
        {
            var body = JsonConvert.SerializeObject(
                new
                {
                    ticket = new
                    {
                        subject = Guid.NewGuid().ToString(),
                        comment = new
                        {
                            body = "Testing"
                        },
                        tags = new [] {"metrist_monitor"}
                    }
                }
            );

            var (time, response) = Timed(() =>
                _client.PostAsync(
                    $"https://{_config.Subdomain}.zendesk.com/api/v2/tickets.json",
                    new StringContent(body, Encoding.UTF8, "application/json")
                )
            );
            response.EnsureSuccessStatusCode();

            var responseBody = response.Content.ReadAsStringAsync().Result;
            var responseData = JsonConvert.DeserializeObject<JObject>(responseBody);
            _ticketId = responseData["ticket"]["id"].Value<string>();

            logger($"Created ticket {_ticketId}");

            return time;
        }

        public double SoftDeleteTicket(Logger logger)
        {
            Action deletionAttempt = () =>
            {
                var response = _client.DeleteAsync($"https://{_config.Subdomain}.zendesk.com/api/v2/tickets/{_ticketId}.json").Result;
                response.EnsureSuccessStatusCode();
            };
            Func<Exception, bool> shouldRetry = (ex) => ex.Message.Contains("404 (Not Found)");
            double time = TimedWithRetries(deletionAttempt, shouldRetry, logger, 10000);

            logger($"Soft deleted ticket {_ticketId}");

            return time;
        }

        public double PermanentlyDeleteTicket(Logger logger)
        {
            Action deletionAttempt = () =>
            {
                var response = _client.DeleteAsync($"https://{_config.Subdomain}.zendesk.com/api/v2/deleted_tickets/{_ticketId}.json").Result;
                response.EnsureSuccessStatusCode();
            };
            Func<Exception, bool> shouldRetry = (ex) => ex.Message.Contains("404 (Not Found)");
            double time = TimedWithRetries(deletionAttempt, shouldRetry, logger, 10000);

            logger($"Permanently deleted ticket {_ticketId}");

            return time;
        }

        public async Task Cleanup(Logger logger)
        {
            try
            {
                logger("Cleanup started");

                var lookback = DateTime.UtcNow.AddMinutes(-30);

                var query = System.Web.HttpUtility.UrlEncode($"created<{lookback.ToString("yyyy-MM-ddTHH:mm:ssZ")} type:ticket tags:metrist_monitor");

                var res = await _client.GetAsync($"https://{_config.Subdomain}.zendesk.com/api/v2/search.json?query={query}");
                var responseJson = await res.Content.ReadAsStringAsync();

                var response = JsonConvert.DeserializeObject<JObject>(responseJson);

                var tickets = response["results"].Value<JArray>();
                var ids = tickets.ToList().Select(i => i["id"].Value<string>());
                var idList = string.Join(",", ids);

                if (!ids.Any())
                {
                  logger("Nothing to cleanup");
                  return;
                }

                logger($"Deleting tickets with IDs {idList}");

                await _client.DeleteAsync($"https://{_config.Subdomain}.zendesk.com/api/v2/tickets/destroy_many?ids={idList}");
                await _client.DeleteAsync($"https://{_config.Subdomain}.zendesk.com/api/v2/deleted_tickets/destroy_many?ids={idList}");

                logger("Cleanup complete");
            }
            catch (Exception ex)
            {
                logger($"Error when trying to cleanup orphaned tickets. {ex}");
            }
        }
    }
}
