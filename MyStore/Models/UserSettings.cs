using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyStore.Models
{
    /// <summary>
    /// ڕێکخستنی تایبەت بە بەکارهێنەر
    /// </summary>
    public class UserSettings
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(450)]
        public string UserId { get; set; } = string.Empty;

        [Display(Name = "زۆرترین داشکاندن %")]
        [Column(TypeName = "decimal(5, 2)")]
        [Range(0, 100)]
        public decimal MaxDiscountPercent { get; set; } = 0;

        [Display(Name = "تێبینی")]
        [MaxLength(500)]
        public string? Note { get; set; }
        public string? ProfileImagePath { get; set; }
        public DateTime LastModified { get; set; } = DateTime.Now;
    }
}
