using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using MyStore.Data;
using MyStore.Models;
using MyStore.Helpers;
using System.IO;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MyStore.Pages.Settings
{
    [Authorize] // لابردنی مەرجی Admin لێرە، بۆ ئەوەی کاشێر بتوانێت بێتە پەڕەکە و تەنها ڕووکار بگۆڕێت
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _config;

        public IndexModel(ApplicationDbContext context, UserManager<IdentityUser> userManager, IWebHostEnvironment environment, IConfiguration config)
        {
            _context = context;
            _userManager = userManager;
            _environment = environment;
            _config = config;
        }

        [BindProperty] public string StoreName { get; set; } = "MyStore";
        [BindProperty] public string StorePhone { get; set; } = "";
        [BindProperty] public string StoreAddress { get; set; } = "";
        [BindProperty] public IFormFile? LogoFile { get; set; }
        public string CurrentLogo { get; set; } = "";

        // backup
        public List<BackupFileInfo> BackupFiles { get; set; } = new();

        // audit log
        public int AuditTotalCount { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var settings = await _context.SystemSettings.ToListAsync();
            StoreName = settings.FirstOrDefault(s => s.Key == SettingsKeys.StoreName)?.Value ?? "MyStore";
            StorePhone = settings.FirstOrDefault(s => s.Key == SettingsKeys.StorePhone)?.Value ?? "";
            StoreAddress = settings.FirstOrDefault(s => s.Key == SettingsKeys.StoreAddress)?.Value ?? "";
            CurrentLogo = settings.FirstOrDefault(s => s.Key == SettingsKeys.StoreLogo)?.Value ?? "";
            LoadBackupFiles();

            // ئەگەر کاشێر بێت، تەنها ژمارەی چالاکییەکانی خۆی دەبینێت
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                var currentUserName = User.Identity?.Name;
                AuditTotalCount = await _context.AuditLogs.CountAsync(l => l.UserName == currentUserName);
            }
            else
            {
                AuditTotalCount = await _context.AuditLogs.CountAsync();
            }

            return Page();
        }

        // ═══════════════════════════════════════════════════════
        // پاشەکەوتکردنی ڕێکخستنەکان (تەنها ئەدمین)
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostAsync()
        {
            // ڕێگریکردن لە کاشێر
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                TempData["Error"] = "دەسەڵاتت نییە بۆ گۆڕینی زانیارییەکانی فرۆشگا.";
                return RedirectToPage();
            }

            var userId = _userManager.GetUserId(User);

            string logoPath = "";
            if (LogoFile != null && LogoFile.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(LogoFile.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(extension))
                { TempData["Error"] = "تەنها فایلی وێنە ڕێگەپێدراوە"; return RedirectToPage(); }
                if (LogoFile.Length > 2 * 1024 * 1024)
                { TempData["Error"] = "قەبارەی فایل زۆرە (زۆرترین: 2MB)"; return RedirectToPage(); }

                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                var fileName = "logo" + extension;
                var filePath = Path.Combine(uploadsFolder, fileName);

                var oldLogo = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == SettingsKeys.StoreLogo);
                if (oldLogo != null && !string.IsNullOrEmpty(oldLogo.Value))
                {
                    var oldPath = Path.Combine(_environment.WebRootPath, oldLogo.Value.TrimStart('/').Split('?')[0]);
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }

                using (var stream = new FileStream(filePath, FileMode.Create))
                    await LogoFile.CopyToAsync(stream);

                logoPath = "/uploads/" + fileName + "?v=" + DateTime.Now.Ticks;
            }

            await SaveSettingAsync(SettingsKeys.StoreName, StoreName, "ناوی فرۆشگا", userId);
            await SaveSettingAsync(SettingsKeys.StorePhone, StorePhone, "ژمارەی تەلەفۆن", userId);
            await SaveSettingAsync(SettingsKeys.StoreAddress, StoreAddress, "ناونیشان", userId);

            if (!string.IsNullOrEmpty(logoPath))
                await SaveSettingAsync(SettingsKeys.StoreLogo, logoPath, "لۆگۆی فرۆشگا", userId);

            await _context.SaveChangesAsync();
            TempData["Success"] = "ڕێکخستنەکان پاشەکەوت کران";
            await AuditHelper.SettingsChanged(_context, User, HttpContext, $"ناو: {StoreName}, تەلەفۆن: {StorePhone}");
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteLogoAsync()
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager")) return Forbid();

            var userId = _userManager.GetUserId(User);
            var logoSetting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == SettingsKeys.StoreLogo);
            if (logoSetting != null && !string.IsNullOrEmpty(logoSetting.Value))
            {
                var filePath = Path.Combine(_environment.WebRootPath, logoSetting.Value.TrimStart('/').Split('?')[0]);
                if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
                logoSetting.Value = "";
                logoSetting.LastModified = DateTime.Now;
                logoSetting.ModifiedBy = userId;
                await _context.SaveChangesAsync();
            }
            TempData["Success"] = "لۆگۆ سڕایەوە";
            return RedirectToPage();
        }

        // ═══════════════════════════════════════════════════════
        // Backup داتابەیس (بە سەلامەتی بەبێ قوفڵکردن)
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostBackupAsync()
        {
            try
            {
                var backupFolder = Path.Combine(_environment.WebRootPath, "backups");
                if (!Directory.Exists(backupFolder)) Directory.CreateDirectory(backupFolder);

                var fileName = $"MyStore_Backup_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.bak";
                var backupPath = Path.Combine(backupFolder, fileName);

                var connStr = _config.GetConnectionString("DefaultConnection");
                using var conn = new SqlConnection(connStr);
                await conn.OpenAsync();

                // فەرمانی باکئەپ بەبێ کوشتنی کۆنێکشنەکانی تر
                var sql = $"BACKUP DATABASE [{conn.Database}] TO DISK = @path WITH FORMAT, INIT, NAME = 'MyStore Backup'";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@path", backupPath);
                cmd.CommandTimeout = 180;
                await cmd.ExecuteNonQueryAsync();

                // کەمێک چاوەڕێ دەکەین بۆ ئەوەی داتابەیس پشوو بدات
                await Task.Delay(2000);

                TempData["Success"] = $"باکئەپ سەرکەوتوو بوو: {fileName}";
                await AuditHelper.BackupCreated(_context, User, HttpContext, fileName);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "هەڵەی باکئەپ: " + ex.Message;
            }
            return RedirectToPage();
        }

        // ═══════════════════════════════════════════════════════
        // Restore داتابەیس (بە پاراستنی دۆخی Multi-User)
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostRestoreAsync(string backupFile)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                TempData["Error"] = "دەسەڵاتت نییە بۆ ڕیستۆرکردنی داتابەیس.";
                return RedirectToPage();
            }

            string dbName = "";
            SqlConnection? conn = null;

            try
            {
                if (string.IsNullOrEmpty(backupFile))
                { TempData["Error"] = "فایلی باکئەپ هەڵنەبژێردراوە"; return RedirectToPage(); }

                var backupFolder = Path.Combine(_environment.WebRootPath, "backups");
                var backupPath = Path.Combine(backupFolder, backupFile);
                if (!System.IO.File.Exists(backupPath))
                { TempData["Error"] = "فایلی باکئەپ نەدۆزرایەوە"; return RedirectToPage(); }

                var connStr = _config.GetConnectionString("DefaultConnection");
                var builder = new SqlConnectionStringBuilder(connStr);
                dbName = builder.InitialCatalog;

                // پێویستە بە master DB کۆنێکشن بکەین بۆ restore
                builder.InitialCatalog = "master";
                conn = new SqlConnection(builder.ConnectionString);
                await conn.OpenAsync();

                // کوشتنی هەموو کۆنێکشنەکان و قوفڵکردن بۆ ڕیستۆر
                var killSql = $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE";
                using (var killCmd = new SqlCommand(killSql, conn)) { killCmd.CommandTimeout = 60; await killCmd.ExecuteNonQueryAsync(); }

                // Restore
                var restoreSql = $"RESTORE DATABASE [{dbName}] FROM DISK = @path WITH REPLACE";
                using (var restoreCmd = new SqlCommand(restoreSql, conn))
                {
                    restoreCmd.Parameters.AddWithValue("@path", backupPath);
                    restoreCmd.CommandTimeout = 300; // کاتی زیاتری پێدەدەین
                    await restoreCmd.ExecuteNonQueryAsync();
                }

                TempData["Success"] = $"داتابەیس سەرکەوتووانە گەڕێنرایەوە لە: {backupFile}";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "هەڵەی Restore: " + ex.Message;
            }
            finally
            {
                // ئەم بەشە زۆر گرنگە: دڵنیا دەبێتەوە داتابەیسەکە قوفڵ نابێت تەنانەت ئەگەر ئیرۆریش هەبێت
                if (conn != null && !string.IsNullOrEmpty(dbName))
                {
                    try
                    {
                        var multiSql = $"ALTER DATABASE [{dbName}] SET MULTI_USER";
                        using (var multiCmd = new SqlCommand(multiSql, conn)) { await multiCmd.ExecuteNonQueryAsync(); }
                        conn.Dispose();
                    }
                    catch { /* گەر پێشتر کرابێتەوە بە MULTI_USER */ }
                }
            }

            // سەیڤکردنی مێژوو لە دەرەوەی پرۆسەی ڕیستۆرەکە
            if (TempData["Success"] != null)
            {
                try { await AuditHelper.BackupRestored(_context, User, HttpContext, backupFile); } catch { }
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteBackupAsync(string backupFile)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                TempData["Error"] = "دەسەڵاتت نییە بۆ سڕینەوەی باکئەپ.";
                return RedirectToPage();
            }

            var backupFolder = Path.Combine(_environment.WebRootPath, "backups");
            var path = Path.Combine(backupFolder, backupFile);
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
                await AuditHelper.BackupDeleted(_context, User, HttpContext, backupFile);
                TempData["Success"] = "فایلی باکئەپ سڕایەوە";
            }
            return RedirectToPage();
        }

        public IActionResult OnGetDownloadBackupAsync(string backupFile)
        {
            var backupFolder = Path.Combine(_environment.WebRootPath, "backups");
            var path = Path.Combine(backupFolder, backupFile);
            if (!System.IO.File.Exists(path)) return NotFound();
            return PhysicalFile(path, "application/octet-stream", backupFile);
        }

        public async Task<IActionResult> OnGetStoreInfoAsync()
        {
            var settings = await _context.SystemSettings.ToListAsync();
            return new JsonResult(new
            {
                storeName = settings.FirstOrDefault(s => s.Key == SettingsKeys.StoreName)?.Value ?? "MyStore",
                storePhone = settings.FirstOrDefault(s => s.Key == SettingsKeys.StorePhone)?.Value ?? "",
                storeAddress = settings.FirstOrDefault(s => s.Key == SettingsKeys.StoreAddress)?.Value ?? "",
                storeLogo = settings.FirstOrDefault(s => s.Key == SettingsKeys.StoreLogo)?.Value ?? ""
            });
        }

        public async Task<IActionResult> OnGetAuditLogsAsync(int page = 1, string? category = null, string? user = null, string? search = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            int pageSize = 25;
            var query = _context.AuditLogs.AsQueryable();

            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                var currentUserName = User.Identity?.Name;
                query = query.Where(l => l.UserName == currentUserName);
            }
            else if (!string.IsNullOrEmpty(user))
            {
                query = query.Where(l => l.UserName!.Contains(user));
            }

            if (!string.IsNullOrEmpty(category))
                query = query.Where(l => l.Category == category);

            if (!string.IsNullOrEmpty(search))
                query = query.Where(l => l.Description.Contains(search));

            if (startDate.HasValue)
            {
                query = query.Where(l => l.CreatedAt >= startDate.Value);
            }
            if (endDate.HasValue)
            {
                var endOfDay = endDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(l => l.CreatedAt <= endOfDay);
            }

            var total = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(total / (double)pageSize);
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var logs = await query
              .OrderByDescending(l => l.CreatedAt)
              .ThenByDescending(l => l.Id)
              .Skip((page - 1) * pageSize)
              .Take(pageSize)
              .Select(l => new
              {
                  l.Id,
                  date = l.CreatedAt.ToString("yyyy/MM/dd HH:mm:ss"),
                  userName = l.UserName,
                  l.Category,
                  l.Action,
                  l.Description,
                  l.EntityId,
                  l.EntityType
              })
              .ToListAsync();

            return new JsonResult(new { success = true, logs, total, totalPages, page });
        }

        private async Task SaveSettingAsync(string key, string value, string description, string? userId)
        {
            var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting == null)
            {
                setting = new SystemSettings { Key = key, Value = value ?? "", Description = description, LastModified = DateTime.Now, ModifiedBy = userId };
                _context.SystemSettings.Add(setting);
            }
            else
            {
                setting.Value = value ?? "";
                setting.LastModified = DateTime.Now;
                setting.ModifiedBy = userId;
            }
        }

        private void LoadBackupFiles()
        {
            var backupFolder = Path.Combine(_environment.WebRootPath, "backups");
            if (!Directory.Exists(backupFolder)) { Directory.CreateDirectory(backupFolder); return; }

            BackupFiles = Directory.GetFiles(backupFolder, "*.bak")
              .Select(f => new FileInfo(f))
              .OrderByDescending(f => f.CreationTime)
              .Select(f => new BackupFileInfo
              {
                  FileName = f.Name,
                  CreatedAt = f.CreationTime,
                  SizeMB = Math.Round(f.Length / 1024.0 / 1024.0, 1)
              })
              .ToList();
        }
    }

    public class BackupFileInfo
    {
        public string FileName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public double SizeMB { get; set; }
    }
}