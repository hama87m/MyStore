using MyStore.Data;
using MyStore.Models;
using Microsoft.EntityFrameworkCore;

namespace MyStore.Helpers
{
    public static class CashHelper
    {
        /// <summary>باڵانسی ئێستای قاسە</summary>
        public static async Task<decimal> GetCurrentBalanceAsync(ApplicationDbContext context)
        {
            var last = await context.CashTransactions
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .FirstOrDefaultAsync();
            return last?.BalanceAfter ?? 0;
        }

        /// <summary>پارەی دەستپێکی ئەمڕۆ</summary>
        public static async Task<decimal> GetTodayOpeningAsync(ApplicationDbContext context)
        {
            var today = DateTime.Now.Date;
            var opening = await context.CashTransactions
                .Where(t => t.CreatedAt.Date == today && t.FlowType == CashFlowType.OpeningBalance)
                .FirstOrDefaultAsync();
            if (opening != null) return opening.Amount;

            var lastYesterday = await context.CashTransactions
                .Where(t => t.CreatedAt.Date < today)
                .OrderByDescending(t => t.CreatedAt)
                .ThenByDescending(t => t.Id)
                .FirstOrDefaultAsync();
            return lastYesterday?.BalanceAfter ?? 0;
        }

        /// <summary>پشکنینی ئایا بەرواری مامەڵە هی ئەمڕۆیە</summary>
        public static bool IsToday(DateTime date) => date.Date == DateTime.Now.Date;

        /// <summary>تۆمارکردنی جوڵەی قاسە</summary>
        public static async Task<CashTransaction> RecordAsync(
            ApplicationDbContext context,
            CashFlowType flowType,
            decimal amount,
            bool isIncome,
            string description,
            string? userId = null,
            string? userName = null,
            int? referenceId = null,
            string? referenceType = null)
        {
            var currentBalance = await GetCurrentBalanceAsync(context);
            var newBalance = isIncome ? currentBalance + amount : currentBalance - amount;

            var tx = new CashTransaction
            {
                FlowType = flowType,
                Amount = amount,
                IsIncome = isIncome,
                Description = description,
                ReferenceId = referenceId,
                ReferenceType = referenceType,
                CreatedAt = DateTime.Now,
                UserId = userId,
                UserName = userName ?? "System",
                BalanceAfter = newBalance
            };

            context.CashTransactions.Add(tx);
            await context.SaveChangesAsync();
            return tx;
        }

        // ═══════════════════════════════════════════════════════
        // مامەڵە ئۆتۆماتیکەکانی فرۆشتن
        // ═══════════════════════════════════════════════════════

        /// <summary>فرۆشتنی نەقد (هاتوو)</summary>
        public static Task<CashTransaction> CashSale(ApplicationDbContext ctx, int saleId, decimal amount, string? userId, string? userName)
            => RecordAsync(ctx, CashFlowType.CashSale, amount, true, $"فرۆشتنی نەقد #{saleId}", userId, userName, saleId, "Sale");

        /// <summary>واصڵی سەر پسوڵەی قەرز — پێشەکی (هاتوو)</summary>
        public static Task<CashTransaction> InlineSalePayment(ApplicationDbContext ctx, int saleId, decimal amount, string? userId, string? userName)
            => RecordAsync(ctx, CashFlowType.InlineSalePayment, amount, true, $"واصڵی سەر پسوڵەی #{saleId}", userId, userName, saleId, "Sale");

        /// <summary>وەرگرتنی قەرز لە دەفتەری قەرز (هاتوو)</summary>
        public static Task<CashTransaction> DebtReceived(ApplicationDbContext ctx, int paymentId, decimal amount, string customerName, string? userId, string? userName)
            => RecordAsync(ctx, CashFlowType.DebtReceived, amount, true, $"واصڵکردنی قەرز — {customerName}", userId, userName, paymentId, "Payment");

