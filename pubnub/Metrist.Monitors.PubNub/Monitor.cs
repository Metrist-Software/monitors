using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Metrist.Core;
using Newtonsoft.Json;
using PubnubApi;

namespace Metrist.Monitors.PubNub
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string UUID { get; set; }
        public string SubscribeKey { get; set; }
        public string PublishKey { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private Pubnub _pubnub;
        private readonly MonitorConfig _config;
        private bool _configured = false;
        private bool _connected;
        private bool _messageSent;
        private bool _receivedMessage;
        private string _errorMessage = null;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
        }

        private void ConfigurePubNub(Logger logger)
        {
            if (_configured)
                return;

            _connected = false;
            _receivedMessage = false;
            _errorMessage = null;

            logger($"Configuring pubnub with config {_config.SubscribeKey}:{_config.PublishKey}:{_config.UUID}");
            PNConfiguration pnConfiguration = new PNConfiguration(new UserId(_config.UUID));
            pnConfiguration.SubscribeKey = _config.SubscribeKey;
            pnConfiguration.PublishKey = _config.PublishKey;

            _pubnub = new Pubnub(pnConfiguration);
            _pubnub.AddListener(new SubscribeCallbackExt(
                delegate (Pubnub pnObj, PNMessageResult<object> pubMsg)
                {
                    logger($"METRIST_MONITOR_ERROR - {DateTime.UtcNow.ToString("MM/dd/yyyy hh:mm:ss.fff tt")} Incoming message from pubnub. {JsonConvert.SerializeObject(pubMsg)}");
                    if (pubMsg != null)
                    {
                        _receivedMessage = true;
                        string jsonString = pubMsg.Message.ToString();
                        Dictionary<string, string> msg = _pubnub.JsonPluggableLibrary.DeserializeToObject<Dictionary<string, string>>(jsonString);
                        logger($"msg: {msg["msg"]}");
                    }
                },
                delegate (Pubnub pnObj, PNPresenceEventResult presenceEvnt)
                {
                    logger($"METRIST_MONITOR_ERROR - Presence event. {JsonConvert.SerializeObject(presenceEvnt)}");
                    if (presenceEvnt != null)
                    {
                        logger(presenceEvnt.Channel + " " + presenceEvnt.Occupancy + " " + presenceEvnt.Event);
                    }
                },
                delegate (Pubnub pnObj, PNStatus pnStatus)
                {
                    logger($"METRIST_MONITOR_ERROR - Status update from pubnub. {JsonConvert.SerializeObject(pnStatus)}");
                    var errorMessage = pnStatus.ErrorData?.Information;

                    //Ignore these errors that are throwing all the time but seem to still allow the operation to continue.
                    if (!string.IsNullOrWhiteSpace(errorMessage)
                        && IgnoreError(errorMessage))
                        {
                            return;
                        }
                    //Will be null if this status message isn't an error
                    _errorMessage = pnStatus.ErrorData?.Information;
                    if (pnStatus.Category == PNStatusCategory.PNConnectedCategory)
                    {
                        _connected = true;
                    }
                }
            ));
            _configured = true;
        }

        private bool IgnoreError(string errorMessage)
        {
            if (errorMessage.Contains("Operation canceled.")) {
                return true;
            }

            if (errorMessage.Contains("The operation was canceled.")) {
                return true;
            }

            return false;
        }

        public async Task SubscribeToChannel(Logger logger)
        {
            ConfigurePubNub(logger);
            _pubnub.Subscribe<string>()
                .Channels(new string[]{
                    "my_channel"
                }).Execute();

            while(!_connected) {
                CheckForError();
                await Task.Delay(10);
            }
        }
        public double SendMessage(Logger logger)
        {
            ConfigurePubNub(logger);
            return Timed(() =>
            {
                logger("Sending pubnub message to my_channel.");
                Dictionary<string, string> message = new Dictionary<string, string>();
                message.Add("msg", "Hello world");

                _pubnub.Publish()
                    .Channel("my_channel")
                    .Message(message)
                    .Execute(new PNPublishResultExt((publishResult, publishStatus) =>
                    {
                        if (!publishStatus.Error)
                        {
                            _messageSent = true;
                            logger($"{DateTime.UtcNow.ToString("MM/dd/yyyy hh:mm:ss.fff tt")} Publish succesful. DateTime {DateTime.UtcNow.ToString("MM/dd/yyyy hh:mm:ss.fff tt")}, In Publish Example, Timetoken: {publishResult.Timetoken}");
                        }
                        else
                        {
                            _errorMessage = publishStatus.ErrorData.Information;
                            throw new System.Exception($"Error during publish. {publishStatus.ErrorData.Information}");
                        }
                    }));

                while(!_messageSent) {
                    CheckForError();
                    Thread.Sleep(10);
                }
            });
        }

        public async Task ReceiveMessage(Logger logger)
        {
            ConfigurePubNub(logger);
            while(!_receivedMessage) {
                CheckForError();
                await Task.Delay(10);
            }
        }

        private void CheckForError()
        {
            if (!string.IsNullOrWhiteSpace(_errorMessage)) {
                throw new Exception(_errorMessage);
            }
        }
    }
}
