using MyStore.Data;
using MyStore.Models;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace MyStore.Helpers
{
    public static class AuditHelper
    {
        /// <summary>
        /// تۆمارکردنی کردار لە AuditLog
        /// </summary>
        public static async Task LogAsync(
            ApplicationDbContext context,
            ClaimsPrincipal? user,
            HttpContext? httpContext,
            string category,
            string action,
            string description,
            int? entityId = null,
            string? entityType = null)
        {
            var userId = user?.FindFirstValue(ClaimTypes.NameIdentifier);
            var userName = user?.Identity?.Name;
            var ip = httpContext?.Connection?.RemoteIpAddress?.ToString();

            var log = new AuditLog
            {
                UserId = userId,
                UserName = userName ?? "System",
                Category = category,
                Action = action,
                Description = description,
                EntityId = entityId,
                EntityType = entityType,
                CreatedAt = DateTime.Now,
                IpAddress = ip
            };

            context.AuditLogs.Add(log);
            await context.SaveChangesAsync();
        }

        // ═══════════════════════════════════════════════════════
        // شۆرتکەتەکان بۆ هەر بەشێک
        // ═══════════════════════════════════════════════════════

        // فرۆشتن
        public static Task SaleCreated(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, int saleId, decimal amount)
            => LogAsync(ctx, user, http, "Sale", "Create", $"فرۆشتنی نوێ #{saleId} — بڕ: {amount:N0}", saleId, "Sale");

        public static Task SaleEdited(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, int saleId, string details)
            => LogAsync(ctx, user, http, "Sale", "Edit", $"دەستکاری پسوڵە #{saleId} — {details}", saleId, "Sale");

        public static Task SaleVoided(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, int saleId, decimal amount)
            => LogAsync(ctx, user, http, "Sale", "Void", $"پووچەڵکردنەوەی پسوڵە #{saleId} — بڕ: {amount:N0}", saleId, "Sale");

        // کڕین
        public static Task PurchaseCreated(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, int purchaseId, decimal amount)
            => LogAsync(ctx, user, http, "Purchase", "Create", $"کڕینی نوێ #{purchaseId} — بڕ: {amount:N0}", purchaseId, "Purchase");

        public static Task PurchaseEdited(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, int purchaseId, string details)
            => LogAsync(ctx, user, http, "Purchase", "Edit", $"دەستکاری کڕین #{purchaseId} — {details}", purchaseId, "Purchase");

        public static Task PurchaseDeleted(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, int purchaseId, decimal amount)
            => LogAsync(ctx, user, http, "Purchase", "Delete", $"سڕینەوەی کڕین #{purchaseId} — بڕ: {amount:N0}", purchaseId, "Purchase");

        // پارەدان / قەرز
        public static Task PaymentReceived(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, int paymentId, int customerId, decimal amount)
            => LogAsync(ctx, user, http, "Payment", "Receive", $"واصڵکردنی #{paymentId} لە کڕیار #{customerId} — بڕ: {amount:N0}", paymentId, "Payment");

        public static Task PaymentUndone(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, int paymentId, int customerId, decimal amount)
            => LogAsync(ctx, user, http, "Payment", "Undo", $"گەڕاندنەوەی واصڵ #{paymentId} لە کڕیار #{customerId} — بڕ: {amount:N0}", paymentId, "Payment");

        // بەکارهێنەر
        public static Task UserCreated(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, string targetUser, string role)
            => LogAsync(ctx, user, http, "User", "Create", $"دروستکردنی بەکارهێنەر: {targetUser} بە ڕۆڵی {role}");

        public static Task UserEdited(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, string targetUser, string details)
            => LogAsync(ctx, user, http, "User", "Edit", $"دەستکاری بەکارهێنەر: {targetUser} — {details}");

        public static Task UserDeleted(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, string targetUser)
            => LogAsync(ctx, user, http, "User", "Delete", $"سڕینەوەی بەکارهێنەر: {targetUser}");

        public static Task UserLocked(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, string targetUser, bool locked)
            => LogAsync(ctx, user, http, "User", locked ? "Lock" : "Unlock", $"{(locked ? "قفڵکردن" : "کردنەوە")}ی بەکارهێنەر: {targetUser}");

        // ڕێکخستنەکان
        public static Task SettingsChanged(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, string details)
            => LogAsync(ctx, user, http, "Settings", "Change", details);

        // باکئەپ
        public static Task BackupCreated(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, string fileName)
            => LogAsync(ctx, user, http, "Backup", "Create", $"باکئەپ: {fileName}");

        public static Task BackupRestored(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, string fileName)
            => LogAsync(ctx, user, http, "Backup", "Restore", $"ڕیستۆر: {fileName}");

        public static Task BackupDeleted(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, string fileName)
            => LogAsync(ctx, user, http, "Backup", "Delete", $"سڕینەوەی باکئەپ: {fileName}");

        // لۆگین
        public static Task UserLoggedIn(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, string userName)
            => LogAsync(ctx, user, http, "Auth", "Login", $"چوونەژوورەوە: {userName}");

        public static Task UserLoggedOut(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, string userName)
            => LogAsync(ctx, user, http, "Auth", "Logout", $"چوونەدەرەوە: {userName}");

        // تۆمارەکان (کاڵا، کڕیار، دابینکەر)
        public static Task ProductAdded(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, int productId, string name)
            => LogAsync(ctx, user, http, "Product", "Create", $"زیادکردنی کاڵا: {name}", productId, "Product");

        public static Task ProductEdited(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, int productId, string name)
            => LogAsync(ctx, user, http, "Product", "Edit", $"دەستکاری کاڵا: {name}", productId, "Product");

        public static Task ProductArchived(ApplicationDbContext ctx, ClaimsPrincipal? user, HttpContext? http, int productId, string name, bool archived)
            => LogAsync(ctx, user, http, "Product", archived ? "Archive" : "Restore", $"{(archived ? "ئەرشیفکردن" : "گەڕاندنەوە")}ی کاڵا: {name}", productId, "Product");
    }
}