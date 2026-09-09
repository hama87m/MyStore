using Microsoft.EntityFrameworkCore;
using MyStore.Data;

namespace MyStore.Helpers
{
    public static class FifoHelper
    {
        /// <summary>
        /// حیسابکردنی تێچووی کاڵا بەپێی نوێترین کڕینەکان
        /// </summary>
        public static async Task<decimal> CalculateFifoCost(ApplicationDbContext context, int productId, int currentStock)
        {
            if (currentStock <= 0) return 0;

            // OrderByDescending = نوێترین کڕینەکان سەرەتا (وەک ئەسڵی)
            var purchases = await context.PurchaseItems
                .Include(pi => pi.Purchase)
                .Where(pi => pi.ProductId == productId)
                .OrderByDescending(pi => pi.Purchase!.PurchaseDate)
                .ThenByDescending(pi => pi.PurchaseId)
                .Select(pi => new { pi.Quantity, pi.UnitPrice })
                .ToListAsync();

            if (!purchases.Any()) return 0;

            decimal totalValue = 0;
            int needed = currentStock;

            foreach (var purchase in purchases)
            {
                if (needed <= 0) break;
                int qtyToTake = Math.Min(needed, purchase.Quantity);
                totalValue += qtyToTake * purchase.UnitPrice;
                needed -= qtyToTake;
            }

            return currentStock > 0 ? Math.Round(totalValue / currentStock, 2) : 0;
        }

        /// <summary>
        /// نوێکردنەوەی ستۆک و نرخی کاڵا
        /// </summary>
        public static async Task UpdateProductStock(ApplicationDbContext context, int productId, int quantityChange)
        {
            var product = await context.Products.FindAsync(productId);
            if (product == null) return;

            product.CurrentStock += quantityChange;
            product.LastPurchasePrice = await CalculateFifoCost(context, productId, product.CurrentStock);
        }

        /// <summary>
        /// نوێکردنەوەی ستۆکی چەند کاڵا بەیەکجار (بۆ باشترکردنی پێرفۆڕمانس)
        /// ئەم فەنکشنە SaveChanges ناکات - پێویستە دواتر بانگی بکەیت
        /// </summary>
        public static async Task UpdateProductStockBatch(
            ApplicationDbContext context, 
            Dictionary<int, int> productQuantityChanges)
        {
            if (!productQuantityChanges.Any()) return;

            var productIds = productQuantityChanges.Keys.ToList();
            var products = await context.Products
                .Where(p => productIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId);

            // نوێکردنەوەی ستۆک
            foreach (var kvp in productQuantityChanges)
            {
                if (products.TryGetValue(kvp.Key, out var product))
                {
                    product.CurrentStock += kvp.Value;
                }
            }

            // نوێکردنەوەی نرخی تێچوو بۆ کاڵاکانی ستۆکیان هەیە
            foreach (var kvp in productQuantityChanges)
            {
                if (products.TryGetValue(kvp.Key, out var product) && product.CurrentStock > 0)
                {
                    product.LastPurchasePrice = await CalculateFifoCost(context, kvp.Key, product.CurrentStock);
                }
            }
        }
    }
}
