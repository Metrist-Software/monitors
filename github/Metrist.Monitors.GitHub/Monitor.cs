using Metrist.Core;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Octokit;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace Metrist.Monitors.GitHub
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string Username {get; set;}
        public string Repository {get; set;}
        public string ApiToken {get; set;}
        public string Organization {get; set;}
        public string PullRequestsUrl { get; set; } = "https://github.com/Metrist-Software/orchestrator/pulls";
        public string IssuesUrl { get; set; } = "https://github.com/Metrist-Software/orchestrator/issues";
        public string RawUrl { get; set; } = "https://raw.githubusercontent.com/Metrist-Software/orchestrator/main/README.md";
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly GitHubClient _client;
        private readonly string _runId;
        private readonly string _authHeader;
        private readonly CredentialsHandler _gitCredentialsProvider;
        private readonly string _baseDir;
        private Issue _createdIssue;
        private readonly HttpClient _webClient;
        private readonly string _branchPrefix;
        private readonly string _branchName;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            _client = new GitHubClient(productInformation: new ProductHeaderValue("canary-0.1"));
            _client.Credentials = new Octokit.Credentials(_config.ApiToken);
            _runId = Guid.NewGuid().ToString();
            _authHeader = "Basic " + Convert.ToBase64String(
                ASCIIEncoding.ASCII.GetBytes($"{_config.Username}:{_config.ApiToken}"));
            _gitCredentialsProvider = (_url, _user, _cred) =>
                    new UsernamePasswordCredentials { Username = _config.Username, Password = _config.ApiToken };
            _baseDir = $"/tmp/{_config.Username}-{_config.Repository}";
            _branchPrefix = $"monitor_{Environment.GetEnvironmentVariable("ORCHESTRATOR_REGION")}";
            _branchName = _branchPrefix + _runId;
            _webClient = new HttpClient();
        }

        public async Task CreateIssue(Logger logger)
        {
            _createdIssue = await _client.Issue.Create(OrganizationNameOrUsername(), _config.Repository, new NewIssue($"[CANARY]: {DateTime.UtcNow}"));
        }

        public async Task CloseIssue(Logger logger)
        {
            var updatedIssue = new IssueUpdate
            {
                State = ItemState.Closed
            };
            await _client.Issue.Update(OrganizationNameOrUsername(), _config.Repository, _createdIssue.Number, updatedIssue);
        }

             public void TriggerAction(Logger logger)
        {

            var url = $"https://api.github.com/repos/{OrganizationNameOrUsername()}/{_config.Repository}/actions/workflows/main.yml/dispatches";

            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post,url);
            request.Headers.Add("Accept","application/vnd.github.v3+json");
            request.Headers.Add("authorization", _authHeader);
            request.Headers.Add("user-agent", "dotnet-core/3.1");
            client.Send(request);
        }

        public void PullCode(Logger logger)
        {
            CleanBaseDir();
            var exitCode = RunGitWithArgs($"clone https://{_config.Username}:{_config.ApiToken}@github.com/{OrganizationNameOrUsername()}/{_config.Repository}.git {_baseDir}", logger);
            if (exitCode != 0)
            {
                throw new Exception("Clone failed with non 0 exit code.");
            }
        }

        public double PushCode(Logger logger)
        {
            Directory.SetCurrentDirectory(_baseDir);
            RunGitWithArgs($"checkout -b {_branchName}", logger);

            File.WriteAllText($"{_baseDir}/test.txt", _runId);

            RunGitWithArgs($"add {_baseDir}/test.txt", logger);
            RunGitWithArgs("commit -m \"Monitor commit\"", logger);

            int exitCode = 0;
            var time = Timed(() => {
                exitCode = RunGitWithArgs($"push https://{_config.Username}:{_config.ApiToken}@github.com/{OrganizationNameOrUsername()}/{_config.Repository}.git --all", logger);
            });
            if (exitCode != 0)
            {
                throw new Exception("Push failed with non 0 exit code.");
            }
            return time;
        }

        public void RemoveRemoteBranch(Logger logger)
        {
            Directory.SetCurrentDirectory(_baseDir);
            var exitCode = RunGitWithArgs($"push https://{_config.Username}:{_config.ApiToken}@github.com/{OrganizationNameOrUsername()}/{_config.Repository}.git --delete {_branchName}", logger);
            if (exitCode != 0)
            {
                throw new Exception("Remove Remote Branch failed with non 0 exit code.");
            }
        }

        public double PullRequests(Logger logger)
        {
            return DoWebRequest(_config.PullRequestsUrl);
        }

        public double Issues(Logger logger)
        {
            return DoWebRequest(_config.IssuesUrl);
        }

        public double Raw(Logger logger)
        {
            return DoWebRequest (_config.RawUrl);
        }

        public async Task Cleanup(Logger logger)
        {
            var authHeader = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiToken);
            var userAgentHeader = new System.Net.Http.Headers.ProductInfoHeaderValue("MetristMonitor", "1.0");

            var branchesRequest = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{OrganizationNameOrUsername()}/{_config.Repository}/branches");
            branchesRequest.Headers.Authorization = authHeader;
            branchesRequest.Headers.UserAgent.Add(userAgentHeader);

            var res = await _webClient.SendAsync(branchesRequest);

            var branches = JsonSerializer.Deserialize<BranchesListInfo[]>(await res.Content.ReadAsStringAsync());

            foreach(var branch in branches)
            {
                if (branch.Name == "main" || branch.Name == "master" || !branch.Name.StartsWith(_branchPrefix)) continue;
                var branchRequest = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{OrganizationNameOrUsername()}/{_config.Repository}/branches/{branch.Name}");
                branchRequest.Headers.Authorization = authHeader;
                branchRequest.Headers.UserAgent.Add(userAgentHeader);

                res = await _webClient.SendAsync(branchRequest);

                var parsedBranch = JsonSerializer.Deserialize<Branch>(await res.Content.ReadAsStringAsync());

                var branchAge = DateTime.UtcNow.Subtract(parsedBranch.Commit.Commit.Author.Date);

                if (branchAge.TotalMinutes > 30)
                {
                    logger($"Removing branch {parsedBranch.Name}");
                    var deleteBranchRequest = new HttpRequestMessage(HttpMethod.Delete, $"https://api.github.com/repos/{OrganizationNameOrUsername()}/{_config.Repository}/git/refs/heads/{branch.Name}");
                    deleteBranchRequest.Headers.Authorization = authHeader;
                    deleteBranchRequest.Headers.UserAgent.Add(userAgentHeader);

                    await _webClient.SendAsync(deleteBranchRequest);
                }
            }
        }

        private string OrganizationNameOrUsername()
        {
            // If running the monitor against an organization repo then we must use organization name which is optional.
            if (!String.IsNullOrWhiteSpace(_config.Organization)) {
                return _config.Organization;
            } else {
                return _config.Username;
            }
        }

        private double DoWebRequest(string url)
        {
            HttpResponseMessage response = null;
            double time;

            try
            {
                (time, response) = Timed(() => _webClient.GetAsync(
                    url
                ));

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    throw new Exception($"Unexpected status code of {response.StatusCode}");
                }

                return time;
            }
            finally
            {
                response?.Dispose();
            }
        }

        private int RunGitWithArgs(string args, Logger logger)
        {
            var startInfo = new ProcessStartInfo();
            startInfo.RedirectStandardError = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.FileName = "git";
            startInfo.Arguments = args;

            var process = Process.Start(startInfo);
            process.WaitForExit();
            logger($"stdout: {process.StandardOutput.ReadToEnd()}");
            logger($"stderr: {process.StandardError.ReadToEnd()}");
            return process.ExitCode;
        }

        private void CleanBaseDir()
        {
            if (Directory.Exists(_baseDir))
            {
                Directory.Delete(_baseDir, true);
            }
        }

        public class BranchesListInfo
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }
        }

        public class Branch
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }
            [JsonPropertyName("commit")]
            public CommitInfo Commit { get; set; }
        }

        public class CommitInfo
        {
            [JsonPropertyName("commit")]
            public Commit Commit { get; set; }
        }

        public class Commit
        {
            [JsonPropertyName("author")]
            public CommitAuthor Author { get; set; }
        }

        public class CommitAuthor
        {
            [JsonPropertyName("date")]
            public DateTime Date { get; set; }
        }
    }
}
