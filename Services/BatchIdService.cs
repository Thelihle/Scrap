// Services/BatchIdService.cs
using Microsoft.Data.SqlClient;
using SCP_Control.Services.Interfaces;
using System.Data;

namespace SCP_Control.Services
{
    public class BatchIdService : IBatchIdService
    {
        private readonly string _connectionString;
        private readonly ILogger<BatchIdService> _logger;

        public BatchIdService(IConfiguration configuration, ILogger<BatchIdService> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<int> GetNextBatchIdAsync(string clockNumber)
        {
            try
            {
                _logger.LogInformation("🔢 Generating batch ID for user: {ClockNumber}", clockNumber);

                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new SqlCommand("sp_GetNextBatchScrapId", connection)
                {
                    CommandType = CommandType.StoredProcedure
                };

                // Input parameter
                command.Parameters.AddWithValue("@ClockNumber", clockNumber);

                // Output parameter
                var outputParam = new SqlParameter
                {
                    ParameterName = "@NextBatchId",
                    SqlDbType = SqlDbType.Int,
                    Direction = ParameterDirection.Output
                };
                command.Parameters.Add(outputParam);

                // Execute
                await command.ExecuteNonQueryAsync();

                // Get result
                var nextBatchId = (int)outputParam.Value;

                _logger.LogInformation("✅ Generated BatchScrapId: {BatchId} for user: {ClockNumber}",
                    nextBatchId, clockNumber);

                return nextBatchId;
            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, "❌ SQL error generating batch ID for user: {ClockNumber}", clockNumber);
                throw new InvalidOperationException($"Database error generating batch ID: {sqlEx.Message}", sqlEx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error generating batch ID for user: {ClockNumber}", clockNumber);
                throw new InvalidOperationException($"Error generating batch ID: {ex.Message}", ex);
            }
        }
    }
}