        /// <summary>کڕینی کاڵا بە نەقد (دەرچوو)</summary>
        public static Task<CashTransaction> PurchaseOut(ApplicationDbContext ctx, int purchaseId, decimal amount, string? userId, string? userName)
            => RecordAsync(ctx, CashFlowType.PurchaseOut, amount, false, $"کڕینی کاڵا #{purchaseId}", userId, userName, purchaseId, "Purchase");

        // ═══════════════════════════════════════════════════════
        // مامەڵە ئۆتۆماتیکی سڕینەوە و ڕاستکردنەوە
        // ═══════════════════════════════════════════════════════

        /// <summary>سڕینەوەی پسوڵەی نەقد (دەرچوو)</summary>
        public static Task<CashTransaction> VoidCashSale(ApplicationDbContext ctx, int saleId, decimal amount, string? userId, string? userName)
            => RecordAsync(ctx, CashFlowType.VoidCashSale, amount, false, $"سڕینەوەی پسوڵەی نەقد #{saleId}", userId, userName, saleId, "Sale");

        /// <summary>سڕینەوەی واصڵی سەر پسوڵە (دەرچوو)</summary>
        public static Task<CashTransaction> VoidInlinePayment(ApplicationDbContext ctx, int saleId, decimal amount, string? userId, string? userName)
            => RecordAsync(ctx, CashFlowType.VoidInlinePayment, amount, false, $"سڕینەوەی واصڵی سەر پسوڵەی #{saleId}", userId, userName, saleId, "Sale");

        /// <summary>گەڕاندنەوەی واصڵی قەرز (دەرچوو)</summary>
        public static Task<CashTransaction> RefundDebtPayment(ApplicationDbContext ctx, int paymentId, decimal amount, string customerName, string? userId, string? userName)
            => RecordAsync(ctx, CashFlowType.RefundDebtPayment, amount, false, $"گەڕاندنەوەی واصڵی قەرز — {customerName}", userId, userName, paymentId, "Payment");

        /// <summary>دەستکاری پسوڵە — جیاوازی بڕی واصڵ</summary>
        public static Task<CashTransaction> SaleEditAdjustment(ApplicationDbContext ctx, int saleId, decimal diffAmount, bool isIncome, string? userId, string? userName)
            => RecordAsync(ctx,
                isIncome ? CashFlowType.SaleEditIncrease : CashFlowType.SaleEditDecrease,
                Math.Abs(diffAmount), isIncome,
                $"دەستکاری پسوڵەی #{saleId} — {(isIncome ? "زیادبوونی" : "کەمبوونی")} واصڵ",
                userId, userName, saleId, "Sale");

        /// <summary>پێدانەوەی پێشینە بۆ کڕیار (دەرچوو)</summary>
        public static Task<CashTransaction> AdvanceRefund(ApplicationDbContext ctx, int customerId, decimal amount, string customerName, string? userId, string? userName)
            => RecordAsync(ctx, CashFlowType.AdvanceRefund, amount, false, $"پێدانەوەی پێشینە — {customerName}", userId, userName, customerId, "Customer");

        // ═══════════════════════════════════════════════════════
        // مامەڵە دەستییەکان (لە پەڕەی قاسە)
        // ═══════════════════════════════════════════════════════

        /// <summary>پارەی هاتوو بە دەستی</summary>
        public static Task<CashTransaction> ManualIn(ApplicationDbContext ctx, decimal amount, string description, string? userId, string? userName)
            => RecordAsync(ctx, CashFlowType.ManualIn, amount, true, description, userId, userName);

        /// <summary>پارەی دەرچوو بە دەستی</summary>
        public static Task<CashTransaction> ManualOut(ApplicationDbContext ctx, decimal amount, string description, string? userId, string? userName)
            => RecordAsync(ctx, CashFlowType.ManualOut, amount, false, description, userId, userName);

        /// <summary>پارەی دەستپێکی ڕۆژ</summary>
        public static Task<CashTransaction> SetOpening(ApplicationDbContext ctx, decimal amount, string? userId, string? userName)
            => RecordAsync(ctx, CashFlowType.OpeningBalance, amount, true, $"پارەی دەستپێکی ڕۆژ: {amount:N0}", userId, userName);
    }
}