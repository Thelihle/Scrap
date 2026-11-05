namespace SCP_Control.Configuration.Scanner
{
    public class InputStream
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public Constraints Constraints { get; set; } = new();
    }
}
