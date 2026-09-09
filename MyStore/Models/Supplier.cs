using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStore.Models
{
    public class Supplier
    {
        [Key]
        public int SupplierId { get; set; }

        [Required]
        [MaxLength(200)]
        [Display(Name = "ناوی دابینکەر")]
        public string Name { get; set; } = "";

        [MaxLength(50)]
        [Display(Name = "تەلەفۆن")]
        public string? Phone { get; set; }

        [MaxLength(300)]
        [Display(Name = "ناونیشان")]
        public string? Address { get; set; }

        [Display(Name = "چالاک")]
        public bool IsActive { get; set; } = true;

        // Navigation
        public ICollection<Purchase>? Purchases { get; set; }
    }
}