namespace SCP_Control.Models
{
    public class UpdateScrapEntryRequest
    {
        public int EntryId { get; set; }
        public int Quantity { get; set; }
        public int? TerminalQuantity { get; set; }
        public decimal? WeightKg { get; set; }
        public decimal? Length { get; set; }
        public string? Position { get; set; }
    }
}
