using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStore.Models
{
    public class Product
    {
        [Key]
        public int ProductId { get; set; }

        [Required(ErrorMessage = "ناوی کاڵا پێویستە.")]
        [Display(Name = "ناوی کاڵا")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "باڕکۆد پێویستە.")]
        [Display(Name = "باڕکۆد")]
        public string Barcode { get; set; } = string.Empty;

        [Required]
        [Display(Name = "نرخی فرۆشتن (تاک)")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal SalePrice { get; set; }

        // ═══════════════════════════════════════════════════════
        // [نوێ] نرخی فرۆشتن بە کۆ
        // ═══════════════════════════════════════════════════════
        [Display(Name = "نرخی فرۆشتن (کۆ)")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal WholesalePrice { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        // ═══════════════════════════════════════════════════════
        // بۆ خێراکردنی سیستەم - بەجێی حیسابکردن لە هەموو خشتەکان
        // ═══════════════════════════════════════════════════════
        [Display(Name = "ستۆکی ئامادە")]
        public int CurrentStock { get; set; } = 0;

        [Display(Name = "دوایین نرخی کڕین")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal LastPurchasePrice { get; set; } = 0;
        // ═══════════════════════════════════════════════════════

        // --- یەکەکان ---
        [Display(Name = "ژمارە لە کارتۆن")]
        [Range(1, 10000)]
        public int QtyPerCarton { get; set; } = 1;

        [Display(Name = "ژمارە لە دەرزەن")]
        [Range(1, 10000)]
        public int QtyPerDozen { get; set; } = 1;

        [Display(Name = "ژمارە لە بەستە")]
        [Range(1, 10000)]
        public int QtyPerBundle { get; set; } = 1;

        // --- داشکاندن ---
        [Display(Name = "داشکاندنی هەیە؟")]
        public bool HasDiscount { get; set; } = false;

        [Display(Name = "ڕێژەی داشکاندن")]
        [Column(TypeName = "decimal(5, 2)")]
        [Range(0, 100)]
        public decimal DiscountPercent { get; set; } = 0;

        [Display(Name = "دەستپێکی داشکاندن")]
        public DateTime? DiscountStartDate { get; set; }

        [Display(Name = "کۆتایی داشکاندن")]
        public DateTime? DiscountEndDate { get; set; }

        // --- Computed ---
        [NotMapped]
        public bool IsDiscountActive
        {
            get
            {
                if (!HasDiscount || DiscountPercent <= 0) return false;
                var now = DateTime.Now;
                if (!DiscountStartDate.HasValue && !DiscountEndDate.HasValue) return true;
                bool afterStart = !DiscountStartDate.HasValue || now >= DiscountStartDate.Value;
                bool beforeEnd = !DiscountEndDate.HasValue || now <= DiscountEndDate.Value;
                return afterStart && beforeEnd;
            }
        }

        [NotMapped]
        public decimal DiscountedPrice => IsDiscountActive ? SalePrice * (1 - DiscountPercent / 100) : SalePrice;
    }
}
