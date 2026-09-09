using System.ComponentModel.DataAnnotations;

namespace MyStore.Models
{
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }

        /// <summary>کێ ئەم کردارەی ئەنجامداوە</summary>
        public string? UserId { get; set; }
        public string? UserName { get; set; }

        /// <summary>جۆری کردار: Sale, Purchase, Payment, User, Settings, Backup, Login...</summary>
        [MaxLength(50)]
        public string Category { get; set; } = "";

        /// <summary>کردار: Create, Edit, Delete, Void, Undo, Login, Logout, Backup, Restore...</summary>
        [MaxLength(50)]
        public string Action { get; set; } = "";

        /// <summary>وردەکاری — وەسفی کورت</summary>
        [MaxLength(500)]
        public string Description { get; set; } = "";

        /// <summary>ئای دی ئەو شتەی کردارەکەی لەسەر کراوە (SaleId, PurchaseId, CustomerId...)</summary>
        public int? EntityId { get; set; }

        /// <summary>جۆری ئەو شتەی لەسەر کراوە: Sale, Purchase, Customer, Product...</summary>
        [MaxLength(50)]
        public string? EntityType { get; set; }

        /// <summary>بەروار و کات</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>IP ـی بەکارهێنەر</summary>
        [MaxLength(50)]
        public string? IpAddress { get; set; }
    }
}