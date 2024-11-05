using SCP_Control.Services.Interfaces;

namespace SCP_Control.Services
{
    public class SMSService : ISMSService
    {
        private readonly ILogger<SMSService> _logger;

        public SMSService(ILogger<SMSService> logger)
        {
            _logger = logger;
        }

        public async Task SendSMS(string phoneNumber, string message)
        {
            // Implement your SMS sending logic here
            _logger.LogInformation($"Sending SMS to {phoneNumber}: {message}");
            await Task.CompletedTask;
        }
    }
}
