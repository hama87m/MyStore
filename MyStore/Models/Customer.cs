// Models/Customer.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStore.Models
{
    public class Customer
    {
        [Key]
        public int CustomerId { get; set; }

        [Required]
        [Display(Name = "ناوی کڕیار")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "مۆبایل")]
        public string? Phone { get; set; }

        public string? Address { get; set; }

        // ⭐ Balance بۆ خوێندنەوەی خێرا
        // هەمیشە هاوتا دەبێت لەگەڵ دوایین Ledger.RunningBalance
        [Column(TypeName = "decimal(18, 2)")]
        [Display(Name = "باڵانس")]
        public decimal Balance { get; set; } = 0;
    }
}

// ⚠️ Migration پێویستە:
// Add-Migration AddBalanceToCustomer
// Update-Database