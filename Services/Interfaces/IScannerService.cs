using SCP_Control.Configuration.Scanner;

namespace SCP_Control.Services.Interfaces
{
    public interface IScannerService
    {
        ScannerOptions GetScannerOptions();
        Task<string> ProcessScannedCode(string code);
        bool ValidateBarcode(string barcode);
    }
}
