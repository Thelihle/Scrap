using System;

namespace SCP_Control.ViewModels
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public bool ShowRequestId { get; set; }
        public string? Message { get; set; }
        public string? ReturnUrl { get; set; }
        public int? StatusCode { get; set; }
        public Exception? Exception { get; set; }

        public ErrorViewModel()
        {
            RequestId = "Unknown";
            Message = "An error occurred. Please try again later.";
            ShowRequestId = !string.IsNullOrEmpty(RequestId);
        }

        public ErrorViewModel(string message)
        {
            RequestId = "Unknown";
            Message = message;
            ShowRequestId = !string.IsNullOrEmpty(RequestId);
        }

        public ErrorViewModel(Exception ex)
        {
            RequestId = "Unknown";
            Message = ex.Message;
            Exception = ex;
            ShowRequestId = !string.IsNullOrEmpty(RequestId);
        }
    }
}