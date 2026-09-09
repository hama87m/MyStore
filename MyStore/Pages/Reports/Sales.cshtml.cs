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
using System.Security.Claims;

namespace MyStore.Pages.Reports
{
    [Authorize]
    public class SalesModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SalesModel(ApplicationDbContext context)
        {
            _context = context;
        }

        // گۆڕاوێک بۆ ئەوەی ڕووکارەکە (HTML) بزانێت کە ئایا کاشێرە یان نا
        public bool IsCashier { get; set; }

        public void OnGet()
        {
            IsCashier = User.IsInRole("Cashier");
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

        public async Task<IActionResult> OnGetCustomersListAsync()
        {
            var customers = await _context.Customers
                .Select(c => new { c.CustomerId, c.Name })
                .OrderBy(c => c.Name)
                .AsNoTracking()
                .ToListAsync();
            return new JsonResult(customers);
        }

        public async Task<IActionResult> OnGetSalesmenListAsync()
        {
            try
            {
                var userIds = await _context.Sales
                    .Where(s => !string.IsNullOrEmpty(s.UserId) && !s.IsVoided)
                    .Select(s => s.UserId)
                    .Distinct()
                    .ToListAsync();

                var userNames = new Dictionary<string, string>();
                try
                {
                    userNames = await _context.Users
                        .Where(u => userIds.Contains(u.Id))
                        .ToDictionaryAsync(u => u.Id, u => u.UserName ?? "بەکارهێنەر");
                }
                catch { }

                var list = userIds.Select(id => new {
                    id = id,
                    name = userNames.ContainsKey(id!) ? userNames[id!].Split('@')[0] : "یوسەر (" + id!.Substring(0, 4) + ")"
                }).OrderBy(x => x.name).ToList();

                return new JsonResult(list);
            }
            catch
            {
                return new JsonResult(new List<object>());
            }
        }

        public async Task<IActionResult> OnGetInvoiceDetailsAsync(int id)
        {
            try
            {
                bool isCashier = User.IsInRole("Cashier");
                string currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

                var sale = await _context.Sales
                    .Include(s => s.Customer)
                    .Include(s => s.SaleItems!)
                        .ThenInclude(si => si.Product)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.SaleId == id);

                if (sale == null) return new JsonResult(new { error = "پسوڵە نەدۆزرایەوە" });

                // ئەگەر کاشێر بوو و هەوڵیدا پسوڵەی کەسێکی تر بکاتەوە
                if (isCashier && sale.UserId != currentUserId)
                {
                    return new JsonResult(new { error = "ببورە، دەسەڵاتت نییە ئەم پسوڵەیە ببینی" });
                }

                string salesmanName = "نەزانراو";
                if (!string.IsNullOrEmpty(sale.UserId))
                {
                    try
                    {
                        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == sale.UserId);
                        if (user != null) salesmanName = user.UserName?.Split('@')[0] ?? "یوسەر";
                    }
                    catch { }
                }

                var settings = await _context.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
                string storeName = settings.ContainsKey(SettingsKeys.StoreName) ? settings[SettingsKeys.StoreName] : "Diamond Center";
                string storePhone = settings.ContainsKey(SettingsKeys.StorePhone) ? settings[SettingsKeys.StorePhone] : "";
                string storeAddress = settings.ContainsKey(SettingsKeys.StoreAddress) ? settings[SettingsKeys.StoreAddress] : "";
                string storeLogo = settings.ContainsKey(SettingsKeys.StoreLogo) ? settings[SettingsKeys.StoreLogo] : "";

                var invoice = new
                {
                    SaleId = sale.SaleId,
                    Date = sale.SaleDate.ToString("yyyy-MM-dd HH:mm"),
                    CustomerName = sale.Customer?.Name ?? "نەزانراو",
                    Salesman = salesmanName,
                    PaymentType = sale.PaymentType == PaymentType.Cash ? "نەقد" : "قەرز",
                    TotalAmount = sale.TotalAmount,
                    Discount = sale.Discount,
                    GrandTotal = sale.GrandTotal,
                    PaidAmount = sale.PaidAmount,
                    Debt = sale.GrandTotal - sale.PaidAmount,

                    StoreName = storeName,
                    StorePhone = storePhone,
                    StoreAddress = storeAddress,
                    StoreLogo = storeLogo,

                    Items = sale.SaleItems?.Select(si => new {
                        ProductName = si.Product?.Name ?? "نەزانراو",
                        Qty = si.Quantity,
                        Price = si.UnitPrice,
                        Total = si.Quantity * si.UnitPrice
                    }).ToList()
                };

                return new JsonResult(invoice);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnGetSalesSummaryAsync(DateTime? from, DateTime? to)
        {
            try
            {
                bool isCashier = User.IsInRole("Cashier");
                string currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

                var fromDate = from ?? DateTime.Today;
                var toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                var baseQuery = _context.Sales
                    .Where(s => !s.IsVoided && s.SaleDate >= fromDate && s.SaleDate <= endDate)
                    .AsNoTracking();

                // فلتەرکردنی داتاکان تەنها بۆ کاشێرەکە
                if (isCashier)
                {
                    baseQuery = baseQuery.Where(s => s.UserId == currentUserId);
                }

                var totalSales = await baseQuery.SumAsync(s => s.GrandTotal);
                // قازانج بشارەوە ئەگەر کاشێر بوو
                var totalProfit = isCashier ? 0 : await baseQuery.SumAsync(s => s.TotalProfit);
                var invoiceCount = await baseQuery.CountAsync();
                var totalDebt = await baseQuery.SumAsync(s => s.GrandTotal - s.PaidAmount);

                var dailyDataDb = await baseQuery
                    .GroupBy(s => s.SaleDate.Date)
                    .Select(g => new {
                        DateVal = g.Key,
                        Total = g.Sum(s => s.GrandTotal)
                    })
                    .OrderBy(x => x.DateVal)
                    .ToListAsync();

                var dailySales = dailyDataDb.Select(x => new {
                    Date = x.DateVal.ToString("yyyy-MM-dd"),
                    Total = x.Total
                }).ToList();

                var paymentStatsDb = await baseQuery
                    .GroupBy(s => s.PaymentType)
                    .Select(g => new {
                        TypeEnum = g.Key,
                        Total = g.Sum(s => s.GrandTotal)
                    })
                    .ToListAsync();

                var paymentStats = paymentStatsDb.Select(x => new {
                    Type = x.TypeEnum == PaymentType.Cash ? "نەقد" : "قەرز",
                    Total = x.Total
                }).ToList();

                var topCustomers = await baseQuery
                    .Where(s => s.Customer != null)
                    .GroupBy(s => new { s.CustomerId, s.Customer!.Name })
                    .Select(g => new {
                        Name = g.Key.Name,
                        TotalBought = g.Sum(s => s.GrandTotal)
                    })
                    .OrderByDescending(x => x.TotalBought)
                    .Take(5)
                    .ToListAsync();

                // بۆ کاڵاکان
                var productsQuery = _context.SaleItems
                    .Include(si => si.Sale)
                    .Include(si => si.Product)
                    .Where(si => si.Sale != null && !si.Sale.IsVoided && si.Sale.SaleDate >= fromDate && si.Sale.SaleDate <= endDate);

                if (isCashier)
                {
                    productsQuery = productsQuery.Where(si => si.Sale!.UserId == currentUserId);
                }

                var topProducts = await productsQuery
                    .GroupBy(si => new { si.ProductId, si.Product!.Name })
                    .Select(g => new {
                        Name = g.Key.Name,
                        Qty = g.Sum(x => x.Quantity)
                    })
                    .OrderByDescending(x => x.Qty)
                    .Take(5)
                    .ToListAsync();

                return new JsonResult(new
                {
                    summary = new { totalSales, totalProfit, invoiceCount, totalDebt },
                    dailySales,
                    paymentStats,
                    topCustomers,
                    topProducts
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnGetSalesTableAsync(DateTime? from, DateTime? to, int? customerId, string paymentType, string salesman)
        {
            try
            {
                bool isCashier = User.IsInRole("Cashier");
                string currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

                var fromDate = from ?? DateTime.Today;
                var toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                var query = _context.Sales
                    .Include(s => s.Customer)
                    .Where(s => !s.IsVoided && s.SaleDate >= fromDate && s.SaleDate <= endDate)
                    .AsNoTracking();

                // جێبەجێکردنی فلتەرەکان
                if (isCashier)
                {
                    query = query.Where(s => s.UserId == currentUserId);
                }
                else if (!string.IsNullOrEmpty(salesman))
                {
                    query = query.Where(s => s.UserId == salesman);
                }

                if (customerId.HasValue && customerId.Value > 0)
                {
                    query = query.Where(s => s.CustomerId == customerId.Value);
                }

                if (!string.IsNullOrEmpty(paymentType))
                {
                    if (paymentType.Equals("cash", StringComparison.OrdinalIgnoreCase))
                        query = query.Where(s => s.PaymentType == PaymentType.Cash);
                    else if (paymentType.Equals("credit", StringComparison.OrdinalIgnoreCase))
                        query = query.Where(s => s.PaymentType == PaymentType.Credit);
                }

                var salesListDb = await query
                    .OrderByDescending(s => s.SaleDate)
                    .Select(s => new {
                        s.SaleId,
                        s.SaleDate,
                        CustomerName = s.Customer != null ? s.Customer.Name : "نەزانراو",
                        s.GrandTotal,
                        TotalProfit = isCashier ? 0 : s.TotalProfit, // شاردنەوەی قازانج بۆ کاشێر
                        s.PaymentType,
                        s.PaidAmount,
                        Debt = s.GrandTotal - s.PaidAmount,
                        UserId = s.UserId
                    })
                    .ToListAsync();

                var userIds = salesListDb.Where(x => !string.IsNullOrEmpty(x.UserId)).Select(x => x.UserId).Distinct().ToList();
                var userNames = new Dictionary<string, string>();
                try
                {
                    userNames = await _context.Users
                        .Where(u => userIds.Contains(u.Id))
                        .ToDictionaryAsync(u => u.Id, u => u.UserName ?? "بەکارهێنەر");
                }
                catch { }

                var salesList = salesListDb.Select(s => new {
                    s.SaleId,
                    Date = s.SaleDate.ToString("yyyy-MM-dd HH:mm"),
                    s.CustomerName,
                    s.GrandTotal,
                    s.TotalProfit,
                    PaymentType = s.PaymentType == PaymentType.Cash ? "نەقد" : "قەرز",
                    s.PaidAmount,
                    s.Debt,
                    Salesman = !string.IsNullOrEmpty(s.UserId) && userNames.ContainsKey(s.UserId)
                                ? userNames[s.UserId].Split('@')[0]
                                : (!string.IsNullOrEmpty(s.UserId) ? "یوسەر (" + s.UserId.Substring(0, 4) + ")" : "سیستەم")
                }).ToList();

                var tableSummary = new
                {
                    count = salesList.Count,
                    totalSales = salesList.Sum(s => s.GrandTotal),
                    totalProfit = isCashier ? 0 : salesList.Sum(s => s.TotalProfit), // شاردنەوەی قازانج
                    totalPaid = salesList.Sum(s => s.PaidAmount),
                    totalDebt = salesList.Sum(s => s.Debt)
                };

                return new JsonResult(new { summary = tableSummary, sales = salesList });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnGetTopProductsReportAsync(DateTime? from, DateTime? to)
        {
            try
            {
                bool isCashier = User.IsInRole("Cashier");
                string currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

                var fromDate = from ?? DateTime.Today;
                var toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                var query = _context.SaleItems
                    .Include(si => si.Sale)
                    .Include(si => si.Product)
                    .Where(si => si.Sale != null && !si.Sale.IsVoided && si.Sale.SaleDate >= fromDate && si.Sale.SaleDate <= endDate);

                if (isCashier)
                {
                    query = query.Where(si => si.Sale!.UserId == currentUserId);
                }

                var topProducts = await query
                    .GroupBy(si => new { si.ProductId, si.Product!.Name })
                    .Select(g => new {
                        id = g.Key.ProductId,
                        name = g.Key.Name,
                        qty = g.Sum(x => x.Quantity),
                        totalAmount = g.Sum(x => x.Quantity * x.UnitPrice)
                    })
                    .OrderByDescending(x => x.qty)
                    .Take(5)
                    .ToListAsync();

                return new JsonResult(topProducts);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        public async Task<IActionResult> OnGetSearchProductsAsync(string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return new JsonResult(new List<object>());

            var products = await _context.Products
                .Where(p => p.Name.Contains(term) || (p.Barcode != null && p.Barcode.Contains(term)))
                .Select(p => new { id = p.ProductId, name = p.Name, barcode = p.Barcode })
                .Take(10)
                .AsNoTracking()
                .ToListAsync();

            return new JsonResult(products);
        }

        public async Task<IActionResult> OnGetSpecificProductsReportAsync(DateTime? from, DateTime? to, string ids)
        {
            try
            {
                bool isCashier = User.IsInRole("Cashier");
                string currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

                var fromDate = from ?? DateTime.Today;
                var toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                if (string.IsNullOrWhiteSpace(ids)) return new JsonResult(new List<object>());

                var idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(int.Parse)
                                .ToList();

                var productsData = await _context.Products
                    .Where(p => idList.Contains(p.ProductId))
                    .Select(p => new {
                        id = p.ProductId,
                        name = p.Name,
                        qty = _context.SaleItems
                            .Where(si => si.ProductId == p.ProductId && si.Sale != null && !si.Sale.IsVoided && si.Sale.SaleDate >= fromDate && si.Sale.SaleDate <= endDate && (!isCashier || si.Sale.UserId == currentUserId))
                            .Sum(si => (int?)si.Quantity) ?? 0,
                        totalAmount = _context.SaleItems
                            .Where(si => si.ProductId == p.ProductId && si.Sale != null && !si.Sale.IsVoided && si.Sale.SaleDate >= fromDate && si.Sale.SaleDate <= endDate && (!isCashier || si.Sale.UserId == currentUserId))
                            .Sum(si => (decimal?)(si.Quantity * si.UnitPrice)) ?? 0m
                    })
                    .ToListAsync();

                productsData = productsData.OrderByDescending(p => p.qty).ToList();

                return new JsonResult(productsData);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }
    }
}