using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;

namespace MyStore.Pages.Sales
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IPermissionService _permissionService;

        public IndexModel(
            ApplicationDbContext context,
            UserManager<IdentityUser> userManager,
            IPermissionService permissionService)
        {
            _context = context;
            _userManager = userManager;
            _permissionService = permissionService;
        }

        // ─── پڕۆپەرتییەکان ──────────────────────────────────────
        public IList<SaleListViewModel> SalesList { get; set; } = new List<SaleListViewModel>();
        public SelectList CustomerList { get; set; } = default!;

        /// <summary>لیستی فرۆشیاران — تەنها بۆ Admin/Manager</summary>
        public List<SalespersonItem> SalespersonList { get; set; } = new();

        public string UserRole { get; private set; } = string.Empty;
        public bool IsManager => UserRole == "Admin" || UserRole == "Manager";
        public string? CurrentUserProfileImagePath { get; private set; }

        // ── فلتەرەکان ──
        [BindProperty(SupportsGet = true)] public string? FilterDateRange { get; set; } = "Today";
        [BindProperty(SupportsGet = true)] public DateTime? StartDate { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? EndDate { get; set; }
        [BindProperty(SupportsGet = true)] public string? CustomStart { get; set; }
        [BindProperty(SupportsGet = true)] public string? CustomEnd { get; set; }
        [BindProperty(SupportsGet = true)] public int? FilterCustomerId { get; set; }
        [BindProperty(SupportsGet = true)] public string? FilterPaymentType { get; set; }
        [BindProperty(SupportsGet = true)] public string? SearchInvoice { get; set; }
        [BindProperty(SupportsGet = true)] public string? FilterUserId { get; set; }

        // ── Pagination ──
        [BindProperty(SupportsGet = true)] public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 900;
        public int TotalPages { get; set; }
        public int TotalFilteredCount { get; set; }

        // ── ئامارەکان (لەسەر تەواوی داتای فلتەرکراو، پێش Pagination) ──
        public decimal TotalSales { get; set; }
        public decimal TotalReceived { get; set; }
        public decimal TotalDebt { get; set; }
        public decimal TotalProfit { get; set; }
        public int InvoiceCount { get; set; }


        private async Task LoadCurrentUserProfileImageAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return;

            CurrentUserProfileImagePath = await _context.UserSettings
                .Where(s => s.UserId == user.Id)
                .Select(s => s.ProfileImagePath)
                .FirstOrDefaultAsync();
        }

        // ═══════════════════════════════════════════════════════
        //  OnGetAsync
        // ═══════════════════════════════════════════════════════
        public async Task OnGetAsync()
        {
            UserRole = await _permissionService.GetUserRoleAsync(User);
            await LoadCurrentUserProfileImageAsync();
            await BuildListsAsync();
            ProcessDateFilters();

            // ئامارەکان لەسەر تەواوی داتای فلتەرکراو حیساب دەکرێت
            var allFiltered = await FetchFilteredQueryAsync();
            CalcStats(allFiltered);

            // پەڕەبەندی
            TotalFilteredCount = allFiltered.Count;
            TotalPages = (int)Math.Ceiling(TotalFilteredCount / (double)PageSize);
            if (CurrentPage < 1) CurrentPage = 1;
            if (CurrentPage > TotalPages && TotalPages > 0) CurrentPage = TotalPages;

            // تەنها داتای پەڕەی مەبەست
            SalesList = allFiltered
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();
        }

        // ═══════════════════════════════════════════════════════
        //  OnGetFilterSalesAsync — AJAX (هەر کاتێک فلتەر گۆڕدرا)
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetFilterSalesAsync()
        {
            UserRole = await _permissionService.GetUserRoleAsync(User);
            await LoadCurrentUserProfileImageAsync();
            ProcessDateFilters();

            var allFiltered = await FetchFilteredQueryAsync();
            CalcStats(allFiltered);

            TotalFilteredCount = allFiltered.Count;
            TotalPages = (int)Math.Ceiling(TotalFilteredCount / (double)PageSize);
            if (CurrentPage < 1) CurrentPage = 1;
            if (CurrentPage > TotalPages && TotalPages > 0) CurrentPage = TotalPages;

            var paged = allFiltered
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            return new JsonResult(new
            {
                count = InvoiceCount,
                totalSales = TotalSales,
                totalReceived = TotalReceived,
                totalDebt = TotalDebt,
                totalProfit = TotalProfit,
                currentPage = CurrentPage,
                totalPages = TotalPages,
                totalCount = TotalFilteredCount,
                sales = paged.Select(s => new
                {
                    saleId = s.SaleId,
                    dateShort = s.SaleDate.ToString("MM/dd"),
                    timeShort = s.SaleDate.ToString("HH:mm"),
                    customerName = s.CustomerName,
                    salespersonName = s.CashierName,
                    salespersonRole = s.CashierRole,
                    itemCount = s.ItemCount,
                    grandTotal = s.GrandTotal,
                    paidAmount = s.PaidAmount,
                    remainingDebt = s.RemainingDebt,
                    totalProfit = s.TotalProfit,
                    isCash = s.PaymentType == PaymentType.Cash
                })
            });
        }

        // ═══════════════════════════════════════════════════════
        //  OnGetInvoiceDataAsync — چاپکردن
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetInvoiceDataAsync(int id)
        {
            var role = await _permissionService.GetUserRoleAsync(User);
            var currentUserId = _userManager.GetUserId(User);
            var isManagerRole = role == "Admin" || role == "Manager";

            var query = _context.Sales
                .Include(s => s.Customer)
                .Include(s => s.SaleItems!).ThenInclude(si => si.Product)
                .Where(s => s.SaleId == id && !s.IsVoided);

            if (!isManagerRole)
                query = query.Where(s => s.UserId == currentUserId);

            var sale = await query.FirstOrDefaultAsync();
            if (sale == null)
                return new JsonResult(new { success = false, message = "پسوڵەکە نەدۆزرایەوە یان دەستت پێنایەوە" });

            var settings = await _context.SystemSettings.ToListAsync();
            var storeName = settings.FirstOrDefault(s => s.Key == "StoreName")?.Value ?? "فرۆشگا";
            var storePhone = settings.FirstOrDefault(s => s.Key == "StorePhone")?.Value ?? "";
            var storeAddr = settings.FirstOrDefault(s => s.Key == "StoreAddress")?.Value ?? "";
            var storeLogo = settings.FirstOrDefault(s => s.Key == "StoreLogo")?.Value ?? "";

            return new JsonResult(new
            {
                success = true,
                invoice = new
                {
                    invoiceId = sale.SaleId,
                    date = sale.SaleDate.ToString("yyyy-MM-dd"),
                    time = sale.SaleDate.ToString("HH:mm"),
                    customerName = sale.Customer?.Name ?? "نەناسراو",
                    totalAmount = sale.TotalAmount,
                    discount = sale.Discount,
                    grandTotal = sale.GrandTotal,
                    paidAmount = sale.PaidAmount,
                    debt = sale.GrandTotal - sale.PaidAmount,
                    items = sale.SaleItems?.Select(si => new
                    {
                        name = si.Product?.Name ?? "-",
                        quantity = si.Quantity,
                        unitPrice = si.UnitPrice,
                        subtotal = si.Quantity * si.UnitPrice
                    }).ToList()
                },
                store = new { name = storeName, phone = storePhone, address = storeAddr, logo = storeLogo }
            });
        }

        // ═══════════════════════════════════════════════════════
        //  Private Helpers
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// کوێری فلتەرکراو ئەنجام دەدات و تەواوی لیست برەوە دەگەڕێنێتەوە.
        /// ئامارەکان لەسەر ئەم لیستەوە حیساب دەکرێن — پێش Pagination.
        /// </summary>
        private async Task<List<SaleListViewModel>> FetchFilteredQueryAsync()
        {
            var currentUserId = _userManager.GetUserId(User);

            var query = _context.Sales
                .Include(s => s.Customer)
                .Include(s => s.SaleItems!).ThenInclude(si => si.Product)
                .Where(s => !s.IsVoided)
                .AsQueryable();

            // کاشێر تەنها پسوڵەکانی خۆی
            if (!IsManager)
                query = query.Where(s => s.UserId == currentUserId);

            // فلتەری بەروار
            if (StartDate.HasValue)
                query = query.Where(s => s.SaleDate >= StartDate.Value);
            if (EndDate.HasValue)
                query = query.Where(s => s.SaleDate <= EndDate.Value);

            // فلتەری کڕیار
            if (FilterCustomerId.HasValue)
                query = query.Where(s => s.CustomerId == FilterCustomerId.Value);

            // فلتەری پارەدان
            if (!string.IsNullOrEmpty(FilterPaymentType))
            {
                query = FilterPaymentType switch
                {
                    "Cash" => query.Where(s => s.PaymentType == PaymentType.Cash),
                    "Credit" => query.Where(s => s.PaymentType == PaymentType.Credit),
                    "Unpaid" => query.Where(s => s.PaymentType == PaymentType.Credit && s.PaidAmount < s.GrandTotal),
                    _ => query
                };
            }

            // فلتەری ژمارەی پسوڵە
            if (!string.IsNullOrEmpty(SearchInvoice) && int.TryParse(SearchInvoice, out int invoiceId))
                query = query.Where(s => s.SaleId == invoiceId);

            // فلتەری فرۆشیار
            if (IsManager && !string.IsNullOrEmpty(FilterUserId))
                query = query.Where(s => s.UserId == FilterUserId);

            var sales = await query
                .OrderByDescending(s => s.SaleDate)
                .ThenByDescending(s => s.SaleId)
                .ToListAsync();

            // ناو و ڕۆڵی فرۆشیارەکان
            var cashierInfo = new Dictionary<string, (string Name, string Role)>();
            if (IsManager)
            {
                var userIds = sales
                    .Where(s => s.UserId != null)
                    .Select(s => s.UserId!)
                    .Distinct().ToList();

                foreach (var uid in userIds)
                {
                    var u = await _userManager.FindByIdAsync(uid);
                    if (u == null) continue;
                    var roles = await _userManager.GetRolesAsync(u);
                    var r = roles.Contains("Admin") ? "Admin"
                              : roles.Contains("Manager") ? "Manager"
                              : "Cashier";
                    cashierInfo[uid] = (u.UserName ?? u.Email ?? "—", r);
                }
            }

            return sales.Select(s =>
            {
                var info = IsManager && s.UserId != null && cashierInfo.TryGetValue(s.UserId, out var ci)
                    ? ci : (Name: "—", Role: "Cashier");

                return new SaleListViewModel
                {
                    SaleId = s.SaleId,
                    SaleDate = s.SaleDate,
                    CustomerName = s.Customer?.Name ?? "-",
                    CustomerId = s.CustomerId,
                    GrandTotal = s.GrandTotal,
                    PaidAmount = s.PaidAmount,
                    RemainingDebt = s.GrandTotal - s.PaidAmount,
                    PaymentType = s.PaymentType,
                    TotalProfit = s.TotalProfit,
                    Discount = s.Discount,
                    ItemCount = s.SaleItems?.Count ?? 0,
                    CashierName = IsManager ? info.Name : null,
                    CashierRole = IsManager ? info.Role : null
                };
            }).ToList();
        }

        private async Task BuildListsAsync()
        {
            if (IsManager)
            {
                var all = await _context.Customers.OrderBy(c => c.Name).ToListAsync();
                CustomerList = new SelectList(all, "CustomerId", "Name");
            }
            else
            {
                var uid = _userManager.GetUserId(User);
                var cids = await _context.Sales
                    .Where(s => s.UserId == uid && !s.IsVoided)
                    .Select(s => s.CustomerId).Distinct().ToListAsync();
                var mine = await _context.Customers
                    .Where(c => cids.Contains(c.CustomerId)).OrderBy(c => c.Name).ToListAsync();
                CustomerList = new SelectList(mine, "CustomerId", "Name");
            }

            if (IsManager)
            {
                var allUsers = await _userManager.Users.OrderBy(u => u.UserName).ToListAsync();
                foreach (var u in allUsers)
                {
                    var roles = await _userManager.GetRolesAsync(u);
                    var role = roles.Contains("Admin") ? "Admin"
                              : roles.Contains("Manager") ? "Manager"
                              : "Cashier";
                    SalespersonList.Add(new SalespersonItem
                    {
                        UserId = u.Id,
                        UserName = u.UserName ?? u.Email ?? u.Id,
                        Role = role
                    });
                }
            }
        }

        private void ProcessDateFilters()
        {
            if (FilterDateRange == "Custom")
            {
                StartDate = DateTime.TryParse(CustomStart, out var cs)
                    ? cs.Date : (DateTime?)null;
                EndDate = DateTime.TryParse(CustomEnd, out var ce)
                    ? ce.Date.AddDays(1).AddTicks(-1) : (DateTime?)null;
                return;
            }

            var now = DateTime.Now;
            switch (FilterDateRange)
            {
                case "Today":
                    StartDate = now.Date;
                    EndDate = now.Date.AddDays(1).AddTicks(-1);
                    break;
                case "Yesterday":
                    StartDate = now.Date.AddDays(-1);
                    EndDate = now.Date.AddTicks(-1);
                    break;
                case "Week":
                    // دۆزینەوەی سەرەتای هەفتە (شەممە)
                    int diff = (7 + (now.DayOfWeek - DayOfWeek.Saturday)) % 7;
                    StartDate = now.Date.AddDays(-1 * diff);
                    EndDate = now.Date.AddDays(1).AddTicks(-1);
                    break;
                case "Month":
                    // لە 1ی مانگەوە تا کۆتایی ئەمڕۆ
                    StartDate = new DateTime(now.Year, now.Month, 1);
                    EndDate = now.Date.AddDays(1).AddTicks(-1);
                    break;
                default:
                    StartDate = null;
                    EndDate = null;
                    break;
            }
        }

        /// <summary>ئامارەکان لەسەر تەواوی لیستی فلتەرکراو حیساب دەکات — پێش Skip/Take</summary>
        private void CalcStats(List<SaleListViewModel> allFiltered)
        {
            TotalSales = allFiltered.Sum(s => s.GrandTotal);
            TotalReceived = allFiltered.Sum(s => s.PaidAmount);
            TotalDebt = TotalSales - TotalReceived;
            TotalProfit = allFiltered.Sum(s => s.TotalProfit);
            InvoiceCount = allFiltered.Count;
        }
    }

    // ── ViewModels ──────────────────────────────────────────────
    public class SaleListViewModel
    {
        public int SaleId { get; set; }
        public DateTime SaleDate { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public int CustomerId { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal RemainingDebt { get; set; }
        public PaymentType PaymentType { get; set; }
        public decimal TotalProfit { get; set; }
        public decimal Discount { get; set; }
        public int ItemCount { get; set; }
        public string? CashierName { get; set; }
        public string? CashierRole { get; set; }
    }

    public class SalespersonItem
    {
        public string UserId { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Role { get; set; } = "Cashier";
    }
}