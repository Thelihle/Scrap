using System.ComponentModel.DataAnnotations.Schema;

namespace SCP_Control.Models
{
    public class StockMaster
    {
        public string StockCode { get; set; }
        [Column("MaterialCost")]
        public decimal MaterialCost { get; set; }
    }
}
