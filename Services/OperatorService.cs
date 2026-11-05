using System.Data.SqlClient;
using Dapper;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SCP_Control.DbContext;
using SCP_Control.Models;
using SCP_Control.Services.Interfaces;
using SCP_Control.ViewModels;

namespace SCP_Control.Services
{
    public class OperatorService : IOperatorService
    {
        private readonly string _connectionString;
        private readonly ILogger<OperatorService> _logger;
        private readonly ApplicationDbContext _context;

        public OperatorService(
            IConfiguration configuration,
            ApplicationDbContext context,
            ILogger<OperatorService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection") ??
                throw new ArgumentNullException(nameof(configuration));
            _context = context;
            _logger = logger;
        }

        public async Task<ScrapSection?> GetScrapSectionById(int sectionId)
        {
            return await _context.scpScrapSection
                .FirstOrDefaultAsync(s => s.ScrapSectionId == sectionId);
        }


        public async Task<List<SelectListItem>> GetScrapReasons()
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                const string query = @"
                SELECT 
                    ErrorId,
                    ScrapReasonName
                FROM [IntegrationCompanyB].[dbo].[scpReason]
                WHERE IsActive = 1
                ORDER BY ScrapReasonName";

                var reasons = await connection.QueryAsync<ScrapReason>(query);
                return reasons.Select(r => new SelectListItem
                {
                    Value = r.ErrorId.ToString(),
                    Text = r.ScrapReasonName
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving scrap reasons");
                return new List<SelectListItem>();
            }
        }

        public async Task<ScrapReason?> GetScrapReasonDetails(int reasonId)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                const string query = @"
                SELECT 
                    ErrorId,
                    ScrapReasonName,
                    Part_Position,
                    Defect_Item
                FROM [IntegrationCompanyB].[dbo].[scpReason]
                WHERE ReasonId = @ReasonId 
                AND IsActive = 1";

                return await connection.QueryFirstOrDefaultAsync<ScrapReason>(
                    query, new { ReasonId = reasonId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving scrap reason details for ID {ReasonId}", reasonId);
                return null;
            }
        }

        public async Task<(bool Success, string Message)> UpdateScrapEntry(UpdateScrapEntryRequest request)
        {
            // Fix: Create proper SqlConnection
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                const string updateQuery = @"
                UPDATE [IntegrationCompanyB].[dbo].[scpScrapData]
                SET Quantity = @Quantity,
                    TerminalQuantity = @TerminalQuantity,
                    WeightKg = @WeightKg,
                    Length = @Length,
                    Position = @Position,
                    ModifiedAt = GETDATE()
                WHERE ScrapId = @EntryId";

                await connection.ExecuteAsync(updateQuery, request, transaction);

                transaction.Commit();
                return (true, "Entry updated successfully");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<(bool Success, string Message)> DeleteScrapEntry(int entryId)
        {
            // Fix: Create proper SqlConnection
            await using var connection = new SqlConnection(_connectionString);
            const string query = @"
            DELETE FROM [IntegrationCompanyB].[dbo].[scpScrapData]
            WHERE ScrapId = @EntryId AND Status = 'Pending'";

            var rowsAffected = await connection.ExecuteAsync(query, new { EntryId = entryId });
            return rowsAffected > 0
                ? (true, "Entry deleted successfully")
                : (false, "Entry not found or already processed");
        }

        public async Task<List<PendingScrapEntry>> GetPendingScrapEntries(string clockNumber)
        {
            // Fix: Create proper SqlConnection
            await using var connection = new SqlConnection(_connectionString);
            const string query = @"
            SELECT 
                s.ScrapId as EntryId,
                s.PartNumber,
                ss.ScrapSectionName as SectionName,
                sce.ScrapReasonName as ReasonName,
                s.Quantity,
                s.TerminalQuantity,
                s.WeightKg,
                s.Length,
                s.Position,
                s.CreatedAt
            FROM [IntegrationCompanyB].[dbo].[scpScrapData] s
            JOIN [IntegrationCompanyB].[dbo].[scpScrapSection] ss ON s.ScrapSectionId = ss.ScrapSectionId
            JOIN [IntegrationCompanyB].[dbo].[scpReason] sce ON s.ReasonId = sce.ReasonId
            WHERE s.ClockNumber = @ClockNumber 
            AND s.Status = 'Pending'
            ORDER BY s.CreatedAt DESC";

            return (await connection.QueryAsync<PendingScrapEntry>(query, new { ClockNumber = clockNumber })).ToList();
        }

        public async Task<OperatorDetailsViewModel?> GetOperatorDetails(string clockNumber)
        {
            await using var connection = new SqlConnection(_connectionString);
            var query = @"
                SELECT Department, Line, Shift 
                FROM scpUser 
                WHERE ClockNumber = @ClockNumber";

            return await connection.QueryFirstOrDefaultAsync<OperatorDetailsViewModel>(
                query, new { ClockNumber = clockNumber });
        }

        public async Task<List<Models.Department>> GetActiveDepartments()
        {
            await using var connection = new SqlConnection(_connectionString);
            var query = "SELECT * FROM scpDepartment WHERE IsActive = 1";
            var departments = await connection.QueryAsync<Models.Department>(query);
            return departments.ToList();
        }

        public async Task<List<Line>> GetLinesByDepartment(string departmentName)
        {
            await using var connection = new SqlConnection(_connectionString);
            var query = @"
                SELECT l.* 
                FROM scpLine l
                JOIN scpDepartment d ON l.DepartmentId = d.DepartmentId
                WHERE d.DepartmentName = @DepartmentName 
                AND l.IsActive = 1";

            var lines = await connection.QueryAsync<Line>(query, new { DepartmentName = departmentName });
            return lines.ToList();
        }

        public async Task<bool> UpdateOperatorDetails(string clockNumber, UpdateOperatorDetailsViewModel model)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                var query = @"
                    UPDATE scpUser 
                    SET Department = @Department,
                        Line = @Line,
                        Shift = @Shift,
                        LastModifiedAt = GETDATE()
                    WHERE ClockNumber = @ClockNumber";

                var result = await connection.ExecuteAsync(query, new
                {
                    model.Department,
                    model.Line,
                    model.Shift,
                    ClockNumber = clockNumber
                });

                return result > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating operator details for clock number {ClockNumber}", clockNumber);
                throw;
            }
        }

