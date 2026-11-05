using Microsoft.Extensions.Options;
using SCP_Control.Configuration.Scanner;
using SCP_Control.Services.Interfaces;

namespace SCP_Control.Services
{
    public class ScannerService : IScannerService
    {
        private readonly ScannerConfig _scannerConfig;
        private readonly ILogger<ScannerService> _logger;

        public ScannerService(
            IOptions<ScannerConfig> scannerConfig,
            ILogger<ScannerService> logger)
        {
            _scannerConfig = scannerConfig.Value;
            _logger = logger;
        }

        public ScannerOptions GetScannerOptions()
        {
            return _scannerConfig.ScannerOptions;
        }

        public async Task<string> ProcessScannedCode(string code)
        {
            try
            {
                _logger.LogInformation("Processing scanned code: {code}", code);
                // Add your processing logic here
                return await Task.FromResult(code);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scanned code");
                throw;
            }
        }

        public bool ValidateBarcode(string barcode)
        {
            if (string.IsNullOrEmpty(barcode))
            {
                return false;
            }

            // Add your barcode validation logic here
            return true;
        }
    }
}
