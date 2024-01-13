using Metrist.Core;
using Stripe;
using System;

namespace Metrist.Monitors.Stripe
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string ApiKey { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        string _intentId;
        string _methodId;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            StripeConfiguration.ApiKey = _config.ApiKey;
        }

        public void CreateMethod(Logger logger) {
            var options = new PaymentMethodCreateOptions
            {
                Type = "card",
                Card = new PaymentMethodCardOptions
                {
                    Number = "4242424242424242",
                    ExpMonth = 12,
                    ExpYear = DateTime.UtcNow.AddYears(1).Year,
                    Cvc = "314",
                },
            };
            var service = new PaymentMethodService();
            var result = service.Create(options);
            _methodId = result.Id;
        }

        public void CreateIntent(Logger logger)
        {
            var options = new PaymentIntentCreateOptions
            {
                Amount = 2000,
                Currency = "usd",
                PaymentMethod = _methodId
            };
            var service = new PaymentIntentService();
            var result = service.Create(options);
            _intentId = result.Id;
        }

        public void ConfirmIntent(Logger logger) {
            var service = new PaymentIntentService();
            service.Confirm(_intentId);
        }

    }
}
