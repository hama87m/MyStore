using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;
using System.Security.Claims;

namespace MyStore.Models
{
    public interface IPermissionService
    {
        Task<decimal> GetMaxDiscountPercentAsync(ClaimsPrincipal user);
        Task<bool> CanApplyDiscountAsync(ClaimsPrincipal user, decimal discountPercent);
        Task<string> GetUserRoleAsync(ClaimsPrincipal user);
    }

    public class PermissionService : IPermissionService
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ApplicationDbContext _context;

        public PermissionService(UserManager<IdentityUser> userManager, ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task<string> GetUserRoleAsync(ClaimsPrincipal user)
        {
            if (user?.Identity?.IsAuthenticated != true) return string.Empty;

            var identityUser = await _userManager.GetUserAsync(user);
            if (identityUser == null) return string.Empty;

            var roles = await _userManager.GetRolesAsync(identityUser);

            if (roles.Contains("Admin")) return "Admin";
            if (roles.Contains("Manager")) return "Manager";
            if (roles.Contains("Cashier")) return "Cashier";

            return string.Empty;
        }

        public async Task<decimal> GetMaxDiscountPercentAsync(ClaimsPrincipal user)
        {
            if (user?.Identity?.IsAuthenticated != true) return 0;

            var identityUser = await _userManager.GetUserAsync(user);
            if (identityUser == null) return 0;

            var role = await GetUserRoleAsync(user);

            // ئەدمین سنووری نییە
            if (role == "Admin") return 100;

            // وەرگرتنی سنوور لە UserSettings
            var settings = await _context.UserSettings
                .FirstOrDefaultAsync(s => s.UserId == identityUser.Id);

            if (settings != null)
                return settings.MaxDiscountPercent;

            // بەهای بنەڕەت
            return 0;
        }

        public async Task<bool> CanApplyDiscountAsync(ClaimsPrincipal user, decimal discountPercent)
        {
            var maxPercent = await GetMaxDiscountPercentAsync(user);
            return discountPercent <= maxPercent;
        }
    }
}
