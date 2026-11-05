namespace SCP_Control.Configuration.Scanner
{
    public class CameraSettings
    {
        public string FacingMode { get; set; } = "environment";
        public DimensionSettings Width { get; set; } = new();
        public DimensionSettings Height { get; set; } = new();
    }
}
