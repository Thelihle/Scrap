using Microsoft.EntityFrameworkCore;

namespace SCP_Control.Models
{
    [Keyless]
    public class InvMaster
    {
        public string StockCode { get; set; } = string.Empty;
        public decimal MaterialCost { get; set; }
    }
}