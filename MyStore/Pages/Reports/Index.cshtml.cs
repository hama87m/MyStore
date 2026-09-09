using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;

namespace MyStore.Pages.Reports
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        // ══════════════════════════════════════════════════════════════
        // داشبۆرد - ئامارە گشتییەکان
        // ══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetDashboardAsync()
        {
            var today = DateTime.Today;
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            var startOfMonth = new DateTime(today.Year, today.Month, 1);

            // فرۆشتنی ئەمڕۆ
            var todaySales = await _context.Sales
                .Where(s => s.SaleDate.Date == today && !s.IsVoided)
                .SumAsync(s => (decimal?)s.GrandTotal) ?? 0;

            var todayProfit = await _context.Sales
                .Where(s => s.SaleDate.Date == today && !s.IsVoided)
                .SumAsync(s => (decimal?)s.TotalProfit) ?? 0;

            var todayCount = await _context.Sales
                .CountAsync(s => s.SaleDate.Date == today && !s.IsVoided);

            // فرۆشتنی ئەم هەفتەیە
            var weekSales = await _context.Sales
                .Where(s => s.SaleDate.Date >= startOfWeek && !s.IsVoided)
                .SumAsync(s => (decimal?)s.GrandTotal) ?? 0;

            // فرۆشتنی ئەم مانگە
            var monthSales = await _context.Sales
                .Where(s => s.SaleDate.Date >= startOfMonth && !s.IsVoided)
                .SumAsync(s => (decimal?)s.GrandTotal) ?? 0;

            var monthProfit = await _context.Sales
                .Where(s => s.SaleDate.Date >= startOfMonth && !s.IsVoided)
                .SumAsync(s => (decimal?)s.TotalProfit) ?? 0;

            // کۆی قەرز
            var totalDebt = await _context.Customers
                .Where(c => c.Name != "ڕاستەوخۆ")
                .SumAsync(c => (decimal?)c.Balance) ?? 0;

            // کۆی بەهای ستۆک
            var stockValue = await _context.Products
                .Where(p => p.IsActive && p.CurrentStock > 0)
                .SumAsync(p => (decimal?)(p.CurrentStock * p.LastPurchasePrice)) ?? 0;

            // کاڵا کەم ستۆک
            var lowStockCount = await _context.Products
                .CountAsync(p => p.IsActive && p.CurrentStock <= 5 && p.CurrentStock > 0);

            var outOfStockCount = await _context.Products
                .CountAsync(p => p.IsActive && p.CurrentStock <= 0);

            // فرۆشتنی 7 ڕۆژی ڕابردوو بۆ چارت
            var last7Days = new List<object>();
            for (int i = 6; i >= 0; i--)
            {
                var date = today.AddDays(-i);
                var daySales = await _context.Sales
                    .Where(s => s.SaleDate.Date == date && !s.IsVoided)
                    .SumAsync(s => (decimal?)s.GrandTotal) ?? 0;
                last7Days.Add(new { date = date.ToString("MM/dd"), sales = daySales });
            }

            // 5 کاڵای زۆرترین فرۆش
            var topItemsData = await _context.SaleItems
                .Include(si => si.Product)
                .Include(si => si.Sale)
                .Where(si => si.Sale != null && !si.Sale.IsVoided && si.Sale.SaleDate >= startOfMonth)
                .Select(si => new { si.ProductId, ProductName = si.Product!.Name, si.Quantity })
                .ToListAsync();

            var topProducts = topItemsData
                .GroupBy(si => new { si.ProductId, si.ProductName })
                .Select(g => new { name = g.Key.ProductName, qty = g.Sum(x => x.Quantity) })
                .OrderByDescending(x => x.qty)
                .Take(5)
                .ToList();

            // قازانجی هەفتە
            var weekProfit = await _context.Sales
                .Where(s => s.SaleDate.Date >= startOfWeek && !s.IsVoided)
                .SumAsync(s => (decimal?)s.TotalProfit) ?? 0;

            // چارتی قازانج بەپێی مانگ (6 مانگی کۆتایی)
            var monthlyProfitChart = new List<object>();
            for (int i = 5; i >= 0; i--)
            {
                var mStart = new DateTime(today.Year, today.Month, 1).AddMonths(-i);
                var mEnd = mStart.AddMonths(1).AddTicks(-1);
                var mProfit = await _context.Sales
                    .Where(s => s.SaleDate >= mStart && s.SaleDate <= mEnd && !s.IsVoided)
                    .SumAsync(s => (decimal?)s.TotalProfit) ?? 0;
                monthlyProfitChart.Add(new { month = mStart.ToString("yyyy/MM"), profit = mProfit });
            }

            return new JsonResult(new
            {
                todaySales,
                todayProfit,
                todayCount,
                weekSales,
                weekProfit,
                monthSales,
                monthProfit,
                totalDebt,
                stockValue,
                lowStockCount,
                outOfStockCount,
                last7Days,
                topProducts,
                monthlyProfitChart
            });
        }

        // ══════════════════════════════════════════════════════════════
        // ڕیپۆرتی فرۆشتن
        // ══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetSalesReportAsync(DateTime? from, DateTime? to, int? customerId, string? paymentType)
        {
            var query = _context.Sales
                .Include(s => s.Customer)
                .Where(s => !s.IsVoided)
                .AsQueryable();

            if (from.HasValue)
                query = query.Where(s => s.SaleDate.Date >= from.Value.Date);
            if (to.HasValue)
                query = query.Where(s => s.SaleDate.Date <= to.Value.Date);
            if (customerId.HasValue && customerId > 0)
                query = query.Where(s => s.CustomerId == customerId);
            if (!string.IsNullOrEmpty(paymentType))
            {
                if (paymentType == "cash")
                    query = query.Where(s => s.PaymentType == PaymentType.Cash);
                else if (paymentType == "credit")
                    query = query.Where(s => s.PaymentType == PaymentType.Credit);
            }

            var sales = await query
                .OrderByDescending(s => s.SaleDate)
                .Take(500)
                .Select(s => new
                {
                    s.SaleId,
                    saleDate = s.SaleDate.ToString("yyyy-MM-dd HH:mm"),
                    customerName = s.Customer!.Name,
                    s.TotalAmount,
                    s.Discount,
                    s.GrandTotal,
                    s.TotalProfit,
                    paymentType = s.PaymentType == PaymentType.Cash ? "نەقد" : "قەرز",
                    s.PaidAmount,
                    debt = s.GrandTotal - s.PaidAmount
                })
                .ToListAsync();

            var summary = new
            {
                count = sales.Count,
                totalSales = sales.Sum(s => s.GrandTotal),
                totalProfit = sales.Sum(s => s.TotalProfit),
                totalDiscount = sales.Sum(s => s.Discount),
                totalPaid = sales.Sum(s => s.PaidAmount),
                totalDebt = sales.Sum(s => s.debt)
            };

            return new JsonResult(new { sales, summary });
        }

        // ══════════════════════════════════════════════════════════════
        // ڕیپۆرتی کڕیارەکان - لە CustomerLedger
        // ══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetCustomersReportAsync(bool onlyDebt = false)
        {
            var query = _context.Customers
                .Where(c => c.Name != "ڕاستەوخۆ")
                .AsQueryable();

            if (onlyDebt)
                query = query.Where(c => c.Balance > 0);

            var customers = await query
                .OrderByDescending(c => c.Balance)
                .Select(c => new
                {
                    c.CustomerId,
                    c.Name,
                    c.Phone,
                    c.Balance
                })
                .ToListAsync();

            var totalDebt = customers.Sum(c => c.Balance);

            return new JsonResult(new { customers, totalDebt });
        }

        // وردەکاری کڕیار - لە CustomerLedger
        public async Task<IActionResult> OnGetCustomerDetailsAsync(int customerId)
        {
            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null)
                return new JsonResult(new { success = false, message = "کڕیار نەدۆزرایەوە" });

            var ledger = await _context.CustomerLedgers
                .Where(l => l.CustomerId == customerId && !l.IsDeleted)
                .OrderByDescending(l => l.TransactionDate)
                .Take(100)
                .Select(l => new
                {
                    l.LedgerId,
                    date = l.TransactionDate.ToString("yyyy-MM-dd HH:mm"),
                    type = l.TransactionType.ToString(),
                    typeName = l.TransactionType == LedgerTransactionType.Sale ? "فرۆشتن" :
                               l.TransactionType == LedgerTransactionType.Payment ? "واصڵ" :
                               l.TransactionType == LedgerTransactionType.Refund ? "گەڕاندنەوە" : "ڕاستکردنەوە",
                    l.Description,
                    l.DebitAmount,
                    l.CreditAmount,
                    l.RunningBalance,
                    l.ReferenceId,
                    l.ReferenceType
                })
                .ToListAsync();

            return new JsonResult(new
            {
                success = true,
                customer = new { customer.CustomerId, customer.Name, customer.Phone, customer.Balance },
                ledger
            });
        }

        // ══════════════════════════════════════════════════════════════
        // ڕیپۆرتی ستۆک
        // ══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetStockReportAsync(string filter = "all")
        {
            var query = _context.Products
                .Where(p => p.IsActive)
                .AsQueryable();

            if (filter == "low")
                query = query.Where(p => p.CurrentStock <= 5 && p.CurrentStock > 0);
            else if (filter == "out")
                query = query.Where(p => p.CurrentStock <= 0);

            var products = await query
                .OrderBy(p => p.CurrentStock)
                .Select(p => new
                {
                    p.ProductId,
                    p.Name,
                    p.Barcode,
                    p.CurrentStock,
                    p.LastPurchasePrice,
                    p.SalePrice,
                    stockValue = p.CurrentStock * p.LastPurchasePrice
                })
                .ToListAsync();

            var summary = new
            {
                totalProducts = products.Count,
                totalStockValue = products.Sum(p => p.stockValue),
                totalQty = products.Sum(p => p.CurrentStock)
            };

            return new JsonResult(new { products, summary });
        }

        // ══════════════════════════════════════════════════════════════
        // ڕیپۆرتی قازانج
        // ══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetProfitReportAsync(DateTime? from, DateTime? to)
        {
            var fromDate = from ?? DateTime.Today.AddDays(-30);
            var toDate = to ?? DateTime.Today;

            // قازانج بەپێی ڕۆژ - سەرەتا داتا بهێنە دواتر گرووپ بکە
            var salesData = await _context.Sales
                .Where(s => s.SaleDate.Date >= fromDate.Date && s.SaleDate.Date <= toDate.Date && !s.IsVoided)
                .Select(s => new { s.SaleDate, s.GrandTotal, s.TotalProfit })
                .ToListAsync();

            var dailyProfit = salesData
                .GroupBy(s => s.SaleDate.Date)
                .Select(g => new
                {
                    date = g.Key.ToString("yyyy-MM-dd"),
                    sales = g.Sum(x => x.GrandTotal),
                    profit = g.Sum(x => x.TotalProfit),
                    count = g.Count()
                })
                .OrderBy(x => x.date)
                .ToList();

            // قازانج بەپێی کاڵا
            var saleItemsData = await _context.SaleItems
                .Include(si => si.Product)
                .Include(si => si.Sale)
                .Where(si => si.Sale != null && !si.Sale.IsVoided &&
                       si.Sale.SaleDate.Date >= fromDate.Date &&
                       si.Sale.SaleDate.Date <= toDate.Date)
                .Select(si => new {
                    si.ProductId,
                    ProductName = si.Product!.Name,
                    si.Quantity,
                    si.UnitPrice,
                    si.UnitPurchasePrice
                })
                .ToListAsync();

            var productProfit = saleItemsData
                .GroupBy(si => new { si.ProductId, si.ProductName })
                .Select(g => new
                {
                    productName = g.Key.ProductName,
                    qty = g.Sum(x => x.Quantity),
                    revenue = g.Sum(x => x.Quantity * x.UnitPrice),
                    cost = g.Sum(x => x.Quantity * x.UnitPurchasePrice),
                    profit = g.Sum(x => x.Quantity * (x.UnitPrice - x.UnitPurchasePrice))
                })
                .OrderByDescending(x => x.profit)
                .Take(20)
                .ToList();

            var summary = new
            {
                totalSales = dailyProfit.Sum(d => d.sales),
                totalProfit = dailyProfit.Sum(d => d.profit),
                totalCount = dailyProfit.Sum(d => d.count),
                avgDailyProfit = dailyProfit.Any() ? dailyProfit.Average(d => d.profit) : 0
            };

            return new JsonResult(new { dailyProfit, productProfit, summary });
        }


        // ══════════════════════════════════════════════════════════════
        // ڕیپۆرتی کڕینەکان
        // ══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetPurchasesReportAsync(DateTime? from, DateTime? to)
        {
            var fromDate = from ?? DateTime.Today;
            var toDate = to ?? DateTime.Today;

            var purchases = await _context.Purchases
                .Include(p => p.Supplier)
                .Include(p => p.PurchaseItems!)
                .Where(p => p.PurchaseDate.Date >= fromDate.Date && p.PurchaseDate.Date <= toDate.Date)
                .OrderByDescending(p => p.PurchaseDate)
                .Take(500)
                .Select(p => new
                {
                    p.PurchaseId,
                    purchaseDate = p.PurchaseDate.ToString("yyyy-MM-dd HH:mm"),
                    supplierName = p.Supplier!.Name,
                    p.TotalAmount,
                    itemCount = p.PurchaseItems!.Count,
                    totalQty = p.PurchaseItems!.Sum(i => i.Quantity)
                })
                .ToListAsync();

            var summary = new
            {
                count = purchases.Count,
                totalAmount = purchases.Sum(p => p.TotalAmount),
                totalItems = purchases.Sum(p => p.itemCount)
            };

            return new JsonResult(new { purchases, summary });
        }

        // ══════════════════════════════════════════════════════════════
        // ڕیپۆرتی قاسە
        // ══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetCashReportAsync(DateTime? from, DateTime? to)
        {
            var fromDate = from ?? DateTime.Today;
            var toDate = to ?? DateTime.Today;

            var transactions = await _context.CashTransactions
                .Where(t => t.CreatedAt.Date >= fromDate.Date && t.CreatedAt.Date <= toDate.Date)
                .OrderByDescending(t => t.CreatedAt)
                .Take(500)
                .Select(t => new
                {
                    t.Id,
                    date = t.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    t.FlowType,
                    t.IsIncome,
                    typeName = t.IsIncome ? "هاتوو" : "دەرچوو",
                    t.Amount,
                    t.Description,
                    t.BalanceAfter
                })
                .ToListAsync();

            var totalIn = transactions.Where(t => t.IsIncome).Sum(t => t.Amount);
            var totalOut = transactions.Where(t => !t.IsIncome).Sum(t => t.Amount);
            var currentBalance = transactions.FirstOrDefault()?.BalanceAfter ?? 0;

            var summary = new
            {
                totalIn,
                totalOut,
                net = totalIn - totalOut,
                currentBalance,
                count = transactions.Count
            };

            return new JsonResult(new { transactions, summary });
        }

        // ══════════════════════════════════════════════════════════════
        // لیستی کڕیارەکان بۆ dropdown
        // ══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetCustomersListAsync()
        {
            var customers = await _context.Customers
                .Where(c => c.Name != "ڕاستەوخۆ")
                .OrderBy(c => c.Name)
                .Select(c => new { c.CustomerId, c.Name })
                .ToListAsync();

            return new JsonResult(customers);
        }
    }
}
// NOTE: this will be inserted before the closing braces