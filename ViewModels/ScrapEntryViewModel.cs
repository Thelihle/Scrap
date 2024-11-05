namespace SCP_Control.ViewModels
{
    public class ScrapEntryViewModel
    {
        public string? PartNumber { get; set; }
        public int Quantity { get; set; }
        public decimal WeightKg { get; set; }
        public decimal Length { get; set; }
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public List<ScrapSectionViewModel> ScrapSections { get; set; } = new();
        public string? PartPosition { get; set; }
    }

    public class ScrapSectionViewModel
    {
        public int ScrapSectionId { get; set; }
        public string SectionName { get; set; } = string.Empty;
        public bool RequiresPosition { get; set; }
    }
}