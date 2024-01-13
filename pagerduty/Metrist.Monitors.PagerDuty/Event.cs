using System.Text.Json.Serialization;

namespace Metrist.Monitors.PagerDuty
{
    public class Event
    {
        [JsonPropertyName("routing_key")]
        public string RoutingKey { get; set; }
        [JsonPropertyName("event_action")]
        public string EventAction { get; set; }
        [JsonPropertyName("dedup_key")]
        public string DedupKey { get; set; }
        [JsonPropertyName("payload")]
        public Payload Payload { get; set; }

        public Event()
        {

        }
    }
}
