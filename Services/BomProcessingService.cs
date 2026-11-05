using BomProcessingLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SCP_Control.Services
{
    public interface IBomProcessingService
    {
        Task<BomProcessingResult> ProcessApprovedScrapAsync(string partNumber, decimal quantity, string approvedBy);
    }

    public class BomProcessingService : IBomProcessingService
    {
        private readonly ILogger<BomProcessingService> _logger;
        private readonly IConfiguration _configuration;

        public BomProcessingService(
            ILogger<BomProcessingService> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<BomProcessingResult> ProcessApprovedScrapAsync(
    string partNumber,
    decimal quantity,
    string approvedBy)
        {
            var result = new BomProcessingResult
            {
                Success = false,
                PartNumber = partNumber,
                Quantity = quantity,
                ProcessedBy = approvedBy,
                ProcessedAt = DateTime.UtcNow
            };

            try
            {
                _logger.LogInformation($"🔄 Starting BOM processing for {partNumber} (Qty: {quantity})");

                // ✅ Use SysproConnection (points to correct database based on environment)
                var connectionString = _configuration.GetConnectionString("SysproConnection");
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new Exception("SysproConnection string not found in configuration");
                }

                // Create BOM processor
                var processor = new BomProcessor(connectionString);

                // Prepare input
                var parentPartInputs = new List<ParentPartInput>
        {
            new ParentPartInput
            {
                PartNumber = partNumber,
                Quantity = quantity
            }
        };

                // Process BOM
                var bomResult = await processor.ProcessBomAsync(parentPartInputs);

                if (bomResult?.FinalBomData != null && bomResult.FinalBomData.Any())
                {
                    result.Success = true;
                    result.ComponentsProcessed = bomResult.FinalBomData.Count;
                    result.BomComponents = bomResult.FinalBomData;
                    result.Message = $"Successfully processed {result.ComponentsProcessed} BOM components";

                    _logger.LogInformation($"✅ BOM processing successful: {result.ComponentsProcessed} components");
                }
                else
                {
                    result.Success = false;
                    result.Message = "No BOM components found";
                    _logger.LogWarning($"⚠️ {result.Message}");
                }
            }
            catch (BomProcessingException ex)
            {
                result.Success = false;
                result.Message = $"BOM Processing Error: {ex.Message}";
                result.ErrorDetails = ex.ToString();
                _logger.LogError(ex, $"❌ BOM processing error for {partNumber}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Unexpected error: {ex.Message}";
                result.ErrorDetails = ex.ToString();
                _logger.LogError(ex, $"❌ Unexpected BOM processing error for {partNumber}");
            }
            return result;
        }
    }

    // Result model
    public class BomProcessingResult
    {
        public bool Success { get; set; }
        public string PartNumber { get; set; }
        public decimal Quantity { get; set; }
        public string ProcessedBy { get; set; }
        public DateTime ProcessedAt { get; set; }
        public int ComponentsProcessed { get; set; }
        public string Message { get; set; }
        public string ErrorDetails { get; set; }
        public List<FinalBomData> BomComponents { get; set; }
    }
}