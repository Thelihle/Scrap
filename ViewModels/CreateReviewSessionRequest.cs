namespace SCP_Control.ViewModels
{
    public class CreateReviewSessionRequest
    {
        public string PartNumber { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public string StockUom { get; set; } = string.Empty;
        public decimal MaterialCost { get; set; }
    }

    public class AddReviewEntryRequest
    {
        public string SecurityStamp { get; set; } = string.Empty;
        public int ScrapSectionId { get; set; }
        public string SectionName { get; set; } = string.Empty;
        public int ReasonId { get; set; }
        public string ScrapReason { get; set; } = string.Empty;
        public string? Position { get; set; }
        public int Quantity { get; set; }
    }

    public class UpdateReviewEntryRequest
    {
        public int EntryId { get; set; }
        public int ScrapSectionId { get; set; }
        public string SectionName { get; set; } = string.Empty;
        public int ReasonId { get; set; }
        public string ScrapReason { get; set; } = string.Empty;
        public string? Position { get; set; }
        public int Quantity { get; set; }
    }

    public class DeleteReviewEntryRequest
    {
        public int EntryId { get; set; }
    }

    public class FinalizeSessionRequest
    {
        public string SecurityStamp { get; set; } = string.Empty;
    }
}
