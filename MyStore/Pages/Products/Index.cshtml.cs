using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;

namespace MyStore.Pages.Products
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<ProductViewModel> ProductList { get; set; } = default!;

        public int TotalProducts { get; set; }
        public int ActiveProducts { get; set; }
        public int InactiveProducts { get; set; }

        [BindProperty(SupportsGet = true)] public bool ShowInactive { get; set; }
        [BindProperty(SupportsGet = true)] public string? SearchTerm { get; set; }

        [BindProperty] public Product ProductInput { get; set; } = new Product();

        // ═══════════════════════════════════════════════════════
        //  OnGetAsync — بارکردنی سەرەتایی
        // ═══════════════════════════════════════════════════════
        public async Task OnGetAsync()
        {
            await LoadProductsAsync();
        }

        // ═══════════════════════════════════════════════════════
        //  OnGetSearchAsync — AJAX بێ ڕیلۆد
        //  GET ?handler=Search&SearchTerm=...&ShowInactive=true
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetSearchAsync()
        {
            await LoadProductsAsync();

            return new JsonResult(new
            {
                total = TotalProducts,
                active = ActiveProducts,
                inactive = InactiveProducts,
                count = ProductList.Count,
                products = ProductList.Select(p => new
                {
                    p.ProductId,
                    p.Name,
                    p.Barcode,
                    p.SalePrice,
                    p.WholesalePrice,
                    p.IsActive,
                    p.QtyPerCarton,
                    p.QtyPerDozen,
                    p.QtyPerBundle,
                    p.CurrentStock
                })
            });
        }

        // ═══════════════════════════════════════════════════════
        //  OnGetProductAsync — زانیاری یەک کاڵا بۆ مۆدال
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetProductAsync(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return new JsonResult(new { success = false });

            return new JsonResult(new
            {
                success = true,
                product = new
                {
                    product.ProductId,
                    product.Name,
                    product.Barcode,
                    product.SalePrice,
                    product.WholesalePrice,
                    product.QtyPerCarton,
                    product.QtyPerDozen,
                    product.QtyPerBundle,
                    product.IsActive
                }
            });
        }

        // ═══════════════════════════════════════════════════════
        //  OnPostAddProductAsync
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostAddProductAsync()
        {
            var exists = await _context.Products.AnyAsync(p => p.Barcode == ProductInput.Barcode);
            if (exists)
                return new JsonResult(new { success = false, message = "ئەم باڕکۆدە پێشتر تۆمارکراوە!" });
            try
            {
                var product = new Product
                {
                    Name = ProductInput.Name,
                    Barcode = ProductInput.Barcode,
                    SalePrice = ProductInput.SalePrice,
                    WholesalePrice = ProductInput.WholesalePrice,
                    QtyPerCarton = ProductInput.QtyPerCarton > 0 ? ProductInput.QtyPerCarton : 1,
                    QtyPerDozen = ProductInput.QtyPerDozen > 0 ? ProductInput.QtyPerDozen : 1,
                    QtyPerBundle = ProductInput.QtyPerBundle > 0 ? ProductInput.QtyPerBundle : 1,
                    IsActive = true,
                    CurrentStock = 0,
                    LastPurchasePrice = 0
                };
                _context.Products.Add(product);
                await _context.SaveChangesAsync();
                return new JsonResult(new { success = true, productId = product.ProductId });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "هەڵە: " + ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════
        //  OnPostUpdateProductAsync
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostUpdateProductAsync()
        {
            var product = await _context.Products.FindAsync(ProductInput.ProductId);
            if (product == null)
                return new JsonResult(new { success = false, message = "کاڵا نەدۆزرایەوە!" });

            var exists = await _context.Products.AnyAsync(
                p => p.Barcode == ProductInput.Barcode && p.ProductId != ProductInput.ProductId);
            if (exists)
                return new JsonResult(new { success = false, message = "ئەم باڕکۆدە پێشتر تۆمارکراوە!" });

            try
            {
                product.Name = ProductInput.Name;
                product.Barcode = ProductInput.Barcode;
                product.SalePrice = ProductInput.SalePrice;
                product.WholesalePrice = ProductInput.WholesalePrice;
                product.QtyPerCarton = ProductInput.QtyPerCarton > 0 ? ProductInput.QtyPerCarton : 1;
                product.QtyPerDozen = ProductInput.QtyPerDozen > 0 ? ProductInput.QtyPerDozen : 1;
                product.QtyPerBundle = ProductInput.QtyPerBundle > 0 ? ProductInput.QtyPerBundle : 1;
                product.IsActive = ProductInput.IsActive;
                await _context.SaveChangesAsync();
                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "هەڵە: " + ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════
        //  OnPostToggleStatusAsync
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostToggleStatusAsync(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null)
                return new JsonResult(new { success = false, message = "کاڵا نەدۆزرایەوە!" });

            product.IsActive = !product.IsActive;
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true, isActive = product.IsActive });
        }

        // ═══════════════════════════════════════════════════════
        //  OnPostDeleteProductAsync — پشکنینی ستۆک + فرۆشتن
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostDeleteProductAsync(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null)
                return new JsonResult(new { success = false, message = "کاڵا نەدۆزرایەوە!" });

            if (product.CurrentStock > 0)
                return new JsonResult(new
                {
                    success = false,
                    message = $"ناتوانرێت بسڕدرێتەوە! ئەم کاڵایە {product.CurrentStock} دانە لە کۆگادا هەیە."
                });

            var usedInSales = await _context.SaleItems.AnyAsync(si => si.ProductId == id);
            if (usedInSales)
                return new JsonResult(new
                {
                    success = false,
                    message = "ناتوانرێت بسڕدرێتەوە! ئەم کاڵایە بەکارهاتووە لە پسوڵەکانی فرۆشتن."
                });

            try
            {
                _context.Products.Remove(product);
                await _context.SaveChangesAsync();
                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "هەڵە: " + ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════
        //  Private Helper
        // ═══════════════════════════════════════════════════════
        private async Task LoadProductsAsync()
        {
            IQueryable<Product> query = _context.Products;

            if (!ShowInactive)
                query = query.Where(p => p.IsActive);

            if (!string.IsNullOrEmpty(SearchTerm))
                query = query.Where(p => p.Name.Contains(SearchTerm) || p.Barcode.Contains(SearchTerm));

            var products = await query.OrderByDescending(p => p.ProductId).ToListAsync();

            ProductList = products.Select(p => new ProductViewModel
            {
                ProductId = p.ProductId,
                Name = p.Name,
                Barcode = p.Barcode,
                SalePrice = p.SalePrice,
                WholesalePrice = p.WholesalePrice,
                LastPurchasePrice = p.LastPurchasePrice,
                IsActive = p.IsActive,
                QtyPerCarton = p.QtyPerCarton,
                QtyPerDozen = p.QtyPerDozen,
                QtyPerBundle = p.QtyPerBundle,
                CurrentStock = p.CurrentStock
            }).ToList();

            var all = await _context.Products.ToListAsync();
            TotalProducts = all.Count;
            ActiveProducts = all.Count(p => p.IsActive);
            InactiveProducts = all.Count(p => !p.IsActive);
        }
    }

    public class ProductViewModel
    {
        public int ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public decimal SalePrice { get; set; }
        public decimal WholesalePrice { get; set; }
        public decimal LastPurchasePrice { get; set; }
        public bool IsActive { get; set; }
        public int QtyPerCarton { get; set; }
        public int QtyPerDozen { get; set; }
        public int QtyPerBundle { get; set; }
        public int CurrentStock { get; set; }
    }
}
