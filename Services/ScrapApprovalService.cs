using Dapper;
using SCP_Control.Models;
using SCP_Control.Services.Interfaces;
using System.Data.SqlClient;

namespace SCP_Control.Services
{
    public class ScrapApprovalService
    {
        private readonly string _connectionString;
        private readonly IEmailService _emailService;
        private const string LogTemplate = "Processing approval for scrap {ScrapId}";

        // Regular constructor instead of primary constructor
        public ScrapApprovalService(IConfiguration configuration, IEmailService emailService)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _emailService = emailService;
        }

        public async Task<IEnumerable<ScrapData>> GetScrapDataForApproval(
            string userType,
            string? department,
            string? line,
            string? shift)
        {
            using var connection = new SqlConnection(_connectionString);
            var query = @"
                SELECT * FROM ScrapData 
                WHERE (@Department IS NULL OR Department = @Department)
                AND (@Line IS NULL OR Line = @Line)
                AND (@Shift IS NULL OR Shift = @Shift)";

            switch (userType.ToLower())
            {
                case "teamleader":
                    query += " AND Status = 'Submitted'";
                    break;
                case "groupleader":
                    query += " AND TeamLeaderApproval = 'Approved'";
                    break;
                case "manager":
                    query += " AND GroupLeaderApproval = 'Approved'";
                    break;
                case "seniormanager":
                    query += " AND ManagerApproval = 'Approved'";
                    break;
            }

            return await connection.QueryAsync<ScrapData>(query,
                new { Department = department, Line = line, Shift = shift });
        }

        public async Task<bool> ApproveScrap(int scrapId, string userType, string decision, string reason)
        {
            using var connection = new SqlConnection(_connectionString);
            var updateQuery = userType.ToLower() switch
            {
                "teamleader" => @"
                    UPDATE ScrapData 
                    SET TeamLeaderApproval = @Decision,
                        TeamLeaderApprovalDate = @Now,
                        TeamLeaderApprovalReason = @Reason,
                        Status = CASE WHEN @Decision = 'Approved' THEN 'TeamLeaderApproved' ELSE 'Rejected' END
                    WHERE ScrapId = @ScrapId",
                "groupleader" => @"
                    UPDATE ScrapData 
                    SET GroupLeaderApproval = @Decision,
                        GroupLeaderApprovalDate = @Now,
                        GroupLeaderApprovalReason = @Reason,
                        Status = CASE WHEN @Decision = 'Approved' THEN 'GroupLeaderApproved' ELSE 'Rejected' END
                    WHERE ScrapId = @ScrapId",
                _ => throw new ArgumentException("Invalid user type", nameof(userType))
            };

            var result = await connection.ExecuteAsync(updateQuery,
                new { ScrapId = scrapId, Decision = decision, Now = DateTime.UtcNow, Reason = reason });

            if (result > 0 && decision == "Rejected")
            {
                await NotifyRejection(scrapId, userType, reason);
            }

            return result > 0;
        }

        private async Task NotifyRejection(int scrapId, string userType, string reason)
        {
            using var connection = new SqlConnection(_connectionString);
            var scrap = await connection.QueryFirstOrDefaultAsync<ScrapData>(
                "SELECT * FROM ScrapData WHERE ScrapId = @ScrapId",
                new { ScrapId = scrapId });

            if (scrap != null)
            {
                var message = $"Scrap submission {scrapId} has been rejected by {userType}. Reason: {reason}";
                await _emailService.SendEmail(scrap.Email ?? "", "Scrap Submission Rejected", message);
            }
        }
    }
}