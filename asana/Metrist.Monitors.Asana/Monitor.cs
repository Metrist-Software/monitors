using Metrist.Core;
using System.Net.Http;
using System.Net;
using System;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Text;
using System.Threading.Tasks;

namespace Metrist.Monitors.Asana
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string PersonalAccessToken {get; set;}
        public string ProjectId {get; set;}
        public string WorkspaceId {get; set;}
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly HttpClient _client;
        private readonly string API_ROOT="https://app.asana.com/api/1.0";

        private string _taskId = null;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.PersonalAccessToken);
        }

        public double Ping(Logger logger)
        {
            (double time, string responseBody) = Timed(() => MakeRequestAsync(
                $"{API_ROOT}/users",
                HttpMethod.Get,
                logger
            ));

            return time;
        }

        public double CreateTask(Logger logger)
        {
            var body = JsonConvert.SerializeObject(
            new {
                data = new {
                    name = "Test monitor task",
                    projects = new [] {
                        _config.ProjectId
                    },
                    workspace = _config.WorkspaceId
                }
            });

            logger(body);

            (double time, string responseBody) = Timed(() => MakeRequestAsync(
               $"{API_ROOT}/tasks",
               HttpMethod.Post,
               logger,
               body
               )
            );

            logger(responseBody);
            dynamic responseJson = JsonConvert.DeserializeObject(responseBody);
            _taskId = responseJson.data.gid.ToString();

            logger($"Created task with id {_taskId}");

            return time;
        }

        public double GetTask(Logger logger)
        {
            (double time, string responseBody) = Timed(() => MakeRequestAsync(
                $"{API_ROOT}/tasks/{_taskId}",
                HttpMethod.Get, 
                logger
            ));

            return time;
        }

        public double DeleteTask(Logger logger)
        {
            (double time, string responseBody) = Timed(() => MakeRequestAsync(
                $"{API_ROOT}/tasks/{_taskId}",
                HttpMethod.Delete, 
                logger
            ));

            return time;
        }

        public void Cleanup(Logger logger) 
        {
            var cutoff = DateTime.UtcNow.AddHours(-1);
            logger($"Running cleanup. Will remove any tasks for workspace id {_config.WorkspaceId} and project id {_config.ProjectId} that are older than {cutoff.ToString("o")}");

            var responseBody = MakeRequestAsync($"{API_ROOT}/tasks?project={_config.ProjectId}&opt_fields=name,created_at", HttpMethod.Get, logger).Result;
            dynamic responseJson = JsonConvert.DeserializeObject(responseBody);
            foreach (var m in responseJson.data) 
            {
                var task_id = m.gid.ToString();
                var name = m.name.ToString();
                var created_at = DateTime.Parse(m.created_at.ToString());

                if (created_at < cutoff) {
                    logger($"Deleting stale task with Id {task_id} as {created_at.ToString("o")} is older than {cutoff.ToString("o")}");
                    MakeRequestAsync($"{API_ROOT}/tasks/{task_id}", HttpMethod.Delete, logger).Wait();
                } else {
                    logger($"Not Deleting task with Id {task_id} as {created_at.ToString("o")} is newer than {cutoff.ToString("o")}");
                }
            }
        }

        private async Task<string> MakeRequestAsync(string url, HttpMethod method, Logger logger, string body = null) 
        {
            HttpResponseMessage response = null;
            try {
                switch (method) {
                    case HttpMethod m when m == HttpMethod.Post:
                        response = await _client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
                        break;
                    case HttpMethod m when m == HttpMethod.Get:
                        response = await _client.GetAsync(url);
                        break;
                    case HttpMethod m when m == HttpMethod.Delete:
                        response = await _client.DeleteAsync(url);
                        break;
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                logger(responseBody);
                response.EnsureSuccessStatusCode();
                return responseBody;
            } finally {
                response?.Dispose();
            }
        }
    }
}
