using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStore.Models
{
    // دڵنیابەرەوە ئەم Enumـە تەنها لێرە نووسراوە
    public enum PaymentType
    {
        Cash = 0,
        Credit = 1
    }

    public class Sale
    {
        [Key]
        public int SaleId { get; set; }

        public DateTime SaleDate { get; set; } = DateTime.Now;

        [Column(TypeName = "decimal(18, 2)")]
        public decimal TotalAmount { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal Discount { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal GrandTotal { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal TotalProfit { get; set; }

        public PaymentType PaymentType { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal PaidAmount { get; set; }

        public string? UserId { get; set; }

        public int CustomerId { get; set; }
        public Customer? Customer { get; set; }

        public ICollection<SaleItem>? SaleItems { get; set; }

        [Display(Name = "پوچەڵکراوە؟")]
        public bool IsVoided { get; set; } = false;
    }
}