using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStore.Models
{
    // ئەم خشتەیە پەیوەندی نێوان فاتوورە و پارەدان ڕێکدەخات
    // لێرەدا دیاری دەکرێت که کام پارە بۆ کام فاتوورە ڕۆیشتووە
    public class SalePayment
    {
        [Key]
        public int Id { get; set; }

        // پەیوەندی بە فاتوورەوە (کام فاتوورە پارەی بۆ دراوە)
        public int SaleId { get; set; }
        public Sale? Sale { get; set; }

        // پەیوەندی بە پارەی وەرگیراوەوە (کام پارەدان بووە)
        public int CustomerPaymentId { get; set; }
        public CustomerPayments? CustomerPayment { get; set; }

        // بڕی پارەی تەرخانکراو بۆ ئەم فاتوورەیە
        // نموونە: کڕیار ٥٠ هەزاری داوە (CustomerPayment)، ٣٠ هەزاری بۆ ئەم فاتوورەیە (SalePayment) ڕۆیشتووە
        [Column(TypeName = "decimal(18, 2)")]
        public decimal AmountAllocated { get; set; }

        public DateTime AllocationDate { get; set; } = DateTime.Now;
    }
}