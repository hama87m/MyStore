using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStore.Models
{
    public class SaleItem
    {
        [Key]
        public int SaleItemId { get; set; }

        public int SaleId { get; set; }
        public Sale? Sale { get; set; }

        public int ProductId { get; set; }
        public Product? Product { get; set; }

        [Required]
        public int Quantity { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal UnitPrice { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal UnitPurchasePrice { get; set; }

        // ئەمانە تەنها بۆ خوێندنەوەن (Calculated Properties) هیچیان پێویست نییە لە داتابەیس
        [NotMapped]
        public decimal Subtotal => Quantity * UnitPrice;
    }
}