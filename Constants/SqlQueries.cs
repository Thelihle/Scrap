// SCP_Control/Constants/SqlQueries.cs
namespace SCP_Control.Constants
{
    public static class SqlQueries
    {
        public const string GetActiveScrapSections = @"
            SELECT 
                ScrapSectionId as value,
                ScrapSectionName as text
            FROM [dbo].[ScrapSection]
            WHERE IsActive = 1
            ORDER BY ScrapSectionName";

        public const string InsertScrapData = @"
            INSERT INTO [dbo].[ScrapData] 
            (ClockNumber, ScrapSectionId, ReasonId, PartNumber, 
             Quantity, TerminalQuantity, ScrapReason, WeightKg, 
             Length, Status, SubmissionDate, Department, Line, 
             Shift, Position, CreatedBy, CreatedAt)
            VALUES 
            (@ClockNumber, @ScrapSectionId, @ReasonId, @PartNumber,
             @Quantity, @TerminalQuantity, @ScrapReason, @WeightKg,
             @Length, @Status, @SubmissionDate, @Department, @Line,
             @Shift, @Position, @CreatedBy, @CreatedAt)";
    }
}