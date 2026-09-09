using System.ComponentModel.DataAnnotations;

namespace MyStore.Models
{
    public enum CashFlowType
    {
        // ═══ مامەڵە ئۆتۆماتیکەکان (لە پەڕەی فرۆشتن و قەرز) ═══
        CashSale = 0,           // فرۆشتنی نەقد
        DebtReceived = 1,       // وەرگرتنی قەرز (لە دەفتەری قەرزەکان)
        PurchaseOut = 2,        // کڕینی کاڵا بە نەقد (دەرچوو)

        // ═══ مامەڵە دەستییەکان (لە پەڕەی قاسە) ═══
        ManualIn = 3,           // پارەی هاتوو بە دەستی
        ManualOut = 4,          // پارەی دەرچوو بە دەستی

        // ═══ دەستپێکی ڕۆژ ═══
        OpeningBalance = 5,     // پارەی دەستپێکی ڕۆژ

        // ═══ [نوێ] مامەڵە ئۆتۆماتیکی ڕاستکردنەوە ═══
        InlineSalePayment = 10,     // واصڵی سەر پسوڵەی قەرز (هاتوو)
        VoidCashSale = 11,          // سڕینەوەی پسوڵەی نەقد (دەرچوو)
        VoidInlinePayment = 12,     // سڕینەوەی واصڵی سەر پسوڵە (دەرچوو)
        RefundDebtPayment = 13,     // گەڕاندنەوەی واصڵی قەرز (دەرچوو)
        SaleEditIncrease = 14,      // زیادبوونی واصڵ دوای دەستکاری پسوڵە (هاتوو)
        SaleEditDecrease = 15,      // کەمبوونی واصڵ دوای دەستکاری پسوڵە (دەرچوو)
        AdvanceRefund = 16          // پێدانەوەی پێشینە بۆ کڕیار (دەرچوو)
    }

    public class CashTransaction
    {
        [Key]
        public int Id { get; set; }

        /// <summary>جۆری جوڵە</summary>
        public CashFlowType FlowType { get; set; }

        /// <summary>بڕی پارە (هەمیشە مثبت)</summary>
        public decimal Amount { get; set; }

        /// <summary>هاتوو = true، دەرچوو = false</summary>
        public bool IsIncome { get; set; }

        /// <summary>هۆکار / وەسف</summary>
        [MaxLength(300)]
        public string Description { get; set; } = "";

        /// <summary>ئای دی مەرجەعی (SaleId, PaymentId, PurchaseId...)</summary>
        public int? ReferenceId { get; set; }

        /// <summary>جۆری مەرجەع: Sale, Payment, Purchase</summary>
        [MaxLength(50)]
        public string? ReferenceType { get; set; }

        /// <summary>بەروار و کات</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>بەکارهێنەری ئەنجامدەر</summary>
        public string? UserId { get; set; }
        [MaxLength(100)]
        public string? UserName { get; set; }

        /// <summary>باڵانسی قاسە دوای ئەم جوڵەیە</summary>
        public decimal BalanceAfter { get; set; }
    }
}