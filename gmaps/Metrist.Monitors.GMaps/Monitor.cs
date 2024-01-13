using Metrist.Core;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Generic;

namespace Metrist.Monitors.GMaps
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string ApiKey { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly HttpClient client;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            client = new HttpClient();
        }

        public async Task GetDirections(Logger logger)
        {
            var response = await client.GetAsync($"https://maps.googleapis.com/maps/api/directions/json?origin=Toronto,Ontario&destination=Ottawa,Ontario&key={_config.ApiKey}");

            // throw if not 200-299
            response.EnsureSuccessStatusCode();

            logger("Successfully got directions");
        }

        public async Task GetStaticMapImage(Logger logger)
        {
            var response = await client.GetAsync($"https://maps.googleapis.com/maps/api/staticmap?center=CN+Tower,Toronto,Ontario&zoom=13&size=600x300&maptype=roadmap&key={_config.ApiKey}");

            // throw if not 200-299
            response.EnsureSuccessStatusCode();

            logger("Successfully got static map image");
        }

        public async Task GetGeocodingFromAddress(Logger logger)
        {
            var response = await client.GetAsync($"https://maps.googleapis.com/maps/api/geocode/json?address=CN+Tower,Toronto,Ontario&key={_config.ApiKey}");

            // throw if not 200-299
            response.EnsureSuccessStatusCode();

            logger("Successfully got geocoding from address");
        }

    }
}
