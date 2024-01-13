using System;
using Bambora.NA.SDK;
using Bambora.NA.SDK.Domain;
using Bambora.NA.SDK.Requests;
using Metrist.Core;
using Newtonsoft.Json;

namespace Metrist.Monitors.Bambora
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string PaymentsAPIKey { get; set; }
        public string MerchantId { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        const decimal PAYMENT_AMOUNT = 0.01M;
        private readonly MonitorConfig _config;
        private string _runId;
        private string _purchaseTransactionId;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            _runId = Guid.NewGuid().ToString();
        }

        public void TestPurchase(Logger logger)
        {
            var response = Purchase(GetBamboraGateway(), logger);
            _purchaseTransactionId = response.TransactionId;

            logger($"Purchase response: {JsonConvert.SerializeObject(response)}");
        }

        private Gateway GetBamboraGateway()
        {
            return new Gateway () {
                MerchantId = int.Parse(_config.MerchantId),
                PaymentsApiKey = _config.PaymentsAPIKey,
                ApiVersion = "1"
            };
        }

        private PaymentResponse Purchase(Gateway bambora, Logger logger, string orderNumberSuffix = null)
        {
            PaymentResponse response = bambora.Payments.MakePayment (
                new CardPaymentRequest {
                    Amount = PAYMENT_AMOUNT,
                    OrderNumber = $"{_runId}{orderNumberSuffix}",
                    Card = new Card {
                        Name = "John Doe",
                        Number = "4030000010001234",
                        ExpiryMonth = "12",
                        ExpiryYear = "25",
                        Cvd = "123",
                        Complete = true
                    }
                }
            );

            return response;
        }

        public void TestRefund(Logger logger)
        {
            var bambora = GetBamboraGateway();
            PaymentResponse response = bambora.Payments.Return (_purchaseTransactionId, new ReturnRequest {
                Amount = PAYMENT_AMOUNT,
                PaymentId = _purchaseTransactionId,
                MerchantId = _config.MerchantId,
                OrderNumber = $"{_runId}"
            });

            logger($"Refund response: {JsonConvert.SerializeObject(response)}");
        }

        public double TestVoid(Logger logger)
        {
            var bambora = GetBamboraGateway();
            var purchaseResponse = Purchase(bambora, logger, "void");
            logger($"Void Purchase Response: {JsonConvert.SerializeObject(purchaseResponse)}");

            return Timed(() => {
                PaymentResponse voidResponse = bambora.Payments.Void (purchaseResponse.TransactionId, PAYMENT_AMOUNT);
                logger($"Void Response: {JsonConvert.SerializeObject(voidResponse)}");
            });
        }
    }
}
