using BT = Braintree;
using Braintree;
using Metrist.Core;
using System;

namespace Metrist.Monitors.Braintree
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string MerchantId { get; set; }
        public string PublicKey { get; set; }
        public string PrivateKey{ get; set; }
        public string CustomerId { get; set; }
    }
    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        public BraintreeGateway Gateway { get; set; }

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            Gateway = new BraintreeGateway
            {
                Environment = BT.Environment.SANDBOX,
                MerchantId = _config.MerchantId,
                PublicKey = _config.PublicKey,
                PrivateKey = _config.PrivateKey,
            };
        }

        public void SubmitSandboxTransaction(Logger logger)
        {
            /**
            * Other payment options:
            *   1. Use credit card credentials directly in monitor:
            *     CreditCard = new TransactionCreditCardRequest
            *     {
            *         Number = "4111111111111111",
            *         ExpirationDate = "06/22",
            *         CVV = "100",
            *     }
            *   --------------------------
            *   2. Use frontend-generated nonce (faked in sandbox):
            *     PaymentMethodNonce = "fake-valid-nonce",
            *     DeviceData = Guid.NewGuid().ToString(),
            *
            **/

            // Charge a pre-existing customer stored in the Braintree customer vault
            var request = new TransactionRequest
            {
                Amount = 1.00m,
                CustomerId = _config.CustomerId,
                Options = new TransactionOptionsRequest
                {
                    SubmitForSettlement = true,
                }
            };

            Result<Transaction> result = Gateway.Transaction.Sale(request);

            if (result.Errors != null)
            {
                throw new Exception(result.Message);
            }
        }
    }
}
