using System;
using System.Text;
using System.Threading.Tasks;
using Metrist.Core;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;

namespace Metrist.Monitors.GCal
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string CalendarName { get; set; }
        public string Base64Credentials {get; set;}
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly CalendarService _service;
        private string _eventId;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            var decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(_config.Base64Credentials));
            _service = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = GoogleCredential
                    .FromJson(decodedCredentials)
                    .CreateScoped(new string[] { CalendarService.Scope.Calendar }),
                ApplicationName = "Google Calendar Monitoring"
            });
        }


        public async Task CreateEvent(Logger logger)
        {
            var evnt = new Google.Apis.Calendar.v3.Data.Event()
            {
                Summary = "Metrist Monitoring Event",
                Start = new Google.Apis.Calendar.v3.Data.EventDateTime()
                {
                    DateTime = DateTime.UtcNow
                },
                End = new Google.Apis.Calendar.v3.Data.EventDateTime()
                {
                    DateTime = DateTime.UtcNow.AddHours(1)
                }
            };

            var response = await _service.Events
                .Insert(evnt, _config.CalendarName)
                .ExecuteAsync();
            _eventId = response.Id;

            logger($"Created event with id {_eventId}");
        }

        public async Task GetEvent(Logger logger)
        {
            var response = await _service.Events
                .Get(_config.CalendarName, _eventId)
                .ExecuteAsync();
            logger("Got event with id: " + response.Id);
        }

        public async Task DeleteEvent(Logger logger)
        {
            var response = await _service.Events
                .Delete(_config.CalendarName, _eventId)
                .ExecuteAsync();

            if (response?.Length > 0)
            {
                throw new Exception($"Error deleting event {_eventId}\n{response}");
            }

            logger("Successfully deleted event " + _eventId);
        }
    }
}
