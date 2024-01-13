using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using Metrist.Core;

namespace Metrist.Monitors.CircleCI
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string ApiToken { get; set; }
        public string ProjectSlug { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly HttpClient _client;
        private List<Workflow> _workflows;
        private const string _errorMessage = "Workflow not completed";

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("Circle-Token", config.ApiToken);
        }

        public double RunMonitorDockerWorkflow(Logger logger) => MonitorWorkflow(logger, "MonitorDocker");
        public double RunMonitorMachineWorkflow(Logger logger) => MonitorWorkflow(logger, "MonitorMachine");

        public async Task<double> StartPipeline(Logger logger)
        {
            var (time, pipelineRes) = Timed(() => _client.PostAsync($"https://circleci.com/api/v2/project/{_config.ProjectSlug}/pipeline", null).Result);
            pipelineRes.EnsureSuccessStatusCode();

            var pipelineContent = await pipelineRes.Content.ReadAsStringAsync();
            dynamic pipeline = JsonConvert.DeserializeObject(pipelineContent);

            var pipelineId = pipeline.id.ToString();

            // Wait a second to let the workflows initialize
            Thread.Sleep(1000);

            var res = _client.GetAsync($"https://circleci.com/api/v2/pipeline/{pipelineId}/workflow").Result;
            var content = res.Content.ReadAsStringAsync().Result;
            var workflowsResponse = JsonConvert.DeserializeObject<WorkflowsResponse>(content);

            _workflows = workflowsResponse.Items;
            return time;
        }

        private double MonitorWorkflow(Logger logger, string workflowName)
        {
            var workflowId = _workflows
                .Where(wf => wf.Name == workflowName)
                .First()
                .Id;
            Workflow workflow = null;
            TimedWithRetries(
                () => {

                    var res = _client.GetAsync($"https://circleci.com/api/v2/workflow/{workflowId}").Result;
                    var content = res.Content.ReadAsStringAsync().Result;
                    workflow = JsonConvert.DeserializeObject<Workflow>(content);

                    if(workflow.Status != "success") throw new Exception(_errorMessage);
                },
                (ex) => ex.Message == _errorMessage,
                logger,
                3000
            );

            var workflowDuration = (DateTime)workflow.StoppedAt - workflow.CreatedAt;

            return workflowDuration.TotalMilliseconds;
        }
    }

    public class Workflow
    {
        [JsonProperty("pipeline_id")]
        public string PipelineId { get; set; }
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("status")]
        public string Status { get; set; }
        [JsonProperty("pipeline_number")]
        public int PipelineNumber { get; set; }
        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }
        [JsonProperty("stopped_at")]
        public DateTime? StoppedAt { get; set; }
    }

    public class WorkflowsResponse
    {
        [JsonProperty("items")]
        public List<Workflow> Items { get; set; }
    }
}
