using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;
using MyStore.Helpers;

namespace MyStore.Pages.Customers
{
    public class DebtViewModel
    {
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public decimal TotalDebt { get; set; }
        public decimal Balance { get; set; }
        public decimal TotalPaid { get; set; }
        public int UnpaidInvoices { get; set; }
    }

    public class DebtsModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public DebtsModel(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public IList<DebtViewModel> DebtList { get; set; } = new List<DebtViewModel>();
        public IList<DebtViewModel> AllCustomersList { get; set; } = new List<DebtViewModel>();

        public decimal TotalStoreDebt { get; set; }
        public int TotalDebtors { get; set; }
        public decimal TotalStoreCredit { get; set; }
        public int TotalCreditors { get; set; }

        public string? CurrentUserProfileImagePath { get; set; }

        [BindProperty(SupportsGet = true)] public string? SearchCustomer { get; set; }
        [BindProperty(SupportsGet = true)] public bool ShowAll { get; set; }

        [BindProperty] public PaymentInputModel PaymentInput { get; set; } = new();

        public async Task OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                CurrentUserProfileImagePath = await _context.UserSettings
                    .Where(s => s.UserId == user.Id)
                    .Select(s => s.ProfileImagePath)
                    .FirstOrDefaultAsync();
            }

            var customersQuery = _context.Customers
                .Where(c => c.Name != "ڕاستەوخۆ");

            var customers = await customersQuery.OrderBy(c => c.Name).ToListAsync();

            AllCustomersList = customers.Select(c => new DebtViewModel
            {
                CustomerId = c.CustomerId,
                CustomerName = c.Name,
                Balance = c.Balance
            }).ToList();

            var allDebts = customers.Select(c =>
            {
                if (!ShowAll && c.Balance == 0) return null;

                return new DebtViewModel
                {
                    CustomerId = c.CustomerId,
                    CustomerName = c.Name,
                    Phone = c.Phone ?? "-",
                    TotalDebt = c.Balance > 0 ? c.Balance : 0,
                    Balance = c.Balance,
                    TotalPaid = 0,
                    UnpaidInvoices = 0
                };
            })
            .Where(d => d != null)
            .OrderByDescending(d => d!.Balance)
            .ToList()!;

            var activeDebtors = customers.Where(c => c.Balance > 0).ToList();
            var activeCreditors = customers.Where(c => c.Balance < 0).ToList();

            TotalDebtors = activeDebtors.Count;
            TotalCreditors = activeCreditors.Count;
            TotalStoreDebt = activeDebtors.Sum(c => c.Balance);
            TotalStoreCredit = activeCreditors.Sum(c => Math.Abs(c.Balance));

            DebtList = allDebts!;

