using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;

namespace MyStore.Helpers
{
    public static class LedgerHelper
    {
        // ═══════════════════════════════════════════════════════
        // خوێندنەوەی باڵانس
        // ═══════════════════════════════════════════════════════

        public static async Task<decimal> GetBalance(ApplicationDbContext context, int customerId)
        {
            var lastLedger = await context.CustomerLedgers
                .Where(l => l.CustomerId == customerId && !l.IsDeleted)
                .OrderByDescending(l => l.LedgerId)
                .FirstOrDefaultAsync();

            return lastLedger?.RunningBalance ?? 0;
        }

        public static async Task<decimal> GetBalanceQuick(ApplicationDbContext context, int customerId)
        {
            var customer = await context.Customers.FindAsync(customerId);
            return customer?.Balance ?? 0;
        }

        // ═══════════════════════════════════════════════════════
        // تۆمارکردن لە Ledger + نوێکردنەوەی باڵانس
        // ═══════════════════════════════════════════════════════

        private static async Task<decimal> RecordLedgerEntry(
            ApplicationDbContext context,
            int customerId,
            DateTime transactionDate,
            LedgerTransactionType transactionType,
            string description,
            decimal debitAmount,
            decimal creditAmount,
            int? referenceId,
            string? referenceType,
            string? userId)
        {
            var lastLedger = await context.CustomerLedgers
                .Where(l => l.CustomerId == customerId && !l.IsDeleted)
                .OrderByDescending(l => l.LedgerId)
                .FirstOrDefaultAsync();

            decimal previousBalance = lastLedger?.RunningBalance ?? 0;
            decimal newBalance = previousBalance + debitAmount - creditAmount;

            var ledger = new CustomerLedger
            {
                CustomerId = customerId,
                TransactionDate = transactionDate,
                TransactionType = transactionType,
                Description = description,
                DebitAmount = debitAmount,
                CreditAmount = creditAmount,
                PreviousBalance = previousBalance,
                RunningBalance = newBalance,
                ReferenceId = referenceId,
                ReferenceType = referenceType,
                CreatedByUserId = userId,
                IsDeleted = false
            };

            context.CustomerLedgers.Add(ledger);

            var customer = await context.Customers.FindAsync(customerId);
            if (customer != null)
            {
                customer.Balance = newBalance;
            }

            await context.SaveChangesAsync();

            return newBalance;
        }

        // ═══════════════════════════════════════════════════════
        // فەنکشنە کورتکراوەکان
        // ═══════════════════════════════════════════════════════

        public static async Task RecordCreditSale(
            ApplicationDbContext context,
            int customerId,
            int saleId,
            decimal amount,
            DateTime saleDate,
            string? userId)
        {
            await RecordLedgerEntry(
                context, customerId, saleDate,
                LedgerTransactionType.Sale,
                $"کڕینی کاڵا بە قەرز (پسوڵەی ژمارە #{saleId})",
                amount, 0, saleId, "Sale", userId);
        }

        public static async Task RecordSalePayment(
            ApplicationDbContext context,
            int customerId,
            int saleId,
            decimal amount,
            DateTime paymentDate,
            string? userId)
        {
            await RecordLedgerEntry(
                context, customerId, paymentDate.AddMilliseconds(100),
                LedgerTransactionType.Payment,
                $"پێدانی بڕێک پارە لەکاتی کڕیندا (سەر پسوڵەی #{saleId})",
                0, amount, saleId, "SalePayment", userId);
        }

        public static async Task RecordPayment(
            ApplicationDbContext context,
            int customerId,
            int paymentId,
            decimal amount,
            DateTime paymentDate,
            string description,
            string? userId)
        {
            await RecordLedgerEntry(
                context, customerId, paymentDate,
                LedgerTransactionType.Payment,
                description,
                0, amount, paymentId, "Payment", userId);
        }

        public static async Task RecordRefund(
            ApplicationDbContext context,
            int customerId,
            int? referenceId,
            decimal amount,
            DateTime refundDate,
            string description,
            string? userId)
        {
            await RecordLedgerEntry(
                context, customerId, refundDate,
                LedgerTransactionType.Refund,
                description,
                amount, 0, referenceId, "Refund", userId);
        }

        public static async Task RecordSaleDeletion(
            ApplicationDbContext context,
            int customerId,
            int saleId,
            decimal remainingBalance,
            DateTime deletionDate,
            string? userId)
        {
            if (remainingBalance > 0)
            {
                // کاتێک پسوڵە دەسڕێتەوە، لای کڕیار وەک گەڕانەوەی کاڵا پیشان دەدرێت
                await RecordLedgerEntry(
                    context, customerId, deletionDate,
                    LedgerTransactionType.Adjustment,
                    $"گەڕانەوەی کاڵا (پووچەڵکردنەوەی پسوڵەی #{saleId} - بڕی گەڕاوە: {remainingBalance:N0})",
                    0, remainingBalance, saleId, "SaleVoid", userId);
            }
        }

        // ═══════════════════════════════════════════════════════
        // فەنکشنی سەرەکی بۆ دەستکاری فاتوورە
        // ═══════════════════════════════════════════════════════

