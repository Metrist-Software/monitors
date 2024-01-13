using System;
using System.Text;
using System.Threading.Tasks;
using Metrist.Core;
using RestSharp;
using RestSharp.Authenticators;

namespace Metrist.Monitors.Artifactory
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public MonitorConfig(BaseMonitorConfig baseCfg) : base(baseCfg) {}
        public string ApiToken { get; set; }
        public string Url { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private string _uniqueId;
        private readonly RestClient _restClient;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            _restClient = new RestClient(_config.Url);
            _restClient.Authenticator = new BearerTokenAuthenticator(_config.ApiToken);
            _uniqueId = Guid.NewGuid().ToString();
        }

        public async Task UploadArtifact(Logger logger)
        {
            // Note that this does not work completely - something here goes wrong with the multipart
            // boundaries. But it is simple and sufficient for our test. Just don't copy this code
            // if you want to deploy actual artifacts to JFrog Artifactory :)
            var filename = "/" + _uniqueId + ".txt";
            var fileBytes = new UTF8Encoding().GetBytes("This is for the monitoring run with unique id " + _uniqueId);
            var request = new RestRequest(filename, Method.Put);
            request.AddFile(filename, fileBytes, filename);
            var response = await _restClient.ExecuteAsync(request);
            if (!response.IsSuccessful)
            {
                throw new Exception($"Unexpected response status code {response.StatusCode}: {response.ErrorMessage}");
            }
        }

        public async Task DownloadArtifact(Logger logger)
        {
            var filename = "/" + _uniqueId + ".txt";
            var request = new RestRequest(filename, Method.Get);
            var response = await _restClient.ExecuteAsync(request);
            if (!response.IsSuccessful)
            {
                throw new Exception($"Unexpected response status code {response.StatusCode}: {response.ErrorMessage}");
            }
        }

        public async Task DeleteArtifact(Logger logger)
        {
            var filename = "/" + _uniqueId + ".txt";
            var request = new RestRequest(filename, Method.Delete);
            var response = await _restClient.ExecuteAsync(request);
            if (!response.IsSuccessful)
            {
                throw new Exception($"Unexpected response status code {response.StatusCode}: {response.ErrorMessage}");
            }
        }
    }

    public class BearerTokenAuthenticator : AuthenticatorBase
    {
        public BearerTokenAuthenticator(string apiKey) : base(apiKey) { }

        protected override ValueTask<Parameter> GetAuthenticationParameter(string accessToken)
        {
          return new(new HeaderParameter(KnownHeaders.Authorization, $"Basic {accessToken}"));
        }
    }
}
