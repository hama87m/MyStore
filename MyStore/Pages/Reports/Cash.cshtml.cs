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
using System.Text.RegularExpressions;

namespace MyStore.Pages.Reports
{
    // لێرەدا دەسەڵاتمان داوە بە هەرسێکیان بۆ ئەوەی بتوانن بچنە ناو پەڕەکەوە
    [Authorize(Roles = "Admin,Manager,Cashier")]
    public class CashFlowModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CashFlowModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public void OnGet()
        {
        }

        // هەر ٣ ڕۆڵەکە دەتوانن ئەمە ببینن (بۆ لۆگۆ و ناوی دوکان)
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

        private static string ExtractCategory(string? description)
        {
            if (string.IsNullOrEmpty(description)) return "نادیار";
            var m = Regex.Match(description, @"^\[([^\]]+)\]");
            return m.Success ? m.Groups[1].Value : "نادیار";
        }

        // ═══════════════════════════════════════════════════════
        // ١. تابی ئامارەکان (تەنها بۆ ئەدمین)
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetDashboardAsync(DateTime? from, DateTime? to)
        {
            // ئەگەر ئەدمین نەبوو، ڕێگە نەدات
            if (!User.IsInRole("Admin"))
            {
                return new JsonResult(new { error = "Unauthorized" }) { StatusCode = 403 };
            }

            try
            {
                var fromDate = from ?? DateTime.Today.AddDays(-30);
                var toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                var currentBalance = await _context.CashTransactions!
                    .OrderByDescending(c => c.CreatedAt)
                    .Select(c => c.BalanceAfter)
                    .FirstOrDefaultAsync();

                var periodTransactions = await _context.CashTransactions!
                    .Where(c => c.CreatedAt >= fromDate && c.CreatedAt <= endDate
                             && c.FlowType != CashFlowType.OpeningBalance)
                    .AsNoTracking()
                    .ToListAsync();

                decimal periodIn = periodTransactions.Where(c => c.IsIncome).Sum(c => c.Amount);
                decimal periodOut = periodTransactions.Where(c => !c.IsIncome).Sum(c => c.Amount);

                var flowBreakdown = periodTransactions
                    .GroupBy(c => c.FlowType)
                    .Select(g => new {
                        Type = g.Key.ToString(),
                        Total = g.Sum(c => c.Amount),
                        IsIncome = g.First().IsIncome
                    })
                    .ToList();

                return new JsonResult(new
                {
                    currentBalance,
                    periodIn,
                    periodOut,
                    flowBreakdown
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════
        // ٢. تابی کەشفی قاسە (تەنها بۆ ئەدمین)
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetLedgerAsync(DateTime? from, DateTime? to, string? type)
        {
            if (!User.IsInRole("Admin"))
            {
                return new JsonResult(new { error = "Unauthorized" }) { StatusCode = 403 };
            }

            try
            {
                var fromDate = from ?? DateTime.Today.AddDays(-30);
                var toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                var query = _context.CashTransactions!
                    .Where(c => c.CreatedAt >= fromDate && c.CreatedAt <= endDate)
                    .AsNoTracking();

                if (!string.IsNullOrEmpty(type) && Enum.TryParse<CashFlowType>(type, out var flowType))
                {
                    query = query.Where(c => c.FlowType == flowType);
                }

                var ledger = await query
                    .OrderByDescending(c => c.CreatedAt)
                    .Select(c => new {
                        c.Id,
                        Date = c.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        Type = c.FlowType.ToString(),
                        c.Description,
                        c.Amount,
                        c.IsIncome,
                        c.BalanceAfter,
                        UserName = c.UserName ?? "نەزانراو"
                    })
                    .ToListAsync();

                return new JsonResult(ledger);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════
        // ٣. تابی خەرجییەکان (تەنها بۆ ئەدمین)
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetExpensesAsync(DateTime? from, DateTime? to, string? category)
        {
            if (!User.IsInRole("Admin"))
            {
                return new JsonResult(new { error = "Unauthorized" }) { StatusCode = 403 };
            }

            try
            {
                var fromDate = from ?? DateTime.Today.AddDays(-30);
                var toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                var allExpenses = await _context.CashTransactions!
                    .Where(c => c.CreatedAt >= fromDate && c.CreatedAt <= endDate
                             && c.FlowType == CashFlowType.ManualOut)
                    .OrderByDescending(c => c.CreatedAt)
                    .Select(c => new {
                        Date = c.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                        c.Description,
                        c.Amount,
                        UserName = c.UserName ?? "نەزانراو"
                    })
                    .AsNoTracking()
                    .ToListAsync();

                var withCategory = allExpenses.Select(e => new {
                    e.Date,
                    Category = ExtractCategory(e.Description),
                    Description = e.Description,
                    CleanDescription = Regex.Replace(e.Description ?? "", @"^\[[^\]]+\]\s*", "").Trim(),
                    e.Amount,
                    e.UserName
                }).ToList();

                var filtered = string.IsNullOrEmpty(category)
                    ? withCategory
                    : withCategory.Where(e => e.Category == category).ToList();

                var breakdown = withCategory
                    .GroupBy(e => e.Category)
                    .Select(g => new {
                        Category = g.Key,
                        Total = g.Sum(e => e.Amount),
                        Count = g.Count()
                    })
                    .OrderByDescending(b => b.Total)
                    .ToList();

                return new JsonResult(new
                {
                    total = filtered.Sum(e => e.Amount),
                    totalAll = allExpenses.Sum(e => e.Amount),
                    list = filtered,
                    breakdown
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════
        // ٤. تابی داخستنی ڕۆژانە (کراوەیە بۆ Admin, Manager, Cashier)
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetZReportAsync(DateTime date)
        {
            try
            {
                var startDate = date.Date;
                var endDate = date.Date.AddDays(1).AddTicks(-1);

                var openingBalance = await _context.CashTransactions!
                    .Where(c => c.CreatedAt < startDate)
                    .OrderByDescending(c => c.CreatedAt)
                    .Select(c => c.BalanceAfter)
                    .FirstOrDefaultAsync();

                var dayTrans = await _context.CashTransactions!
                    .Where(c => c.CreatedAt >= startDate && c.CreatedAt <= endDate)
                    .AsNoTracking()
                    .ToListAsync();

                decimal cashSales = dayTrans.Where(c => c.FlowType == CashFlowType.CashSale).Sum(c => c.Amount);
                decimal inlinePayments = dayTrans.Where(c => c.FlowType == CashFlowType.InlineSalePayment).Sum(c => c.Amount);
                decimal debtReceived = dayTrans.Where(c => c.FlowType == CashFlowType.DebtReceived).Sum(c => c.Amount);
                decimal saleEditIncrease = dayTrans.Where(c => c.FlowType == CashFlowType.SaleEditIncrease).Sum(c => c.Amount);
                decimal manualIn = dayTrans.Where(c => c.FlowType == CashFlowType.ManualIn).Sum(c => c.Amount);

                decimal purchasesOut = dayTrans.Where(c => c.FlowType == CashFlowType.PurchaseOut).Sum(c => c.Amount);
                decimal voidCashSale = dayTrans.Where(c => c.FlowType == CashFlowType.VoidCashSale).Sum(c => c.Amount);
                decimal voidInlinePayment = dayTrans.Where(c => c.FlowType == CashFlowType.VoidInlinePayment).Sum(c => c.Amount);
                decimal refundDebtPayment = dayTrans.Where(c => c.FlowType == CashFlowType.RefundDebtPayment).Sum(c => c.Amount);
                decimal saleEditDecrease = dayTrans.Where(c => c.FlowType == CashFlowType.SaleEditDecrease).Sum(c => c.Amount);
                decimal advanceRefund = dayTrans.Where(c => c.FlowType == CashFlowType.AdvanceRefund).Sum(c => c.Amount);
                decimal manualOut = dayTrans.Where(c => c.FlowType == CashFlowType.ManualOut).Sum(c => c.Amount);

                var manualOutByCategory = dayTrans
                    .Where(c => c.FlowType == CashFlowType.ManualOut)
                    .GroupBy(c => ExtractCategory(c.Description))
                    .Select(g => new {
                        Category = g.Key,
                        Total = g.Sum(c => c.Amount)
                    })
                    .OrderByDescending(x => x.Total)
                    .ToList();

                decimal totalIn = cashSales + inlinePayments + debtReceived + saleEditIncrease + manualIn;
                decimal totalOut = purchasesOut + voidCashSale + voidInlinePayment + refundDebtPayment
                                 + saleEditDecrease + advanceRefund + manualOut;

                decimal expectedClosingBalance = openingBalance + totalIn - totalOut;

                var actualLastBalance = dayTrans.OrderByDescending(c => c.CreatedAt)
                    .Select(c => c.BalanceAfter).FirstOrDefault();
                if (!dayTrans.Any()) actualLastBalance = openingBalance;

                return new JsonResult(new
                {
                    date = date.ToString("yyyy-MM-dd"),
                    openingBalance,
                    cashSales,
                    inlinePayments,
                    debtReceived,
                    saleEditIncrease,
                    manualIn,
                    purchasesOut,
                    voidCashSale,
                    voidInlinePayment,
                    refundDebtPayment,
                    saleEditDecrease,
                    advanceRefund,
                    manualOut,
                    manualOutByCategory,
                    totalIn,
                    totalOut,
                    expectedClosingBalance,
                    actualLastBalance,
                    transactionCount = dayTrans.Count
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }
    }
}