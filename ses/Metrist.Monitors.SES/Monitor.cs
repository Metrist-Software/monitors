using System;
using Metrist.Core;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metrist.Monitors.SES
{
    public class MonitorConfig : BaseMonitorConfig
    {
        public string FromEmail { get; set; }
        public string ToEmail { get; set; }
        public string AwsAccessKeyId { get; set; }
        public string AwsSecretAccessKey { get; set; }
    }

    public class Monitor : BaseMonitor
    {
        private readonly MonitorConfig _config;

        public Monitor(MonitorConfig config) : base(config)
        {
            _config = config;
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", _config.AwsAccessKeyId);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", _config.AwsSecretAccessKey);
        }

        public async Task SendEmail(Logger logger)
        {
            using IAmazonSimpleEmailService emailService = new AmazonSimpleEmailServiceClient();
            var sendRequest = new SendEmailRequest
            {
                Source = $"\"Metrist Software\" <{_config.FromEmail}>",
                Destination = new Destination
                {
                    ToAddresses = new List<string> { _config.ToEmail  }
                },
                Message = new Message
                {
                    Subject = new Content($"SES Test from {_config.InstanceIdentifier}"),
                    Body = new Body
                    {
                        Html = new Content
                        {
                            Charset = "UTF-8",
                            Data = "<p>HTML Content would go here.</p>"
                        },
                        Text = new Content
                        {
                            Charset = "UTF-8",
                            Data = "Text content would go here"
                        }
                    }
                },
            };
            logger($"Sending email.");
            var emailResponse = await emailService.SendEmailAsync(sendRequest);
            logger($"Sent email with message id {emailResponse.MessageId}");
        }
    }
}
