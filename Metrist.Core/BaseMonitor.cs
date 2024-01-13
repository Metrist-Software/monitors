using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Metrist.Core
{
    /// A simple logger function. Depending on how a monitor is run, full logging
    /// may not be available. Therefore, we pass this delegate around; the monitor
    /// running is responsible for making sure that it emits logging in a useful
    /// place.
    public delegate void Logger(string message);

    /// Simple function that can be used to ask for a wait on a webhook from
    /// an external processing source
    public delegate string WaitForWebhook(string uid);

    /// Monitor configuration. Every monitor is associated with a subclass
    /// of this base class, adding monitor-specific configuration (like API keys
    /// to use, etcetera.
    public class BaseMonitorConfig
    {
        /// Identifier for the monitor for logging purposes etc. Usually all
        /// lowercased.
        public string MonitorId { get; set; }
        /// Where the monitor is running; a region or host name.
        public string InstanceIdentifier { get; set; }
        /// Whether a cleanup should be run to make sure that unneeded
        /// data is removed from the monitored API
        public bool IsCleanupEnabled { get; set; }

        public BaseMonitorConfig() {}
        public BaseMonitorConfig(BaseMonitorConfig src)
        {
            MonitorId = src.MonitorId;
            InstanceIdentifier = src.InstanceIdentifier;
            IsCleanupEnabled = src.IsCleanupEnabled;
        }
    }

    /// Base class for all monitors. This class has a bunch of support code which, including
    /// the "Scenario" DSL, makes writing monitors easy.
    public abstract class BaseMonitor
    {
        public const string TRAP_ERROR_STRING = "METRIST_MONITOR_ERROR";

        private readonly BaseMonitorConfig _config;

        public BaseMonitor(BaseMonitorConfig config)
        {
            _config = config;
        }

        /// Time an action, return the elapsed time
        public static double Timed(Action a)
        {
            var (time, dummy) = Timed(() =>
            {
                a();
                return -1;
            });
            return time;
        }

        /// Time the duration of a task that the <c>taskMaker</c> argument
        /// passes in.
        public static double Timed(Func<Task> taskMaker)
        {
            return Timed(() =>
            {
                Task t = taskMaker();
                t.Wait();
            });
        }

        /// Time the duration of a task that the <c>taskMaker</c> argument
        /// passes in and return the result of the task.
        public static (double, T) Timed<T>(Func<Task<T>> taskMaker)
        {
            return Timed(() =>
            {
                var t = taskMaker();
                t.Wait();
                return t.Result;
            });
        }

        /// Time the duration of the function and return the result
        /// of calling the function.
        public static (double, T) Timed<T>(Func<T> a)
        {
            var watch = new Stopwatch();
            watch.Start();
            T result = a();
            watch.Stop();
            return (watch.ElapsedMilliseconds, result);
        }

        /// Wait on a task and return its result.
        public static T WaitWithResult<T>(Task<T> t)
        {
            t.Wait();
            return t.Result;
        }

        /// Utility for functions that deal with webhooks
        public double TimeWebhook(WaitForWebhook waiter, string dedupKey, DateTime startTime, Action<JObject> handler)
        {
            var response = waiter(dedupKey);
            var responseObj = JsonConvert.DeserializeObject<JObject>(response);
            var inserted = DateTime.Parse(responseObj["inserted_at"].Value<string>());
            var data = responseObj["data"].Value<string>();
            var obj = JsonConvert.DeserializeObject<JObject>(data);
            handler(obj);

            return (inserted - startTime).TotalMilliseconds;
        }
        public double TimeWebhook(WaitForWebhook waiter, string dedupKey, DateTime startTime)
        {
            return TimeWebhook(waiter, dedupKey, startTime, _ => {});
        }

        /// Time an action, but on certain exceptions retry. This can be used to
        /// retry when data isn't found (404 errors) because of propagation delays
        /// between API instances we're testing.
        /// There's no maximum time here - timeouts are configured in the backend and
        /// enforced by the orchestrator, so the monitor will get killed if this takes
        /// too long.
        public static double TimedWithRetries(Action a, Func<Exception, bool> shouldRetry)
        {
            return TimedWithRetries(a, shouldRetry, _ => { });
        }
        public static double TimedWithRetries(Func<Task> a, Func<Exception, bool> shouldRetry, Logger logger, int sleepTimeout = 1000)
        {
           return DoTimedWithRetries(() => Timed(a), shouldRetry, logger, sleepTimeout);
        }
        public static double TimedWithRetries(Action a, Func<Exception, bool> shouldRetry, Logger logger, int sleepTimeout = 1000)
        {
           return DoTimedWithRetries(() => Timed(a), shouldRetry, logger, sleepTimeout);
        }
        private static double DoTimedWithRetries(Func<double> a, Func<Exception, bool> shouldRetry, Logger logger, int sleepTimeout = 1000)
        {
            int retry = 0;
            while (true)
            {
                try
                {
                    logger($"Attempting retry {retry}");
                    var time = a();
                    logger($"Successful in {time} ms, returning");
                    return time;
                }
                catch (Exception ex)
                {
                    logger($"{TRAP_ERROR_STRING} - Exception caught: {ex.Message}");
                    if (shouldRetry(ex))
                    {
                        retry++;
                        logger("Should retry, sleeping");
                        Thread.Sleep(sleepTimeout);
                    }
                    else
                    {
                        logger("Not something we should retry on, rethrowing exception");
                        throw;
                    }
                }
            }
        }
    }
}
