using System.ComponentModel.DataAnnotations;

namespace MyStore.Models
{
    public class InventoryStatusViewModel
    {
        public int ProductId { get; set; }

        [Display(Name = "ناوی کاڵا")]
        public string ProductName { get; set; } = string.Empty;

        [Display(Name = "باڕکۆد")]
        public string Barcode { get; set; } = string.Empty;

        [Display(Name = "ستۆکی ماوە")]
        public int CurrentStock { get; set; }

        // [گۆڕانکاری] لێرە دەیکەین بە تێکڕای نرخ
        [Display(Name = "تێکڕای نرخی کڕین")]
        [DataType(DataType.Currency)]
        public decimal AveragePurchasePrice { get; set; }

        [Display(Name = "نرخی فرۆشتن")]
        [DataType(DataType.Currency)]
        public decimal SalePrice { get; set; }

        // قازانجی پێشبینیکراو لەسەر بنەمای تێکڕای نرخ حیساب دەکرێت
        [Display(Name = "کۆی قازانجی پێشبینیکراو")]
        public decimal ExpectedProfit => CurrentStock * (SalePrice - AveragePurchasePrice);
    }
}