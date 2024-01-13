using Metrist.Core;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Text;
using System.Threading.Tasks;

namespace Metrist.Monitors.SendGrid
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string ApiKey { get; set; }
        public string FromEmail { get; set; }
        public string ToEmail { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private HttpClient _client;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }

        public async Task SendEmail(Logger logger)
        {
            var body = JsonConvert.SerializeObject(
                new
                {
                    personalizations = new[]
                    {
                        new
                        {
                            to = new []
                            {
                                new
                                {
                                    email = _config.ToEmail
                                }
                            },
                            subject = $"SendGrid Test from {_config.InstanceIdentifier}"
                        }
                    },
                    content = new []
                    {
                        new
                        {
                            type = "text/plain",
                            value = "Plain text content would go here."
                        }
                    },
                    from = new
                    {
                        email = _config.FromEmail
                    },
                    reply_to = new
                    {
                        email = _config.FromEmail
                    }
                }
            );

            logger($"Sending email");

            var response = await _client.PostAsync(
                "https://api.sendgrid.com/v3/mail/send",
                new StringContent(body, Encoding.UTF8, "application/json")
            );
            response.EnsureSuccessStatusCode();

            logger($"Sent email with response status code {response.StatusCode}");
        }
    }
}
