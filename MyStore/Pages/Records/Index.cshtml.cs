using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;
using MyStore.Helpers;

namespace MyStore.Pages.Records
{
    // دەسەڵاتی کشێر زیاد کرا
    [Authorize(Roles = "Admin,Manager,Cashier")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _ctx;
        private readonly UserManager<IdentityUser> _userManager;

        public IndexModel(ApplicationDbContext ctx, UserManager<IdentityUser> userManager)
        {
            _ctx = ctx;
            _userManager = userManager;
        }

        public bool IsCashier { get; set; }

        // ── داتا ──────────────────────────────────────────────
        public List<ProductRowVM> Products { get; set; } = new();
        public List<CustomerRowVM> Customers { get; set; } = new();
        public List<SupplierRowVM> Suppliers { get; set; } = new();

        // ── ئامارەکان ──────────────────────────────────────────
        public int P_Total { get; set; }
        public int P_Active { get; set; }
        public int P_Archived { get; set; }
        public int P_LowStock { get; set; }
        public int P_OutStock { get; set; }
        public decimal P_StockValue { get; set; }
        public int C_Total { get; set; }
        public int C_DebtCount { get; set; }
        public decimal C_TotalDebt { get; set; }
        public decimal C_TotalCredit { get; set; }
        public int S_Total { get; set; }
        public int S_Active { get; set; }
        public int S_Invoices { get; set; }

        // ── پەیجبەندی ─────────────────────────────────────────
        public const int PageSize = 1000000000;
        [BindProperty(SupportsGet = true)] public string ActiveTab { get; set; } = "p";
        [BindProperty(SupportsGet = true)] public int PageP { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int PageC { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public int PageS { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public string? SearchP { get; set; }
        [BindProperty(SupportsGet = true)] public string? SearchC { get; set; }
        [BindProperty(SupportsGet = true)] public string? SearchS { get; set; }
        public int TotalCountP { get; set; }
        public int TotalCountC { get; set; }
        public int TotalCountS { get; set; }
        public int TotalPagesP { get; set; }
        public int TotalPagesC { get; set; }
        public int TotalPagesS { get; set; }

        public string? CurrentUserProfileImagePath { get; private set; }

        // ── Input ─────────────────────────────────────────────
        [BindProperty] public Product ProductInput { get; set; } = new();
        [BindProperty] public Customer CustomerInput { get; set; } = new();
        [BindProperty] public Supplier SupplierInput { get; set; } = new();
        [BindProperty] public int DeleteSupplierId { get; set; }

        private async Task LoadCurrentUserProfileImageAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return;

            CurrentUserProfileImagePath = await _ctx.UserSettings
                .Where(s => s.UserId == user.Id)
                .Select(s => s.ProfileImagePath)
                .FirstOrDefaultAsync();
        }

        // ═══════════════════════════════════════════════════════
        // OnGetAsync — بارکردنی پەیجی سەرەکی
        // ═══════════════════════════════════════════════════════
        public async Task OnGetAsync()
        {
            IsCashier = User.IsInRole("Cashier");
            await LoadCurrentUserProfileImageAsync();
            await LoadAllData();
        }

        // ═══════════════════════════════════════════════════════
        // AJAX — کاڵاکان (گەڕان + پەیجبەندی بەبێ ڕیلۆد)
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetProductsAjaxAsync(string? q, int pageNumber = 1, string? stockFilter = null)
        {
            var query = _ctx.Products.AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(p => p.Name.Contains(q) || p.Barcode.Contains(q));
            if (stockFilter == "low")
                query = query.Where(p => p.IsActive && p.CurrentStock > 0 && p.CurrentStock < 5);
            else if (stockFilter == "out")
                query = query.Where(p => p.IsActive && p.CurrentStock <= 0);

            int total = await query.CountAsync();
            int pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            pageNumber = Math.Clamp(pageNumber, 1, pages);

            var rows = await query.OrderBy(p => p.Name)
                .Skip((pageNumber - 1) * PageSize).Take(PageSize)
                .Select(p => new {
                    p.ProductId,
                    p.Name,
                    p.Barcode,
                    p.CurrentStock,
                    p.SalePrice,
                    p.WholesalePrice,
                    p.LastPurchasePrice,
                    p.IsActive,
                    p.QtyPerCarton,
                    p.QtyPerDozen,
                    p.QtyPerBundle,
                    Profit = p.SalePrice - p.LastPurchasePrice
                }).ToListAsync();

            return new JsonResult(new { success = true, rows, total, pages, page = pageNumber });
        }

        // ═══════════════════════════════════════════════════════
        // AJAX — کڕیارەکان
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetCustomersAjaxAsync(string? q, int pageNumber = 1)
        {
            var query = _ctx.Customers.AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(c => c.Name.Contains(q) || (c.Phone != null && c.Phone.Contains(q)));

            int total = await query.CountAsync();
            int pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            pageNumber = Math.Clamp(pageNumber, 1, pages);

            var rows = await query.OrderBy(c => c.Name)
                .Skip((pageNumber - 1) * PageSize).Take(PageSize)
                .Select(c => new { c.CustomerId, c.Name, Phone = c.Phone ?? "", Address = c.Address ?? "", c.Balance })
                .ToListAsync();

            return new JsonResult(new { success = true, rows, total, pages, page = pageNumber });
        }

        // ═══════════════════════════════════════════════════════
        // AJAX — دابینکەران
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetSuppliersAjaxAsync(string? q, int pageNumber = 1)
        {
            if (User.IsInRole("Cashier")) return new JsonResult(new { success = false });

            var query = _ctx.Suppliers.AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(s => s.Name.Contains(q) || (s.Phone != null && s.Phone.Contains(q)));

            int total = await query.CountAsync();
            int pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            pageNumber = Math.Clamp(pageNumber, 1, pages);

            var rows = await query
                .OrderBy(s => s.SupplierId == 1 ? 0 : 1).ThenBy(s => s.Name)
                .Skip((pageNumber - 1) * PageSize).Take(PageSize)
                .Select(s => new {
                    s.SupplierId,
                    s.Name,
                    Phone = s.Phone ?? "",
                    Address = s.Address ?? "",
                    s.IsActive,
                    PurchaseCount = _ctx.Purchases.Count(p => p.SupplierId == s.SupplierId)
                }).ToListAsync();

            return new JsonResult(new { success = true, rows, total, pages, page = pageNumber });
        }

        // ═══════════════════════════════════════════════════════
        // GET — تاک ئایتەم
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetProductAsync(int id)
        {
            var p = await _ctx.Products.FindAsync(id);
            if (p == null) return new JsonResult(new { success = false });
            return new JsonResult(new
            {
                success = true,
                productId = p.ProductId,
                name = p.Name,
                barcode = p.Barcode,
                salePrice = p.SalePrice,
                wholesalePrice = p.WholesalePrice,
                lastPurchasePrice = p.LastPurchasePrice,
                currentStock = p.CurrentStock,
                qtyPerCarton = p.QtyPerCarton,
                qtyPerDozen = p.QtyPerDozen,
                qtyPerBundle = p.QtyPerBundle,
                isActive = p.IsActive
            });
        }
        public async Task<IActionResult> OnGetCustomerAsync(int id)
        {
            var c = await _ctx.Customers.FindAsync(id);
            if (c == null) return new JsonResult(new { success = false });
            return new JsonResult(new
            {
                success = true,
                customerId = c.CustomerId,
                name = c.Name,
                phone = c.Phone ?? "",
                address = c.Address ?? "",
                balance = c.Balance
            });
        }
        public async Task<IActionResult> OnGetSupplierAsync(int id)
        {
            if (User.IsInRole("Cashier")) return new JsonResult(new { success = false });
            var s = await _ctx.Suppliers.FindAsync(id);
            if (s == null) return new JsonResult(new { success = false });
            return new JsonResult(new
            {
                success = true,
                supplierId = s.SupplierId,
                name = s.Name,
                phone = s.Phone ?? "",
                address = s.Address ?? "",
                isActive = s.IsActive
            });
        }

        // ═══════════════════════════════════════════════════════
        // کاڵا CRUD (زیادکردن کراوەیە بۆ هەمووان)
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostAddProductAsync()
        {
            if (string.IsNullOrWhiteSpace(ProductInput.Name) || string.IsNullOrWhiteSpace(ProductInput.Barcode))
                return J(false, "ناو و باڕکۆد پێویستن");
            if (await _ctx.Products.AnyAsync(p => p.Barcode == ProductInput.Barcode))
                return J(false, "ئەم باڕکۆدە پێشتر تۆمارکراوە");
            var p = new Product
            {
                Name = ProductInput.Name.Trim(),
                Barcode = ProductInput.Barcode.Trim(),
                SalePrice = ProductInput.SalePrice,
                WholesalePrice = ProductInput.WholesalePrice,
                QtyPerCarton = Math.Max(1, ProductInput.QtyPerCarton),
                QtyPerDozen = Math.Max(1, ProductInput.QtyPerDozen),
                QtyPerBundle = Math.Max(1, ProductInput.QtyPerBundle),
                IsActive = true,
                CurrentStock = 0,
                LastPurchasePrice = 0
            };
            _ctx.Products.Add(p);
            await _ctx.SaveChangesAsync();
            await AuditHelper.ProductAdded(_ctx, User, HttpContext, p.ProductId, p.Name);
            return J(true);
        }

        // دەستکاری و سڕینەوە قەدەغەیە بۆ کشێر
        public async Task<IActionResult> OnPostUpdateProductAsync()
        {
            if (User.IsInRole("Cashier")) return J(false, "ببوورە، دەسەڵاتت نییە بۆ دەستکاریکردن");
            var p = await _ctx.Products.FindAsync(ProductInput.ProductId);
            if (p == null) return J(false, "کاڵا نەدۆزرایەوە");
            if (await _ctx.Products.AnyAsync(x => x.Barcode == ProductInput.Barcode && x.ProductId != ProductInput.ProductId))
                return J(false, "ئەم باڕکۆدە بەکارهاتووە");
            p.Name = ProductInput.Name.Trim(); p.Barcode = ProductInput.Barcode.Trim();
            p.SalePrice = ProductInput.SalePrice; p.WholesalePrice = ProductInput.WholesalePrice;
            p.QtyPerCarton = Math.Max(1, ProductInput.QtyPerCarton);
            p.QtyPerDozen = Math.Max(1, ProductInput.QtyPerDozen);
            p.QtyPerBundle = Math.Max(1, ProductInput.QtyPerBundle);
            p.IsActive = ProductInput.IsActive;
            await _ctx.SaveChangesAsync();
            await AuditHelper.ProductEdited(_ctx, User, HttpContext, p.ProductId, p.Name);
            return J(true);
        }
        public async Task<IActionResult> OnPostArchiveProductAsync(int id)
        {
            if (User.IsInRole("Cashier")) return J(false, "ببوورە، دەسەڵاتت نییە");
            var p = await _ctx.Products.FindAsync(id);
            if (p == null) return J(false, "کاڵا نەدۆزرایەوە");
            p.IsActive = false; await _ctx.SaveChangesAsync();
            await AuditHelper.ProductArchived(_ctx, User, HttpContext, p.ProductId, p.Name, true);
            return J(true);
        }
        public async Task<IActionResult> OnPostRestoreProductAsync(int id)
        {
            if (User.IsInRole("Cashier")) return J(false, "ببوورە، دەسەڵاتت نییە");
            var p = await _ctx.Products.FindAsync(id);
            if (p == null) return J(false, "کاڵا نەدۆزرایەوە");
            p.IsActive = true; await _ctx.SaveChangesAsync();
            await AuditHelper.ProductArchived(_ctx, User, HttpContext, p.ProductId, p.Name, false);
            return J(true);
        }
        public async Task<IActionResult> OnPostDeleteProductAsync(int id)
        {
            if (User.IsInRole("Cashier")) return J(false, "ببوورە، دەسەڵاتت نییە بۆ سڕینەوە");
            var p = await _ctx.Products.FindAsync(id);
            if (p == null) return J(false, "کاڵا نەدۆزرایەوە");
            if (p.CurrentStock > 0) return J(false, $"ستۆک هەیە ({p.CurrentStock} دانە) — ئەرشیف بکە");
            if (await _ctx.SaleItems.AnyAsync(si => si.ProductId == id)) return J(false, "بەکارهاتووە لە فرۆشتن — ئەرشیف بکە");
            if (await _ctx.PurchaseItems.AnyAsync(pi => pi.ProductId == id)) return J(false, "بەکارهاتووە لە کڕین — ئەرشیف بکە");
            _ctx.Products.Remove(p); await _ctx.SaveChangesAsync(); return J(true);
        }

        // ═══════════════════════════════════════════════════════
        // کڕیار CRUD (زیادکردن کراوەیە)
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostAddCustomerAsync()
        {
            if (string.IsNullOrWhiteSpace(CustomerInput.Name)) return J(false, "ناوی کڕیار پێویستە");
            _ctx.Customers.Add(new Customer { Name = CustomerInput.Name.Trim(), Phone = CustomerInput.Phone?.Trim() ?? "", Address = CustomerInput.Address?.Trim() ?? "", Balance = 0 });
            await _ctx.SaveChangesAsync(); return J(true);
        }
        public async Task<IActionResult> OnPostUpdateCustomerAsync()
        {
            if (User.IsInRole("Cashier")) return J(false, "ببوورە، دەسەڵاتت نییە بۆ دەستکاریکردن");
            var c = await _ctx.Customers.FindAsync(CustomerInput.CustomerId);
            if (c == null) return J(false, "کڕیار نەدۆزرایەوە");
            c.Name = CustomerInput.Name.Trim(); c.Phone = CustomerInput.Phone?.Trim() ?? ""; c.Address = CustomerInput.Address?.Trim() ?? "";
            await _ctx.SaveChangesAsync(); return J(true);
        }
        public async Task<IActionResult> OnPostDeleteCustomerAsync(int id)
        {
            if (User.IsInRole("Cashier")) return J(false, "ببوورە، دەسەڵاتت نییە بۆ سڕینەوە");
            var c = await _ctx.Customers.FindAsync(id);
            if (c == null) return J(false, "کڕیار نەدۆزرایەوە");
            if (await _ctx.Sales.AnyAsync(s => s.CustomerId == id)) return J(false, "کڕیار مێژووی فرۆشتنی هەیە");
            if (await _ctx.CustomerLedgers.AnyAsync(l => l.CustomerId == id)) return J(false, "کڕیار تۆماری لیجەری هەیە");
            if (c.Balance != 0) return J(false, $"باڵانس {c.Balance:N0} دینارە — پێشتر چاکبکەرەوە");
            _ctx.Customers.Remove(c); await _ctx.SaveChangesAsync(); return J(true);
        }

        // ═══════════════════════════════════════════════════════
        // دابینکەر CRUD (هەمووی داخراوە بۆ کشێر)
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostAddSupplierAsync()
        {
            if (User.IsInRole("Cashier")) return J(false, "ببوورە، دەسەڵاتت نییە");
            if (string.IsNullOrWhiteSpace(SupplierInput.Name)) return J(false, "ناوی دابینکەر پێویستە");
            _ctx.Suppliers.Add(new Supplier { Name = SupplierInput.Name.Trim(), Phone = string.IsNullOrWhiteSpace(SupplierInput.Phone) ? null : SupplierInput.Phone.Trim(), Address = string.IsNullOrWhiteSpace(SupplierInput.Address) ? null : SupplierInput.Address.Trim(), IsActive = true });
            await _ctx.SaveChangesAsync(); return J(true);
        }
        public async Task<IActionResult> OnPostUpdateSupplierAsync()
        {
            if (User.IsInRole("Cashier")) return J(false, "ببوورە، دەسەڵاتت نییە");
            var s = await _ctx.Suppliers.FindAsync(SupplierInput.SupplierId);
            if (s == null) return J(false, "دابینکەر نەدۆزرایەوە");
            s.Name = SupplierInput.Name.Trim(); s.Phone = string.IsNullOrWhiteSpace(SupplierInput.Phone) ? null : SupplierInput.Phone.Trim(); s.Address = string.IsNullOrWhiteSpace(SupplierInput.Address) ? null : SupplierInput.Address.Trim(); s.IsActive = SupplierInput.IsActive;
            await _ctx.SaveChangesAsync(); return J(true);
        }
        public async Task<IActionResult> OnPostArchiveSupplierAsync(int id)
        {
            if (User.IsInRole("Cashier")) return J(false, "ببوورە، دەسەڵاتت نییە");
            var s = await _ctx.Suppliers.FindAsync(id);
            if (s == null) return J(false, "دابینکەر نەدۆزرایەوە");
            s.IsActive = false; await _ctx.SaveChangesAsync(); return J(true);
        }
        public async Task<IActionResult> OnPostRestoreSupplierAsync(int id)
        {
            if (User.IsInRole("Cashier")) return J(false, "ببوورە، دەسەڵاتت نییە");
            var s = await _ctx.Suppliers.FindAsync(id);
            if (s == null) return J(false, "دابینکەر نەدۆزرایەوە");
            s.IsActive = true; await _ctx.SaveChangesAsync(); return J(true);
        }
        public async Task<IActionResult> OnPostDeleteSupplierAsync()
        {
            if (User.IsInRole("Cashier")) return J(false, "ببوورە، دەسەڵاتت نییە");
            var s = await _ctx.Suppliers.Include(x => x.Purchases).FirstOrDefaultAsync(x => x.SupplierId == DeleteSupplierId);
            if (s == null) return J(false, "دابینکەر نەدۆزرایەوە");
            if (s.Purchases != null && s.Purchases.Any()) return J(false, $"دابینکەر {s.Purchases.Count} پسوڵەی هەیە — ئەرشیف بکە");
            _ctx.Suppliers.Remove(s); await _ctx.SaveChangesAsync(); return J(true);
        }

        // ═══════════════════════════════════════════════════════
        // هیلپەر
        // ═══════════════════════════════════════════════════════
        private async Task LoadAllData()
        {
            // ئامارەکانی کاڵا
            var allP = await _ctx.Products.ToListAsync();
            P_Total = allP.Count; P_Active = allP.Count(p => p.IsActive); P_Archived = allP.Count(p => !p.IsActive);
            P_LowStock = allP.Count(p => p.IsActive && p.CurrentStock > 0 && p.CurrentStock < 5);
            P_OutStock = allP.Count(p => p.IsActive && p.CurrentStock <= 0);
            P_StockValue = allP.Sum(p => p.CurrentStock * p.LastPurchasePrice);

            // پەیجی کاڵا (بارکردنی یەکەمی بارکردن)
            var pQ = _ctx.Products.AsQueryable();
            if (!string.IsNullOrWhiteSpace(SearchP)) pQ = pQ.Where(p => p.Name.Contains(SearchP) || p.Barcode.Contains(SearchP));
            TotalCountP = await pQ.CountAsync();
            TotalPagesP = Math.Max(1, (int)Math.Ceiling(TotalCountP / (double)PageSize));
            PageP = Math.Clamp(PageP, 1, TotalPagesP);
            Products = await pQ.OrderBy(p => p.Name).Skip((PageP - 1) * PageSize).Take(PageSize)
                .Select(p => new ProductRowVM { ProductId = p.ProductId, Name = p.Name, Barcode = p.Barcode, CurrentStock = p.CurrentStock, SalePrice = p.SalePrice, WholesalePrice = p.WholesalePrice, LastPurchasePrice = p.LastPurchasePrice, IsActive = p.IsActive, QtyPerCarton = p.QtyPerCarton, QtyPerDozen = p.QtyPerDozen, QtyPerBundle = p.QtyPerBundle }).ToListAsync();

            // ئامارەکانی کڕیار
            var allC = await _ctx.Customers.ToListAsync();
            C_Total = allC.Count; C_DebtCount = allC.Count(c => c.Balance > 0);
            C_TotalDebt = allC.Where(c => c.Balance > 0).Sum(c => c.Balance);
            C_TotalCredit = allC.Where(c => c.Balance > 0).Sum(c => c.Balance);

            var cQ = _ctx.Customers.AsQueryable();
            if (!string.IsNullOrWhiteSpace(SearchC)) cQ = cQ.Where(c => c.Name.Contains(SearchC) || (c.Phone != null && c.Phone.Contains(SearchC)));
            TotalCountC = await cQ.CountAsync();
            TotalPagesC = Math.Max(1, (int)Math.Ceiling(TotalCountC / (double)PageSize));
            PageC = Math.Clamp(PageC, 1, TotalPagesC);
            Customers = await cQ.OrderBy(c => c.Name).Skip((PageC - 1) * PageSize).Take(PageSize)
                .Select(c => new CustomerRowVM { CustomerId = c.CustomerId, Name = c.Name, Phone = c.Phone ?? "", Address = c.Address ?? "", Balance = c.Balance }).ToListAsync();

            // دابینکەران مەهێنە ئەگەر کشێر بێت
            if (!IsCashier)
            {
                S_Total = await _ctx.Suppliers.CountAsync();
                S_Active = await _ctx.Suppliers.CountAsync(s => s.IsActive);
                S_Invoices = await _ctx.Purchases.CountAsync();

                var sQ = _ctx.Suppliers.AsQueryable();
                if (!string.IsNullOrWhiteSpace(SearchS)) sQ = sQ.Where(s => s.Name.Contains(SearchS) || (s.Phone != null && s.Phone.Contains(SearchS)));
                TotalCountS = await sQ.CountAsync();
                TotalPagesS = Math.Max(1, (int)Math.Ceiling(TotalCountS / (double)PageSize));
                PageS = Math.Clamp(PageS, 1, TotalPagesS);
                Suppliers = await sQ.OrderBy(s => s.SupplierId == 1 ? 0 : 1).ThenBy(s => s.Name)
                    .Skip((PageS - 1) * PageSize).Take(PageSize)
                    .Select(s => new SupplierRowVM { SupplierId = s.SupplierId, Name = s.Name, Phone = s.Phone ?? "", Address = s.Address ?? "", IsActive = s.IsActive, PurchaseCount = _ctx.Purchases.Count(p => p.SupplierId == s.SupplierId) }).ToListAsync();
            }
        }

        // ═══════════════════════════════════════════════════════
        // AJAX — نوێکردنەوەی ئامارەکان بەبێ ڕیلۆد
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetStatsAjaxAsync()
        {
            var allP = await _ctx.Products.ToListAsync();
            var allC = await _ctx.Customers.ToListAsync();

            if (User.IsInRole("Cashier"))
            {
                return new JsonResult(new
                {
                    success = true,
                    p_Total = allP.Count,
                    p_Active = allP.Count(p => p.IsActive),
                    p_Archived = allP.Count(p => !p.IsActive),
                    p_LowStock = allP.Count(p => p.IsActive && p.CurrentStock > 0 && p.CurrentStock < 5),
                    p_OutStock = allP.Count(p => p.IsActive && p.CurrentStock <= 0),
                    p_StockValue = allP.Sum(p => p.CurrentStock * p.LastPurchasePrice),
                    c_Total = allC.Count,
                    c_DebtCount = allC.Count(c => c.Balance > 0),
                    c_TotalDebt = allC.Where(c => c.Balance > 0).Sum(c => c.Balance),
                    c_TotalCredit = allC.Where(c => c.Balance > 0).Sum(c => c.Balance)
                });
            }

            return new JsonResult(new
            {
                success = true,
                p_Total = allP.Count,
                p_Active = allP.Count(p => p.IsActive),
                p_Archived = allP.Count(p => !p.IsActive),
                p_LowStock = allP.Count(p => p.IsActive && p.CurrentStock > 0 && p.CurrentStock < 5),
                p_OutStock = allP.Count(p => p.IsActive && p.CurrentStock <= 0),
                p_StockValue = allP.Sum(p => p.CurrentStock * p.LastPurchasePrice),
                c_Total = allC.Count,
                c_DebtCount = allC.Count(c => c.Balance > 0),
                c_TotalDebt = allC.Where(c => c.Balance > 0).Sum(c => c.Balance),
                c_TotalCredit = allC.Where(c => c.Balance > 0).Sum(c => c.Balance),
                s_Total = await _ctx.Suppliers.CountAsync(),
                s_Active = await _ctx.Suppliers.CountAsync(s => s.IsActive),
                s_Invoices = await _ctx.Purchases.CountAsync()
            });
        }

        private JsonResult J(bool ok, string msg = "") => new JsonResult(new { success = ok, message = msg });
    }

    public class ProductRowVM { public int ProductId { get; set; } public string Name { get; set; } = ""; public string Barcode { get; set; } = ""; public int CurrentStock { get; set; } public decimal SalePrice { get; set; } public decimal WholesalePrice { get; set; } public decimal LastPurchasePrice { get; set; } public bool IsActive { get; set; } public int QtyPerCarton { get; set; } public int QtyPerDozen { get; set; } public int QtyPerBundle { get; set; } public decimal Profit => SalePrice - LastPurchasePrice; }
    public class CustomerRowVM { public int CustomerId { get; set; } public string Name { get; set; } = ""; public string Phone { get; set; } = ""; public string Address { get; set; } = ""; public decimal Balance { get; set; } }
    public class SupplierRowVM { public int SupplierId { get; set; } public string Name { get; set; } = ""; public string Phone { get; set; } = ""; public string Address { get; set; } = ""; public bool IsActive { get; set; } public int PurchaseCount { get; set; } }
}