        public static async Task HandleSaleEdit(
            ApplicationDbContext context,
            int oldCustomerId,
            decimal oldGrandTotal,
            decimal oldPaidAmount,
            PaymentType oldPaymentType,
            int newCustomerId,
            decimal newGrandTotal,
            decimal newPaidAmount,
            PaymentType newPaymentType,
            int saleId,
            DateTime editDate,
            string? userId)
        {
            decimal oldDebt = oldPaymentType == PaymentType.Credit ? oldGrandTotal - oldPaidAmount : 0;
            decimal newDebt = newPaymentType == PaymentType.Credit ? newGrandTotal - newPaidAmount : 0;

            // ═══════════════════════════════════════════════════════
            // حاڵەتی ١: کڕیار گۆڕاوە
            // ═══════════════════════════════════════════════════════
            if (oldCustomerId != newCustomerId)
            {
                if (oldDebt > 0)
                {
                    var newCustomerName = await GetCustomerName(context, newCustomerId);
                    await RecordLedgerEntry(
                        context, oldCustomerId, editDate,
                        LedgerTransactionType.Adjustment,
                        $"گواستنەوەی قەرز بۆ سەر '{newCustomerName}' (پسوڵەی #{saleId} - بڕی: {oldDebt:N0})",
                        0, oldDebt, saleId, "CustomerChange", userId);
                }

                if (newDebt > 0)
                {
                    var oldCustomerName = await GetCustomerName(context, oldCustomerId);
                    await RecordLedgerEntry(
                        context, newCustomerId, editDate.AddMilliseconds(50),
                        LedgerTransactionType.Sale,
                        $"هاتنی قەرز لە '{oldCustomerName}'ـەوە (پسوڵەی #{saleId} - بڕی: {newDebt:N0})",
                        newDebt, 0, saleId, "CustomerChange", userId);
                }
                return;
            }

            // ═══════════════════════════════════════════════════════
            // حاڵەتی ٢: کڕیار نەگۆڕاوە، بەڵام جۆری پارەدان گۆڕاوە
            // ═══════════════════════════════════════════════════════
            if (oldPaymentType != newPaymentType)
            {
                if (oldPaymentType == PaymentType.Credit && newPaymentType == PaymentType.Cash)
                {
                    if (oldDebt > 0)
                    {
                        await RecordLedgerEntry(
                            context, oldCustomerId, editDate,
                            LedgerTransactionType.Adjustment,
                            $"لابردنی قەرز (پسوڵەی #{saleId} پارەکەی بە نەقد درا - بڕی: {oldDebt:N0})",
                            0, oldDebt, saleId, "TypeChange", userId);
                    }
                }
                else if (oldPaymentType == PaymentType.Cash && newPaymentType == PaymentType.Credit)
                {
                    if (newDebt > 0)
                    {
                        await RecordLedgerEntry(
                            context, oldCustomerId, editDate,
                            LedgerTransactionType.Sale,
                            $"کڕینی کاڵا (پسوڵەی نەقدی #{saleId} گۆڕا بۆ قەرز - بڕی: {newDebt:N0})",
                            newDebt, 0, saleId, "TypeChange", userId);
                    }
                }
                return;
            }

            // ═══════════════════════════════════════════════════════
            // حاڵەتی ٣: کڕیار و جۆر نەگۆڕاون، بەڵام بڕ یان پارە گۆڕاوە
            // ═══════════════════════════════════════════════════════
            if (oldPaymentType == PaymentType.Credit && newPaymentType == PaymentType.Credit)
            {
                decimal debtDifference = newDebt - oldDebt;

                if (debtDifference > 0)
                {
                    // قەرز زیادبووە (شتێکی تری بردووە)
                    await RecordLedgerEntry(
                        context, oldCustomerId, editDate,
                        LedgerTransactionType.Sale,
                        $"زیادبوونی کڕین (دەستکاریکردنی پسوڵەی #{saleId} - بڕی زیادکراو: {debtDifference:N0})",
                        debtDifference, 0, saleId, "SaleEdit", userId);
                }
                else if (debtDifference < 0)
                {
                    // قەرز کەمبووەتەوە (شتی هێناوەتەوە یان پارەی داوە)
                    await RecordLedgerEntry(
                        context, oldCustomerId, editDate,
                        LedgerTransactionType.Adjustment,
                        $"گەڕانەوەی کاڵا / لێخۆشبوون (دەستکاریکردنی پسوڵەی #{saleId} - بڕی گەڕاوە: {Math.Abs(debtDifference):N0})",
                        0, Math.Abs(debtDifference), saleId, "SaleEdit", userId);
                }
            }
        }

        // ═══════════════════════════════════════════════════════
        // فەنکشنی یاریدەدەر بۆ وەرگرتنی ناوی کڕیار
        // ═══════════════════════════════════════════════════════
        private static async Task<string> GetCustomerName(ApplicationDbContext context, int customerId)
        {
            var customer = await context.Customers.FindAsync(customerId);
            return customer?.Name ?? $"کڕیار #{customerId}";
        }
    }
}