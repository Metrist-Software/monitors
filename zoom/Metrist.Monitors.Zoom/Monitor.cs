using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Metrist.Core;
using Newtonsoft.Json;

namespace Metrist.Monitors.Zoom
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        // User ID to create the meetings for (also the user that the 100 per day rate limit for create/delete applies against)
        // Only required if CreateMeeting step is included
        public string MeetingUserId { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private HttpClient _client;
        private readonly string _url = "https://api.zoom.us/v2";
        private ulong _meetingId;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            _client = new HttpClient();
        }

        public async Task GetUsers(Logger logger)
        {
            // TODO: can auth be moved to the constructor or does the token expire too quickly?
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_url}/users");
            Authorize(request);

            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            logger("Got users");
        }


        public async Task CreateMeeting(Logger logger)
        {
            if (String.IsNullOrWhiteSpace(_config.MeetingUserId)) {
                throw new Exception("MeetingUserId is required when running the CreateMeeting step.");
            }

            var args = new
                {
                    agenda = "Test Meeting",
                };
            var jsonString = JsonConvert.SerializeObject(args);
            var data = new StringContent(jsonString, Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_url}/users/{_config.MeetingUserId}/meetings");
            request.Content = data;
            Authorize(request);

            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<dynamic>(body);
            _meetingId = result.id;
        }

        public async Task GetMeeting(Logger logger)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_url}/meetings/{_meetingId}");
            Authorize(request);
            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        public async Task DeleteMeeting(Logger logger)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{_url}/meetings/{_meetingId}");
            Authorize(request);
            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }

        public async Task Cleanup(Logger logger)
        {
            if (String.IsNullOrWhiteSpace(_config.MeetingUserId)) {
                logger("Skipping cleanup as MeetingUserId is not configured.");
                return;
            }

            var cutOff = DateTime.Now.Subtract(new System.TimeSpan(0, 1, 0, 0));
            logger($"Cleanup: cleaning up all outstanding meetings scheduled to happen before {cutOff}");

            var request = new HttpRequestMessage(HttpMethod.Get, $"{_url}/users/{_config.MeetingUserId}/meetings");
            Authorize(request);
            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<dynamic>(body);
            foreach (var meeting in result.meetings)
            {
                DateTime startTime = meeting.start_time;
                logger($"Got meeting {meeting.id} starting at {startTime}");
                if (startTime < cutOff)
                {
                    logger("Too old, deleting");
                    // Yes, maybe a bit of a hack... ;-)
                    _meetingId = meeting.id;
                    await DeleteMeeting(logger);
                }
            }
            logger("Cleanup all done!");
        }

        private void Authorize(HttpRequestMessage request)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                JwtTokenGenerator.GenerateToken(_config.ApiKey, _config.ApiSecret)
            );
        }
    }
}
