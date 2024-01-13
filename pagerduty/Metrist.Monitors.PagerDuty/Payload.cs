using System.Text.Json.Serialization;

namespace Metrist.Monitors.PagerDuty
{
    public class Payload
    {
        [JsonPropertyName("summary")]
        public string Summary { get; set; }
        [JsonPropertyName("severity")]
        public string Severity { get; set; }
        [JsonPropertyName("source")]
        public string Source { get; set; }
    }
}
