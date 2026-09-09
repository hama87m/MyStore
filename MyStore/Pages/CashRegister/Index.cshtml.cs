using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;
using MyStore.Helpers;
using System.Globalization;

namespace MyStore.Pages.CashRegister
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

        // ئامارەکان
        public decimal OpeningBalance { get; set; }
        public decimal TotalIn { get; set; }
        public decimal TotalOut { get; set; }
        public decimal CurrentBalance { get; set; }

        // وێنەی بەکارهێنەر
        public string? CurrentUserProfileImagePath { get; set; }

        // خشتە
        public List<CashTransaction> Transactions { get; set; } = new();

        // فلتەر
        [BindProperty(SupportsGet = true)] public string? FilterDate { get; set; }
        [BindProperty(SupportsGet = true)] public string? FilterType { get; set; }

        // بەرواری ئەکتیڤ بۆ UI (هەمیشە yyyy-MM-dd)
        public string ActiveFilterDate { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");

        public async Task OnGetAsync()
        {
            // وێنەی بەکارهێنەر
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                CurrentUserProfileImagePath = await _context.UserSettings
                    .Where(s => s.UserId == user.Id)
                    .Select(s => s.ProfileImagePath)
                    .FirstOrDefaultAsync();
            }

            // پارسکردنی بەروار بە شێوازی گشتی (Invariant + Current culture)
            var filterDate = DateTime.Now.Date;
            if (!string.IsNullOrEmpty(FilterDate))
            {
                if (DateTime.TryParse(FilterDate, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var fd1))
                    filterDate = fd1.Date;
                else if (DateTime.TryParseExact(FilterDate, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var fd2))
                    filterDate = fd2.Date;
                else if (DateTime.TryParse(FilterDate, out var fd3))
                    filterDate = fd3.Date;
            }

            // بەرواری ئەکتیڤ — هەمیشە بە yyyy-MM-dd بۆ input type=date
            ActiveFilterDate = filterDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // ئامار
            OpeningBalance = await CashHelper.GetTodayOpeningAsync(_context);
            CurrentBalance = await CashHelper.GetCurrentBalanceAsync(_context);

            var todayTx = await _context.CashTransactions
                .Where(t => t.CreatedAt.Date == filterDate && t.FlowType != CashFlowType.OpeningBalance)
                .ToListAsync();

            TotalIn = todayTx.Where(t => t.IsIncome).Sum(t => t.Amount);
            TotalOut = todayTx.Where(t => !t.IsIncome).Sum(t => t.Amount);

            // خشتە
            var query = _context.CashTransactions
                .Where(t => t.CreatedAt.Date == filterDate)
                .AsQueryable();

            if (!string.IsNullOrEmpty(FilterType))
            {
                if (Enum.TryParse<CashFlowType>(FilterType, out var ft))
                    query = query.Where(t => t.FlowType == ft);
            }

            Transactions = await query
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .Take(100)
                .ToListAsync();
        }

        // ═══ پارەی هاتوو بە دەستی ═══
        public async Task<IActionResult> OnPostManualInAsync(decimal amount, string? category, string? description)
        {
            if (amount <= 0) return new JsonResult(new { success = false, message = "بڕ پێویستە" });

            if (string.IsNullOrWhiteSpace(category)) category = "دیکە";
            // فۆرماتی Description: [جۆر] وردەکاری
            string finalDesc = string.IsNullOrWhiteSpace(description)
                ? $"[{category}]"
                : $"[{category}] {description.Trim()}";

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userName = User.Identity?.Name;

            await CashHelper.ManualIn(_context, amount, finalDesc, userId, userName);
            await AuditHelper.LogAsync(_context, User, HttpContext, "Cash", "ManualIn", $"پارەی هاتوو: {amount:N0} — {finalDesc}");

            return new JsonResult(new { success = true });
        }

        // ═══ پارەی دەرچوو بە دەستی ═══
        public async Task<IActionResult> OnPostManualOutAsync(decimal amount, string? category, string? description)
        {
            if (amount <= 0) return new JsonResult(new { success = false, message = "بڕ پێویستە" });

            var currentBalance = await CashHelper.GetCurrentBalanceAsync(_context);
            if (amount > currentBalance)
                return new JsonResult(new { success = false, message = $"پارەی قاسە بەسی نییە ({currentBalance:N0})" });

            if (string.IsNullOrWhiteSpace(category)) category = "دیکە";
            string finalDesc = string.IsNullOrWhiteSpace(description)
                ? $"[{category}]"
                : $"[{category}] {description.Trim()}";

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var userName = User.Identity?.Name;

            await CashHelper.ManualOut(_context, amount, finalDesc, userId, userName);
            await AuditHelper.LogAsync(_context, User, HttpContext, "Cash", "ManualOut", $"پارەی دەرچوو: {amount:N0} — {finalDesc}");

            return new JsonResult(new { success = true });
        }

        // ═══ AJAX — نوێکردنەوەی ئامار ═══
        public async Task<IActionResult> OnGetStatsAsync()
        {
            var today = DateTime.Now.Date;
            var opening = await CashHelper.GetTodayOpeningAsync(_context);
            var current = await CashHelper.GetCurrentBalanceAsync(_context);

            var todayTx = await _context.CashTransactions
                .Where(t => t.CreatedAt.Date == today && t.FlowType != CashFlowType.OpeningBalance)
                .ToListAsync();

            return new JsonResult(new
            {
                success = true,
                opening,
                totalIn = todayTx.Where(t => t.IsIncome).Sum(t => t.Amount),
                totalOut = todayTx.Where(t => !t.IsIncome).Sum(t => t.Amount),
                current
            });
        }
    }
}