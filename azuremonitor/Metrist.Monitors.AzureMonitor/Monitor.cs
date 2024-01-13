using System;
using System.Threading;
using System.Threading.Tasks;
using Metrist.Core;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel;
using Microsoft.ApplicationInsights.WorkerService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Metrist.Monitors.AzureMonitor
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public MonitorConfig() { }
        public MonitorConfig(BaseMonitorConfig baseCfg) : base(baseCfg) { }
        public string ConnectionString { get; set; }
    }

    public class Monitor : BaseMonitor
    {

        private MonitorConfig _config;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
        }

        public Task<double> TrackEvent(Logger logger)
        {
            return WithClient(logger, (client, _) => client.TrackEvent("MonitorEvent"));
        }

        public Task<double> TrackMetricValue(Logger logger)
        {
            return WithClient(logger, (client, _) => client.GetMetric("MonitorMetric").TrackValue(123));
        }

        public Task<double> TrackExc(Logger logger)
        {
            return WithClient(logger, (client, _) => client.TrackException(new Exception("Monitor")));
        }

        public Task<double> TrackTrace(Logger logger)
        {
            return WithClient(logger, (client, _) => client.TrackTrace("Monitor Trace"));
        }

        public Task<double> SendLog(Logger logger)
        {
            return WithClient(logger, (_, log) => log.LogError("Monitor Log"));
        }

        public void Cleanup(Logger logger)
        {
            // Use Application Insight's data retention settings for the cleanup
            // see https://docs.microsoft.com/en-us/azure/azure-monitor/app/pricing#change-the-data-retention-period
        }

        private Task<double> WithClient(Logger monitorLogger, Action<TelemetryClient, ILogger> method)
        {
            var tcs = new TaskCompletionSource<double>();
            Task.Run(() =>
            {
                var services = new ServiceCollection();
                var channel = new ServerTelemetryChannel()
                {
                    TransmissionStatusEvent = (object _, TransmissionStatusEventArgs transmissionStatusEventArgs) =>
                    {
                        var response = transmissionStatusEventArgs.Response;
                        if (response.StatusCode >= 400)
                        {
                            tcs.TrySetException(new Exception($"Failed to call client. Status Code {response.StatusCode}"));
                            return;
                        }
                        tcs.TrySetResult(Convert.ToDouble(transmissionStatusEventArgs.ResponseDurationInMs));
                    }
                };
                services.AddLogging(loggingBuilder => loggingBuilder.AddFilter<Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider>("Category", LogLevel.Information));
                services.AddSingleton(typeof(ITelemetryChannel), channel);
                services.AddApplicationInsightsTelemetryWorkerService(new ApplicationInsightsServiceOptions()
                {
                    ConnectionString = _config.ConnectionString,
                    EnableHeartbeat = false,
                    EnableAdaptiveSampling = false,
                });
                var serviceProvider = services.BuildServiceProvider();
                var logger = serviceProvider.GetRequiredService<ILogger<Monitor>>();
                var telemetryClient = serviceProvider.GetRequiredService<TelemetryClient>();
                method.Invoke(telemetryClient, logger);
                // We need to flush so that the api calls will get triggered
                telemetryClient.Flush();
            });
            return tcs.Task;
        }
    }
}
