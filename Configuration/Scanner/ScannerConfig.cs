namespace SCP_Control.Configuration.Scanner
{
    public class ScannerConfig
    {
        public string[] AllowedBarcodeTypes { get; set; } = Array.Empty<string>();
        public CameraSettings CameraSettings { get; set; } = new();
        public ScannerOptions ScannerOptions { get; set; } = new();
    }
}
