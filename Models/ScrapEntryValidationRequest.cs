namespace SCP_Control.Models
{
    public class ScrapEntryValidationRequest
    {
        public string PartNumber { get; set; }
        public int ScrapSectionId { get; set; }
        public int ReasonId { get; set; }
        public int Quantity { get; set; }
        public int? TerminalQuantity { get; set; }
        public decimal? WeightKg { get; set; }
        public decimal? Length { get; set; }
        public string? Position { get; set; }
    }
}
