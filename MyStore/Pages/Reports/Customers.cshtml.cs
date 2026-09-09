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
    [Authorize]
    public class CustomersReportModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CustomersReportModel(ApplicationDbContext context)
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

        public async Task<IActionResult> OnGetCustomersDropdownAsync()
        {
            var customers = await _context.Customers
                .Select(c => new { c.CustomerId, c.Name, c.Phone, c.Balance })
                .OrderBy(c => c.Name)
                .AsNoTracking()
                .ToListAsync();
            return new JsonResult(customers);
        }

        public async Task<IActionResult> OnGetDashboardDataAsync(DateTime? from, DateTime? to, string saleType = "all")
        {
            try
            {
                var fromDate = from ?? new DateTime(2000, 1, 1);
                var toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                int totalCustomers = await _context.Customers.CountAsync();
                int debtorsCount = await _context.Customers.CountAsync(c => c.Balance > 0);
                decimal totalDebt = await _context.Customers.Where(c => c.Balance > 0).SumAsync(c => c.Balance);

                decimal totalAdvance = await _context.Customers.Where(c => c.Balance < 0).SumAsync(c => c.Balance);
                totalAdvance = Math.Abs(totalAdvance);

                int debtInvoicesCount = await _context.Sales
                    .Where(s => !s.IsVoided && s.PaymentType == PaymentType.Credit
                             && s.PaidAmount < s.GrandTotal
                             && s.SaleDate >= fromDate && s.SaleDate <= endDate)
                    .CountAsync();

                var periodSales = await _context.Sales
                    .Where(s => !s.IsVoided && s.SaleDate >= fromDate && s.SaleDate <= endDate)
                    .Select(s => new { s.CustomerId, s.Customer!.Name, s.GrandTotal, s.PaidAmount, s.PaymentType })
                    .ToListAsync();

                var buyersStats = periodSales
                    .GroupBy(s => new { s.CustomerId, s.Name })
                    .Select(g => new {
                        Name = g.Key.Name,
                        Total = g.Sum(s => s.GrandTotal),
                        CashInvoicesTotal = g.Sum(s => s.PaymentType == PaymentType.Cash ? s.GrandTotal : 0),
                        DebtInvoicesTotal = g.Sum(s => s.PaymentType == PaymentType.Credit ? s.GrandTotal : 0)
                    })
                    .ToList();

                var topBuyersTotal = buyersStats.Where(x => x.Total > 0).OrderByDescending(x => x.Total).Select(x => new { Name = x.Name, Total = x.Total }).Take(5).ToList();
                var topBuyersCash = buyersStats.Where(x => x.CashInvoicesTotal > 0).OrderByDescending(x => x.CashInvoicesTotal).Select(x => new { Name = x.Name, Total = x.CashInvoicesTotal }).Take(5).ToList();
                var topBuyersDebt = buyersStats.Where(x => x.DebtInvoicesTotal > 0).OrderByDescending(x => x.DebtInvoicesTotal).Select(x => new { Name = x.Name, Total = x.DebtInvoicesTotal }).Take(5).ToList();

                var currentDebtors = await _context.Customers
                    .Where(c => c.Balance > 0)
                    .Select(c => new { c.Name, Total = c.Balance })
                    .OrderByDescending(c => c.Total)
                    .ToListAsync();

                var top5Debtors = currentDebtors.Take(5).ToList();
                var otherDebtorsTotal = currentDebtors.Skip(5).Sum(x => x.Total);

                var recentDebtPaymentsAllTime = await _context.CustomerPayments!
                    .Where(p => !p.IsVoided && p.TransactionType == PaymentTransactionType.Received)
                    .OrderByDescending(p => p.PaymentDate)
                    .Take(10)
                    .Select(p => new {
                        date = p.PaymentDate,
                        name = p.Customer!.Name,
                        amount = p.Amount,
                        note = p.Note ?? "واصڵی دەفتەری قەرز"
                    }).ToListAsync();

                var recentInlinePaymentsAllTime = await _context.Sales!
                    .Where(s => !s.IsVoided && s.PaymentType == PaymentType.Credit && s.PaidAmount > 0)
                    .OrderByDescending(s => s.SaleDate)
                    .Take(10)
                    .Select(s => new {
                        date = s.SaleDate,
                        name = s.Customer!.Name,
                        amount = s.PaidAmount,
                        note = $"واصڵی سەر پسوڵەی #{s.SaleId}"
                    }).ToListAsync();

                var recentPayments = recentDebtPaymentsAllTime.Concat(recentInlinePaymentsAllTime)
                    .OrderByDescending(p => p.date)
                    .Take(5)
                    .Select(p => new {
                        date = p.date.ToString("yyyy-MM-dd"),
                        name = p.name,
                        amount = p.amount,
                        note = p.note
                    }).ToList();

                var salesQueryAllTime = _context.Sales!
                    .Where(s => !s.IsVoided);

                if (saleType == "debt")
                {
                    salesQueryAllTime = salesQueryAllTime.Where(s => s.PaymentType == PaymentType.Credit);
                }
                else if (saleType == "cash")
                {
                    salesQueryAllTime = salesQueryAllTime.Where(s => s.PaymentType == PaymentType.Cash);
                }

                var recentSales = await salesQueryAllTime
                    .OrderByDescending(s => s.SaleDate)
                    .Take(5)
                    .Select(s => new {
                        date = s.SaleDate.ToString("yyyy-MM-dd"),
                        name = s.Customer!.Name,
                        total = s.GrandTotal,
                        debt = s.GrandTotal - s.PaidAmount
                    }).ToListAsync();

                return new JsonResult(new
                {
                    cards = new { totalCustomers, debtorsCount, debtInvoicesCount, totalDebt, totalAdvance },
                    buyers = new { total = topBuyersTotal, cash = topBuyersCash, debt = topBuyersDebt },
                    debtors = new { top = top5Debtors, othersTotal = otherDebtorsTotal },
                    quickTables = new { recentPayments, recentSales }
                });
            }
            catch (Exception ex) { return new JsonResult(new { error = ex.Message }); }
        }

        public async Task<IActionResult> OnGetCustomersListAsync()
        {
            try
            {
                var customers = await _context.Customers
                    .OrderByDescending(c => c.Balance)
                    .Select(c => new { c.CustomerId, c.Name, c.Phone, c.Address, c.Balance })
                    .AsNoTracking().ToListAsync();

                return new JsonResult(customers);
            }
            catch (Exception ex) { return new JsonResult(new { error = ex.Message }); }
        }

        public async Task<IActionResult> OnGetCustomerLedgerAsync(int customerId, string filterType, DateTime? from, DateTime? to)
        {
            if (customerId <= 0) return new JsonResult(new { error = "تکایە کڕیار هەڵبژێرە" });
            try
            {
                var customer = await _context.Customers.FindAsync(customerId);
                if (customer == null) return new JsonResult(new { error = "کڕیارەکە نەدۆزرایەوە" });

                DateTime fromDate;
                DateTime toDate = DateTime.Today;

                if (filterType != "sinceDebt" && filterType != "allTime")
                {
                    fromDate = from ?? new DateTime(2000, 1, 1);
                    toDate = to ?? DateTime.Today;
                }
                else if (filterType == "allTime")
                {
                    fromDate = new DateTime(2000, 1, 1);
                }
                else // sinceDebt
                {
                    var lastZeroBalanceTx = await _context.CustomerLedgers!
                        .Where(l => l.CustomerId == customerId && !l.IsDeleted && l.RunningBalance <= 0)
                        .OrderByDescending(l => l.TransactionDate)
                        .FirstOrDefaultAsync();

                    fromDate = lastZeroBalanceTx != null ? lastZeroBalanceTx.TransactionDate : new DateTime(2000, 1, 1);
                }

                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                // دۆزینەوەی باڵانسی سەرەتا (پێش بەرواری دەستپێکردنی ڕیپۆرتەکە)
                var lastLedgerBeforeFromDate = await _context.CustomerLedgers!
                    .Where(l => l.CustomerId == customerId && !l.IsDeleted && l.TransactionDate < fromDate)
                    .OrderByDescending(l => l.TransactionDate)
                    .FirstOrDefaultAsync();

                decimal openingBalance = lastLedgerBeforeFromDate?.RunningBalance ?? 0;

                var ledger = await _context.CustomerLedgers!
                    .Where(l => l.CustomerId == customerId && !l.IsDeleted && l.TransactionDate >= fromDate && l.TransactionDate <= endDate)
                    .OrderBy(l => l.TransactionDate)
                    .Select(l => new {
                        Date = l.TransactionDate.ToString("yyyy-MM-dd HH:mm"),
                        Type = (int)l.TransactionType,
                        l.Description,
                        l.PreviousBalance,
                        l.DebitAmount,
                        l.CreditAmount,
                        l.RunningBalance,
                        l.ReferenceId
                    })
                    .AsNoTracking()
                    .ToListAsync();

                return new JsonResult(new
                {
                    customerName = customer.Name,
                    customerPhone = customer.Phone,
                    currentBalance = customer.Balance,
                    fromDate = fromDate.ToString("yyyy-MM-dd"),
                    toDate = toDate.ToString("yyyy-MM-dd"),
                    openingBalance = openingBalance,
                    ledger
                });
            }
            catch (Exception ex) { return new JsonResult(new { error = ex.Message }); }
        }

        public async Task<IActionResult> OnGetCustomerHistoryAsync(int customerId, DateTime? from, DateTime? to)
        {
            if (customerId <= 0) return new JsonResult(new { error = "تکایە کڕیار هەڵبژێرە" });
            try
            {
                var customer = await _context.Customers.FindAsync(customerId);
                decimal currentBalance = customer?.Balance ?? 0;

                DateTime fromDate = from ?? new DateTime(2000, 1, 1);
                DateTime toDate = to ?? DateTime.Today;
                var endDate = toDate.Date.AddDays(1).AddTicks(-1);

                var salesQuery = await _context.Sales!
                    .Where(s => s.CustomerId == customerId && !s.IsVoided
                             && s.PaymentType == PaymentType.Credit
                             && s.SaleDate >= fromDate && s.SaleDate <= endDate)
                    .OrderByDescending(s => s.SaleDate)
                    .ToListAsync();

                var sales = salesQuery.Select(s => new {
                    s.SaleId,
                    Date = s.SaleDate.ToString("yyyy-MM-dd HH:mm"),
                    s.GrandTotal,
                    s.PaidAmount,
                    Remaining = s.GrandTotal - s.PaidAmount
                }).ToList();

                var debtPaymentsQuery = await _context.CustomerPayments!
                    .Where(p => p.CustomerId == customerId && !p.IsVoided
                             && p.PaymentDate >= fromDate && p.PaymentDate <= endDate)
                    .OrderByDescending(p => p.PaymentDate)
                    .ToListAsync();

                var inlinePayments = salesQuery
                    .Where(s => s.PaidAmount > 0)
                    .Select(s => new {
                        Date = s.SaleDate.ToString("yyyy-MM-dd HH:mm"),
                        PaymentId = s.SaleId,
                        Amount = s.PaidAmount,
                        Note = $"واصڵی سەر پسوڵەی #{s.SaleId}",
                        Type = 0,
                        IsFromSale = true
                    }).ToList();

                var debtPaymentsList = debtPaymentsQuery.Select(p => new {
                    Date = p.PaymentDate.ToString("yyyy-MM-dd HH:mm"),
                    PaymentId = p.PaymentId,
                    Amount = p.Amount,
                    Note = p.Note,
                    Type = (int)p.TransactionType,
                    IsFromSale = false
                }).ToList();

                var payments = debtPaymentsList.Concat(inlinePayments)
                    .OrderByDescending(p => p.Date)
                    .ToList();

                decimal totalSales = salesQuery.Sum(s => s.GrandTotal);

                decimal totalInlinePayments = salesQuery.Sum(s => s.PaidAmount);
                decimal totalDebtReceived = debtPaymentsQuery.Where(p => p.TransactionType == PaymentTransactionType.Received).Sum(p => p.Amount);
                decimal totalDebtRefunded = debtPaymentsQuery.Where(p => p.TransactionType == PaymentTransactionType.Refund).Sum(p => p.Amount);

                decimal totalPayments = totalInlinePayments + totalDebtReceived - totalDebtRefunded;

                decimal periodDifference = totalSales - totalPayments;

                return new JsonResult(new
                {
                    sales,
                    payments,
                    summary = new
                    {
                        totalSales,
                        totalPayments,
                        periodDifference,
                        currentBalance
                    }
                });
            }
            catch (Exception ex) { return new JsonResult(new { error = ex.Message }); }
        }
    }
}