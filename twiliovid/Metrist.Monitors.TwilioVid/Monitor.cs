using System;
using Metrist.Core;
using Twilio;
using Twilio.Rest.Video.V1;

namespace Metrist.Monitors.TwilioVid
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string AccountSid { get; set; }
        public string AuthToken { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;

        private RoomResource _room;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;

            TwilioClient.Init(_config.AccountSid, _config.AuthToken);
        }

        public void CreateRoom(Logger logger)
        {
            _room = RoomResource.Create(uniqueName: $"Monitor-{Guid.NewGuid()}", type: RoomResource.RoomTypeEnum.Group);
            logger($"Created room {_room.Sid}");
        }

        public void GetRoom(Logger logger)
        {
            RoomResource.Fetch(pathSid: _room.Sid);
        }

        public void CompleteRoom(Logger logger)
        {
            RoomResource.Update(
                status: RoomResource.RoomStatusEnum.Completed,
                pathSid: _room.Sid
            );
        }
    }
}
