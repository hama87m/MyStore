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
    // لێرەدا گۆڕانکارییەکە کراوە: تەنها ڕێگە بە ئەدمین و مەنەجەر دەدرێت بێنە ژوورەوە
    [Authorize(Roles = "Admin,Manager")]
    public class PurchasesModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public PurchasesModel(ApplicationDbContext context)
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
                    StorePhone = settings.ContainsKey(SettingsKeys.StorePhone) ? settings[SettingsKeys.StorePhone] : "",
                    StoreAddress = settings.ContainsKey(SettingsKeys.StoreAddress) ? settings[SettingsKeys.StoreAddress] : "",
                    StoreLogo = settings.ContainsKey(SettingsKeys.StoreLogo) ? settings[SettingsKeys.StoreLogo] : ""
                });
            }
            catch
            {
                return new JsonResult(new { StoreName = "Diamond Center" });
            }
        }

        public async Task<IActionResult> OnGetSuppliersListAsync()
        {
            var suppliers = await _context.Suppliers!
                .Where(s => s.IsActive)
                .Select(s => new { s.SupplierId, s.Name })
                .OrderBy(s => s.Name)
                .AsNoTracking()
                .ToListAsync();
            return new JsonResult(suppliers);
        }

        public async Task<IActionResult> OnGetProductsListAsync()
        {
            var products = await _context.Products
                .Where(p => p.IsActive)
                .Select(p => new { p.ProductId, p.Name, p.Barcode })
                .OrderBy(p => p.Name)
                .AsNoTracking()
                .ToListAsync();
            return new JsonResult(products);
        }

        public async Task<IActionResult> OnGetPurchasesSummaryAsync(DateTime? from, DateTime? to)
        {
            try
            {
                var fromDate = from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                var toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                var baseQuery = _context.Purchases!
                    .Where(p => p.PurchaseDate >= fromDate && p.PurchaseDate <= endDate)
                    .AsNoTracking();

                var totalPurchasesAmount = await baseQuery.SumAsync(p => p.TotalAmount);
                var invoiceCount = await baseQuery.CountAsync();

                // داتای چارت: کۆی کڕینەکان بەپێی ڕۆژ
                var dailyDataDb = await baseQuery
                    .GroupBy(p => p.PurchaseDate.Date)
                    .Select(g => new {
                        DateVal = g.Key,
                        Total = g.Sum(p => p.TotalAmount)
                    })
                    .OrderBy(x => x.DateVal)
                    .ToListAsync();

                var dailyPurchases = dailyDataDb.Select(x => new {
                    Date = x.DateVal.ToString("yyyy-MM-dd"),
                    Total = x.Total
                }).ToList();

                return new JsonResult(new
                {
                    summary = new { totalPurchasesAmount, invoiceCount },
                    dailyPurchases
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        // فەنکشنی نوێ بۆ هێنانی داتای چارتی زۆرترین کاڵا کڕدراوەکان
        public async Task<IActionResult> OnGetTopPurchasedProductsAsync(DateTime? from, DateTime? to)
        {
            try
            {
                var fromDate = from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                var toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                var topProducts = await _context.PurchaseItems!
                    .Include(pi => pi.Purchase)
                    .Include(pi => pi.Product)
                    .Where(pi => pi.Purchase != null && pi.Purchase.PurchaseDate >= fromDate && pi.Purchase.PurchaseDate <= endDate)
                    .GroupBy(pi => new { pi.ProductId, pi.Product!.Name })
                    .Select(g => new {
                        id = g.Key.ProductId,
                        name = g.Key.Name,
                        qty = g.Sum(x => x.Quantity),
                        totalCost = g.Sum(x => x.Quantity * x.UnitPrice)
                    })
                    .OrderByDescending(x => x.qty) // ڕیزکردن بەپێی زۆری دانە
                    .Take(10) // تەنها ١٠ پڕکڕینترین
                    .ToListAsync();

                return new JsonResult(topProducts);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnGetProductPurchaseStatsAsync(int productId, DateTime? from, DateTime? to)
        {
            try
            {
                var fromDate = from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                var toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                var query = _context.PurchaseItems!
                    .Include(pi => pi.Purchase)
                    .Where(pi => pi.ProductId == productId && pi.Purchase != null && pi.Purchase.PurchaseDate >= fromDate && pi.Purchase.PurchaseDate <= endDate)
                    .AsNoTracking();

                var totalQty = await query.SumAsync(pi => pi.Quantity);
                var totalCost = await query.SumAsync(pi => pi.Quantity * pi.UnitPrice);
                var timesBought = await query.Select(pi => pi.PurchaseId).Distinct().CountAsync();

                return new JsonResult(new { success = true, totalQty, totalCost, timesBought });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public async Task<IActionResult> OnGetPurchasesTableAsync(DateTime? from, DateTime? to, int? supplierId)
        {
            try
            {
                var fromDate = from ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                var toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                var query = _context.Purchases!
                    .Include(p => p.Supplier)
                    .Where(p => p.PurchaseDate >= fromDate && p.PurchaseDate <= endDate)
                    .AsNoTracking();

                if (supplierId.HasValue && supplierId.Value > 0)
                {
                    query = query.Where(p => p.SupplierId == supplierId.Value);
                }

                var purchasesList = await query
                    .OrderByDescending(p => p.PurchaseDate)
                    .Select(p => new {
                        p.PurchaseId,
                        Date = p.PurchaseDate.ToString("yyyy-MM-dd HH:mm"),
                        SupplierName = p.Supplier != null ? p.Supplier.Name : "نەزانراو",
                        p.TotalAmount
                    })
                    .ToListAsync();

                var tableSummary = new
                {
                    count = purchasesList.Count,
                    totalPurchases = purchasesList.Sum(p => p.TotalAmount)
                };

                return new JsonResult(new { summary = tableSummary, purchases = purchasesList });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnGetPurchasesDropdownAsync()
        {
            var list = await _context.Purchases!
                .Include(p => p.Supplier)
                .OrderByDescending(p => p.PurchaseDate)
                .Take(100)
                .Select(p => new {
                    id = p.PurchaseId,
                    text = $"کڕینی {p.PurchaseId} - {p.Supplier!.Name} ({p.PurchaseDate:yyyy-MM-dd})"
                })
                .ToListAsync();
            return new JsonResult(list);
        }

        public async Task<IActionResult> OnGetPurchaseDetailsAsync(int id)
        {
            try
            {
                var p = await _context.Purchases!
                    .Include(x => x.Supplier)
                    .Include(x => x.PurchaseItems!)
                        .ThenInclude(i => i.Product)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.PurchaseId == id);

                if (p == null) return new JsonResult(new { error = "پسوڵە نەدۆزرایەوە" });

                var data = new
                {
                    PurchaseId = p.PurchaseId,
                    Date = p.PurchaseDate.ToString("yyyy-MM-dd"),
                    SupplierName = p.Supplier?.Name ?? "نەزانراو",
                    TotalAmount = p.TotalAmount,
                    Items = p.PurchaseItems?.Select(i => new {
                        ProductName = i.Product?.Name ?? "نەزانراو",
                        Quantity = i.Quantity,
                        UnitPrice = i.UnitPrice,
                        Subtotal = i.Quantity * i.UnitPrice
                    }).ToList()
                };

                return new JsonResult(data);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }
    }
}