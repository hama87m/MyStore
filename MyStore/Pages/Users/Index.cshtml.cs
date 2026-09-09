using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;
using MyStore.Helpers;

namespace MyStore.Pages.Users
{
    [Authorize(Roles = "Admin")]
    public class IndexModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;

        public IndexModel(UserManager<IdentityUser> userManager, RoleManager<IdentityRole> roleManager, ApplicationDbContext context, IWebHostEnvironment env)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _env = env;
        }

        public List<UserViewModel> Users { get; set; } = new();
        public string? CurrentUserProfileImagePath { get; set; }
        public string? CurrentUserName { get; set; }
        public string? CurrentUserRoleKurdish { get; set; }

        public int TotalUsers { get; set; }
        public int AdminCount { get; set; }
        public int ManagerCount { get; set; }
        public int CashierCount { get; set; }
        public int LockedCount { get; set; }

        public async Task OnGetAsync(string? searchTerm = null, string? roleFilter = null)
        {
            ViewData["SearchTerm"] = searchTerm;
            ViewData["RoleFilter"] = roleFilter;

            var usersQuery = _userManager.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                // ── کێشەکە لێرەدا چارەسەر کرا: هەردووکیان کران بە StartsWith ──
                usersQuery = usersQuery.Where(u =>
                    u.UserName.ToLower().StartsWith(term) ||
                    (u.Email != null && u.Email.ToLower().StartsWith(term)));
            }

            var filteredUsers = await usersQuery.ToListAsync();
            var userSettings = await _context.UserSettings.ToListAsync();
            Users = new List<UserViewModel>();

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser != null)
            {
                var currentSettings = userSettings.FirstOrDefault(s => s.UserId == currentUser.Id);
                CurrentUserProfileImagePath = currentSettings?.ProfileImagePath;
                CurrentUserName = currentUser.UserName;
                var currentRoles = await _userManager.GetRolesAsync(currentUser);
                var currentRole = currentRoles.FirstOrDefault() ?? "Cashier";
                CurrentUserRoleKurdish = currentRole switch
                {
                    "Admin" => "ئەدمین",
                    "Manager" => "مەنەجەر",
                    _ => "کاشێر"
                };
            }

            foreach (var user in filteredUsers)
            {
                var roles = await _userManager.GetRolesAsync(user);
                var userRole = roles.FirstOrDefault() ?? "Cashier";

                // ── فلتەرکردنی ڕۆڵ ──
                if (!string.IsNullOrWhiteSpace(roleFilter) && userRole != roleFilter)
                {
                    continue;
                }

                var isLocked = await _userManager.IsLockedOutAsync(user);
                var settings = userSettings.FirstOrDefault(s => s.UserId == user.Id);

                Users.Add(new UserViewModel
                {
                    Id = user.Id,
                    UserName = user.UserName ?? "",
                    Email = user.Email ?? "",
                    Role = userRole,
                    IsLocked = isLocked,
                    MaxDiscountPercent = settings?.MaxDiscountPercent ?? 0,
                    ProfileImagePath = settings?.ProfileImagePath
                });
            }

            TotalUsers = Users.Count;
            AdminCount = Users.Count(u => u.Role == "Admin");
            ManagerCount = Users.Count(u => u.Role == "Manager");
            CashierCount = Users.Count(u => u.Role == "Cashier");
            LockedCount = Users.Count(u => u.IsLocked);
        }

        public async Task<IActionResult> OnPostAddUserAsync(string userName, string email, string password, string role, decimal maxDiscount, IFormFile? profileImage)
        {
            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                return new JsonResult(new { success = false, message = "هەموو خانەکان پڕ بکەرەوە" });

            if (await _userManager.FindByNameAsync(userName) != null)
                return new JsonResult(new { success = false, message = "ئەم ناوە پێشتر بەکارهاتووە" });

            if (await _userManager.FindByEmailAsync(email) != null)
                return new JsonResult(new { success = false, message = "ئەم ئیمەیڵە پێشتر تۆمارکراوە" });

            var user = new IdentityUser
            {
                UserName = userName,
                Email = email,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, password);

            if (!result.Succeeded)
            {
                var errors = string.Join("، ", result.Errors.Select(e => e.Description));
                return new JsonResult(new { success = false, message = errors });
            }

            if (!await _roleManager.RoleExistsAsync(role))
                await _roleManager.CreateAsync(new IdentityRole(role));
            await _userManager.AddToRoleAsync(user, role);

            var settings = new UserSettings
            {
                UserId = user.Id,
                MaxDiscountPercent = maxDiscount,
                ProfileImagePath = await SaveProfileImageAsync(profileImage, user.Id),
                LastModified = DateTime.Now
            };
            _context.UserSettings.Add(settings);
            await _context.SaveChangesAsync();

            await AuditHelper.UserCreated(_context, User, HttpContext, userName, role);
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnGetUserAsync(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return new JsonResult(new { success = false });

            var roles = await _userManager.GetRolesAsync(user);
            var settings = await _context.UserSettings.FirstOrDefaultAsync(s => s.UserId == id);

            return new JsonResult(new
            {
                success = true,
                user = new
                {
                    id = user.Id,
                    userName = user.UserName,
                    email = user.Email,
                    role = roles.FirstOrDefault() ?? "Cashier",
                    maxDiscount = settings?.MaxDiscountPercent ?? 0,
                    profileImagePath = settings?.ProfileImagePath
                }
            });
        }

        public async Task<IActionResult> OnPostUpdateUserAsync(string id, string userName, string email, string password, string role, decimal maxDiscount, IFormFile? profileImage, bool removeProfileImage = false)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return new JsonResult(new { success = false, message = "بەکارهێنەر نەدۆزرایەوە" });

            var existingUser = await _userManager.FindByNameAsync(userName);
            if (existingUser != null && existingUser.Id != id)
                return new JsonResult(new { success = false, message = "ئەم ناوە پێشتر بەکارهاتووە" });

            existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null && existingUser.Id != id)
                return new JsonResult(new { success = false, message = "ئەم ئیمەیڵە پێشتر تۆمارکراوە" });

            user.UserName = userName;
            user.Email = email;

            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, role);

            if (!string.IsNullOrEmpty(password))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var result = await _userManager.ResetPasswordAsync(user, token, password);
                if (!result.Succeeded)
                {
                    var errors = string.Join("، ", result.Errors.Select(e => e.Description));
                    return new JsonResult(new { success = false, message = errors });
                }
            }

            await _userManager.UpdateAsync(user);

            var settings = await _context.UserSettings.FirstOrDefaultAsync(s => s.UserId == id);
            if (settings == null)
            {
                settings = new UserSettings { UserId = id };
                _context.UserSettings.Add(settings);
            }
            settings.MaxDiscountPercent = maxDiscount;

            if (removeProfileImage && !string.IsNullOrWhiteSpace(settings.ProfileImagePath))
            {
                DeleteProfileImageIfExists(settings.ProfileImagePath);
                settings.ProfileImagePath = null;
            }

            if (profileImage != null)
            {
                if (!string.IsNullOrWhiteSpace(settings.ProfileImagePath))
                    DeleteProfileImageIfExists(settings.ProfileImagePath);

                settings.ProfileImagePath = await SaveProfileImageAsync(profileImage, user.Id);
            }

            settings.LastModified = DateTime.Now;
            await _context.SaveChangesAsync();

            await AuditHelper.UserEdited(_context, User, HttpContext, userName, $"ڕۆڵ: {role}");
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostToggleLockAsync(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return new JsonResult(new { success = false, message = "بەکارهێنەر نەدۆزرایەوە" });

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser?.Id == id)
                return new JsonResult(new { success = false, message = "ناتوانیت خۆت قفڵ بکەیت" });

            var isLocked = await _userManager.IsLockedOutAsync(user);

            if (isLocked)
                await _userManager.SetLockoutEndDateAsync(user, null);
            else
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));

            await AuditHelper.UserLocked(_context, User, HttpContext, user.UserName ?? "", !isLocked);
            return new JsonResult(new { success = true, isLocked = !isLocked });
        }

        public async Task<IActionResult> OnPostDeleteUserAsync(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return new JsonResult(new { success = false, message = "بەکارهێنەر نەدۆزرایەوە" });

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser?.Id == id)
                return new JsonResult(new { success = false, message = "ناتوانیت خۆت بسڕیتەوە" });

            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Admin"))
            {
                var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
                if (adminUsers.Count <= 1)
                    return new JsonResult(new { success = false, message = "کۆتا ئەدمین ناسڕدرێتەوە!" });
            }

            var settings = await _context.UserSettings.FirstOrDefaultAsync(s => s.UserId == id);
            if (settings != null)
            {
                if (!string.IsNullOrWhiteSpace(settings.ProfileImagePath))
                    DeleteProfileImageIfExists(settings.ProfileImagePath);

                _context.UserSettings.Remove(settings);
            }

            await _userManager.DeleteAsync(user);
            await _context.SaveChangesAsync();

            await AuditHelper.UserDeleted(_context, User, HttpContext, user.UserName ?? "");
            return new JsonResult(new { success = true });
        }

        private async Task<string?> SaveProfileImageAsync(IFormFile? file, string userId)
        {
            if (file == null || file.Length == 0) return null;

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext))
                throw new InvalidOperationException("تەنها JPG, JPEG, PNG, WEBP ڕێگەپێدراون.");

            var uploadsRoot = Path.Combine(_env.WebRootPath, "uploads", "users");
            Directory.CreateDirectory(uploadsRoot);

            var fileName = $"{userId}_{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(uploadsRoot, fileName);

            await using var stream = new FileStream(fullPath, FileMode.Create);
            await file.CopyToAsync(stream);

            return $"/uploads/users/{fileName}";
        }

        private void DeleteProfileImageIfExists(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return;
            var relativePath = imagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(_env.WebRootPath, relativePath);
            if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
        }

        public class UserViewModel
        {
            public string Id { get; set; } = "";
            public string UserName { get; set; } = "";
            public string Email { get; set; } = "";
            public string Role { get; set; } = "";
            public bool IsLocked { get; set; }
            public decimal MaxDiscountPercent { get; set; }
            public string? ProfileImagePath { get; set; }
        }
    }
}