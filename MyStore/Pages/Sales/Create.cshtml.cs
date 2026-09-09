using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;
using MyStore.Helpers;
using System.Text.Json;

namespace MyStore.Pages.Sales
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IPermissionService _permissionService;

        public CreateModel(ApplicationDbContext context, UserManager<IdentityUser> userManager, IPermissionService permissionService)
        {
            _context = context;
            _userManager = userManager;
            _permissionService = permissionService;
        }

        [BindProperty]
        public Sale SingleSale { get; set; } = new Sale();

        [BindProperty(SupportsGet = true)]
        public string? ItemsJson { get; set; }

        public int NextInvoiceId { get; set; }
        public int DefaultCustomerId { get; set; }
        public string DefaultCustomerName { get; set; } = "ڕاستەوخۆ";
        public string StoreName { get; set; } = "MyStore";
        public string? StoreLogo { get; set; }

        public string? UserProfileImage { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? SaleId { get; set; }

        // ═══════════════════════════════════════════════════════════════
        // Timeline Replay — پاراستنی لۆجیکی حیسابکردنی تێچوو (MAC)
        // ═══════════════════════════════════════════════════════════════
        private record TimelineEvent(DateTime Date, int Id, bool IsPurchase, Purchase? Purchase, Sale? Sale, StockAdjustment? Adjustment = null);

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

            if (adjustments != null)
                foreach (var a in adjustments.Where(a => a.PurchaseId == null))
                    list.Add(new TimelineEvent(a.AdjustmentDate, a.Id, false, null, null, a));
            return list.OrderBy(e => e.Date).ThenBy(e => e.Id).ToList();
        }

        // ═══════════════════════════════════════════════════════════════
        // OnGet
        // ═══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGet()
        {
            SingleSale.SaleDate = DateTime.Now;
            var maxId = await _context.Sales.MaxAsync(s => (int?)s.SaleId) ?? 0;
            NextInvoiceId = maxId + 1;

            var defaultCustomer = await _context.Customers.FirstOrDefaultAsync(c => c.Name == "ڕاستەوخۆ");
            if (defaultCustomer == null)
            {
                defaultCustomer = new Customer { Name = "ڕاستەوخۆ", Phone = "-" };
                _context.Customers.Add(defaultCustomer);
                await _context.SaveChangesAsync();
            }

            DefaultCustomerId = defaultCustomer.CustomerId;
            DefaultCustomerName = defaultCustomer.Name;
            SingleSale.CustomerId = defaultCustomer.CustomerId;

            var settings = await _context.SystemSettings.ToListAsync();
            StoreName = settings.FirstOrDefault(s => s.Key == "StoreName")?.Value ?? "MyStore";
            StoreLogo = settings.FirstOrDefault(s => s.Key == "StoreLogo")?.Value;

            var userId = _userManager.GetUserId(User);
            if (!string.IsNullOrEmpty(userId))
            {
                var userSettings = await _context.Set<UserSettings>().FirstOrDefaultAsync(u => u.UserId == userId);
                UserProfileImage = userSettings?.ProfileImagePath;
            }

            return Page();
        }

        public async Task<IActionResult> OnGetStoreInfo()
        {
            var settings = await _context.SystemSettings.ToListAsync();
            return new JsonResult(new
            {
                storeName = settings.FirstOrDefault(s => s.Key == "StoreName")?.Value ?? "فرۆشگای MyStore",
                storePhone = settings.FirstOrDefault(s => s.Key == "StorePhone")?.Value ?? "",
                storeAddress = settings.FirstOrDefault(s => s.Key == "StoreAddress")?.Value ?? "",
                storeLogo = settings.FirstOrDefault(s => s.Key == "StoreLogo")?.Value ?? ""
            });
        }

        public async Task<IActionResult> OnGetSearchProducts(string? term)
        {
            var query = _context.Products.Where(p => p.CurrentStock > 0);

            if (!string.IsNullOrWhiteSpace(term))
                query = query.Where(p => p.Name.Contains(term) || p.Barcode.Contains(term));

            var products = await query.OrderBy(p => p.Name).Take(20)
              .Select(p => new {
                  productId = p.ProductId,
                  name = p.Name,
                  barcode = p.Barcode,
                  unitPrice = p.SalePrice,
                  wholesalePrice = p.WholesalePrice,
                  unitPurchasePrice = p.LastPurchasePrice,
                  currentStock = p.CurrentStock
              })
              .ToListAsync();

            return new JsonResult(new { success = true, products });
        }

        public async Task<IActionResult> OnGetSalesList(string? dateRange, int? customerId)
        {
            var userId = _userManager.GetUserId(User);
            var role = await _permissionService.GetUserRoleAsync(User);

            var query = _context.Sales
              .Include(s => s.Customer)
              .Where(s => !s.IsVoided)
              .AsQueryable();

            if (role == "Cashier")
                query = query.Where(s => s.UserId == userId);

            var now = DateTime.Now;
            switch (dateRange)
            {
                case "Today":
                    query = query.Where(s => s.SaleDate.Date == now.Date); break;
                case "Week":
                    query = query.Where(s => s.SaleDate >= now.Date.AddDays(-7)); break;
                case "Month":
                    query = query.Where(s => s.SaleDate >= new DateTime(now.Year, now.Month, 1)); break;
                case "All":
                    break;
            }

            if (customerId.HasValue && customerId > 0)
                query = query.Where(s => s.CustomerId == customerId);

            var sales = await query
              .OrderByDescending(s => s.SaleDate)
              .Take(100)
              .Select(s => new {
                  saleId = s.SaleId,
                  saleDate = s.SaleDate.ToString("yyyy-MM-dd HH:mm"),
                  customerName = s.Customer != null ? s.Customer.Name : "-",
                  grandTotal = s.GrandTotal,
                  paidAmount = s.PaidAmount,
                  debt = s.GrandTotal - s.PaidAmount,
                  paymentType = (int)s.PaymentType
              })
              .ToListAsync();

            return new JsonResult(new { success = true, sales, userRole = role });
        }

        public async Task<IActionResult> OnGetSearchCustomers(string? term)
        {
            var query = _context.Customers.AsQueryable();
            if (!string.IsNullOrWhiteSpace(term))
                query = query.Where(c => c.Name.Contains(term) || c.Phone.Contains(term));

            var customers = await query.OrderBy(c => c.CustomerId).Take(15)
                      .Select(c => new { id = c.CustomerId, text = c.Name, balance = c.Balance })
              .ToListAsync();

            return new JsonResult(new { results = customers });
        }

        public async Task<IActionResult> OnGetNavigateInvoice(int currentId, string direction)
        {
            var userId = _userManager.GetUserId(User);
            var role = await _permissionService.GetUserRoleAsync(User);

            int? targetId = null;
            var maxOverallId = await _context.Sales.MaxAsync(s => (int?)s.SaleId) ?? 0;
            if (currentId == 0) currentId = maxOverallId + 1;

            var query = _context.Sales.Where(s => !s.IsVoided);

            // --- خاڵی گرنگ: تەنها پسوڵەی خۆی ببینێت لە کاتی گەڕان بە دوگمەی پێشوو/دواتر ---
            if (role == "Cashier")
            {
                query = query.Where(s => s.UserId == userId);
            }

            if (direction == "prev")
            {
                targetId = await query
                  .Where(s => s.SaleId < currentId)
                  .OrderByDescending(s => s.SaleId).Select(s => s.SaleId).FirstOrDefaultAsync();
            }
            else
            {
                targetId = await query
                  .Where(s => s.SaleId > currentId)
                  .OrderBy(s => s.SaleId).Select(s => s.SaleId).FirstOrDefaultAsync();

                if (targetId == 0 || targetId == null)
                    return new JsonResult(new { success = true, isNew = true, nextId = maxOverallId + 1 });
            }

            if (targetId == null || targetId == 0)
                return new JsonResult(new { success = false, message = "پسوڵەی چالاکی تر نەدۆزرایەوە" });

            return await OnGetInvoiceData(targetId.Value);
        }

        public async Task<IActionResult> OnGetInvoiceData(int id)
        {
            var userId = _userManager.GetUserId(User);
            var role = await _permissionService.GetUserRoleAsync(User);

            var query = _context.Sales
              .Include(s => s.SaleItems!).ThenInclude(si => si.Product)
              .Include(s => s.Customer).AsNoTracking()
              .Where(s => s.SaleId == id && !s.IsVoided);

            // --- خاڵی گرنگ: تەنها پسوڵەی خۆی ببینێت لە کاتی گەڕان بە ژمارە ---
            if (role == "Cashier")
            {
                query = query.Where(s => s.UserId == userId);
            }

            var sale = await query.FirstOrDefaultAsync();

            if (sale == null)
                return new JsonResult(new { success = false, message = "پسوڵە نەدۆزرایەوە، پووچەڵکراوەتەوە، یان هی تۆ نییە" });

            var itemsList = sale.SaleItems!.Select(item => new
            {
                productId = item.ProductId,
                name = item.Product?.Name ?? "کاڵا",
                unitPrice = item.Product != null ? item.Product.SalePrice : item.UnitPrice,
                wholesalePrice = item.Product != null ? item.Product.WholesalePrice : item.UnitPrice,
                appliedPrice = item.UnitPrice,
                unitPurchasePrice = item.UnitPurchasePrice,
                quantity = item.Quantity,
                currentStock = (item.Product?.CurrentStock ?? 0) + item.Quantity
            }).ToList();

            decimal originalPaidAmount = sale.PaidAmount;

            return new JsonResult(new
            {
                success = true,
                saleId = sale.SaleId,
                saleDate = sale.SaleDate.ToString("yyyy-MM-dd"),
                customerId = sale.CustomerId,
                customerName = sale.Customer?.Name,
                customerBalance = sale.Customer?.Balance ?? 0,
                paymentType = (int)sale.PaymentType,
                paidAmount = originalPaidAmount,
                discount = sale.Discount,
                grandTotal = sale.GrandTotal,
                totalAmount = sale.TotalAmount,
                items = itemsList,
                isVoided = sale.IsVoided
            });
        }

        public async Task<IActionResult> OnGetCustomerBalanceAsync(int customerId)
        {
            var customer = await _context.Customers
              .Where(c => c.CustomerId == customerId)
              .Select(c => new { balance = c.Balance })
              .FirstOrDefaultAsync();

            return new JsonResult(new { success = true, balance = customer?.balance ?? 0 });
        }

        // ═══════════════════════════════════════════════════════════════
        // OnPostAsync 
        // ═══════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostAsync()
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var items = string.IsNullOrEmpty(ItemsJson) || ItemsJson == "[]"
                  ? new List<SaleItemDto>()
                  : JsonSerializer.Deserialize<List<SaleItemDto>>(ItemsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var userId = _userManager.GetUserId(User);
                var role = await _permissionService.GetUserRoleAsync(User);

                Sale? existingSale = null;
                if (SingleSale.SaleId > 0)
                {
                    var query = _context.Sales.Include(s => s.SaleItems).AsQueryable();

                    // --- خاڵی گرنگ: ڕێگری لە دەستکاریکردنی پسوڵەی کەسانی تر ---
                    if (role == "Cashier")
                    {
                        query = query.Where(s => s.UserId == userId);
                    }

                    existingSale = await query.FirstOrDefaultAsync(s => s.SaleId == SingleSale.SaleId);

                    // ئەگەر کاشێر بوو وە هەوڵیدا پسوڵەیەک دەستکاری بکات کە هی خۆی نییە
                    if (existingSale == null && items != null && items.Any())
                    {
                        return new JsonResult(new { success = false, message = "ببورە، تۆ دەسەڵاتی دەستکاریکردنی ئەم پسوڵەیەت نییە یان نەدۆزرایەوە." });
                    }
                }

                if (existingSale != null && (items == null || !items.Any()))
                    return await HandleVoidSale(existingSale, userId, transaction);

                if (items == null || !items.Any())
                    return new JsonResult(new { success = false, message = "لیست بەتاڵە یان دەسەڵاتی سڕینەوەت نییە" });

                decimal totalAmount = items.Sum(i => i.UnitPrice * i.Quantity);
                if (SingleSale.Discount > 0 && totalAmount > 0)
                {
                    decimal discountPercent = (SingleSale.Discount / totalAmount) * 100;
                    decimal maxAllowedPercent = await _permissionService.GetMaxDiscountPercentAsync(User);
                    if (discountPercent > maxAllowedPercent)
                        return new JsonResult(new
                        {
                            success = false,
                            message = $"داشکاندن ({discountPercent:F1}%) زیاترە لە سنوورەکەت ({maxAllowedPercent}%)"
                        });
                }

                var productIds = items.Select(i => i.ProductId).Distinct().ToList();
                var products = await _context.Products
                  .Where(p => productIds.Contains(p.ProductId))
                  .ToDictionaryAsync(p => p.ProductId);

                foreach (var item in items)
                {
                    if (!products.TryGetValue(item.ProductId, out var product))
                        return new JsonResult(new { success = false, message = "کاڵا نەدۆزرایەوە" });

                    var existingItem = existingSale?.SaleItems?.FirstOrDefault(si => si.ProductId == item.ProductId);
                    var availableStock = product.CurrentStock + (existingItem?.Quantity ?? 0);

                    if (item.Quantity > availableStock)
                        return new JsonResult(new
                        {
                            success = false,
                            message = $"ببورە، کاڵای {product.Name} تەنها {availableStock} دانەی لە کۆگادا ماوە."
                        });
                }

                decimal totalProfit = 0;
                var finalItems = new List<SaleItem>();
                foreach (var item in items)
                {
                    var product = products[item.ProductId];
                    finalItems.Add(new SaleItem
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        UnitPurchasePrice = product.LastPurchasePrice
                    });
                    totalProfit += (item.UnitPrice - product.LastPurchasePrice) * item.Quantity;
                }

                decimal grandTotal = totalAmount - SingleSale.Discount;
                decimal finalProfit = totalProfit - SingleSale.Discount;

                decimal newPaidAmount = SingleSale.PaidAmount;

                if (existingSale != null)
                {
                    decimal oldPaidAmount = existingSale.PaidAmount;
                    return await HandleEditSale(existingSale, items!, totalAmount,
                      grandTotal, oldPaidAmount, newPaidAmount, userId, transaction);
                }
                else
                {
                    return await HandleCreateSale(finalItems, totalAmount,
                      grandTotal, finalProfit, newPaidAmount, userId, transaction);
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return new JsonResult(new { success = false, message = "هەڵە: " + ex.Message });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // HandleVoidSale (سڕینەوەی پسوڵە)
        // ═══════════════════════════════════════════════════════════════
        private async Task<IActionResult> HandleVoidSale(
      Sale existingSale, string? userId,
      Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        {
            var deleteDate = existingSale.SaleDate;
            var affectedProductIds = existingSale.SaleItems!
              .Select(i => i.ProductId).Distinct().ToList();

            var products = await _context.Products
              .Where(p => affectedProductIds.Contains(p.ProductId))
              .ToDictionaryAsync(p => p.ProductId);

            var allPurchases = await _context.Purchases
              .Include(p => p.PurchaseItems!)
              .Where(p => p.PurchaseItems!.Any(pi => affectedProductIds.Contains(pi.ProductId)))
              .ToListAsync();

            var allOtherSales = await _context.Sales
              .Include(s => s.SaleItems!)
              .Where(s => s.SaleId != existingSale.SaleId
                  && !s.IsVoided
                  && s.SaleItems!.Any(si => affectedProductIds.Contains(si.ProductId)))
              .ToListAsync();

            var allAdjustments = await _context.StockAdjustments
              .Where(a => affectedProductIds.Contains(a.ProductId) && a.PurchaseId == null)
              .ToListAsync();

            var beforeEvents = BuildTimeline(
              allPurchases.Where(p => p.PurchaseDate < deleteDate),
              allOtherSales.Where(s => s.SaleDate < deleteDate),
              allAdjustments.Where(a => a.AdjustmentDate < deleteDate));

            var afterEvents = BuildTimeline(
              allPurchases.Where(p => p.PurchaseDate > deleteDate),
              allOtherSales.Where(s => s.SaleDate > deleteDate),
              allAdjustments.Where(a => a.AdjustmentDate > deleteDate));

            foreach (var prod in products.Values)
            {
                int simulatedStock = 0;
                decimal mac = 0;

                foreach (var ev in beforeEvents)
                {
                    if (ev.IsPurchase)
                    {
                        var pi = ev.Purchase!.PurchaseItems!
                          .FirstOrDefault(x => x.ProductId == prod.ProductId);
                        if (pi == null) continue;

                        if (simulatedStock <= 0)
                        {
                            mac = pi.UnitPrice;
                            simulatedStock = pi.Quantity;
                        }
                        else
                        {
                            mac = Math.Round(
                              (simulatedStock * mac + pi.Quantity * pi.UnitPrice)
                              / (simulatedStock + pi.Quantity), 2);
                            simulatedStock += pi.Quantity;
                        }
                    }
                    else if (ev.Adjustment != null)
                    {
                        if (ev.Adjustment.ProductId == prod.ProductId)
                        {
                            if (ev.Adjustment.IsInventoryReset)
                            {
                                simulatedStock = ev.Adjustment.StockAfter;
                                mac = ev.Adjustment.ResetPrice;
                            }
                            else
                            {
                                simulatedStock += ev.Adjustment.AdjustmentQuantity;
                            }
                        }
                    }
                    else
                    {
                        var si = ev.Sale!.SaleItems!
                          .FirstOrDefault(x => x.ProductId == prod.ProductId);
                        if (si != null)
                            simulatedStock = Math.Max(0, simulatedStock - si.Quantity);
                    }
                }

                var hasResetDel = afterEvents.Any(e =>
                  e.Adjustment != null &&
                  e.Adjustment.ProductId == prod.ProductId &&
                  e.Adjustment.IsInventoryReset);
                bool resetDoneDel = false;

                foreach (var ev in afterEvents)
                {
                    if (ev.IsPurchase)
                    {
                        var pi = ev.Purchase!.PurchaseItems!
                          .FirstOrDefault(x => x.ProductId == prod.ProductId);
                        if (pi == null) continue;

                        if (hasResetDel && !resetDoneDel) continue;

                        if (simulatedStock <= 0)
                        {
                            mac = pi.UnitPrice;
                            simulatedStock = pi.Quantity;
                        }
                        else
                        {
                            mac = Math.Round(
                              (simulatedStock * mac + pi.Quantity * pi.UnitPrice)
                              / (simulatedStock + pi.Quantity), 2);
                            simulatedStock += pi.Quantity;
                        }
                    }
                    else if (ev.Adjustment != null)
                    {
                        if (ev.Adjustment.ProductId == prod.ProductId)
                        {
                            if (ev.Adjustment.IsInventoryReset)
                            {
                                simulatedStock = ev.Adjustment.StockAfter;
                                mac = ev.Adjustment.ResetPrice;
                                resetDoneDel = true;
                            }
                            else
                            {
                                if (!hasResetDel || resetDoneDel)
                                    simulatedStock += ev.Adjustment.AdjustmentQuantity;
                            }
                        }
                    }
                    else
                    {
                        var si = ev.Sale!.SaleItems!
                          .FirstOrDefault(x => x.ProductId == prod.ProductId);
                        if (si == null) continue;

                        if (hasResetDel && !resetDoneDel) continue;

                        if (simulatedStock - si.Quantity < 0)
                        {
                            await transaction.RollbackAsync();
                            return new JsonResult(new
                            {
                                success = false,
                                message = "ناتوانیت ئەم پسوڵەیە بسڕیتەوە چونکە دەبێتە هۆی سالب بوونی ستۆک بۆ فرۆشتنەکانی دواتر."
                            });
                        }

                        decimal oldItemProfit = si.Quantity * (si.UnitPrice - si.UnitPurchasePrice);
                        decimal newItemProfit = si.Quantity * (si.UnitPrice - mac);
                        si.UnitPurchasePrice = mac;
                        ev.Sale.TotalProfit = ev.Sale.TotalProfit - oldItemProfit + newItemProfit;

                        simulatedStock = Math.Max(0, simulatedStock - si.Quantity);
                    }
                }

                prod.CurrentStock = simulatedStock;
                prod.LastPurchasePrice = mac;
            }

            existingSale.IsVoided = true;

            if (existingSale.PaymentType == PaymentType.Credit)
            {
                await LedgerHelper.RecordSaleDeletion(_context, existingSale.CustomerId,
                  existingSale.SaleId, existingSale.GrandTotal, DateTime.Now.AddMilliseconds(50), userId);

                if (existingSale.PaidAmount > 0 && CashHelper.IsToday(existingSale.SaleDate))
                {
                    await CashHelper.VoidInlinePayment(_context, existingSale.SaleId,
                      existingSale.PaidAmount, userId, User.Identity?.Name);
                }
            }
            else if (existingSale.PaymentType == PaymentType.Cash)
            {
                if (CashHelper.IsToday(existingSale.SaleDate))
                {
                    await CashHelper.VoidCashSale(_context, existingSale.SaleId,
                      existingSale.GrandTotal, userId, User.Identity?.Name);
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            await AuditHelper.SaleVoided(_context, User, HttpContext, existingSale.SaleId, existingSale.GrandTotal);

            var nextId = (await _context.Sales.MaxAsync(s => (int?)s.SaleId) ?? 0) + 1;
            return new JsonResult(new
            {
                success = true,
                message = "پسوڵەکە پووچەڵ کرایەوە",
                invoiceId = existingSale.SaleId,
                nextNewId = nextId
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // HandleEditSale (دەستکاریکردنی پسوڵە)
        // ═══════════════════════════════════════════════════════════════
        private async Task<IActionResult> HandleEditSale(
      Sale existingSale, List<SaleItemDto> items,
      decimal totalAmount, decimal grandTotal,
      decimal oldPaidAmount, decimal newPaidAmount, string? userId,
      Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        {
            var editDate = existingSale.SaleDate;
            int oldCustomerId = existingSale.CustomerId;
            decimal oldGrandTotal = existingSale.GrandTotal;
            var oldPaymentType = existingSale.PaymentType;

            var affectedProductIds = items.Select(i => i.ProductId)
              .Union(existingSale.SaleItems!.Select(o => o.ProductId))
              .Distinct().ToList();

            var products = await _context.Products
              .Where(p => affectedProductIds.Contains(p.ProductId))
              .ToDictionaryAsync(p => p.ProductId);

            var allPurchases = await _context.Purchases
              .Include(p => p.PurchaseItems!)
              .Where(p => p.PurchaseItems!.Any(pi => affectedProductIds.Contains(pi.ProductId)))
              .ToListAsync();

            var allOtherSales = await _context.Sales
              .Include(s => s.SaleItems!)
              .Where(s => s.SaleId != existingSale.SaleId
                  && !s.IsVoided
                  && s.SaleItems!.Any(si => affectedProductIds.Contains(si.ProductId)))
              .ToListAsync();

            var allAdjustments = await _context.StockAdjustments
              .Where(a => affectedProductIds.Contains(a.ProductId) && a.PurchaseId == null)
              .ToListAsync();

            var beforeEvents = BuildTimeline(
              allPurchases.Where(p => p.PurchaseDate < editDate),
              allOtherSales.Where(s => s.SaleDate < editDate),
              allAdjustments.Where(a => a.AdjustmentDate < editDate));

            var afterEvents = BuildTimeline(
              allPurchases.Where(p => p.PurchaseDate > editDate),
              allOtherSales.Where(s => s.SaleDate > editDate),
              allAdjustments.Where(a => a.AdjustmentDate > editDate));

            var historicalMacs = new Dictionary<int, decimal>();

            foreach (var prod in products.Values)
            {
                int simulatedStock = 0;
                decimal mac = 0;

                foreach (var ev in beforeEvents)
                {
                    if (ev.IsPurchase)
                    {
                        var pi = ev.Purchase!.PurchaseItems!.FirstOrDefault(x => x.ProductId == prod.ProductId);
                        if (pi == null) continue;

                        if (simulatedStock <= 0) { mac = pi.UnitPrice; simulatedStock = pi.Quantity; }
                        else
                        {
                            mac = Math.Round((simulatedStock * mac + pi.Quantity * pi.UnitPrice) / (simulatedStock + pi.Quantity), 2);
                            simulatedStock += pi.Quantity;
                        }
                    }
                    else if (ev.Adjustment != null && ev.Adjustment.ProductId == prod.ProductId)
                    {
                        if (ev.Adjustment.IsInventoryReset) { simulatedStock = ev.Adjustment.StockAfter; mac = ev.Adjustment.ResetPrice; }
                        else { simulatedStock += ev.Adjustment.AdjustmentQuantity; }
                    }
                    else if (ev.Sale != null)
                    {
                        var si = ev.Sale.SaleItems!.FirstOrDefault(x => x.ProductId == prod.ProductId);
                        if (si != null) simulatedStock = Math.Max(0, simulatedStock - si.Quantity);
                    }
                }

                historicalMacs[prod.ProductId] = mac;

                var dto = items.FirstOrDefault(x => x.ProductId == prod.ProductId);
                if (dto != null)
                {
                    if (simulatedStock < dto.Quantity)
                    {
                        await transaction.RollbackAsync();
                        return new JsonResult(new { success = false, message = $"ببورە، کاڵای {prod.Name} تەنها {simulatedStock} دانەی لە کۆگادا ماوە." });
                    }
                    simulatedStock -= dto.Quantity;
                }

                var hasResetEdit = afterEvents.Any(e => e.Adjustment != null && e.Adjustment.ProductId == prod.ProductId && e.Adjustment.IsInventoryReset);
                bool resetDoneEdit = false;

                foreach (var ev in afterEvents)
                {
                    if (ev.IsPurchase)
                    {
                        var pi = ev.Purchase!.PurchaseItems!.FirstOrDefault(x => x.ProductId == prod.ProductId);
                        if (pi == null) continue;
                        if (hasResetEdit && !resetDoneEdit) continue;

                        if (simulatedStock <= 0) { mac = pi.UnitPrice; simulatedStock = pi.Quantity; }
                        else
                        {
                            mac = Math.Round((simulatedStock * mac + pi.Quantity * pi.UnitPrice) / (simulatedStock + pi.Quantity), 2);
                            simulatedStock += pi.Quantity;
                        }
                    }
                    else if (ev.Adjustment != null && ev.Adjustment.ProductId == prod.ProductId)
                    {
                        if (ev.Adjustment.IsInventoryReset)
                        {
                            simulatedStock = ev.Adjustment.StockAfter; mac = ev.Adjustment.ResetPrice; resetDoneEdit = true;
                        }
                        else
                        {
                            if (!hasResetEdit || resetDoneEdit) simulatedStock += ev.Adjustment.AdjustmentQuantity;
                        }
                    }
                    else if (ev.Sale != null)
                    {
                        var si = ev.Sale.SaleItems!.FirstOrDefault(x => x.ProductId == prod.ProductId);
                        if (si == null) continue;
                        if (hasResetEdit && !resetDoneEdit) continue;

                        if (simulatedStock - si.Quantity < 0)
                        {
                            await transaction.RollbackAsync();
                            return new JsonResult(new { success = false, message = $"ئەم گۆڕانکارییە دەبێتە هۆی سالب بوونی ستۆک لە داهاتوودا (کاڵای {prod.Name})." });
                        }

                        decimal oldItemProfit = si.Quantity * (si.UnitPrice - si.UnitPurchasePrice);
                        decimal newItemProfit = si.Quantity * (si.UnitPrice - mac);
                        si.UnitPurchasePrice = mac;
                        ev.Sale.TotalProfit = ev.Sale.TotalProfit - oldItemProfit + newItemProfit;

                        simulatedStock = Math.Max(0, simulatedStock - si.Quantity);
                    }
                }

                prod.CurrentStock = simulatedStock;
                prod.LastPurchasePrice = mac;
            }

            var finalItems = new List<SaleItem>();
            decimal finalProfit = 0;
            foreach (var dto in items)
            {
                decimal historicalCost = historicalMacs.TryGetValue(dto.ProductId, out var hc) ? hc : (products.TryGetValue(dto.ProductId, out var fallback) ? fallback.LastPurchasePrice : 0);
                finalItems.Add(new SaleItem { ProductId = dto.ProductId, Quantity = dto.Quantity, UnitPrice = dto.UnitPrice, UnitPurchasePrice = historicalCost });
                finalProfit += (dto.UnitPrice - historicalCost) * dto.Quantity;
            }
            finalProfit -= SingleSale.Discount;

            _context.SaleItems.RemoveRange(existingSale.SaleItems!);
            existingSale.SaleItems = finalItems;
            existingSale.CustomerId = SingleSale.CustomerId;
            existingSale.TotalAmount = totalAmount;
            existingSale.GrandTotal = grandTotal;
            existingSale.TotalProfit = finalProfit;
            existingSale.Discount = SingleSale.Discount;

            existingSale.PaidAmount = newPaidAmount;
            existingSale.PaymentType = SingleSale.PaymentType;
            existingSale.IsVoided = false;

            await _context.SaveChangesAsync();

            var editTimestamp = DateTime.Now;
            await LedgerHelper.HandleSaleEdit(_context,
              oldCustomerId, oldGrandTotal, oldPaidAmount, oldPaymentType,
              SingleSale.CustomerId, grandTotal, newPaidAmount, SingleSale.PaymentType,
              existingSale.SaleId, editTimestamp, userId);

            if (CashHelper.IsToday(existingSale.SaleDate))
            {
                decimal oldCashAmount = oldPaymentType == PaymentType.Cash
                  ? oldGrandTotal
                  : oldPaidAmount;

                decimal newCashAmount = SingleSale.PaymentType == PaymentType.Cash
                  ? grandTotal
                  : newPaidAmount;

                decimal cashDiff = newCashAmount - oldCashAmount;

                if (cashDiff != 0)
                {
                    await CashHelper.SaleEditAdjustment(_context, existingSale.SaleId,
                      cashDiff, cashDiff > 0, userId, User.Identity?.Name);
                }
            }

            await transaction.CommitAsync();
            await AuditHelper.SaleEdited(_context, User, HttpContext, existingSale.SaleId, $"کۆی: {grandTotal:N0}");

            var nextIdNew = (await _context.Sales.MaxAsync(s => (int?)s.SaleId) ?? 0) + 1;
            return new JsonResult(new
            {
                success = true,
                message = "گۆڕانکارییەکان بەسەرکەوتوویی تۆمارکران",
                invoiceId = existingSale.SaleId,
                nextNewId = nextIdNew
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // HandleCreateSale (دروستکردنی پسوڵەی نوێ)
        // ═══════════════════════════════════════════════════════════════
        private async Task<IActionResult> HandleCreateSale(
          List<SaleItem> finalItems,
          decimal totalAmount, decimal grandTotal, decimal finalProfit,
          decimal newPaidAmount, string? userId,
          Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        {
            var productIds = finalItems.Select(i => i.ProductId).ToList();
            var products = await _context.Products
              .Where(p => productIds.Contains(p.ProductId))
              .ToDictionaryAsync(p => p.ProductId);

            foreach (var item in finalItems)
            {
                if (products.TryGetValue(item.ProductId, out var product))
                {
                    if (item.Quantity > product.CurrentStock)
                    {
                        await transaction.RollbackAsync();
                        return new JsonResult(new
                        {
                            success = false,
                            message = $"ببورە، کاڵای {product.Name} تەنها {product.CurrentStock} دانەی لە کۆگادا ماوە."
                        });
                    }
                    CostHelper.RemoveStock(product, item.Quantity);
                }
            }

            SingleSale.UserId = userId;
            SingleSale.SaleItems = finalItems;
            SingleSale.TotalAmount = totalAmount;
            SingleSale.GrandTotal = grandTotal;
            SingleSale.PaidAmount = newPaidAmount;
            SingleSale.TotalProfit = finalProfit;
            SingleSale.IsVoided = false;
            SingleSale.SaleDate = DateTime.Now;

            _context.Sales.Add(SingleSale);
            await _context.SaveChangesAsync();

            if (SingleSale.PaymentType == PaymentType.Credit)
            {
                await LedgerHelper.RecordCreditSale(_context, SingleSale.CustomerId,
                  SingleSale.SaleId, grandTotal, SingleSale.SaleDate, userId);

                if (newPaidAmount > 0)
                {
                    await LedgerHelper.RecordSalePayment(_context, SingleSale.CustomerId,
                      SingleSale.SaleId, newPaidAmount, SingleSale.SaleDate.AddSeconds(1), userId);

                    await CashHelper.InlineSalePayment(_context, SingleSale.SaleId, newPaidAmount, userId, User.Identity?.Name);
                }
            }
            else if (SingleSale.PaymentType == PaymentType.Cash)
            {
                await CashHelper.CashSale(_context, SingleSale.SaleId, grandTotal, userId, User.Identity?.Name);
            }

            await transaction.CommitAsync();
            await AuditHelper.SaleCreated(_context, User, HttpContext, SingleSale.SaleId, grandTotal);

            var nextIdNew = (await _context.Sales.MaxAsync(s => (int?)s.SaleId) ?? 0) + 1;
            return new JsonResult(new
            {
                success = true,
                message = "سەرکەوتووانە پاشەکەوت کرا",
                invoiceId = SingleSale.SaleId,
                nextNewId = nextIdNew
            });
        }

        public async Task<IActionResult> OnPostBarcode(string barcode)
        {
            var product = await _context.Products.AsNoTracking()
              .FirstOrDefaultAsync(p => p.Barcode == barcode);
            if (product == null)
                return new JsonResult(new { success = false, message = "نەدۆزرایەوە!" });
            if (product.CurrentStock <= 0)
                return new JsonResult(new { success = false, message = $"ستۆکی {product.Name} نەماوە!" });

            return new JsonResult(new
            {
                success = true,
                product = new
                {
                    productId = product.ProductId,
                    name = product.Name,
                    unitPrice = product.SalePrice,
                    wholesalePrice = product.WholesalePrice,
                    unitPurchasePrice = product.LastPurchasePrice,
                    currentStock = product.CurrentStock
                }
            });
        }

        public async Task<IActionResult> OnGetMaxDiscountAsync()
        {
            var maxPercent = await _permissionService.GetMaxDiscountPercentAsync(User);
            var role = await _permissionService.GetUserRoleAsync(User);
            return new JsonResult(new { maxPercent, role });
        }

        public async Task<IActionResult> OnPostAddCustomer(string name, string phone)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new JsonResult(new { success = false, message = "ناوی کڕیار پێویستە" });

            var customer = new Customer
            {
                Name = name.Trim(),
                Phone = string.IsNullOrWhiteSpace(phone) ? "-" : phone.Trim()
            };
            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            return new JsonResult(new
            {
                success = true,
                customer = new { customerId = customer.CustomerId, name = customer.Name }
            });
        }
    }

    public class SaleItemDto
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}