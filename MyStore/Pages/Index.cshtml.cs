using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;

namespace MyStore.Pages
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public IndexModel(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ═══ دۆخی گشتی بازاڕ (جێگیر) — تەنها بۆ مەنەجەر ═══
        public decimal CashBalance { get; set; }
        public decimal TotalStoreDebt { get; set; }
        public int TotalCustomers { get; set; }
        public int TotalProducts { get; set; }
        public int OutOfStockCount { get; set; }
        public int LowStockCount { get; set; }

        // ═══ کارتەکانی ماوەی هەڵبژێردراو (دەگۆڕێن) ═══
        public decimal PeriodSales { get; set; }
        public decimal PeriodReceived { get; set; }
        public decimal PeriodDebt { get; set; }
        public decimal PeriodProfit { get; set; }
        public int PeriodInvoicesCount { get; set; }
        public decimal PeriodPurchases { get; set; }

        // ═══ ماوەی هەڵبژێردراو ═══
        public string CurrentPeriod { get; set; } = "Today";

        // ═══ خشتەکان ═══
        public IList<RecentSaleViewModel> RecentSales { get; set; } = new List<RecentSaleViewModel>();
        public IList<RecentPurchaseViewModel> RecentPurchases { get; set; } = new List<RecentPurchaseViewModel>();

        // ═══ ئاگادارییەکان ═══
        public IList<ProductAlertViewModel> OutOfStockList { get; set; } = new List<ProductAlertViewModel>();
        public IList<ProductAlertViewModel> LowStockList { get; set; } = new List<ProductAlertViewModel>();
        public IList<TopDebtorViewModel> TopDebtors { get; set; } = new List<TopDebtorViewModel>();

        // ═══ چارت (ماوەی هەڵبژێردراو) ═══
        public string ChartLabelsJson { get; set; } = "[]";
        public string ChartSalesJson { get; set; } = "[]";
        public string ChartProfitJson { get; set; } = "[]";

        // ═══ ڕۆڵ ═══
        public bool IsManager { get; set; }
        public bool IsCashier { get; set; }

        public async Task OnGetAsync(string? period = "Today")
        {
            CurrentPeriod = period ?? "Today";
            await LoadDataAsync();
        }

        // AJAX endpoint بۆ گۆڕینی ماوە بەبێ reload ی پەیج
        public async Task<IActionResult> OnGetPeriodDataAsync(string period)
        {
            CurrentPeriod = period ?? "Today";
            await LoadDataAsync();

            return new JsonResult(new
            {
                cards = new
                {
                    periodSales = PeriodSales,
                    periodReceived = PeriodReceived,
                    periodDebt = PeriodDebt,
                    periodProfit = PeriodProfit,
                    periodInvoicesCount = PeriodInvoicesCount,
                    periodPurchases = PeriodPurchases
                },
                chart = new
                {
                    labels = ChartLabelsJson,
                    sales = ChartSalesJson,
                    profit = ChartProfitJson
                },
                recentSales = RecentSales,
                recentPurchases = RecentPurchases
            });
        }

        private async Task LoadDataAsync()
        {
            var today = DateTime.Today;
            var currentUser = await _userManager.GetUserAsync(User);
            var currentUserId = currentUser?.Id;

            IsManager = User.IsInRole("Manager") || User.IsInRole("Admin");
            IsCashier = User.IsInRole("Cashier");

            // ═══ دیاریکردنی ماوە ═══
            var (fromDate, toDate) = GetPeriodRange(CurrentPeriod, today);
            var endDate = toDate.AddDays(1).AddTicks(-1);

            // ═══ ١. ئاماری فرۆشتنی ماوە ═══
            IQueryable<Sale> salesQuery = _context.Sales
                .Where(s => s.SaleDate >= fromDate && s.SaleDate <= endDate && !s.IsVoided);

            if (IsCashier && !IsManager)
                salesQuery = salesQuery.Where(s => s.UserId == currentUserId);

            var periodSalesData = await salesQuery.ToListAsync();

            PeriodSales = periodSalesData.Sum(s => s.GrandTotal);
            PeriodReceived = periodSalesData.Sum(s => s.PaidAmount);
            PeriodDebt = PeriodSales - PeriodReceived;
            PeriodProfit = periodSalesData.Sum(s => s.TotalProfit);
            PeriodInvoicesCount = periodSalesData.Count;

            // ═══ ٢. داتای جێگیر (تەنها بۆ مەنەجەر) ═══
            if (IsManager)
            {
                TotalProducts = await _context.Products.CountAsync(p => p.IsActive);
                TotalCustomers = await _context.Customers.CountAsync();

                // کۆی قەرزی گشتی بازاڕ (باڵانسی کڕیاران)
                TotalStoreDebt = await _context.Customers
                    .Where(c => c.Balance > 0)
                    .SumAsync(c => (decimal?)c.Balance) ?? 0;

                // باڵانسی قاسە
                var lastCashTx = await _context.CashTransactions
                    .OrderByDescending(c => c.Id)
                    .FirstOrDefaultAsync();
                CashBalance = lastCashTx?.BalanceAfter ?? 0;

                // کڕینی ماوە
                PeriodPurchases = await _context.Purchases
                    .Where(p => p.PurchaseDate >= fromDate && p.PurchaseDate <= endDate)
                    .SumAsync(p => (decimal?)p.TotalAmount) ?? 0;

                // کاڵا ستۆک
                OutOfStockCount = await _context.Products
                    .CountAsync(p => p.IsActive && p.CurrentStock <= 0);
                LowStockCount = await _context.Products
                    .CountAsync(p => p.IsActive && p.CurrentStock > 0 && p.CurrentStock <= 5);

                // ═══ ئاگادارییەکان: کاڵای تەواوبوو ═══
                OutOfStockList = await _context.Products
                    .Where(p => p.IsActive && p.CurrentStock <= 0)
                    .OrderBy(p => p.Name)
                    .Take(5)
                    .Select(p => new ProductAlertViewModel
                    {
                        ProductId = p.ProductId,
                        Name = p.Name,
                        CurrentStock = p.CurrentStock,
                        Barcode = p.Barcode ?? ""
                    })
                    .ToListAsync();

                // کاڵای کەم (١ تا ٥ دانە)
                LowStockList = await _context.Products
                    .Where(p => p.IsActive && p.CurrentStock > 0 && p.CurrentStock <= 5)
                    .OrderBy(p => p.CurrentStock)
                    .Take(5)
                    .Select(p => new ProductAlertViewModel
                    {
                        ProductId = p.ProductId,
                        Name = p.Name,
                        CurrentStock = p.CurrentStock,
                        Barcode = p.Barcode ?? ""
                    })
                    .ToListAsync();

                // زۆرترین قەرزدارەکان
                TopDebtors = await _context.Customers
                    .Where(c => c.Balance > 0)
                    .OrderByDescending(c => c.Balance)
                    .Take(5)
                    .Select(c => new TopDebtorViewModel
                    {
                        CustomerId = c.CustomerId,
                        Name = c.Name,
                        Phone = c.Phone ?? "",
                        Balance = c.Balance
                    })
                    .ToListAsync();

                // دوایین کڕینەکان
                var recentPurchases = await _context.Purchases
                    .Include(p => p.Supplier)
                    .OrderByDescending(p => p.PurchaseId)
                    .Take(10)
                    .ToListAsync();
                RecentPurchases = recentPurchases.Select(p => new RecentPurchaseViewModel
                {
                    PurchaseId = p.PurchaseId,
                    PurchaseDate = p.PurchaseDate,
                    SupplierName = p.Supplier?.Name ?? "-",
                    TotalAmount = p.TotalAmount
                }).ToList();
            }

            // ═══ ٣. دوایین فرۆشتنەکان ═══
            IQueryable<Sale> recentSalesQuery = _context.Sales
                .Include(s => s.Customer)
                .Where(s => !s.IsVoided);

            if (IsCashier && !IsManager)
                recentSalesQuery = recentSalesQuery.Where(s => s.UserId == currentUserId);

            var recentSales = await recentSalesQuery
                .OrderByDescending(s => s.SaleId)
                .Take(10)
                .ToListAsync();

            RecentSales = recentSales.Select(s => new RecentSaleViewModel
            {
                SaleId = s.SaleId,
                SaleDate = s.SaleDate,
                CustomerName = s.Customer?.Name ?? "-",
                GrandTotal = s.GrandTotal,
                PaidAmount = s.PaidAmount,
                RemainingDebt = s.GrandTotal - s.PaidAmount,
                PaymentType = s.PaymentType,
                TotalProfit = s.TotalProfit
            }).ToList();

            // ═══ ٤. چارت ═══
            await BuildChartAsync(fromDate, toDate, currentUserId);
        }

        // ═══ دیاریکردنی ماوە ═══
        private (DateTime from, DateTime to) GetPeriodRange(string period, DateTime today)
        {
            // دۆزینەوەی سەرەتای هەفتە (شەممە)
            int diff = (7 + (today.DayOfWeek - DayOfWeek.Saturday)) % 7;
            DateTime startOfWeek = today.AddDays(-1 * diff);

            return period switch
            {
                "Week" => (startOfWeek, today),
                "Month" => (new DateTime(today.Year, today.Month, 1), today),
                "Year" => (new DateTime(today.Year, 1, 1), today),
                "AllTime" => (new DateTime(2000, 1, 1), today),
                _ => (today, today) // Today (default)
            };
        }

        // ═══ چارتی ماوە ═══
        private async Task BuildChartAsync(DateTime fromDate, DateTime toDate, string? userId)
        {
            var endDate = toDate.AddDays(1).AddTicks(-1);
            var dayCount = (toDate - fromDate).Days + 1;

            IQueryable<Sale> chartQuery = _context.Sales
                .Where(s => s.SaleDate >= fromDate && s.SaleDate <= endDate && !s.IsVoided);

            if (IsCashier && !IsManager)
                chartQuery = chartQuery.Where(s => s.UserId == userId);

            var labels = new List<string>();
            var salesData = new List<decimal>();
            var profitData = new List<decimal>();

            if (CurrentPeriod == "Today")
            {
                // گەر فلتەرەکە (ئەمڕۆ) بێت، چارتەکە بەپێی کاتژمێرەکان دابەش دەبێت
                var hourlyData = await chartQuery
                    .GroupBy(s => s.SaleDate.Hour)
                    .Select(g => new
                    {
                        Hour = g.Key,
                        Sales = g.Sum(s => s.GrandTotal),
                        Profit = g.Sum(s => s.TotalProfit)
                    })
                    .ToListAsync();

                // دیاریکردنی کاتژمێری یەکەمین و دواهەمین فرۆشتن
                int minHour = hourlyData.Any() ? hourlyData.Min(d => d.Hour) : DateTime.Now.Hour;
                int maxHour = hourlyData.Any() ? hourlyData.Max(d => d.Hour) : DateTime.Now.Hour;

                for (int i = minHour; i <= maxHour; i++)
                {
                    var hData = hourlyData.FirstOrDefault(d => d.Hour == i);
                    labels.Add($"{i:D2}:00");
                    salesData.Add(hData?.Sales ?? 0);
                    profitData.Add(hData?.Profit ?? 0);
                }
            }
            else
            {
                // ئەگەر فلتەرێکی تر بێت، چارتەکە بەپێی ڕۆژەکان دەردەکەوێت
                var chartData = await chartQuery
                    .GroupBy(s => s.SaleDate.Date)
                    .Select(g => new
                    {
                        Date = g.Key,
                        Sales = g.Sum(s => s.GrandTotal),
                        Profit = g.Sum(s => s.TotalProfit)
                    })
                    .ToListAsync();

                if (dayCount > 31)
                {
                    // ئەگەر زیاتر لە مانگێک بێت بەپێی مانگ دەبێت
                    var monthlyData = chartData
                        .GroupBy(x => new { x.Date.Year, x.Date.Month })
                        .Select(g => new
                        {
                            Year = g.Key.Year,
                            Month = g.Key.Month,
                            Sales = g.Sum(x => x.Sales),
                            Profit = g.Sum(x => x.Profit)
                        })
                        .OrderBy(x => x.Year).ThenBy(x => x.Month)
                        .ToList();

                    foreach (var m in monthlyData)
                    {
                        labels.Add($"{m.Year}/{m.Month:D2}");
                        salesData.Add(m.Sales);
                        profitData.Add(m.Profit);
                    }
                }
                else
                {
                    for (int i = 0; i < dayCount; i++)
                    {
                        var date = fromDate.AddDays(i);
                        var dayData = chartData.FirstOrDefault(d => d.Date == date);
                        labels.Add(date.ToString("MM/dd"));
                        salesData.Add(dayData?.Sales ?? 0);
                        profitData.Add(dayData?.Profit ?? 0);
                    }
                }
            }

            ChartLabelsJson = System.Text.Json.JsonSerializer.Serialize(labels);
            ChartSalesJson = System.Text.Json.JsonSerializer.Serialize(salesData);
            ChartProfitJson = System.Text.Json.JsonSerializer.Serialize(profitData);
        }
    }

    public class RecentSaleViewModel
    {
        public int SaleId { get; set; }
        public DateTime SaleDate { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public decimal GrandTotal { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal RemainingDebt { get; set; }
        public PaymentType PaymentType { get; set; }
        public decimal TotalProfit { get; set; }
    }

    public class RecentPurchaseViewModel
    {
        public int PurchaseId { get; set; }
        public DateTime PurchaseDate { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
    }

    public class ProductAlertViewModel
    {
        public int ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int CurrentStock { get; set; }
        public string Barcode { get; set; } = string.Empty;
    }

    public class TopDebtorViewModel
    {
        public int CustomerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public decimal Balance { get; set; }
    }
}