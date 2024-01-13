using System.Net.Http;
using Metrist.Core;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Text;
using System.Collections.Generic;
using System;

namespace Metrist.Monitors.AzureDevOpsBoards
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public MonitorConfig() {}
        public MonitorConfig(BaseMonitorConfig baseCfg) : base(baseCfg) {}

        public string Organization { get; set ;}
        public string Project { get; set; }
        public string Team { get; set; }
        public string PersonalAccessToken { get; set ;}

        public string WorkItemUrl
        {
            get
            {
                return $"https://dev.azure.com/{this.Organization}/{this.Project}/_apis/wit/workitems";
            }
        }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly HttpClient _client;

        private string _workItemId;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_config.PersonalAccessToken}"))
            );
            _client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json-patch+json"));
        }

        public async Task CreateWorkItem(Logger logger)
        {
            var body = JsonConvert.SerializeObject(new List<object>
            {
                new {
                    op = "add",
                    path = "/fields/System.Title",
                    from = "",
                    value = "Monitor Task"
                }
            });

            var response = await _client.PostAsync(
                $"{_config.WorkItemUrl}/$Task?api-version=6.0",
                new StringContent(body, Encoding.UTF8, "application/json-patch+json")
            );

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var workItem = JsonConvert.DeserializeObject<WorkItem>(content);

            _workItemId = workItem.id.ToString();
        }

        public async Task GetWorkItem(Logger logger)
        {
            var response = await _client.GetAsync($"{_config.WorkItemUrl}/{_workItemId}?api-version=6.0");
            response.EnsureSuccessStatusCode();
        }

        public async Task EditWorkItem(Logger logger)
        {
            var body = JsonConvert.SerializeObject(new List<object>
            {
                new {
                    op = "replace",
                    path = "/fields/System.Title",
                    from = "",
                    value = "Monitor Task - Edited"
                }
            });

            var response = await _client.PatchAsync(
                $"{_config.WorkItemUrl}/{_workItemId}?api-version=6.0",
                new StringContent(body, Encoding.UTF8, "application/json-patch+json")
            );

            response.EnsureSuccessStatusCode();
        }

        public async Task DeleteWorkItem(Logger logger)
        {
            var response = await _client.DeleteAsync($"{_config.WorkItemUrl}/{_workItemId}?api-version=6.0");

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
        }

        public void TearDown(Logger logger)
        {
        }

        public void Cleanup(Logger logger)
        {
            logger("Running cleanup");

            var queryUrl = $"https://dev.azure.com/{_config.Organization}/{_config.Project}/{_config.Team}/_apis/wit/wiql?api-version=6.0";
            var body = JsonConvert.SerializeObject(new{
                query = "Select [System.Id] From WorkItems Where [System.CreatedDate] < @StartOfDay - 1 order by [Microsoft.VSTS.Common.Priority] asc, [System.CreatedDate] desc"
            });

            var response = _client.PostAsync(
                queryUrl,
                new StringContent(body, Encoding.UTF8, "application/json")
            ).Result;

            var content = response.Content.ReadAsStringAsync().Result;

            var data = JsonConvert.DeserializeObject<WorkItemsResponse>(content);
            if (data != null) { 
                data.workItems.ForEach(workItem =>
                {
                    var deleteUrl = $"https://dev.azure.com/{_config.Organization}/{_config.Project}/_apis/wit/workitems/{workItem.id}?destroy=true&api-version=6.0";
                    logger($"Deleting workitem {workItem.id}");
                    var res = _client.DeleteAsync(deleteUrl).Result;
                });
            }
        }
    }

    internal class WorkItemsResponse
    {
        public List<WorkItem> workItems { get; set; }
    }

    internal class WorkItem
    {
        public int id { get; set; }
    }
}
