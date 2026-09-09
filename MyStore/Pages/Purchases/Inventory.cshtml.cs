using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;
using MyStore.Helpers;

namespace MyStore.Pages.Inventory
{
    // لێرەدا دەسەڵاتمان داوە تەنها بە ئەدمین و مەنەجەر کە بتوانن ئەم پەیجە بکەنەوە
    [Authorize(Roles = "Admin,Manager")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public IndexModel(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ئامار
        public int TotalProducts { get; set; }
        public int LowStockCount { get; set; }
        public int OutOfStockCount { get; set; }
        public decimal TotalStockValue { get; set; }

        // داتا
        public IList<InventoryStatusViewModel> InventoryList { get; set; } = new List<InventoryStatusViewModel>();
        public IList<StockAdjustment> AdjustmentHistory { get; set; } = new List<StockAdjustment>();

        // ڕاستکردنەوە
        [BindProperty] public StockAdjustment AdjustmentInput { get; set; } = new();

        // وێنەی بەکارهێنەر
        public string? CurrentUserProfileImagePath { get; set; }

        public async Task OnGetAsync()
        {
            // وێنەی بەکارهێنەر
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                CurrentUserProfileImagePath = await _context.UserSettings
                    .Where(s => s.UserId == user.Id)
                    .Select(s => s.ProfileImagePath)
                    .FirstOrDefaultAsync();
            }

            // ستۆک
            var products = await _context.Products.Where(p => p.IsActive).ToListAsync();
            var list = products.Select(p => new InventoryStatusViewModel
            {
                ProductId = p.ProductId,
                ProductName = p.Name,
                Barcode = p.Barcode,
                SalePrice = p.SalePrice,
                AveragePurchasePrice = p.LastPurchasePrice,
                CurrentStock = p.CurrentStock
            }).ToList();

            InventoryList = list.OrderBy(i => i.CurrentStock).ToList();
            TotalProducts = list.Count;
            LowStockCount = list.Count(i => i.CurrentStock > 0 && i.CurrentStock < 5);
            OutOfStockCount = list.Count(i => i.CurrentStock <= 0);
            TotalStockValue = list.Sum(i => i.CurrentStock * i.AveragePurchasePrice);

            // مێژووی ڕاستکردنەوە
            AdjustmentHistory = await _context.StockAdjustments
                .Include(a => a.Product)
                .OrderByDescending(a => a.AdjustmentDate)
                .Take(100)
                .ToListAsync();
        }

        // ═══ ڕاستکردنەوەی ستۆک ═══
        public async Task<IActionResult> OnPostAdjustStockAsync()
        {
            if (AdjustmentInput.ProductId == 0 || AdjustmentInput.AdjustmentQuantity == 0)
            {
                await OnGetAsync();
                return Page();
            }

            var prod = await _context.Products.FindAsync(AdjustmentInput.ProductId);
            if (prod == null) return NotFound();

            if (prod.CurrentStock + AdjustmentInput.AdjustmentQuantity < 0)
            {
                ModelState.AddModelError("", "ستۆک دەبێتە ڕەش");
                await OnGetAsync();
                return Page();
            }

            AdjustmentInput.StockBefore = prod.CurrentStock;

            if (AdjustmentInput.AdjustmentQuantity > 0)
            {
                CostHelper.AddStockWithoutPriceChange(prod, AdjustmentInput.AdjustmentQuantity);
            }
            else
            {
                CostHelper.RemoveStock(prod, Math.Abs(AdjustmentInput.AdjustmentQuantity));
            }

            AdjustmentInput.StockAfter = prod.CurrentStock;
            AdjustmentInput.AdjustmentDate = DateTime.Now;
            AdjustmentInput.AdjustmentType = "ڕاستکردنەوە";
            AdjustmentInput.UserName = User.Identity?.Name ?? "System";

            if (string.IsNullOrWhiteSpace(AdjustmentInput.Reason))
            {
                AdjustmentInput.Reason = AdjustmentInput.AdjustmentQuantity > 0 ? "زیادکردنی ستۆک" : "کەمکردنەوەی ستۆک";
            }

            _context.StockAdjustments.Add(AdjustmentInput);
            await _context.SaveChangesAsync();

            return RedirectToPage();
        }
    }
}