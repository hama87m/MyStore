// Models/CustomerLedger.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStore.Models
{
    // جۆری مامەڵە لە کەشف حیساب
    public enum LedgerTransactionType
    {
        Sale = 0,           // فرۆشتنی قەرز (قەرز زیاد دەبێت)
        Payment = 1,        // واصڵکردن (قەرز کەم دەبێت)
        Refund = 2,         // گەڕاندنەوە (قەرز زیاد دەبێت)
        Adjustment = 3      // ڕاستکردنەوە (کۆرەکشن)
    }

    public class CustomerLedger
    {
        [Key]
        public int LedgerId { get; set; }

        // پەیوەندی بە کڕیار
        public int CustomerId { get; set; }
        public Customer? Customer { get; set; }

        // کات
        [Required]
        public DateTime TransactionDate { get; set; } = DateTime.Now;

        // جۆری مامەڵە
        [Required]
        public LedgerTransactionType TransactionType { get; set; }

        // وردەکاری
        [Required]
        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        // بڕەکان
        [Column(TypeName = "decimal(18, 2)")]
        [Display(Name = "قەرز")]
        public decimal DebitAmount { get; set; } = 0;  // قەرز (لەسەری)

        [Column(TypeName = "decimal(18, 2)")]
        [Display(Name = "واصڵ")]
        public decimal CreditAmount { get; set; } = 0; // واصڵ (داویەتی)

        // ═══════════════════════════════════════════════════════
        // [نوێ] باڵانسی پێشوو - پێش ئەم مامەڵەیە
        // ═══════════════════════════════════════════════════════
        [Column(TypeName = "decimal(18, 2)")]
        [Display(Name = "باڵانسی پێشوو")]
        public decimal PreviousBalance { get; set; } = 0;

        // باڵانس دوای ئەم مامەڵەیە
        [Column(TypeName = "decimal(18, 2)")]
        [Display(Name = "باڵانسی نوێ")]
        public decimal RunningBalance { get; set; }

        // ڕیفرێنس بۆ وردەکاری زیاتر
        public int? ReferenceId { get; set; }      // SaleId, PaymentId, هتد
        public string? ReferenceType { get; set; } // "Sale", "Payment", هتد

        // کێ تۆماری کردووە
        public string? CreatedByUserId { get; set; }

        // [گرنگ] ئەمە نابێت گۆڕانکاری بکرێت
        public bool IsDeleted { get; set; } = false; // بۆ Soft Delete
        public DateTime? DeletedAt { get; set; }
        public string? DeletedByUserId { get; set; }
        public string? DeletionReason { get; set; }
    }
}