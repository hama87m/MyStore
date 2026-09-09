using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;

namespace MyStore.Helpers
{
    /// <summary>
    /// سیستەمی Weighted Moving Average Cost
    /// تێچوو تەنها لەکاتی کڕیندا نوێ دەبێتەوە
    /// </summary>
    public static class CostHelper
    {
        /// <summary>
        /// نوێکردنەوەی ستۆک و تێچوو لەکاتی کڕینی نوێدا
        /// 
        /// فۆرمولا:
        /// Cost_new = ((Q_old × P_old) + (Q_purchase × P_purchase)) / (Q_old + Q_purchase)
        /// 
        /// نموونە:
        /// - ستۆکی کۆن: 20 دانە بە 500 دینار
        /// - کڕینی نوێ: 10 دانە بە 800 دینار
        /// - تێچووی نوێ = (20×500 + 10×800) / (20+10) = 18000/30 = 600 دینار
        /// </summary>
        public static void AddPurchaseStock(Product product, int quantityPurchased, decimal unitPrice)
        {
            if (quantityPurchased <= 0) return;

            int oldStock = product.CurrentStock;
            decimal oldCost = product.LastPurchasePrice;

            // حیسابکردنی تێچووی نوێ بە Moving Average
            if (oldStock <= 0)
            {
                // ئەگەر ستۆک نەبوو، تەنها نرخی نوێ
                product.LastPurchasePrice = unitPrice;
            }
            else
            {
                // Weighted Average: ((Q_old × P_old) + (Q_new × P_new)) / (Q_old + Q_new)
                decimal totalOldValue = oldStock * oldCost;
                decimal totalNewValue = quantityPurchased * unitPrice;
                int totalQuantity = oldStock + quantityPurchased;

                product.LastPurchasePrice = Math.Round((totalOldValue + totalNewValue) / totalQuantity, 2);
            }

            // نوێکردنەوەی ستۆک
            product.CurrentStock += quantityPurchased;
        }

        /// <summary>
        /// کەمکردنەوەی ستۆک (فرۆشتن یان ڕاستکردنەوە)
        /// تێچوو ناگۆڕێت - تەنها ستۆک کەم دەبێت
        /// </summary>
        public static void RemoveStock(Product product, int quantityRemoved)
        {
            if (quantityRemoved <= 0) return;
            product.CurrentStock -= quantityRemoved;
            // تێچوو ناگۆڕێت لەکاتی فرۆشتندا
        }

        /// <summary>
        /// زیادکردنی ستۆک بە نرخی کۆن (گەڕانەوەی کاڵا، ڕاستکردنەوە)
        /// تێچوو ناگۆڕێت
        /// </summary>
        public static void AddStockWithoutPriceChange(Product product, int quantityAdded)
        {
            if (quantityAdded <= 0) return;
            product.CurrentStock += quantityAdded;
            // تێچوو ناگۆڕێت
        }

        // ═══════════════════════════════════════════════════════
        // Batch Operations بۆ پێرفۆڕمانس
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// زیادکردنی ستۆک بۆ چەند کاڵا بەیەکجار (کڕینی نوێ)
        /// </summary>
        public static async Task AddPurchaseStockBatch(
            ApplicationDbContext context,
            List<(int ProductId, int Quantity, decimal UnitPrice)> items)
        {
            if (!items.Any()) return;

            var productIds = items.Select(i => i.ProductId).Distinct().ToList();
            var products = await context.Products
                .Where(p => productIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId);

            foreach (var (productId, quantity, unitPrice) in items)
            {
                if (products.TryGetValue(productId, out var product))
                {
                    AddPurchaseStock(product, quantity, unitPrice);
                }
            }
        }

        /// <summary>
        /// کەمکردنەوەی ستۆک بۆ چەند کاڵا بەیەکجار (فرۆشتن)
        /// </summary>
        public static async Task RemoveStockBatch(
            ApplicationDbContext context,
            Dictionary<int, int> productQuantities)
        {
            if (!productQuantities.Any()) return;

            var productIds = productQuantities.Keys.ToList();
            var products = await context.Products
                .Where(p => productIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId);

            foreach (var (productId, quantity) in productQuantities)
            {
                if (products.TryGetValue(productId, out var product))
                {
                    RemoveStock(product, quantity);
                }
            }
        }

        /// <summary>
        /// گۆڕینی ستۆک بەپێی جیاوازی (بۆ دەستکاری فاتوورە)
        /// ئەگەر زیادکردن بوو بەبێ گۆڕینی نرخ، ئەگەر کەمکردن بوو تەنها ستۆک
        /// </summary>
        public static async Task AdjustStockBatch(
            ApplicationDbContext context,
            Dictionary<int, int> productQuantityChanges)
        {
            if (!productQuantityChanges.Any()) return;

            var productIds = productQuantityChanges.Keys.ToList();
            var products = await context.Products
                .Where(p => productIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId);

            foreach (var (productId, change) in productQuantityChanges)
            {
                if (products.TryGetValue(productId, out var product))
                {
                    if (change > 0)
                    {
                        // زیادکردن بەبێ گۆڕینی نرخ
                        AddStockWithoutPriceChange(product, change);
                    }
                    else if (change < 0)
                    {
                        // کەمکردنەوە
                        RemoveStock(product, Math.Abs(change));
                    }
                }
            }
        }
    }
}
