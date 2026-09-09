using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MyStore.Pages.Reports
{
    // لێرەدا دەسەڵاتمان تەنها بە Admin داوە، هیچ کەسێکی تر ناتوانێت پەڕەکە بکاتەوە یان زانیارییەکان ببینێت
    [Authorize(Roles = "Admin")]
    public class ProfitModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public ProfitModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public void OnGet()
        {
            // تەنها پەڕەکە لۆد دەکات، داتاکان بە جاڤاسکریپت دەهێنرێن
        }

        // ══════════════════════════════════════════════════════════════
        // 1. هێنانی لیستی هەموو کاڵاکان بۆ درۆپداونەکەی ناو پەڕەکە
        // ══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetProductsListAsync()
        {
            var products = await _context.Products
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .Select(p => new { productId = p.ProductId, name = p.Name, barcode = p.Barcode })
                .AsNoTracking()
                .ToListAsync();

            return new JsonResult(products);
        }

        // ══════════════════════════════════════════════════════════════
        // 2. تابی یەکەم: قازانجی گشتی
        // ══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetAllProfitAsync(DateTime? from, DateTime? to)
        {
            var fromDate = from ?? DateTime.Today.AddDays(-30);
            var toDate = to ?? DateTime.Today;
            var endDate = toDate.Date.AddDays(1).AddTicks(-1);

            // هێنانی پسوڵەکان لەسەر بنەمای مۆدێلەکانی خۆت
            var salesQuery = _context.Sales
                .Include(s => s.Customer)
                .Where(s => !s.IsVoided && s.SaleDate >= fromDate.Date && s.SaleDate <= endDate)
                .AsNoTracking();

            var salesList = await salesQuery
                .OrderByDescending(s => s.SaleDate)
                .Select(s => new
                {
                    saleId = s.SaleId,
                    date = s.SaleDate.ToString("yyyy-MM-dd HH:mm"),
                    customerName = s.Customer != null ? s.Customer.Name : "نەزانراو",
                    sales = s.GrandTotal,
                    profit = s.TotalProfit,
                    cost = s.GrandTotal - s.TotalProfit
                })
                .ToListAsync();

            var summary = new
            {
                totalSales = salesList.Sum(s => s.sales),
                totalProfit = salesList.Sum(s => s.profit),
                totalCost = salesList.Sum(s => s.cost)
            };

            return new JsonResult(new { summary, sales = salesList });
        }

        // ══════════════════════════════════════════════════════════════
        // 3. تابی دووەم: قازانجی یەک کاڵا بە دیاریکراوی
        // ══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetItemProfitAsync(DateTime? from, DateTime? to, int productId)
        {
            var fromDate = from ?? DateTime.Today.AddDays(-30);
            var toDate = to ?? DateTime.Today;
            var endDate = toDate.Date.AddDays(1).AddTicks(-1);

            // بەکارهێنانی Quantity, UnitPrice, UnitPurchasePrice لە SaleItem
            var itemsQuery = _context.SaleItems
                .Include(si => si.Sale)
                .Where(si => si.ProductId == productId &&
                             si.Sale != null && !si.Sale.IsVoided &&
                             si.Sale.SaleDate >= fromDate.Date && si.Sale.SaleDate <= endDate)
                .AsNoTracking();

            var itemsList = await itemsQuery
                .OrderByDescending(si => si.Sale!.SaleDate)
                .Select(si => new
                {
                    saleId = si.SaleId,
                    date = si.Sale!.SaleDate.ToString("yyyy-MM-dd HH:mm"),
                    qty = si.Quantity,
                    buyPrice = si.UnitPurchasePrice,
                    sellPrice = si.UnitPrice,
                    itemProfit = si.Quantity * (si.UnitPrice - si.UnitPurchasePrice)
                })
                .ToListAsync();

            var totalQty = itemsList.Sum(i => i.qty);
            var totalProfit = itemsList.Sum(i => i.itemProfit);
            var avgProfit = totalQty > 0 ? (totalProfit / totalQty) : 0;

            var summary = new
            {
                totalQty,
                totalProfit,
                avgProfit
            };

            return new JsonResult(new { summary, items = itemsList });
        }

        // ══════════════════════════════════════════════════════════════
        // 4. تابی سێیەم: بەراوردکردنی کاڵاکان بۆ چارت
        // ══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetComparisonAsync(DateTime? from, DateTime? to, string productIds)
        {
            var fromDate = from ?? DateTime.Today.AddDays(-30);
            var toDate = to ?? DateTime.Today;
            var endDate = toDate.Date.AddDays(1).AddTicks(-1);

            var pIds = new List<int>();
            if (!string.IsNullOrWhiteSpace(productIds))
            {
                pIds = productIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Where(x => int.TryParse(x.Trim(), out _))
                                 .Select(int.Parse)
                                 .ToList();
            }

            if (!pIds.Any())
                return new JsonResult(new { labels = new string[0], salesData = new decimal[0], profitData = new decimal[0] });

            var groupedData = await _context.SaleItems
                .Include(si => si.Product)
                .Include(si => si.Sale)
                .Where(si => pIds.Contains(si.ProductId) &&
                             si.Sale != null && !si.Sale.IsVoided &&
                             si.Sale.SaleDate >= fromDate.Date && si.Sale.SaleDate <= endDate)
                .GroupBy(si => new { si.ProductId, si.Product!.Name })
                .Select(g => new
                {
                    ProductName = g.Key.Name,
                    TotalSales = g.Sum(x => x.Quantity * x.UnitPrice),
                    TotalProfit = g.Sum(x => x.Quantity * (x.UnitPrice - x.UnitPurchasePrice))
                })
                .AsNoTracking()
                .ToListAsync();

            var labels = groupedData.Select(g => g.ProductName).ToArray();
            var salesData = groupedData.Select(g => g.TotalSales).ToArray();
            var profitData = groupedData.Select(g => g.TotalProfit).ToArray();

            return new JsonResult(new { labels, salesData, profitData });
        }
    }
}