        public async Task<List<ScrapError>> GetScrapErrorsBySection(int sectionId)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                const string query = @"
                SELECT 
                    ReasonId as ErrorId,
                    ScrapSectionId,
                    ScrapReasonName as ErrorName,
                    CASE WHEN Part_Position IS NOT NULL THEN 1 ELSE 0 END as RequiresPosition,
                    Defect_Item as DefectItem,
                    Part_Description as PartDescription,
                    IsActive
                FROM [IntegrationCompanyB].[dbo].[scpReason]
                WHERE ScrapSectionId = @SectionId
                AND IsActive = 1
                ORDER BY ScrapReasonName";

                var errors = await connection.QueryAsync<ScrapError>(query, new { SectionId = sectionId });
                return errors.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving scrap errors for section {SectionId}", sectionId);
                throw;
            }
        }

        public async Task<(bool IsValid, string Message)> ValidateScrapEntry(ScrapEntryValidationRequest request)
        {
            if (request == null)
                return (false, "Invalid request");

            try
            {
                await using var connection = new SqlConnection(_connectionString);

                // Validate section exists
                const string sectionQuery = @"
                SELECT COUNT(1) FROM scpScrapSection 
                WHERE ScrapSectionId = @SectionId AND IsActive = 1";

                var sectionExists = await connection.ExecuteScalarAsync<int>(
                    sectionQuery, new { SectionId = request.ScrapSectionId }) > 0;

                if (!sectionExists)
                    return (false, "Invalid scrap section");

                // Additional validation logic as needed
                if (request.Quantity <= 0)
                    return (false, "Quantity must be greater than zero");

                return (true, "Validation successful");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating scrap entry");
                throw;
            }
        }

        public async Task<List<SelectListItem>> GetActiveScrapSections()
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                const string query = @"
            SELECT 
                ScrapSectionId,
                ScrapSectionName
            FROM [IntegrationCompanyB].[dbo].[scpScrapSection]
            WHERE IsActive = 1
            ORDER BY ScrapSectionName";

                var sections = await connection.QueryAsync<dynamic>(query);

                return sections.Select(s => new SelectListItem
                {
                    Value = s.ScrapSectionId.ToString(),
                    Text = s.ScrapSectionName
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving scrap sections");
                return new List<SelectListItem>();
            }
        }
        
        public async Task<List<SelectListItem>> GetScrapReasonsBySection(int sectionId)
        {
            try
            {
                using var connection = new SqlConnection(_connectionString);
                const string query = @"
            SELECT DISTINCT
                ErrorId,
                ScrapReasonName
            FROM [IntegrationCompanyB].[dbo].[scpReason]
            WHERE ScrapSectionId = @SectionId
                AND IsActive = 1
            ORDER BY ScrapReasonName";

                var reasons = await connection.QueryAsync<ScrapReason>(
                    query, new { SectionId = sectionId });

                return reasons.Select(r => new SelectListItem
                {
                    Value = r.ErrorId.ToString(),
                    Text = r.ScrapReasonName
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting reasons for section {SectionId}", sectionId);
                throw;
            }
        }

        public async Task<ScrapEntry?> GetExistingScrapEntry(string partNumber)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                const string query = @"
                SELECT TOP 1 ScrapId, PartNumber, ScrapSectionId, ScrapReason,
                       Quantity, TerminalQuantity, WeightKg, Length, Position
                FROM [IntegrationCompanyB].[dbo].[scpScrapData]
                WHERE PartNumber = @PartNumber
                AND Status = 'Pending'";

                return await connection.QueryFirstOrDefaultAsync<ScrapEntry>(query, new { PartNumber = partNumber });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking existing scrap entry for part {PartNumber}", partNumber);
                return null;
            }
        }

        public async Task<List<string>> GetPartPositions(int sectionId, int reasonId)
        {
            try
            {
                _logger.LogInformation("Getting positions for section {SectionId} and reason {ReasonId}", sectionId, reasonId);

                await using var connection = new SqlConnection(_connectionString);
                const string query = @"
            SELECT DISTINCT Part_Position
            FROM [IntegrationCompanyB].[dbo].[scpReason]
            WHERE ScrapSectionId = @SectionId 
            AND Part_Position IS NOT NULL
            AND Part_Position <> ''
            AND IsActive = 1
            ORDER BY Part_Position";

                var positions = await connection.QueryAsync<string>(query, new { SectionId = sectionId });

                // If no positions found, return default list for CombiMeter
                if (!positions.Any() && sectionId == 1)
                {
                    var defaultPositions = new List<string>
            {
                "AMBIENT PRISM",
                "BEZEL",
                "BARCODE LABEL",
                "CASE",
                "COMBI METER",
                "DIAL HOLDER",
                "DISPLAY LCD",
                "FRONT GLASS",
                "FUEL POINTE",
                 "ID LABEL",
                "LCD PRISM",
                "LCD",
                "MODULE CASE",
                "PCB ASSY",
                "REAR COVER",
                "RING",
                "RUBBER BUSH",
                "SPEED METER",
                "SPEED PRISM",
                "SPEED SCREW",
                "TEMPO POINTER",
                "TRIP POINTER PCB ASSY",
                "TRIP KNOB",
                "STEPPER MOTOR"
            };

                    _logger.LogInformation("Returning default positions for CombiMeter: {Positions}",
                        string.Join(", ", defaultPositions));

                    return defaultPositions;
                }

                _logger.LogInformation("Retrieved positions: {Positions}",
                    string.Join(", ", positions));

                return positions.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting part positions for section {SectionId}", sectionId);
                return new List<string>();
            }
        }

        public async Task<bool> SaveScrapEntry(ScrapEntry entry)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    const string query = @"
                INSERT INTO [IntegrationCompanyB].[dbo].[scpScrapData]
                (ClockNumber, PartNumber, ScrapSectionId, ErrorId,
                 Quantity, TerminalQuantity, WeightKg, Length, Position,
                 Status, SubmissionDate, Department, Line, Shift)
                VALUES
                (@CreatedBy, @PartNumber, @ScrapSectionId, @ReasonId,
                 @Quantity, @TerminalQuantity, @WeightKg, @Length, @Position,
                 'Pending', @CreatedAt, @Department, @Line, @Shift)";

                    await connection.ExecuteAsync(query, entry, transaction);

                    await transaction.CommitAsync();
                    return true;
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving scrap entry");
                return false;
            }
        }
        public async Task<bool> SectionRequiresPosition(int sectionId)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                const string query = @"
            SELECT RequiresPosition
            FROM [IntegrationCompanyB].[dbo].[scpScrapSection]
            WHERE ScrapSectionId = @SectionId
            AND IsActive = 1";

                var requiresPosition = await connection.QueryFirstOrDefaultAsync<bool>(
                    query, new { SectionId = sectionId });

                return requiresPosition;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if section requires position");
                return false;
            }
        }

        public async Task<bool> SubmitScrapEntry(ScrapEntry entry)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var transaction = await connection.BeginTransactionAsync();

                try
                {
                    const string query = @"
                INSERT INTO [dbo].[ScrapData]
                (ClockNumber, PartNumber, ScrapSectionId, ReasonId, ScrapReason,
                 Quantity, TerminalQuantity, WeightKg, Length, Position,
                 Status, SubmissionDate, Department, Line, Shift, 
                 CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
                VALUES
                (@ClockNumber, @PartNumber, @ScrapSectionId, @ReasonId, @ScrapReason,
                 @Quantity, @TerminalQuantity, @WeightKg, @Length, @Position,
                 @Status, @SubmissionDate, @Department, @Line, @Shift,
                 @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy)";

                    // Log the section ID being saved
                    _logger.LogInformation("Saving scrap entry with section ID: {SectionId}", entry.ScrapSectionId);

                    await connection.ExecuteAsync(query, entry, transaction);
                    await transaction.CommitAsync();
                    return true;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Error executing scrap entry insert");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting scrap entry");
                return false;
            }
        }
    }
}