using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStore.Models
{
    public class Purchase
    {
        [Key]
        public int PurchaseId { get; set; }

        [Required]
        [Display(Name = "بەرواری کڕین")]
        [DataType(DataType.Date)]
        public DateTime PurchaseDate { get; set; } = DateTime.Now;

        // دابینکەر — پێویستە (پێشکەوت: دیارینەکراو = 1)
        public int SupplierId { get; set; } = 1;
        public Supplier? Supplier { get; set; }

        [Display(Name = "کۆی گشتی کڕین")]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal TotalAmount { get; set; }

        public ICollection<PurchaseItem>? PurchaseItems { get; set; }
    }
}