            if (!string.IsNullOrEmpty(SearchCustomer))
            {
                DebtList = DebtList
                    .Where(d => d.CustomerName.Contains(SearchCustomer, StringComparison.OrdinalIgnoreCase) ||
                                d.Phone.Contains(SearchCustomer))
                    .ToList();
            }
        }

        public async Task<IActionResult> OnGetCustomerInfoAsync(int customerId)
        {
            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null)
                return new JsonResult(new { success = false });

            return new JsonResult(new
            {
                success = true,
                customerName = customer.Name,
                phone = customer.Phone,
                balance = customer.Balance
            });
        }

        public async Task<IActionResult> OnGetPaymentHistoryAsync(int customerId, DateTime? fromDate, DateTime? toDate)
        {
            var regularPaymentsQuery = _context.CustomerPayments
                .Include(p => p.Customer)
                .Where(p => !p.IsVoided);

            var salePaymentsQuery = _context.Sales
                .Include(s => s.Customer)
                .Where(s => !s.IsVoided &&
                           ((s.PaymentType == PaymentType.Credit && s.PaidAmount > 0) || s.PaymentType == PaymentType.Cash));

            if (customerId > 0)
            {
                regularPaymentsQuery = regularPaymentsQuery.Where(p => p.CustomerId == customerId);
                salePaymentsQuery = salePaymentsQuery.Where(s => s.CustomerId == customerId);
            }

            if (fromDate.HasValue)
            {
                var start = fromDate.Value.Date;
                regularPaymentsQuery = regularPaymentsQuery.Where(p => p.PaymentDate >= start);
                salePaymentsQuery = salePaymentsQuery.Where(s => s.SaleDate >= start);
            }
            if (toDate.HasValue)
            {
                var end = toDate.Value.Date.AddDays(1).AddTicks(-1);
                regularPaymentsQuery = regularPaymentsQuery.Where(p => p.PaymentDate <= end);
                salePaymentsQuery = salePaymentsQuery.Where(s => s.SaleDate <= end);
            }

            var regularPayments = await regularPaymentsQuery
                .Select(p => new
                {
                    Id = p.PaymentId,
                    Date = p.PaymentDate,
                    Amount = p.Amount,
                    Note = p.Note,
                    IsFromSale = false,
                    CustomerName = p.Customer!.Name,
                    CustomerId = p.CustomerId
                })
                .ToListAsync();

            var salePayments = await salePaymentsQuery
                .Select(s => new
                {
                    Id = s.SaleId,
                    Date = s.SaleDate,
                    Amount = s.PaymentType == PaymentType.Cash ? s.GrandTotal : s.PaidAmount,
                    Note = $"واصڵ لە پسوڵەی #{s.SaleId}",
                    IsFromSale = true,
                    CustomerName = s.Customer!.Name,
                    CustomerId = s.CustomerId
                })
                .ToListAsync();

            var allPayments = regularPayments.Concat(salePayments)
                .OrderByDescending(p => p.Date)
                .Take(100)
                .Select(p => new
                {
                    paymentId = p.Id,
                    paymentDate = p.Date,
                    amount = p.Amount,
                    note = p.Note,
                    isSale = p.IsFromSale,
                    customerName = p.CustomerName,
                    customerId = p.CustomerId
                })
                .ToList();

            string title = customerId > 0
                ? (await _context.Customers.FindAsync(customerId))?.Name ?? "نەناسراو"
                : "هەموو کڕیارەکان";

            decimal currentBalance = 0;
            if (customerId > 0)
            {
                var cust = await _context.Customers.FindAsync(customerId);
                if (cust != null) currentBalance = cust.Balance;
            }

            return new JsonResult(new
            {
                success = true,
                customerName = title,
                balance = currentBalance,
                payments = allPayments,
                totalAmount = allPayments.Sum(p => p.amount)
            });
        }

        // بەشی هێنانی پسوڵەی وەرگرتنی پارە (بە باشترکراوی بۆ هێنانی باڵانس و لۆگۆ)
        public async Task<IActionResult> OnGetPaymentReceiptAsync(int paymentId, bool isSale = false)
        {
            var settings = await _context.SystemSettings.ToListAsync();
            var storeInfo = new
            {
                name = settings.FirstOrDefault(s => s.Key == "StoreName")?.Value ?? "فرۆشگا",
                phone = settings.FirstOrDefault(s => s.Key == "StorePhone")?.Value ?? "",
                address = settings.FirstOrDefault(s => s.Key == "StoreAddress")?.Value ?? "",
                logo = settings.FirstOrDefault(s => s.Key == "StoreLogo")?.Value ?? "" // لێرەدا لۆگۆکە زیاد کرا
            };

            if (!isSale)
            {
                // ئەگەر پارەدانێکی ئاسایی بوو
                var payment = await _context.CustomerPayments.Include(p => p.Customer)
                    .FirstOrDefaultAsync(p => p.PaymentId == paymentId && !p.IsVoided);
                if (payment == null) return new JsonResult(new { success = false });

                // هێنانی باڵانس لە Ledger بۆ دڵنیابوون لە مێژووەکە
                var ledger = await _context.CustomerLedgers
                    .FirstOrDefaultAsync(l => l.ReferenceId == paymentId &&
                                             (l.TransactionType == LedgerTransactionType.Payment || l.TransactionType == LedgerTransactionType.Refund) &&
                                             !l.IsDeleted);

                decimal prevBal = ledger != null ? ledger.PreviousBalance : payment.Customer?.Balance + payment.Amount ?? 0;
                decimal newBal = ledger != null ? ledger.RunningBalance : payment.Customer?.Balance ?? 0;

                return new JsonResult(new
                {
                    success = true,
                    receipt = new
                    {
                        paymentId = payment.PaymentId,
                        date = payment.PaymentDate.ToString("yyyy-MM-dd"),
                        time = payment.PaymentDate.ToString("HH:mm"),
                        customerName = payment.Customer?.Name ?? "-",
                        amount = payment.Amount,
                        prevBalance = prevBal,
                        newBalance = newBal
                    },
                    store = storeInfo
                });
            }
            else
            {
                // ئەگەر واصڵکردن بوو لەسەر کاتی فرۆشتنی پسوڵەیەک
                var sale = await _context.Sales.Include(s => s.Customer)
                    .FirstOrDefaultAsync(s => s.SaleId == paymentId && !s.IsVoided);
                if (sale == null) return new JsonResult(new { success = false });

                decimal amount = sale.PaymentType == PaymentType.Cash ? sale.GrandTotal : sale.PaidAmount;

                var ledger = await _context.CustomerLedgers
                    .Where(l => l.ReferenceId == paymentId && !l.IsDeleted &&
                               (l.TransactionType == LedgerTransactionType.Sale || l.TransactionType == LedgerTransactionType.Payment))
                    .OrderByDescending(l => l.LedgerId)
                    .FirstOrDefaultAsync();

                decimal newBal = ledger != null ? ledger.RunningBalance : sale.Customer?.Balance ?? 0;
                decimal prevBal = newBal + amount;

                return new JsonResult(new
                {
                    success = true,
                    receipt = new
                    {
                        paymentId = sale.SaleId,
                        date = sale.SaleDate.ToString("yyyy-MM-dd"),
                        time = sale.SaleDate.ToString("HH:mm"),
                        customerName = sale.Customer?.Name ?? "-",
                        amount = amount,
                        prevBalance = prevBal,
                        newBalance = newBal
                    },
                    store = storeInfo
                });
            }
        }

        public async Task<IActionResult> OnPostPaymentAsync()
        {
            if (PaymentInput.CustomerId == 0 || PaymentInput.Amount <= 0)
                return new JsonResult(new { success = false, message = "تکایە زانیاری پارەدان پڕ بکەرەوە" });

            var customer = await _context.Customers.FindAsync(PaymentInput.CustomerId);
            if (customer == null) return new JsonResult(new { success = false, message = "کڕیار نەدۆزرایەوە" });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var userId = _userManager.GetUserId(User);
                DateTime paymentDate = PaymentInput.PaymentDate == DateTime.MinValue ? DateTime.Now : new DateTime(PaymentInput.PaymentDate.Year, PaymentInput.PaymentDate.Month, PaymentInput.PaymentDate.Day, PaymentInput.PaymentDate.Hour, PaymentInput.PaymentDate.Minute, DateTime.Now.Second, DateTime.Now.Millisecond);

                var payment = new CustomerPayments
                {
                    CustomerId = PaymentInput.CustomerId,
                    Amount = PaymentInput.Amount,
                    PaymentDate = paymentDate,
                    TransactionType = PaymentTransactionType.Received,
                    UsedAmount = 0,
                    Note = "واصڵکراو بە کاش"
                };
                _context.CustomerPayments.Add(payment);

                decimal previousBalance = customer.Balance;
                customer.Balance -= PaymentInput.Amount;

                await _context.SaveChangesAsync();

                var ledgerEntry = new CustomerLedger
                {
                    CustomerId = customer.CustomerId,
                    TransactionDate = paymentDate,
                    TransactionType = LedgerTransactionType.Payment,
                    ReferenceId = payment.PaymentId,
                    DebitAmount = 0,
                    CreditAmount = PaymentInput.Amount,
                    PreviousBalance = previousBalance,
                    RunningBalance = customer.Balance,
                    Description = "واصڵکراو بە کاش",
                    CreatedByUserId = userId,
                    IsDeleted = false
                };
                _context.CustomerLedgers.Add(ledgerEntry);

                await _context.SaveChangesAsync();

                try { await AuditHelper.PaymentReceived(_context, User, HttpContext, payment.PaymentId, PaymentInput.CustomerId, PaymentInput.Amount); } catch { }

                // قاسە — تەنها ئەگەر پارەدانەکە هی ئەمڕۆیە
                if (CashHelper.IsToday(paymentDate))
                {
                    try { await CashHelper.DebtReceived(_context, payment.PaymentId, PaymentInput.Amount, customer.Name, userId, User.Identity?.Name); } catch { }
                }

                await transaction.CommitAsync();
                return new JsonResult(new { success = true, paymentId = payment.PaymentId });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new JsonResult(new { success = false, message = "هەڵە: " + ex.Message });
            }
        }

        public async Task<IActionResult> OnPostRefundAdvanceAsync(int customerId, decimal amount)
        {
            if (customerId == 0 || amount <= 0)
                return new JsonResult(new { success = false, message = "تکایە زانیاری تەواو داخڵ بکە" });

            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null) return new JsonResult(new { success = false, message = "کڕیار نەدۆزرایەوە" });

            if (customer.Balance >= 0)
                return new JsonResult(new { success = false, message = "ئەم کڕیارە هیچ پێشینەیەکی لای ئێمە نییە بۆ ئەوەی پێی بدەینەوە!" });

            decimal maxRefund = Math.Abs(customer.Balance);
            if (amount > maxRefund)
                return new JsonResult(new { success = false, message = $"ناتوانیت لە {maxRefund:N0} د.ع زیاتری پێ بدەیتەوە" });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var userId = _userManager.GetUserId(User);
                DateTime transDate = DateTime.Now;

                decimal previousBalance = customer.Balance;
                customer.Balance += amount;

                var payment = new CustomerPayments
                {
                    CustomerId = customerId,
                    Amount = amount,
                    PaymentDate = transDate,
                    TransactionType = PaymentTransactionType.Refund,
                    UsedAmount = 0,
                    Note = "پێدانەوەی پێشینە بە کاش"
                };
                _context.CustomerPayments.Add(payment);
                await _context.SaveChangesAsync();

                var ledgerEntry = new CustomerLedger
                {
                    CustomerId = customerId,
                    TransactionDate = transDate,
                    TransactionType = LedgerTransactionType.Refund,
                    ReferenceId = payment.PaymentId,
                    DebitAmount = amount,
                    CreditAmount = 0,
                    PreviousBalance = previousBalance,
                    RunningBalance = customer.Balance,
                    Description = "پێدانەوەی پێشینە بە کاش (سافکردنەوە)",
                    CreatedByUserId = userId,
                    IsDeleted = false
                };
                _context.CustomerLedgers.Add(ledgerEntry);
                await _context.SaveChangesAsync();

                try { await CashHelper.AdvanceRefund(_context, customerId, amount, customer.Name, userId, User.Identity?.Name); } catch { }

                await transaction.CommitAsync();
                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new JsonResult(new { success = false, message = "هەڵە: " + ex.Message });
            }
        }

        public async Task<IActionResult> OnPostUndoPaymentAsync(int paymentId)
        {
            var payment = await _context.CustomerPayments.Include(p => p.Customer).FirstOrDefaultAsync(p => p.PaymentId == paymentId && !p.IsVoided);
            if (payment == null) return new JsonResult(new { success = false, message = "نەدۆزرایەوە یان پێشتر پووچەڵکراوەتەوە" });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var userId = _userManager.GetUserId(User);
                payment.IsVoided = true;

                decimal prevBalance = payment.Customer!.Balance;
                payment.Customer.Balance += payment.Amount;

                var ledgerEntry = new CustomerLedger
                {
                    CustomerId = payment.CustomerId,
                    TransactionDate = DateTime.Now,
                    TransactionType = LedgerTransactionType.Refund,
                    ReferenceId = paymentId,
                    DebitAmount = payment.Amount,
                    CreditAmount = 0,
                    PreviousBalance = prevBalance,
                    RunningBalance = payment.Customer.Balance,
                    Description = $"گەڕاندنەوەی واصڵ #{paymentId}",
                    CreatedByUserId = userId,
                    IsDeleted = false
                };

                _context.CustomerLedgers.Add(ledgerEntry);
                await _context.SaveChangesAsync();

                // قاسە — گەڕاندنەوەی واصڵ ئەگەر پارەدانەکە هی ئەمڕۆیە
                if (CashHelper.IsToday(payment.PaymentDate))
                {
                    try
                    {
                        await CashHelper.RefundDebtPayment(_context, paymentId, payment.Amount,
                            payment.Customer?.Name ?? "", userId, User.Identity?.Name);
                    }
                    catch { }
                }

                try { await AuditHelper.PaymentUndone(_context, User, HttpContext, paymentId, payment.CustomerId, payment.Amount); } catch { }

                await transaction.CommitAsync();
                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new JsonResult(new { success = false, message = "هەڵە: " + ex.Message });
            }
        }
    }

    public class PaymentInputModel
    {
        public int CustomerId { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; } = DateTime.Now;
    }
}