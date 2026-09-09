using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStore.Models
{
    // جۆری مامەڵەی پارە
    public enum PaymentTransactionType
    {
        [Display(Name = "وەرگرتن (Wasil)")]
        Received = 0,

        [Display(Name = "گەڕاندنەوە (Refund)")]
        Refund = 1
    }

    public class CustomerPayments
    {
        [Key]
        public int PaymentId { get; set; }

        public int CustomerId { get; set; }
        public Customer? Customer { get; set; }

        [Required]
        public DateTime PaymentDate { get; set; } = DateTime.Now;

        [Column(TypeName = "decimal(18, 2)")]
        [Display(Name = "بڕی پارە")]
        public decimal Amount { get; set; }

        public string? Note { get; set; }

        // [نوێ] دیاریکردنی جۆری پارەدانەکە
        public PaymentTransactionType TransactionType { get; set; } = PaymentTransactionType.Received;

        // [نوێ] بڕی بەکارهاتوو (بۆ ئەوەی بزانین چەندی ماوەتەوە بۆ بەستنەوە)
        [Column(TypeName = "decimal(18, 2)")]
        public decimal UsedAmount { get; set; } = 0;

        // لیستی بەستنەوەکان
        public ICollection<SalePayment>? SalePayments { get; set; }

        [Display(Name = "پوچەڵکراوە؟")]
        public bool IsVoided { get; set; } = false;
    }
}