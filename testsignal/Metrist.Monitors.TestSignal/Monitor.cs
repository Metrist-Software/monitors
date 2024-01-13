using System;
using System.Threading;
using System.Threading.Tasks;
using Metrist.Core;
using MathNet.Numerics.Distributions;

namespace Metrist.Monitors.TestSignal
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public Poisson Poisson { get; internal set; } = new Poisson(3.0);
        public Normal Normal { get; internal set; } = new Normal(10.0, 2.0);
    }

    public class Monitor: BaseMonitor
    {
        private readonly MonitorConfig _config;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
        }

        public void Zero(Logger _)
        {
            Task.Delay(0).Wait();
        }

        public double Normal(Logger _) {
            return _config.Normal.Sample();
        }

        public double Poisson(Logger _)
        {
            return _config.Poisson.Sample();
        }

        public void Slow(Logger _)
        {
            Thread.Sleep(30000);
        }

        public void Crash(Logger _)
        {
            throw new Exception("TestSignal Crash always Crashes!");
        }
    }
}
