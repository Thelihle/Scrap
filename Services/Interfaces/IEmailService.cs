﻿namespace SCP_Control.Services.Interfaces
{
    public interface IEmailService
    {
        Task SendEmail(string to, string subject, string body);


    }
}
