using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStore.Models
{
    // مۆدێلی وردەکاری کاڵاکانی ناو هەر فاتوورەیەکی کڕین
    public class PurchaseItem
    {
        [Key]
        public int PurchaseItemId { get; set; }

        // پەیوەندی بە Purchase (فاتوورەی کڕین)
        [Display(Name = "ژمارەی فاتوورە")]
        public int PurchaseId { get; set; }
        public Purchase? Purchase { get; set; }

        // پەیوەندی بە Product (کام کاڵا کڕدراوە)
        [Display(Name = "کاڵا")]
        public int ProductId { get; set; }
        public Product? Product { get; set; }

        // ژمارەی دانەی کڕدراو
        [Required]
        [Display(Name = "ژمارەی دانە")]
        [Range(1, int.MaxValue, ErrorMessage = "ژمارەی دانە دەبێت زیاتر بێت لە سفر.")]
        public int Quantity { get; set; }

        // نرخی یەکەی کڕین لەم کڕینەدا (نرخی دانەکە)
        [Required]
        [Display(Name = "نرخی یەکە")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal UnitPrice { get; set; }

        // کۆی گشتی نرخ بۆ ئەم هێڵە
        [Display(Name = "کۆی گشتی")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal Subtotal => Quantity * UnitPrice;
    }
}