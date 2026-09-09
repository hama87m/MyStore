using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;
using MyStore.Helpers;
using System.Text.Json;

namespace MyStore.Pages.Purchases
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

        public IList<PurchaseListViewModel> PurchaseList { get; set; } = default!;
        public IList<InventoryStatusViewModel> InventoryList { get; set; } = default!;
        public IList<StockAdjustment> AdjustmentHistory { get; set; } = default!;

        [BindProperty(SupportsGet = true)] public string? FilterDateRange { get; set; } = "Today";
        [BindProperty(SupportsGet = true)] public DateTime? StartDate { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? EndDate { get; set; }
        [BindProperty(SupportsGet = true)] public string? SearchProduct { get; set; }
        [BindProperty(SupportsGet = true)] public string? StockFilter { get; set; }
        [BindProperty(SupportsGet = true)] public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; }
        public int TotalFilteredCount { get; set; }
        public const int PageSize = 10;

        public decimal TotalPurchases { get; set; }
        public int TotalInvoices { get; set; }
        public int TotalProducts { get; set; }
        public int LowStockCount { get; set; }
        public int OutOfStockCount { get; set; }
        public decimal TotalStockValue { get; set; }

        [BindProperty] public StockAdjustment AdjustmentInput { get; set; } = new();
        [BindProperty] public Purchase NewPurchase { get; set; } = new();
        [BindProperty] public string? ItemsJson { get; set; }
        [BindProperty] public int EditPurchaseId { get; set; }
        [BindProperty] public int DeletePurchaseId { get; set; }
        [BindProperty] public string? PayFromCash { get; set; }

        public IList<ProductForPurchaseViewModel> ProductsForPurchase { get; set; } = default!;

        public string? CurrentUserProfileImagePath { get; private set; }

        private async Task LoadCurrentUserProfileImageAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return;

            CurrentUserProfileImagePath = await _context.UserSettings
                .Where(s => s.UserId == user.Id)
                .Select(s => s.ProfileImagePath)
                .FirstOrDefaultAsync();
        }

        public async Task OnGetAsync()
        {
            await LoadCurrentUserProfileImageAsync();
            ProcessDateFilters();

            var query = _context.Purchases
                .Include(p => p.Supplier)
                .Include(p => p.PurchaseItems!)
                .ThenInclude(pi => pi.Product)
                .Where(p => (!StartDate.HasValue || p.PurchaseDate >= StartDate.Value)
                         && (!EndDate.HasValue || p.PurchaseDate <= EndDate.Value))
                .OrderByDescending(p => p.PurchaseDate)
                .ThenByDescending(p => p.PurchaseId);

            TotalFilteredCount = await query.CountAsync();
            TotalPages = (int)Math.Ceiling(TotalFilteredCount / (double)PageSize);
            if (CurrentPage < 1) CurrentPage = 1;
            if (CurrentPage > TotalPages && TotalPages > 0) CurrentPage = TotalPages;

            // کۆی تێچووی هەموو فلتەرکراوەکان
            TotalPurchases = await _context.Purchases
                .Where(p => (!StartDate.HasValue || p.PurchaseDate >= StartDate.Value)
                         && (!EndDate.HasValue || p.PurchaseDate <= EndDate.Value))
                .SumAsync(p => p.TotalAmount);
            TotalInvoices = TotalFilteredCount;

            // جۆری کاڵا لە ماوەی فلتەرکراو
            TotalProducts = await _context.Purchases
                .Where(p => (!StartDate.HasValue || p.PurchaseDate >= StartDate.Value)
                         && (!EndDate.HasValue || p.PurchaseDate <= EndDate.Value))
                .SelectMany(p => p.PurchaseItems!)
                .Select(pi => pi.ProductId)
                .Distinct()
                .CountAsync();

            var purchases = await query
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            PurchaseList = purchases.Select(p => new PurchaseListViewModel
            {
                PurchaseId = p.PurchaseId,
                PurchaseDate = p.PurchaseDate,
                TotalAmount = p.TotalAmount,
                SupplierId = p.SupplierId,
                SupplierName = p.Supplier!.Name,
                ItemCount = p.PurchaseItems?.Count ?? 0,
                TotalQuantity = p.PurchaseItems?.Sum(i => i.Quantity) ?? 0,
                Items = p.PurchaseItems?.Select(i => new PurchaseItemViewModel
                {
                    ProductId = i.ProductId,
                    ProductName = i.Product?.Name ?? "",
                    Barcode = i.Product?.Barcode ?? "",
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    Subtotal = i.Subtotal,
                    QtyPerCarton = i.Product?.QtyPerCarton ?? 1,
                    QtyPerDozen = i.Product?.QtyPerDozen ?? 1,
                    QtyPerBundle = i.Product?.QtyPerBundle ?? 1
                }).ToList() ?? new()
            }).ToList();

            AdjustmentHistory = await _context.StockAdjustments
                .Include(a => a.Product)
                .OrderByDescending(a => a.AdjustmentDate)
                .Take(100)
                .ToListAsync();

            await LoadInventoryData();

            ProductsForPurchase = (await _context.Products.Where(p => p.IsActive).ToListAsync())
                .Select(p => new ProductForPurchaseViewModel
                {
                    ProductId = p.ProductId,
                    Name = p.Name,
                    Barcode = p.Barcode,
                    CurrentStock = p.CurrentStock,
                    QtyPerCarton = p.QtyPerCarton,
                    QtyPerDozen = p.QtyPerDozen,
                    QtyPerBundle = p.QtyPerBundle,
                    SalePrice = p.SalePrice,
                    WholesalePrice = p.WholesalePrice
                })
                .OrderBy(p => p.Name)
                .ToList();

            SuppliersList = await _context.Suppliers
                .Where(s => s.IsActive)
                .OrderBy(s => s.SupplierId == 1 ? 0 : 1)
                .ThenBy(s => s.Name)
                .ToListAsync();
        }

        private void ProcessDateFilters()
        {
            var now = DateTime.Now;

            // Custom: StartDate/EndDate لە query string دێت
            if (FilterDateRange == "Custom")
            {
                // StartDate سەرەتای ئەو ڕۆژە
                if (StartDate.HasValue)
                    StartDate = StartDate.Value.Date;
                // EndDate کۆتایی ئەو ڕۆژە (23:59:59)
                if (EndDate.HasValue)
                    EndDate = EndDate.Value.Date.AddDays(1).AddTicks(-1);
                // ئەگەر تەنها StartDate هەبوو، EndDate = کۆتایی ئەو ڕۆژە
                else if (StartDate.HasValue)
                    EndDate = StartDate.Value.Date.AddDays(1).AddTicks(-1);
                return;
            }

            // ئەم هەفتەیە: دووشەممەی ئێستا → ئێستا کۆتایی ڕۆژ
            // DayOfWeek: Sunday=0, Monday=1, ..., Saturday=6
            int dow = (int)now.DayOfWeek;
            int daysFromMonday = (dow == 0) ? 6 : dow - 1; // دووشەممە = 0
            DateTime weekStart = now.Date.AddDays(-daysFromMonday);
            DateTime weekEnd = weekStart.AddDays(7).AddTicks(-1);

            (StartDate, EndDate) = FilterDateRange switch
            {
                "Today" => (now.Date, now.Date.AddDays(1).AddTicks(-1)),
                "Week" => (weekStart, weekEnd),
                "Month" => (new DateTime(now.Year, now.Month, 1),
                            new DateTime(now.Year, now.Month, 1).AddMonths(1).AddTicks(-1)),
                _ => ((DateTime?)null, (DateTime?)null)
            };
        }

        private async Task LoadInventoryData()
        {
            var products = await _context.Products.Where(p => p.IsActive).ToListAsync();
            var list = products.Select(p => new InventoryStatusViewModel
            {
                ProductId = p.ProductId,
                ProductName = p.Name,
                Barcode = p.Barcode,
                SalePrice = p.SalePrice,
                AveragePurchasePrice = p.LastPurchasePrice,  // Moving Average Cost
                CurrentStock = p.CurrentStock
            }).ToList();

            if (!string.IsNullOrEmpty(StockFilter))
            {
                list = StockFilter switch
                {
                    "OutOfStock" => list.Where(i => i.CurrentStock <= 0).ToList(),
                    "LowStock" => list.Where(i => i.CurrentStock > 0 && i.CurrentStock < 5).ToList(),
                    "InStock" => list.Where(i => i.CurrentStock >= 5).ToList(),
                    _ => list
                };
            }

            if (!string.IsNullOrEmpty(SearchProduct))
            {
                list = list.Where(i =>
                    i.ProductName.Contains(SearchProduct, StringComparison.OrdinalIgnoreCase) ||
                    i.Barcode.Contains(SearchProduct, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            InventoryList = list.OrderBy(i => i.CurrentStock).ToList();
            // TotalProducts لە OnGetAsync بەپێی فلتەر حسابکراوە — لێرە overwrite ناکرێت
            LowStockCount = list.Count(i => i.CurrentStock > 0 && i.CurrentStock < 5);
            OutOfStockCount = list.Count(i => i.CurrentStock <= 0);
            TotalStockValue = list.Sum(i => i.CurrentStock * i.AveragePurchasePrice);
        }

        // ═══════════════════════════════════════════════════════
        // گەڕان بە باڕکۆد
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostBarcodeAsync(string barcode)
        {
            var p = await _context.Products
                .FirstOrDefaultAsync(x => x.Barcode == barcode || x.Name.Contains(barcode));

            if (p == null)
                return new JsonResult(new { success = false, message = "نەدۆزرایەوە" });

            return new JsonResult(new
            {
                success = true,
                product = new
                {
                    productId = p.ProductId,
                    name = p.Name,
                    unitPrice = 0,
                    currentStock = p.CurrentStock,
                    salePrice = p.SalePrice,
                    wholesalePrice = p.WholesalePrice,
                    qtyPerCarton = p.QtyPerCarton,
                    qtyPerDozen = p.QtyPerDozen,
                    qtyPerBundle = p.QtyPerBundle
                }
            });
        }

        // ═══════════════════════════════════════════════════════
        // وەرگرتنی زانیاری فاتوورە بۆ دەستکاری
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetGetPurchaseAsync(int id)
        {
            var p = await _context.Purchases
                .Include(x => x.Supplier)
                .Include(x => x.PurchaseItems!)
                .ThenInclude(pi => pi.Product)
                .FirstOrDefaultAsync(x => x.PurchaseId == id);

            if (p == null)
                return new JsonResult(new { success = false });

            return new JsonResult(new
            {
                success = true,
                purchaseId = p.PurchaseId,
                purchaseDate = p.PurchaseDate.ToString("yyyy-MM-ddTHH:mm"),
                supplierId = p.SupplierId,
                supplierName = p.Supplier?.Name ?? "دیارینەکراو",
                items = p.PurchaseItems!.Select(i => new
                {
                    productId = i.ProductId,
                    name = i.Product?.Name,
                    unitPrice = i.UnitPrice,
                    quantity = i.Quantity,
                    currentStock = i.Product?.CurrentStock ?? 0,
                    qtyPerCarton = i.Product?.QtyPerCarton ?? 1,
                    qtyPerDozen = i.Product?.QtyPerDozen ?? 1,
                    qtyPerBundle = i.Product?.QtyPerBundle ?? 1
                })
            });
        }

        // ═══════════════════════════════════════════════════════
        // زیادکردنی فاتوورەی نوێ - بە Transaction و Moving Average
        // ═══════════════════════════════════════════════════════
        [BindProperty] public int NewPurchaseSupplierId { get; set; } = 1;
        public List<Supplier> SuppliersList { get; set; } = new();

        public async Task<IActionResult> OnPostAddPurchaseAsync()
        {
            if (string.IsNullOrEmpty(ItemsJson))
                return new JsonResult(new { success = false, message = "لیستە بەتاڵە" });

            var items = JsonSerializer.Deserialize<List<PurchaseItemInput>>(ItemsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (items == null || !items.Any())
                return new JsonResult(new { success = false, message = "لیستە بەتاڵە" });

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var userName = User.Identity?.Name ?? "System";

                // بارکردنی کاڵاکان بەیەکجار (N+1 Fix)
                var productIds = items.Select(i => i.ProductId).Distinct().ToList();
                var products = await _context.Products
                    .Where(p => productIds.Contains(p.ProductId))
                    .ToDictionaryAsync(p => p.ProductId);

                // بەروار و کاتی داخڵکراو parse بکە، ئەگەر نەبوو DateTime.Now
                var purchaseDt = DateTime.Now;
                if (NewPurchase.PurchaseDate != default)
                {
                    // چرکەی ئێستا زیاد بکە بۆ ئەوەی دوو پسوڵەی هەمان خولەک جیا بن
                    var dt = NewPurchase.PurchaseDate;
                    var now = DateTime.Now;
                    purchaseDt = new DateTime(dt.Year, dt.Month, dt.Day,
                                             dt.Hour, dt.Minute, now.Second,
                                             now.Millisecond);
                }

                var purchase = new Purchase
                {
                    PurchaseDate = purchaseDt,
                    SupplierId = NewPurchaseSupplierId > 0 ? NewPurchaseSupplierId : 1,
                    TotalAmount = items.Sum(i => i.Quantity * i.UnitPrice),
                    PurchaseItems = items.Select(i => new PurchaseItem
                    {
                        ProductId = i.ProductId,
                        Quantity = i.Quantity,
                        UnitPrice = i.UnitPrice
                    }).ToList()
                };

                _context.Purchases.Add(purchase);
                await _context.SaveChangesAsync();

                // ═══════════════════════════════════════════════════════
                // Moving Average: نوێکردنەوەی تێچوو بەپێی فۆرمولا
                // Cost_new = ((Q_old × P_old) + (Q_purchase × P_purchase)) / (Q_old + Q_purchase)
                // ═══════════════════════════════════════════════════════
                foreach (var item in items)
                {
                    if (products.TryGetValue(item.ProductId, out var product))
                    {
                        int stockBefore = product.CurrentStock;

                        // Moving Average Cost
                        CostHelper.AddPurchaseStock(product, item.Quantity, item.UnitPrice);

                        // Audit Log
                        _context.StockAdjustments.Add(new StockAdjustment
                        {
                            ProductId = item.ProductId,
                            AdjustmentQuantity = item.Quantity,
                            StockBefore = stockBefore,
                            StockAfter = product.CurrentStock,
                            AdjustmentDate = purchaseDt,
                            PurchaseId = purchase.PurchaseId,
                            AdjustmentType = $"فاتوورەی کڕین #{purchase.PurchaseId}",
                            Reason = $"کڕین: {item.Quantity} × {item.UnitPrice:N0}",
                            UserName = userName
                        });
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                await AuditHelper.PurchaseCreated(_context, User, HttpContext, purchase.PurchaseId, purchase.TotalAmount);

                // قاسە — تەنها کاتێک پارەدان لە قاسە هەڵبژێردراوە
                if (PayFromCash == "true")
                {
                    var uid = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    await CashHelper.PurchaseOut(_context, purchase.PurchaseId, purchase.TotalAmount, uid, User.Identity?.Name);
                }
                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new JsonResult(new { success = false, message = "هەڵە: " + ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════
        // دەستکاری فاتوورە — Recalculate from Point of Change
        //
        // ئەلگۆریزم:
        // ١. هەموو کڕین/فرۆشتنەکانی پێش ئەو پسوڵەیە Replay دەکرێن
        //    بۆ بەدەستهێنانی MAC و ستۆکی Snapshot
        // ٢. پسوڵەی نوێ Apply دەکرێت
        // ٣. هەموو تراکنزیکشنەکانی دواتر Recalculate دەکرێن
        //    کڕین: MAC نوێ | فرۆشتن: تەنها ستۆک (COGS ناگۆڕێت)
        // ═══════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostEditPurchaseAsync()
        {
            if (string.IsNullOrEmpty(ItemsJson))
                return new JsonResult(new { success = false, message = "لیستە بەتاڵە" });

            var existing = await _context.Purchases
                .Include(p => p.Supplier)
                .Include(p => p.PurchaseItems!)
                .FirstOrDefaultAsync(p => p.PurchaseId == EditPurchaseId);

            if (existing == null)
                return new JsonResult(new { success = false, message = "فاتوورە نەدۆزرایەوە" });

            var newItems = JsonSerializer.Deserialize<List<PurchaseItemInput>>(ItemsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (newItems == null || !newItems.Any())
                return new JsonResult(new { success = false, message = "لیستە بەتاڵە" });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var userName = User.Identity?.Name ?? "System";
                var editDate = existing.PurchaseDate;
                var newDate = editDate; // بەروار هەرگیز نەگۆڕێت

                // ═══ ١. هەموو کاڵاکانی پەیوەندیدار ══════════════════
                var affectedProductIds = newItems.Select(i => i.ProductId)
                    .Union(existing.PurchaseItems!.Select(o => o.ProductId))
                    .Distinct().ToList();

                var products = await _context.Products
                    .Where(p => affectedProductIds.Contains(p.ProductId))
                    .ToDictionaryAsync(p => p.ProductId);

                // ═══ ٢. Audit Logـی کۆن بسڕەوە ══════════════════════
                var oldAuditLogs = await _context.StockAdjustments
                    .Where(a => a.PurchaseId == EditPurchaseId)
                    .ToListAsync();
                _context.StockAdjustments.RemoveRange(oldAuditLogs);

                // ═══ ٣. هەموو تراکنزیکشنەکان یەکجار لۆد بکە ════════
                var allPurchases = await _context.Purchases
                    .Include(p => p.Supplier)
                .Include(p => p.PurchaseItems!)
                    .Where(p => p.PurchaseItems!.Any(pi =>
                        affectedProductIds.Contains(pi.ProductId)))
                    .OrderBy(p => p.PurchaseDate).ThenBy(p => p.PurchaseId)
                    .ToListAsync();

                var allSales = await _context.Sales
                    .Include(s => s.SaleItems!)
                    .Where(s => s.SaleItems!.Any(si =>
                        affectedProductIds.Contains(si.ProductId)))
                    .OrderBy(s => s.SaleDate).ThenBy(s => s.SaleId)
                    .ToListAsync();

                // ڕاستکردنەوە دەستییەکانی کۆگا (نەک Recalc)
                var allAdjustments = await _context.StockAdjustments
                    .Where(a => affectedProductIds.Contains(a.ProductId) && a.PurchaseId == null)
                    .ToListAsync();

                // ═══ ٤. Strict Chronological Timeline ════════════════
                // before: تراکنزیکشنەکانی پێش editDate → Snapshot
                // after:  تراکنزیکشنەکانی دوای editDate → MAC + COGS نوێ
                var beforeEvents = BuildTimeline(
                    allPurchases.Where(p =>
                        p.PurchaseId != EditPurchaseId &&
                        (p.PurchaseDate < editDate ||
                         (p.PurchaseDate == editDate && p.PurchaseId < EditPurchaseId))),
                    allSales.Where(s => s.SaleDate < editDate),
                    allAdjustments.Where(a => a.AdjustmentDate < editDate));

                var afterEvents = BuildTimeline(
                    allPurchases.Where(p =>
                        p.PurchaseId != EditPurchaseId &&
                        (p.PurchaseDate > editDate ||
                         (p.PurchaseDate == editDate && p.PurchaseId > EditPurchaseId))),
                    allSales.Where(s => s.SaleDate >= editDate),
                    allAdjustments.Where(a => a.AdjustmentDate >= editDate));

                // ═══ ٥. هەر کاڵا: Snapshot → Apply → Recalc ═════════
                foreach (var prod in products.Values)
                {
                    int stock = 0;
                    decimal mac = 0;

                    // ─── beforeEvents → Snapshot ───────────────────────
                    foreach (var ev in beforeEvents)
                    {
                        if (ev.IsPurchase)
                        {
                            var pi = ev.Purchase!.PurchaseItems!
                                .FirstOrDefault(x => x.ProductId == prod.ProductId);
                            if (pi == null) continue;

                            if (stock <= 0) { mac = pi.UnitPrice; stock = pi.Quantity; }
                            else
                            {
                                mac = Math.Round((stock * mac + pi.Quantity * pi.UnitPrice)
                                                  / (stock + pi.Quantity), 2);
                                stock += pi.Quantity;
                            }
                        }
                        else if (ev.Adjustment != null)
                        {
                            if (ev.Adjustment.ProductId == prod.ProductId)
                            {
                                if (ev.Adjustment.IsInventoryReset)
                                {
                                    // جەردکردنی کۆگا: ڕیسێت بکە بۆ بەهای جەرد
                                    stock = ev.Adjustment.StockAfter;
                                    mac = ev.Adjustment.ResetPrice;
                                }
                                else
                                {
                                    stock += ev.Adjustment.AdjustmentQuantity;
                                }
                            }
                        }
                        else
                        {
                            if (!ev.Sale!.IsVoided)
                            {
                                var si = ev.Sale.SaleItems!
                                    .FirstOrDefault(x => x.ProductId == prod.ProductId);
                                if (si != null)
                                    stock -= si.Quantity; // سالب ڕێدەدرێت بۆ validation
                            }
                        }
                    }

                    // Snapshot دانە
                    prod.CurrentStock = Math.Max(0, stock);
                    prod.LastPurchasePrice = mac;

                    // ─── پسوڵەی نوێ Apply (کڕینی ئیدیتکراو) ──────────
                    var ni = newItems.FirstOrDefault(x => x.ProductId == prod.ProductId);
                    if (ni != null)
                    {
                        int sb = prod.CurrentStock;
                        CostHelper.AddPurchaseStock(prod, ni.Quantity, ni.UnitPrice);
                        _context.StockAdjustments.Add(new StockAdjustment
                        {
                            ProductId = ni.ProductId,
                            AdjustmentQuantity = ni.Quantity,
                            StockBefore = sb,
                            StockAfter = prod.CurrentStock,
                            AdjustmentDate = newDate,
                            PurchaseId = EditPurchaseId,
                            AdjustmentType = $"فاتوورەی کڕین #{EditPurchaseId}",
                            Reason = $"کڕین: {ni.Quantity} × {ni.UnitPrice:N0}",
                            UserName = userName
                        });
                    }

                    // ─── afterEvents: MAC + COGS نوێبکەوە ─────────────
                    // سەرەتا ببینە ئایا Reset Point هەیە بۆ ئەم کاڵایە
                    var hasResetPointEdit = afterEvents.Any(e =>
                        e.Adjustment != null &&
                        e.Adjustment.ProductId == prod.ProductId &&
                        e.Adjustment.IsInventoryReset);

                    bool resetAppliedEdit = false;

                    foreach (var ev in afterEvents)
                    {
                        if (ev.IsPurchase)
                        {
                            var pi = ev.Purchase!.PurchaseItems!
                                .FirstOrDefault(x => x.ProductId == prod.ProductId);
                            if (pi == null) continue;

                            if (hasResetPointEdit && !resetAppliedEdit) continue;

                            int sb = prod.CurrentStock;
                            CostHelper.AddPurchaseStock(prod, pi.Quantity, pi.UnitPrice);
                            _context.StockAdjustments.Add(new StockAdjustment
                            {
                                ProductId = pi.ProductId,
                                AdjustmentQuantity = pi.Quantity,
                                StockBefore = sb,
                                StockAfter = prod.CurrentStock,
                                AdjustmentDate = ev.Purchase!.PurchaseDate,
                                PurchaseId = ev.Purchase.PurchaseId,
                                AdjustmentType = $"فاتوورەی کڕین #{ev.Purchase.PurchaseId} (Recalc)",
                                Reason = $"کڕین: {pi.Quantity} × {pi.UnitPrice:N0}",
                                UserName = userName
                            });
                        }
                        else if (ev.Adjustment != null)
                        {
                            if (ev.Adjustment.ProductId == prod.ProductId)
                            {
                                if (ev.Adjustment.IsInventoryReset)
                                {
                                    prod.CurrentStock = ev.Adjustment.StockAfter;
                                    prod.LastPurchasePrice = ev.Adjustment.ResetPrice;
                                    resetAppliedEdit = true;
                                }
                                else
                                {
                                    if (!hasResetPointEdit || resetAppliedEdit)
                                        prod.CurrentStock += ev.Adjustment.AdjustmentQuantity;
                                }
                            }
                        }
                        else
                        {
                            var si = ev.Sale!.SaleItems!
                                .FirstOrDefault(x => x.ProductId == prod.ProductId);
                            if (si == null) continue;

                            if (hasResetPointEdit && !resetAppliedEdit) continue;

                            if (!ev.Sale.IsVoided)
                            {
                                // دیواری پاراستن: ستۆکی سالب ڕێگری بکە
                                if (prod.CurrentStock - si.Quantity < 0)
                                {
                                    await transaction.RollbackAsync();
                                    return new JsonResult(new
                                    {
                                        success = false,
                                        message = "ناتوانیت بڕی ئەم کڕینە کەم بکەیتەوە، چونکە بڕی پێویست لە کۆگادا نامێنێت بۆ ئەو فرۆشتنانەی دوای ئەم پسوڵەیە کراون."
                                    });
                                }

                                decimal currentMac = prod.LastPurchasePrice;
                                decimal oldItemProfit = si.Quantity * (si.UnitPrice - si.UnitPurchasePrice);
                                decimal newItemProfit = si.Quantity * (si.UnitPrice - currentMac);
                                si.UnitPurchasePrice = currentMac;
                                ev.Sale.TotalProfit = ev.Sale.TotalProfit - oldItemProfit + newItemProfit;
                                CostHelper.RemoveStock(prod, si.Quantity);
                            }
                        }
                    }
                }

                // ═══ ٦. پسوڵەکە نوێبکەوە ════════════════════════════
                _context.PurchaseItems.RemoveRange(existing.PurchaseItems!);
                existing.PurchaseDate = newDate;
                existing.SupplierId = NewPurchaseSupplierId > 0 ? NewPurchaseSupplierId : 1;
                existing.TotalAmount = newItems.Sum(i => i.Quantity * i.UnitPrice);
                existing.PurchaseItems = newItems.Select(i => new PurchaseItem
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice
                }).ToList();

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                await AuditHelper.PurchaseEdited(_context, User, HttpContext, EditPurchaseId, $"کۆی: {existing.TotalAmount:N0}");
                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new JsonResult(new { success = false, message = "هەڵە: " + ex.Message });
            }
        }

        public async Task<IActionResult> OnPostDeletePurchaseAsync()
        {
            var existing = await _context.Purchases
                .Include(x => x.PurchaseItems!)
                .FirstOrDefaultAsync(x => x.PurchaseId == DeletePurchaseId);

            if (existing == null)
                return new JsonResult(new { success = false, message = "فاتوورە نەدۆزرایەوە" });

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var userName = User.Identity?.Name ?? "System";
                var deleteDate = existing.PurchaseDate;

                // ═══ ١. کاڵاکانی پەیوەندیدار ════════════════════════
                var affectedProductIds = existing.PurchaseItems!
                    .Select(i => i.ProductId).Distinct().ToList();

                var products = await _context.Products
                    .Where(pr => affectedProductIds.Contains(pr.ProductId))
                    .ToDictionaryAsync(pr => pr.ProductId);

                // ═══ ٢. Audit Logـی کۆن بسڕەوە ══════════════════════
                var oldAuditLogs = await _context.StockAdjustments
                    .Where(a => a.PurchaseId == DeletePurchaseId)
                    .ToListAsync();
                _context.StockAdjustments.RemoveRange(oldAuditLogs);

                // ═══ ٣. هەموو تراکنزیکشنەکان یەکجار لۆد بکە ════════
                var allPurchases = await _context.Purchases
                    .Include(p => p.Supplier)
                .Include(p => p.PurchaseItems!)
                    .Where(p => p.PurchaseItems!.Any(pi =>
                        affectedProductIds.Contains(pi.ProductId)))
                    .OrderBy(p => p.PurchaseDate).ThenBy(p => p.PurchaseId)
                    .ToListAsync();

                var allSales = await _context.Sales
                    .Include(s => s.SaleItems!)
                    .Where(s => s.SaleItems!.Any(si =>
                        affectedProductIds.Contains(si.ProductId)))
                    .OrderBy(s => s.SaleDate).ThenBy(s => s.SaleId)
                    .ToListAsync();

                // ڕاستکردنەوە دەستییەکانی کۆگا (نەک Recalc)
                var allAdjustments = await _context.StockAdjustments
                    .Where(a => affectedProductIds.Contains(a.ProductId) && a.PurchaseId == null)
                    .ToListAsync();

                // ═══ ٤. Strict Chronological Timeline ════════════════
                // before: تراکنزیکشنەکانی پێش deleteDate → Snapshot
                // after:  تراکنزیکشنەکانی دوای deleteDate → MAC + COGS نوێ
                var beforeEvents = BuildTimeline(
                    allPurchases.Where(p =>
                        p.PurchaseId != DeletePurchaseId &&
                        (p.PurchaseDate < deleteDate ||
                         (p.PurchaseDate == deleteDate && p.PurchaseId < DeletePurchaseId))),
                    allSales.Where(s => s.SaleDate < deleteDate),
                    allAdjustments.Where(a => a.AdjustmentDate < deleteDate));

                var afterEvents = BuildTimeline(
                    allPurchases.Where(p =>
                        p.PurchaseId != DeletePurchaseId &&
                        (p.PurchaseDate > deleteDate ||
                         (p.PurchaseDate == deleteDate && p.PurchaseId > DeletePurchaseId))),
                    allSales.Where(s => s.SaleDate >= deleteDate),
                    allAdjustments.Where(a => a.AdjustmentDate >= deleteDate));

                // ═══ ٥. هەر کاڵا: Snapshot → Recalc ══════════════════
                foreach (var prod in products.Values)
                {
                    int stock = 0;
                    decimal mac = 0;

                    // ─── beforeEvents → Snapshot (بەبێ پسوڵەی سڕاوە) ──
                    foreach (var ev in beforeEvents)
                    {
                        if (ev.IsPurchase)
                        {
                            var pi = ev.Purchase!.PurchaseItems!
                                .FirstOrDefault(x => x.ProductId == prod.ProductId);
                            if (pi == null) continue;

                            if (stock <= 0) { mac = pi.UnitPrice; stock = pi.Quantity; }
                            else
                            {
                                mac = Math.Round((stock * mac + pi.Quantity * pi.UnitPrice)
                                                  / (stock + pi.Quantity), 2);
                                stock += pi.Quantity;
                            }
                        }
                        else if (ev.Adjustment != null)
                        {
                            if (ev.Adjustment.ProductId == prod.ProductId)
                            {
                                if (ev.Adjustment.IsInventoryReset)
                                {
                                    stock = ev.Adjustment.StockAfter;
                                    mac = ev.Adjustment.ResetPrice;
                                }
                                else
                                {
                                    stock += ev.Adjustment.AdjustmentQuantity;
                                }
                            }
                        }
                        else
                        {
                            if (!ev.Sale!.IsVoided)
                            {
                                var si = ev.Sale.SaleItems!
                                    .FirstOrDefault(x => x.ProductId == prod.ProductId);
                                if (si != null)
                                    stock = Math.Max(0, stock - si.Quantity);
                            }
                        }
                    }

                    // Snapshot دانە (بەبێ پسوڵەی سڕاوە)
                    prod.CurrentStock = stock;
                    prod.LastPurchasePrice = mac;

                    // ─── afterEvents: MAC + COGS نوێبکەوە ─────────────
                    // ─── afterEvents: MAC + COGS نوێبکەوە ─────────────
                    // سەرەتا ببینە ئایا Reset Point هەیە بۆ ئەم کاڵایە
                    var hasResetPoint = afterEvents.Any(e =>
                        e.Adjustment != null &&
                        e.Adjustment.ProductId == prod.ProductId &&
                        e.Adjustment.IsInventoryReset);

                    bool resetApplied = false;

                    foreach (var ev in afterEvents)
                    {
                        if (ev.IsPurchase)
                        {
                            var pi = ev.Purchase!.PurchaseItems!
                                .FirstOrDefault(x => x.ProductId == prod.ProductId);
                            if (pi == null) continue;

                            // ئەگەر Reset Point هەیە و هێشتا نەگەیشتووین، ئەم کڕینە skip بکە
                            if (hasResetPoint && !resetApplied) continue;

                            int sb = prod.CurrentStock;
                            CostHelper.AddPurchaseStock(prod, pi.Quantity, pi.UnitPrice);
                            _context.StockAdjustments.Add(new StockAdjustment
                            {
                                ProductId = pi.ProductId,
                                AdjustmentQuantity = pi.Quantity,
                                StockBefore = sb,
                                StockAfter = prod.CurrentStock,
                                AdjustmentDate = ev.Purchase!.PurchaseDate,
                                PurchaseId = ev.Purchase.PurchaseId,
                                AdjustmentType = $"فاتوورەی کڕین #{ev.Purchase.PurchaseId} (Recalc)",
                                Reason = $"کڕین: {pi.Quantity} × {pi.UnitPrice:N0}",
                                UserName = userName
                            });
                        }
                        else if (ev.Adjustment != null)
                        {
                            if (ev.Adjustment.ProductId == prod.ProductId)
                            {
                                if (ev.Adjustment.IsInventoryReset)
                                {
                                    prod.CurrentStock = ev.Adjustment.StockAfter;
                                    prod.LastPurchasePrice = ev.Adjustment.ResetPrice;
                                    resetApplied = true;
                                }
                                else
                                {
                                    if (!hasResetPoint || resetApplied)
                                        prod.CurrentStock += ev.Adjustment.AdjustmentQuantity;
                                }
                            }
                        }
                        else
                        {
                            var si = ev.Sale!.SaleItems!
                                .FirstOrDefault(x => x.ProductId == prod.ProductId);
                            if (si == null) continue;

                            // ئەگەر Reset Point هەیە و هێشتا نەگەیشتووین، ئەم فرۆشتنە skip بکە
                            if (hasResetPoint && !resetApplied) continue;

                            if (!ev.Sale.IsVoided)
                            {
                                // دیواری پاراستن: سڕینەوەکە دەبێتە هۆی سالب بوونی ستۆک؟
                                if (prod.CurrentStock - si.Quantity < 0)
                                {
                                    await transaction.RollbackAsync();
                                    return new JsonResult(new
                                    {
                                        success = false,
                                        message = "ناتوانیت ئەم کڕینە بسڕیتەوە، چونکە دەبێتە هۆی سالب بوونی ستۆک بۆ فرۆشتنەکانی دواتر."
                                    });
                                }

                                decimal currentMac = prod.LastPurchasePrice;
                                decimal oldItemProfit = si.Quantity * (si.UnitPrice - si.UnitPurchasePrice);
                                decimal newItemProfit = si.Quantity * (si.UnitPrice - currentMac);
                                si.UnitPurchasePrice = currentMac;
                                ev.Sale.TotalProfit = ev.Sale.TotalProfit - oldItemProfit + newItemProfit;
                                CostHelper.RemoveStock(prod, si.Quantity);
                            }
                        }
                    }
                }

                // ═══ ٦. پسوڵەکە بسڕەوە ══════════════════════════════
                var delPurchaseId = existing.PurchaseId;
                var delTotalAmount = existing.TotalAmount;
                _context.PurchaseItems.RemoveRange(existing.PurchaseItems!);
                _context.Purchases.Remove(existing);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                await AuditHelper.PurchaseDeleted(_context, User, HttpContext, delPurchaseId, delTotalAmount);
                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new JsonResult(new { success = false, message = "هەڵە: " + ex.Message });
            }
        }

        // ─── Helper: Timeline Replay ──────────────────────────────────
        private static List<TimelineEvent> BuildTimeline(
            IEnumerable<Purchase> purchases,
            IEnumerable<Sale> sales,
            IEnumerable<StockAdjustment>? adjustments = null)
        {
            var list = new List<TimelineEvent>();
            foreach (var p in purchases)
                list.Add(new TimelineEvent(p.PurchaseDate, p.PurchaseId, true, p, null));
            foreach (var s in sales)
                list.Add(new TimelineEvent(s.SaleDate, s.SaleId, false, null, s));
            // ڕاستکردنەوە دەستییەکانی کۆگا (نەک Recalc)
            if (adjustments != null)
                foreach (var a in adjustments)
                    list.Add(new TimelineEvent(a.AdjustmentDate, a.Id, false, null, null, a));
            return list.OrderBy(e => e.Date).ThenBy(e => e.Id).ToList();
        }

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

            // ڕاستکردنەوە تەنها ستۆک دەگۆڕێت، تێچوو ناگۆڕێت
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

            // ئەگەر هۆکار نەنووسرابوو، دیفۆڵت دابنێ
            if (string.IsNullOrWhiteSpace(AdjustmentInput.Reason))
            {
                AdjustmentInput.Reason = AdjustmentInput.AdjustmentQuantity > 0 ? "زیادکردنی ستۆک" : "کەمکردنەوەی ستۆک";
            }

            _context.StockAdjustments.Add(AdjustmentInput);
            await _context.SaveChangesAsync();

            return RedirectToPage();
        }

        // ═══════════════════════════════════════════════════════
        // زیادکردنی کاڵای نوێ لەناو مۆدالی کڕین
        // ═══════════════════════════════════════════════════════
        [BindProperty] public string? NewProductName { get; set; }
        [BindProperty] public string? NewProductBarcode { get; set; }
        [BindProperty] public decimal NewProductSalePrice { get; set; }
        [BindProperty] public decimal NewProductWholesalePrice { get; set; }
        [BindProperty] public decimal NewProductBuyPrice { get; set; }
        [BindProperty] public int NewProductCarton { get; set; } = 1;
        [BindProperty] public int NewProductDozen { get; set; } = 1;
        [BindProperty] public int NewProductBundle { get; set; } = 1;

        public async Task<IActionResult> OnPostAddNewProductAsync()
        {
            if (string.IsNullOrWhiteSpace(NewProductName))
                return new JsonResult(new { success = false, message = "ناوی کاڵا پێویستە" });

            // پشکنینی ناوی دووبارە
            var exists = await _context.Products
                .AnyAsync(p => p.Name == NewProductName);
            if (exists)
                return new JsonResult(new { success = false, message = "کاڵایەکی بەم ناوە پێشتر تۆمارکراوە" });

            // باڕکۆدی دووبارە
            if (!string.IsNullOrWhiteSpace(NewProductBarcode))
            {
                var dupBarcode = await _context.Products
                    .AnyAsync(p => p.Barcode == NewProductBarcode);
                if (dupBarcode)
                    return new JsonResult(new { success = false, message = "ئەم باڕکۆدە پێشتر تۆمارکراوە" });
            }

            var product = new Product
            {
                Name = NewProductName,
                Barcode = string.IsNullOrWhiteSpace(NewProductBarcode)
                    ? Guid.NewGuid().ToString("N")[..8].ToUpper()
                    : NewProductBarcode,
                SalePrice = NewProductSalePrice,
                WholesalePrice = NewProductWholesalePrice,
                LastPurchasePrice = NewProductBuyPrice,
                CurrentStock = 0,
                QtyPerCarton = Math.Max(1, NewProductCarton),
                QtyPerDozen = Math.Max(1, NewProductDozen),
                QtyPerBundle = Math.Max(1, NewProductBundle),
                IsActive = true
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            return new JsonResult(new
            {
                success = true,
                productId = product.ProductId,
                name = product.Name
            });
        }
    }

    // ═══════════════════════════════════════════════════════
    // Helper Record بۆ Timeline
    // ═══════════════════════════════════════════════════════
    public record TimelineEvent(DateTime Date, int Id, bool IsPurchase, Purchase? Purchase, Sale? Sale, StockAdjustment? Adjustment = null);

    // ═══════════════════════════════════════════════════════
    // ViewModels
    // ═══════════════════════════════════════════════════════
    public class PurchaseListViewModel
    {
        public int PurchaseId { get; set; }
        public DateTime PurchaseDate { get; set; }
        public decimal TotalAmount { get; set; }
        public int ItemCount { get; set; }
        public int TotalQuantity { get; set; }
        public int SupplierId { get; set; }
        public string SupplierName { get; set; } = "";
        public List<PurchaseItemViewModel> Items { get; set; } = new();
    }

    public class PurchaseItemViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public string Barcode { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }
        public int QtyPerCarton { get; set; }
        public int QtyPerDozen { get; set; }
        public int QtyPerBundle { get; set; }
    }

    public class ProductForPurchaseViewModel
    {
        public int ProductId { get; set; }
        public string Name { get; set; } = "";
        public string Barcode { get; set; } = "";
        public int CurrentStock { get; set; }
        public int QtyPerCarton { get; set; }
        public int QtyPerDozen { get; set; }
        public int QtyPerBundle { get; set; }
        public decimal SalePrice { get; set; }
        public decimal WholesalePrice { get; set; }
    }

    public class PurchaseItemInput
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}