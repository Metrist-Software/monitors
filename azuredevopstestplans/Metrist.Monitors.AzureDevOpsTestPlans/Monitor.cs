using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Metrist.Core;
using Newtonsoft.Json;

namespace Metrist.Monitors.AzureDevOpsTestPlans
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public MonitorConfig() {}
        public MonitorConfig(BaseMonitorConfig baseCfg) : base(baseCfg) {}

        public string Organization { get; set ;}
        public string Project { get; set; }
        public string Team { get; set; }
        public string PersonalAccessToken { get; set ;}

        public string ApiRoot 
        {
            get
            {
                return $"https://dev.azure.com/{this.Organization}/{this.Project}/_apis/";
            }
        }

        public string TestRootUrl { get { return $"{this.ApiRoot}test"; } }
        public string TestPlanRoot { get { return $"{this.ApiRoot}testplan"; } }
        public string WorkItemUrl { get { return $"{this.ApiRoot}wit/workitems"; } }   
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private HttpClient _client;
        private TestPlan _testPlan;
        private SuiteValue _testSuite;
        private WorkItem _testCase;
        private AddTestCaseResponse _testCaseAssignments;
        private TestRun _testRun;
        private TestCaseResults _testCaseResults;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            _client = SetupHttpClient();
        }

        public async Task CreateTestCase(Logger logger) 
        {
            var body = JsonConvert.SerializeObject(new List<object>
            {
                new {
                    op = "add",
                    path = "/fields/System.Title",
                    from = "",
                    value = "New Test Case"
                },
                new {
                    op = "add", 
                    path = "/fields/Microsoft.VSTS.TCM.Steps",
                    value = "<steps id=\"0\" last=\"1\"><step id=\"2\" type=\"ValidateStep\"><parameterizedString isformatted=\"true\">Input step 1</parameterizedString><parameterizedString isformatted=\"true\">Expectation step 1</parameterizedString><description/></step></steps>"  

                }
            });

            _testCase = await DoPatchPost<WorkItem>(body, $"{_config.WorkItemUrl}/$Test Case?api-version=6.0", logger);
        }

        public async Task CreateTestPlan(Logger logger)
        {
            var body = JsonConvert.SerializeObject(
                new {
                    name = "monitor-test-plan",
                    description = "Test plan for monitor"
                }
            );

            _testPlan = await DoStandardPost<TestPlan>(body, $"{_config.TestPlanRoot}/plans?api-version=6.0-preview.1", logger);
        }

        public async Task CreateTestSuite(Logger logger)
        {
            var body = JsonConvert.SerializeObject(
                new {
                    suiteType = "staticTestSuite",
                    name = "MonitorTestSuite",
                    parentSuite = new {
                        id = _testPlan.rootSuite.id
                    }
                }
            );

            _testSuite = await DoStandardPost<SuiteValue>(body, $"{_config.TestPlanRoot}/Plans/{_testPlan.id}/suites?api-version=6.0-preview.1", logger);
        }

        public async Task AddTestCasesToSuite(Logger logger) 
        {
            var body = JsonConvert.SerializeObject(new List<object> {
                new {
                    workItem = new {
                        id = _testCase.id
                    }
                }
            }
            );

            _testCaseAssignments = await DoStandardPost<AddTestCaseResponse>(body, $"{_config.TestPlanRoot}/Plans/{_testPlan.id}/Suites/{_testSuite.id}/TestCase?api-version=6.0-preview.2", logger);
        }

        public async Task CreateTestRun(Logger logger) 
        {
            var body = JsonConvert.SerializeObject(
                new {
                    name = "TestRun",
                    plan = new {
                        id = _testPlan.id
                    },
                    pointIds = new [] {
                        _testCaseAssignments.value.First().pointAssignments.First().id
                    }
                }
            );

            _testRun = await DoStandardPost<TestRun>(body, $"{_config.TestRootUrl}/runs?api-version=6.0", logger);
        }

        public async Task AddResultsToTestRun(Logger logger) 
        {
            var body = JsonConvert.SerializeObject(new List<object> {
                new {
                    testCaseTitle = "New Test Case",
                    priority = 1,
                    outcome = "Passed"
                }
            }
            );

            _testCaseResults = await DoStandardPost<TestCaseResults>(body, $"{_config.TestRootUrl}/Runs/{_testRun.id}/results?api-version=6.0", logger);

        }

        public async Task GetResults(Logger logger) 
        {
            var response = await _client.GetAsync(
                $"{_config.TestRootUrl}/Runs/{_testRun.id}/results/{_testCaseResults.value.First().id}?api-version=6.0"
            );
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            logger(content);        
        }

        public async Task DeleteTestRun(Logger logger) 
        {
            await DoDeleteTestRun(_testRun.id);
        }

        public async Task DeleteTestPlan(Logger logger) 
        {
            await DoDeleteTestPlan(_testPlan.id);
        }

        public async Task DeleteTestCase(Logger logger) 
        {
            await DoDeleteTestCase(_testCase.id);
        }

        public void TearDown(Logger logger)
        {
            // Dispose as Cleanup may not be called by the orchestrator
            _client?.Dispose();
        }

        public void Cleanup(Logger logger)
        {
            try 
            {
                logger("Running cleanup");
                // Teardown will dispose the original client as cleanup is not called in every region so reset it to a new instance
                _client = SetupHttpClient();
                CleanupTestRuns(logger);
                CleanupTestPlans(logger);
                CleanupTestCases(logger);
                CleanupTestSuites(logger);
            } 
            finally 
            {
                _client?.Dispose();
            }
        }

        private async Task DoDeleteTestPlan(int id) 
        {
            var response = await _client.DeleteAsync($"{_config.TestPlanRoot}/plans/{id}?api-version=6.0");
            response.EnsureSuccessStatusCode();
        }       

        private async Task DoDeleteTestRun(int id) 
        {
            var response = await _client.DeleteAsync($"{_config.TestRootUrl}/runs/{id}?api-version=6.0");
            response.EnsureSuccessStatusCode();
        }

        private async Task DoDeleteTestCase(int id) 
        {
            var response = await _client.DeleteAsync($"{_config.TestRootUrl}/testcases/{id}?api-version=6.0-preview.1");
            response.EnsureSuccessStatusCode();
        }

        private async Task<R> DoPatchPost<R>(string jsonBody, string url, Logger logger) 
        {
            // Needs a different content type for Work Item operations
            return await DoPost<R>(jsonBody, url, "application/json-patch+json", logger);
        }   

        private async Task<R> DoStandardPost<R>(string jsonBody, string url, Logger logger) 
        {
            return await DoPost<R>(jsonBody, url, "application/json", logger);
        }

        private async Task<R> DoPost<R>(string jsonBody, string url, string contentType, Logger logger) 
        {
            logger(url);
            logger(jsonBody);
            var response = await _client.PostAsync(
                url,
                new StringContent(jsonBody, Encoding.UTF8, contentType)
            );
            response.EnsureSuccessStatusCode();            

            var content = await response.Content.ReadAsStringAsync();
            logger(content);
            return JsonConvert.DeserializeObject<R>(content);
        }
        private HttpClient SetupHttpClient() 
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_config.PersonalAccessToken}"))
            );
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json-patch+json"));
            client.Timeout = Timeout.InfiniteTimeSpan;

            return client;
        }

        private void CleanupTestRuns(Logger logger)
        {
            // Standard GET without query returns deleted runs... this one doesn't.
            var url = $"{_config.TestRootUrl}/runs?minLastUpdatedDate={DateTime.UtcNow.AddDays(-5).ToString("o")}&maxLastUpdatedDate={DateTime.UtcNow.AddDays(-1).ToString("o")}&api-version=6.0";
            var response = _client.GetAsync(
                url
            ).Result;
            response.EnsureSuccessStatusCode();

            var content = response.Content.ReadAsStringAsync().Result;
            logger(content);

            var testRuns = JsonConvert.DeserializeObject<ListTestRunResponse>(content);
            if (testRuns != null) 
            {
                testRuns.value.ForEach(o => {
                    logger($"Deleting test run with id {o.id}");
                    DoDeleteTestRun(o.id).Wait();
                });
            }
        }

        // Every once in a while AzureDevOps will fail its own cleanup leaving orphaned objects around.
        // Deleting test plans are supposed to delete all suites and artifacts and normally do but when they don't
        // the plan is sometimes deleted, sometimes moved to a test suite, or sometimes the suite sticks around...
        // This deals with that.
        // Because the plans are gone you can't use the normal test suite delete as you no longer have a planid (It's gone off the suites too)
        // You also can't directly delete a testsuite using the WIT API's so change the type of the orphans to "Task" and then delete with the WIT API's
        private void CleanupTestSuites(Logger logger)
        {
            var data = QueryWorkItemsByTypeAsync("Test Suite", logger);
            if (data != null) { 
                data.workItems.ForEach(workItem =>
                {
                    var patchUrl = $"https://dev.azure.com/{_config.Organization}/{_config.Project}/_apis/wit/workitems/{workItem.id}?bypassRules=true&api-version=6.0";

                    var patchBody = JsonConvert.SerializeObject(new List<object>
                    {
                        new {
                            op = "add",
                            path = "/fields/System.WorkItemType",
                            value = "Task"
                        }
                    });

                    var patchResult = _client.PatchAsync(patchUrl, new StringContent(patchBody, Encoding.UTF8, "application/json-patch+json")).Result;
                    patchResult.EnsureSuccessStatusCode();

                    var deleteUrl = $"https://dev.azure.com/{_config.Organization}/{_config.Project}/_apis/wit/workitems/{workItem.id}?destroy=true&api-version=6.0";
                    logger($"Deleting workitem {workItem.id} - deleteUrl {deleteUrl}");
                    var res = _client.DeleteAsync(deleteUrl).Result;
                    logger(res.Content.ReadAsStringAsync().Result);
                    res.EnsureSuccessStatusCode();
                });
            }              
        }             

        private void CleanupTestPlans(Logger logger)
        {
            // Include plan details so that we have the updatedDate
            var response = _client.GetAsync(
                $"{_config.TestPlanRoot}/plans?includePlanDetails=True&filterActivePlans=False&api-version=6.0-preview.1"
            ).Result;
            response.EnsureSuccessStatusCode();

            var content = response.Content.ReadAsStringAsync().Result;
            logger(content);

            var testPlans = JsonConvert.DeserializeObject<ListTestPlansrResponse>(content);
            if (testPlans != null) 
            {
                testPlans.value.ForEach(o => { 
                    if ((DateTime.UtcNow - o.updatedDate).TotalDays > 1.0) { 
                        logger($"Deleting test plan with id {o.id}");
                        DoDeleteTestPlan(o.id).Wait();
                    } 
                });
            }     
        }

        private void CleanupTestCases(Logger logger) 
        {
            var data = QueryWorkItemsByTypeAsync("Test Case", logger);

            if (data != null) 
            {
                data.workItems.ForEach(workItem =>
                {
                    logger($"Deleting test case with id {workItem.id}");
                    DoDeleteTestCase(workItem.id).Wait();
                });          
            }      
        }

        private WorkItemsResponse QueryWorkItemsByTypeAsync(string type, Logger logger) 
        {
            var queryUrl = $"https://dev.azure.com/{_config.Organization}/{_config.Project}/_apis/wit/wiql?api-version=6.0";
            var body = JsonConvert.SerializeObject(new{
                query = $"Select [System.Id] From WorkItems Where [System.CreatedDate] < @StartOfDay - 1 and [Work Item Type] = '{type}' order by [Microsoft.VSTS.Common.Priority] asc, [System.CreatedDate] desc"
            });

            var response = _client.PostAsync(
                queryUrl,
                new StringContent(body, Encoding.UTF8, "application/json")
            ).Result;
            response.EnsureSuccessStatusCode();

            var content = response.Content.ReadAsStringAsync().Result;
            logger(content);

            return JsonConvert.DeserializeObject<WorkItemsResponse>(content);
        }
    }

    #region AutoGenerated JSON Deserialization Objects

    internal class WorkItemsResponse
    {
        public List<WorkItem> workItems { get; set; }
    }

    internal class RootSuite
    {
        public string id { get; set; }
    }

    internal class TestPlan
    {
        public int id { get; set; }
        public RootSuite rootSuite { get; set; }
        public DateTime updatedDate { get; set; }
    }

    internal class Plan
    {
        public string id { get; set; }
    }

    internal class LastUpdatedBy
    {
        public string id { get; set; }
    }

    internal class SuiteValue
    {
        public int id { get; set; }
    }

    internal class TestSuite
    {
        public List<SuiteValue> value { get; set; }
    }

    internal class WorkItem
    {
        public int id { get; set; }
    }

    internal class TestCase
    {
        public string id { get; set; }
    }

    internal class PointAssignment
    {
        public int id { get; set; }
    }

    internal class TestCaseResponseValue
    {
        public List<PointAssignment> pointAssignments { get; set; }
    }

    internal class AddTestCaseResponse
    {
        public int count { get; set; }
        public List<TestAssignmentResponse> value { get; set; }
    }

    internal class TestAssignmentResponse 
    {
        public TestPlan testPlan { get; set; }
        public TestSuite testSuite { get; set; }
        public WorkItem workItem { get; set; }
        public List<PointAssignment> pointAssignments { get; set; }
    }

    internal class TestRun
    {
        public int id { get; set; }
        public DateTime createdDate { get; set; }
        public DateTime lastUpdatedDate { get; set; }
    }

    internal class TestCaseResultValue
    {
        public int id { get; set; }
    }

    internal class TestCaseResults
    {
        public List<TestCaseResultValue> value { get; set; }
    }

    internal class TestPlanListResponse
    {
        public List<TestPlan> value { get; set; }
    }

    internal class ListTestRunResponse 
    {
        public List<TestRun> value { get; set; }
    }

    internal class ListTestPlansrResponse 
    {
        public List<TestPlan> value { get; set; }
    }

    #endregion
}
