using SCP_Control.Services.Interfaces;

namespace SCP_Control.Services
{
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;
        private const string EmailLogTemplate = "Sending email to {EmailAddress} with subject: {Subject}";

        public EmailService(ILogger<EmailService> logger)
        {
            _logger = logger;
        }

        public async Task SendEmail(string to, string subject, string body)
        {
            _logger.LogInformation(EmailLogTemplate, to, subject);
            await Task.CompletedTask;
        }
    }
}