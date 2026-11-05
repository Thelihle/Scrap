using Microsoft.CodeAnalysis;
using System.Text;

namespace SCP_Control.Configuration.Scanner
{
    public class ScannerOptions
    {
        public int NumberOfWorkers { get; set; }
        public bool Locate { get; set; }
        public InputStream InputStream { get; set; } = new();
        public Locator Locator { get; set; } = new();
        public Decoder Decoder { get; set; } = new();
    }
}
