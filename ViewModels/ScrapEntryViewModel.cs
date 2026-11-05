using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
namespace SCP_Control.ViewModels
{
    public class ScrapEntryViewModel
    {
        // Basic properties
        public int ScrapId { get; set; }
        public string ScrapType { get; set; } = "component"; // 🆕 NEW
        public int ErrorId { get; set; }
        public string ClockNumber { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string ScrapReason { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public decimal MaterialCost { get; set; }
        // Scrap details
        public int TerminalQuantity { get; set; }
        public decimal WeightKg { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Line { get; set; } = string.Empty;
        public string Shift { get; set; } = string.Empty;
        public string? Position { get; set; }
        public string Section { get; set; } = string.Empty;
        // Add this new property
        public string StockUom { get; set; }

        [StringLength(1)]
        public string PartCategory { get; set; } = "";

        [StringLength(50)]
        public string Plant { get; set; } = "";

        [StringLength(1)]
        public string Company { get; set; } = "B";

        // IDs for selections
        public int? ScrapSectionId { get; set; }
        public int? ReasonId { get; set; }

        // SelectList properties for dropdowns
        public List<SelectListItem> ScrapSections { get; set; } = new();

        // Dictionary for section-specific reasons
        public Dictionary<string, List<ScrapReasonItem>> SectionReasons { get; set; }

        // Formatted display properties
        public string FormattedCreatedAt => CreatedAt?.ToString("MM/dd/yyyy HH:mm") ?? "-";
        public string FormattedWeight => WeightKg.ToString("F2");

        public List<string> CombiMeterPositions { get; set; }

        public ScrapEntryViewModel()
        {
            ScrapSections = new List<SelectListItem>();
            SectionReasons = new Dictionary<string, List<ScrapReasonItem>>();
            CombiMeterPositions = new List<string>
            {
                "AMBIENT PRISM",
                "BEZEL",
                "BARCODE LABEL",
                "CASE",
                "COMBI METER",
                "DIAL HOLDER",
                "DISPLAY LCD",
                "FRONT GLASS",
                "FUEL POINTER",
                "ID LABEL",
                "LCD PRISM",
                "LCD",
                "MODULE CASE",
                "PCB ASSY",
                "REAR COVER",
                "RING",
                "RUBBER BUSH",
                "SPEED METER",
                "SPEED PRISM",
                "SPEED SCREW",
                "TEMPO POINTER",
                "TRIP POINTER PCB ASSY",
                "TRIP KNOB",
                "STEPPER MOTOR"
            };
        }
    }

    public class ScrapReasonItem
    {
        public string Value { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public bool RequiresPosition { get; set; }
    }
}