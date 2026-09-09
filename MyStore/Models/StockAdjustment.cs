using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStore.Models
{
    public class StockAdjustment
    {
        [Key]
        public int Id { get; set; }

        public int ProductId { get; set; }
        public Product? Product { get; set; }

        // پسوڵەی کڕینی پەیوەندیدار (nullable — هەندێک adjustment پسوڵەی نییە)
        public int? PurchaseId { get; set; }
        public Purchase? Purchase { get; set; }

        [Display(Name = "ژمارەی دەستکاریکراو")]
        public int AdjustmentQuantity { get; set; }

        public int StockBefore { get; set; }
        public int StockAfter { get; set; }

        [Required(ErrorMessage = "تکایە هۆکارێک بنووسە")]
        [Display(Name = "هۆکار")]
        public string Reason { get; set; } = string.Empty;

        // [نوێ] جۆری گۆڕانکاری
        [Display(Name = "جۆر")]
        public string? AdjustmentType { get; set; }

        public DateTime AdjustmentDate { get; set; } = DateTime.Now;

        public string? UserName { get; set; }

        // ═══════════════════════════════════════════════════════
        // [نوێ] جەردکردنی کۆگا — Reset Point
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// ئایا ئەم ڕێکۆردە جەردکردنی کۆگایە؟
        /// ئەگەر true بوو، لە Recalc دا وەک نوقتەی سفرکردنەوە بەکاردەهێنرێت
        /// </summary>
        [Display(Name = "جەردکردنی کۆگا")]
        public bool IsInventoryReset { get; set; } = false;

        /// <summary>
        /// تێچووی نوێ کە لە جەرددا تۆمار کراوە
        /// تەنها کاتێک IsInventoryReset = true واتایی هەیە
        /// </summary>
        [Display(Name = "تێچووی جەرد")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal ResetPrice { get; set; } = 0;
    }
}