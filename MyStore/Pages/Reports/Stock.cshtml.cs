using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MyStore.Pages.Reports
{
    // لێرەدا دەسەڵاتمان داوە تەنها بە ئەدمین و مەنەجەر کە بتوانن بێنە ژوورەوە
    // کاشێر هەر لە بنچینەوە ناتوانێت ئەم پەڕەیە بکاتەوە.
    [Authorize(Roles = "Admin,Manager")]
    public class StockModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public StockModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnGetStoreSettingsAsync()
        {
            try
            {
                var settings = await _context.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
                return new JsonResult(new
                {
                    StoreName = settings.ContainsKey(SettingsKeys.StoreName) ? settings[SettingsKeys.StoreName] : "Diamond Center",
                    StoreLogo = settings.ContainsKey(SettingsKeys.StoreLogo) ? settings[SettingsKeys.StoreLogo] : ""
                });
            }
            catch
            {
                return new JsonResult(new { StoreName = "Diamond Center" });
            }
        }

        // ئەم فەنکشنە بۆ گەڕانی خێرا بەکاردێت. مەنەجەریش پێویستی پێیەتی بۆ چاپکردنی باڕکۆد
        public async Task<IActionResult> OnGetSearchProductsAsync(string term)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(term))
                    return new JsonResult(new List<object>());

                term = term.ToLower().Trim();

                var products = await _context.Products
                    .Where(p => p.IsActive && (p.Name.ToLower().Contains(term) || p.Barcode.ToLower().Contains(term)))
                    .Select(p => new {
                        p.ProductId,
                        p.Name,
                        p.Barcode,
                        p.SalePrice
                    })
                    .Take(50) // تەنها ٥٠ دانە بۆ خێرایی
                    .AsNoTracking()
                    .ToListAsync();

                return new JsonResult(products);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        // داتای کۆگا و ئامارەکان: تەنها ڕێگەپێدراوە بۆ ئەدمین
        public async Task<IActionResult> OnGetInventoryDataAsync()
        {
            if (!User.IsInRole("Admin"))
            {
                return new JsonResult(new { error = "ڕێگەپێنەدراوە. تەنها ئەدمین دەتوانێت ئەم زانیارییانە ببینێت." });
            }

            try
            {
                var avgPrices = await _context.PurchaseItems!
                    .GroupBy(pi => pi.ProductId)
                    .Select(g => new {
                        ProductId = g.Key,
                        AvgPrice = g.Average(x => x.UnitPrice)
                    })
                    .ToDictionaryAsync(x => x.ProductId, x => x.AvgPrice);

                var products = await _context.Products
                    .Where(p => p.IsActive)
                    .AsNoTracking()
                    .ToListAsync();

                var inventoryList = new List<InventoryStatusViewModel>();
                decimal totalCapital = 0;
                decimal totalExpectedSales = 0;
                decimal totalExpectedProfit = 0;
                int totalQuantityInStock = 0;

                foreach (var p in products)
                {
                    decimal avgPrice = avgPrices.ContainsKey(p.ProductId) ? avgPrices[p.ProductId] : p.LastPurchasePrice;

                    var item = new InventoryStatusViewModel
                    {
                        ProductId = p.ProductId,
                        ProductName = p.Name,
                        Barcode = p.Barcode,
                        CurrentStock = p.CurrentStock,
                        AveragePurchasePrice = avgPrice,
                        SalePrice = p.SalePrice
                    };

                    inventoryList.Add(item);

                    if (p.CurrentStock > 0)
                    {
                        totalCapital += (p.CurrentStock * avgPrice);
                        totalExpectedSales += (p.CurrentStock * p.SalePrice);
                        totalExpectedProfit += item.ExpectedProfit;
                        totalQuantityInStock += p.CurrentStock;
                    }
                }

                var lowStockItems = inventoryList
                    .Where(x => x.CurrentStock <= 5)
                    .OrderBy(x => x.CurrentStock)
                    .Select(x => new { x.ProductName, x.Barcode, x.CurrentStock })
                    .ToList();

                var topQuantityItems = inventoryList
                    .Where(x => x.CurrentStock > 0)
                    .OrderByDescending(x => x.CurrentStock)
                    .Take(10)
                    .Select(x => new { x.ProductName, x.CurrentStock, x.AveragePurchasePrice, Capital = x.CurrentStock * x.AveragePurchasePrice })
                    .ToList();

                return new JsonResult(new
                {
                    summary = new
                    {
                        totalCapital,
                        totalExpectedSales,
                        totalExpectedProfit,
                        totalItems = inventoryList.Count,
                        lowStockCount = lowStockItems.Count,
                        totalQuantityInStock
                    },
                    lowStockItems,
                    topQuantityItems,
                    inventoryList = inventoryList.OrderBy(x => x.ProductName).ToList()
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        // مێژووی ڕاستکردنەوەکان: ڕێگەپێدراوە بۆ ئەدمین و مەنەجەر
        public async Task<IActionResult> OnGetAdjustmentsHistoryAsync(DateTime? from, DateTime? to, int? productId)
        {
            try
            {
                var fromDate = from ?? DateTime.Today;
                var toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                var query = _context.StockAdjustments!
                    .Include(s => s.Product)
                    .Where(s => s.AdjustmentDate >= fromDate && s.AdjustmentDate <= endDate)
                    .AsNoTracking();

                if (productId.HasValue && productId.Value > 0)
                {
                    query = query.Where(s => s.ProductId == productId.Value);
                }

                var history = await query
                    .OrderByDescending(s => s.AdjustmentDate)
                    .Select(s => new {
                        Id = s.Id,
                        Date = s.AdjustmentDate.ToString("yyyy-MM-dd HH:mm"),
                        ProductName = s.Product != null ? s.Product.Name : "نەزانراو",
                        s.StockBefore,
                        s.StockAfter,
                        s.AdjustmentQuantity,
                        s.AdjustmentType,
                        s.Reason,
                        s.UserName,
                        Cost = s.Product != null ? s.Product.LastPurchasePrice : 0
                    })
                    .ToListAsync();

                return new JsonResult(history);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        // شیکاری کاڵای دیاریکراو: تەنها ڕێگەپێدراوە بۆ ئەدمین
        public async Task<IActionResult> OnGetSingleProductStatsAsync(int productId)
        {
            if (!User.IsInRole("Admin"))
            {
                return new JsonResult(new { success = false, message = "ڕێگەپێنەدراوە. تەنها ئەدمین دەتوانێت ئەم زانیارییانە ببینێت." });
            }

            try
            {
                var product = await _context.Products.FindAsync(productId);
                if (product == null) return new JsonResult(new { success = false, message = "کاڵاکە نەدۆزرایەوە." });

                var avgPricesQuery = await _context.PurchaseItems!
                    .Where(pi => pi.ProductId == productId)
                    .ToListAsync();

                decimal avgPrice = avgPricesQuery.Any() ? avgPricesQuery.Average(x => x.UnitPrice) : product.LastPurchasePrice;

                var stats = new
                {
                    success = true,
                    name = product.Name,
                    stock = product.CurrentStock,
                    avgPrice = avgPrice,
                    salePrice = product.SalePrice,
                    totalCapital = product.CurrentStock * avgPrice,
                    expectedProfit = product.CurrentStock * (product.SalePrice - avgPrice)
                };

                return new JsonResult(stats);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }
    }
}