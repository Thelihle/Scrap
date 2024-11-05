namespace SCP_Control.Services.Interfaces
{
    public interface ISMSService
    {
        // Make the method explicitly public
        public Task SendSMS(string phoneNumber, string message);
    }
}
