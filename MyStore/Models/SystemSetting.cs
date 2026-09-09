using System.ComponentModel.DataAnnotations;

namespace MyStore.Models
{
    /// <summary>
    /// ڕێکخستنەکانی سیستەم
    /// </summary>
    public class SystemSettings
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Value { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Description { get; set; }

        public DateTime LastModified { get; set; } = DateTime.Now;
        public string? ModifiedBy { get; set; }
    }

    /// <summary>
    /// کلیلەکانی ڕێکخستن
    /// </summary>
    public static class SettingsKeys
    {
        public const string CashierMaxDiscountPercent = "CashierMaxDiscountPercent";
        public const string ManagerMaxDiscountPercent = "ManagerMaxDiscountPercent";
        public const string StoreName = "StoreName";
        public const string StorePhone = "StorePhone";
        public const string StoreAddress = "StoreAddress";
        public const string StoreLogo = "StoreLogo";
    }
}
