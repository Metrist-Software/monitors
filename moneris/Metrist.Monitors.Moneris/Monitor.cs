using System;
using Metrist.Core;
using Moneris;

namespace Metrist.Monitors.Moneris
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string StoreId;
        public string ApiToken;
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;
        private readonly string _runId;
        private string _purchaseTransactionId;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            _runId = Guid.NewGuid().ToString();
        }

        public void TestPurchase(Logger logger)
        {
            var order_id = "Test-" + _runId;
            var amount = "5.00";
            var pan = "4242424242424242";
            var crypt = "7";
            var processing_country_code = "CA";
            var status_check = false;

            var cof = new CofInfo();
            cof.SetPaymentIndicator("U");
            cof.SetPaymentInformation("2");
            cof.SetIssuerId("168451306048014");

            var purchase = new Purchase();
            purchase.SetOrderId(order_id);
            purchase.SetAmount(amount);
            purchase.SetPan(pan);
            purchase.SetExpDate("2011");
            purchase.SetCryptType(crypt);
            purchase.SetDynamicDescriptor("2134565");
            purchase.SetCofInfo(cof);

            var mpgReq = new HttpsPostRequest();
            mpgReq.SetProcCountryCode(processing_country_code);
            mpgReq.SetTestMode(true);
            mpgReq.SetStoreId(_config.StoreId);
            mpgReq.SetApiToken(_config.ApiToken);
            mpgReq.SetTransaction(purchase);
            mpgReq.SetStatusCheck(status_check);
            mpgReq.Send();

            try
            {
                var receipt = mpgReq.GetReceipt();
                _purchaseTransactionId = receipt.GetTransactionId();
            }
            catch (Exception e)
            {
                logger("Error during purchase: " + e.Message);
                throw;
            }
        }

        public void TestRefund(Logger logger)
        {
            var amount = "1.00";
            var crypt = "7";
            var dynamic_descriptor = "123456";
            var custid = "mycust9";
            var order_id = "Test-" + _runId;
            var txn_number = _purchaseTransactionId;
            var processing_country_code = "CA";
            var status_check = false;

            var refund = new Refund();
            refund.SetTxnNumber(txn_number);
            refund.SetOrderId(order_id);
            refund.SetAmount(amount);
            refund.SetCryptType(crypt);
            refund.SetCustId(custid);
            refund.SetDynamicDescriptor(dynamic_descriptor);

            var mpgReq = new HttpsPostRequest();
            mpgReq.SetProcCountryCode(processing_country_code);
            mpgReq.SetTestMode(true);
            mpgReq.SetStoreId(_config.StoreId);
            mpgReq.SetApiToken(_config.ApiToken);
            mpgReq.SetTransaction(refund);
            mpgReq.SetStatusCheck(status_check);
            mpgReq.Send();

            try
            {
                Receipt receipt = mpgReq.GetReceipt();
            }
            catch (Exception e)
            {
                logger("Error during refund: " + e.Message);
                throw;
            }
        }
    }
}
