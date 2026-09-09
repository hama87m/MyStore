using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Helpers;
using MyStore.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MyStore.Pages.Purchases
{
    // لێرەدا دەسەڵاتمان داوە تەنها بە ئەدمین کە بتوانن ئەم پەیجە بکەنەوە
    [Authorize(Roles = "Admin")]
    public class StockTakeModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public StockTakeModel(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ═══ داتای پەڕە ═══
        public IList<StockTakeItemVM> Products { get; set; } = new List<StockTakeItemVM>();

        // ئامار
        public int TotalProducts { get; set; }
        public int CompletedCount { get; set; }
        public int DiffCount { get; set; }
        public int RemainingCount { get; set; }

        public string? CurrentUserProfileImagePath { get; set; }

        // ═══ داتای فۆڕم ═══
        [BindProperty]
        public List<StockTakeInputItem> Items { get; set; } = new();

        public async Task OnGetAsync()
        {
            await LoadData();
        }

        // ═══ پاشەکەوتکردنی جەرد ═══
        public async Task<IActionResult> OnPostSaveStockTakeAsync()
        {
            if (Items == null || !Items.Any())
                return RedirectToPage();

            var checkedItems = Items.Where(i => i.IsChecked).ToList();
            if (!checkedItems.Any())
                return RedirectToPage();

            var userName = User.Identity?.Name ?? "System";
            var now = DateTime.Now;

            // ═══ گۆڕانکاری نوێ: هێنانی هەموو کاڵا دیاریکراوەکان بەیەکەوە لە جیاتی N+1 ═══
            var checkedProductIds = checkedItems.Select(i => i.ProductId).ToList();
            var productsToUpdate = await _context.Products
                .Where(p => checkedProductIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId);

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var item in checkedItems)
                {
                    if (item.ActualStock < 0) continue;

                    // ڕاستەوخۆ کاڵاکە لە ڕام-ەوە دەهێنین لەبری پەیوەندیکردنەوە بە داتابەیس
                    if (!productsToUpdate.TryGetValue(item.ProductId, out var product))
                        continue;

                    int stockBefore = product.CurrentStock;
                    int newStock = item.ActualStock;
                    decimal newCost = item.NewCost > 0 ? item.NewCost : product.LastPurchasePrice;
                    int diff = newStock - stockBefore;

                    // ═══ ١. سفرکردنەوەی ستۆک ═══
                    product.CurrentStock = 0;

                    // ═══ ٢. دانانی ستۆکی نوێ بە تێچووی نوێ ═══
                    CostHelper.AddPurchaseStock(product, newStock, newCost);

                    // ═══ ٣. تۆمارکردنی StockAdjustment وەک Reset Point ═══
                    var adjustment = new StockAdjustment
                    {
                        ProductId = item.ProductId,
                        AdjustmentQuantity = diff,
                        StockBefore = stockBefore,
                        StockAfter = newStock,
                        AdjustmentDate = now,
                        AdjustmentType = "جەردکردنی کۆگا",
                        Reason = $"جەردکردنی کۆگا — ستۆک: {stockBefore} ← {newStock}, تێچوو: {newCost:N0}",
                        UserName = userName,
                        IsInventoryReset = true,
                        ResetPrice = newCost,
                        PurchaseId = null
                    };

                    _context.StockAdjustments.Add(adjustment);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            return RedirectToPage();
        }

        // ═══ زیادکردنی کاڵای نوێ (AJAX) ═══
        public async Task<IActionResult> OnPostAddProductAsync([FromBody] NewProductDto data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.Name) || string.IsNullOrWhiteSpace(data.Barcode))
            {
                return new JsonResult(new { success = false, message = "زانیارییەکان تەواو نین." });
            }

            // پشکنینی باڕکۆد ئەگەر دووبارە بێت
            bool barcodeExists = await _context.Products.AnyAsync(p => p.Barcode == data.Barcode);
            if (barcodeExists)
            {
                return new JsonResult(new { success = false, message = "ئەم باڕکۆدە پێشتر بەکارهاتووە. تکایە باڕکۆدێکی نوێ دروست بکە." });
            }

            try
            {
                // دروستکردنی کاڵاکە - لە سەرەتادا بڕی ستۆک و تێچوو سفرە
                var newProduct = new Product
                {
                    Name = data.Name,
                    Barcode = data.Barcode,
                    SalePrice = data.SalePrice,
                    WholesalePrice = data.WholesalePrice,
                    CurrentStock = 0,
                    LastPurchasePrice = 0,
                    QtyPerCarton = data.QtyPerCarton > 0 ? data.QtyPerCarton : 1,
                    QtyPerDozen = data.QtyPerDozen > 0 ? data.QtyPerDozen : 1,
                    QtyPerBundle = data.QtyPerBundle > 0 ? data.QtyPerBundle : 1,
                    HasDiscount = false,
                    IsActive = true
                };

                _context.Products.Add(newProduct);
                await _context.SaveChangesAsync();

                // گەڕاندنەوەی زانیارییەکانی کاڵا نوێیەکە بۆ فڕۆنت-ئێند بۆ ئەوەی بخرێتە ناو خشتەکە بەبێ ڕیفرێش
                return new JsonResult(new
                {
                    success = true,
                    product = new
                    {
                        productId = newProduct.ProductId,
                        name = newProduct.Name,
                        barcode = newProduct.Barcode,
                        systemStock = 0,
                        lastPurchasePrice = 0
                    }
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "هەڵەیەک ڕوویدا لە ڕاژە: " + ex.Message });
            }
        }

        // ═══ گەڕان بە AJAX ═══
        public async Task<IActionResult> OnGetSearchAsync(string? q)
        {
            var query = _context.Products.Where(p => p.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(p =>
                    p.Name.ToLower().Contains(term) ||
                    p.Barcode.ToLower().Contains(term));
            }

            var products = await query
                .OrderBy(p => p.Name)
                .Select(p => new
                {
                    p.ProductId,
                    p.Name,
                    p.Barcode,
                    p.CurrentStock,
                    p.LastPurchasePrice
                })
                .ToListAsync();

            return new JsonResult(products);
        }

        private async Task LoadData()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                CurrentUserProfileImagePath = await _context.UserSettings
                    .Where(s => s.UserId == user.Id)
                    .Select(s => s.ProfileImagePath)
                    .FirstOrDefaultAsync();
            }

            var products = await _context.Products
                .Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();

            Products = products.Select(p => new StockTakeItemVM
            {
                ProductId = p.ProductId,
                ProductName = p.Name,
                Barcode = p.Barcode,
                SystemStock = p.CurrentStock,
                LastPurchasePrice = p.LastPurchasePrice
            }).ToList();

            TotalProducts = Products.Count;
            CompletedCount = 0;
            DiffCount = 0;
            RemainingCount = TotalProducts;
        }
    }

    // ═══ ViewModels ═══
    public class StockTakeItemVM
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public int SystemStock { get; set; }
        public decimal LastPurchasePrice { get; set; }
    }

    public class StockTakeInputItem
    {
        public int ProductId { get; set; }
        public bool IsChecked { get; set; }
        public int ActualStock { get; set; }
        public decimal NewCost { get; set; }
    }

    // بۆ وەرگرتنی زانیاری کاڵای نوێ لە فۆڕمەکەوە (تەنها ئەو زانیارییانەی پێویستن ماونەتەوە)
    public class NewProductDto
    {
        public string Name { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public decimal SalePrice { get; set; }
        public decimal WholesalePrice { get; set; }
        public int QtyPerCarton { get; set; }
        public int QtyPerDozen { get; set; }
        public int QtyPerBundle { get; set; }
    }
}