using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;

namespace MyStore.Pages.Admin
{
    [Authorize(Roles = "Admin")]
    public class SettingsModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public SettingsModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public decimal CashierMaxDiscount { get; set; }
        public decimal ManagerMaxDiscount { get; set; }
        public string StoreName { get; set; } = "";
        public string StorePhone { get; set; } = "";
        public string StoreAddress { get; set; } = "";

        public async Task OnGetAsync()
        {
            var settings = await _context.SystemSettings.ToListAsync();

            CashierMaxDiscount = GetDecimal(settings, SettingsKeys.CashierMaxDiscountPercent, 5);
            ManagerMaxDiscount = GetDecimal(settings, SettingsKeys.ManagerMaxDiscountPercent, 15);
            StoreName = GetString(settings, SettingsKeys.StoreName, "");
            StorePhone = GetString(settings, SettingsKeys.StorePhone, "");
            StoreAddress = GetString(settings, SettingsKeys.StoreAddress, "");
        }

        private decimal GetDecimal(List<SystemSettings> settings, string key, decimal def)
        {
            var s = settings.FirstOrDefault(x => x.Key == key);
            return s != null && decimal.TryParse(s.Value, out var v) ? v : def;
        }

        private string GetString(List<SystemSettings> settings, string key, string def)
        {
            return settings.FirstOrDefault(x => x.Key == key)?.Value ?? def;
        }

        public async Task<IActionResult> OnPostSaveDiscountAsync(decimal cashierMax, decimal managerMax)
        {
            await SaveSettingAsync(SettingsKeys.CashierMaxDiscountPercent, cashierMax.ToString());
            await SaveSettingAsync(SettingsKeys.ManagerMaxDiscountPercent, managerMax.ToString());
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostSaveStoreAsync(string storeName, string storePhone, string storeAddress)
        {
            await SaveSettingAsync(SettingsKeys.StoreName, storeName ?? "");
            await SaveSettingAsync(SettingsKeys.StorePhone, storePhone ?? "");
            await SaveSettingAsync(SettingsKeys.StoreAddress, storeAddress ?? "");
            return new JsonResult(new { success = true });
        }

        private async Task SaveSettingAsync(string key, string value)
        {
            var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting == null)
            {
                setting = new SystemSettings { Key = key, Value = value, LastModified = DateTime.Now, ModifiedBy = User.Identity?.Name };
                _context.SystemSettings.Add(setting);
            }
            else
            {
                setting.Value = value;
                setting.LastModified = DateTime.Now;
                setting.ModifiedBy = User.Identity?.Name;
            }
            await _context.SaveChangesAsync();
        }
    }
}
