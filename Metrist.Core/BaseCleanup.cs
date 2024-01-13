using System;
using System.Net.Http;

namespace Metrist.Core
{
    /// Monitors can be associated with cleanup actions. This base class
    /// contains some common code for such cleanup actions.
    public abstract class BaseCleanup
    {
        protected readonly HttpClient _client;
        protected readonly string _apiToken;
        protected DateTime _lookback;
        protected Logger _logger;
        protected abstract string BaseURL { get; }

        public BaseCleanup(HttpClient client, string apiToken, DateTime lookback, Logger logger)
        {
            _client = client;
            _apiToken = apiToken;
            _lookback = lookback;
            _logger = logger;
        }

        public abstract void Run();
    }
}
