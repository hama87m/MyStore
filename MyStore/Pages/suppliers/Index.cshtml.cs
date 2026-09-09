using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;

namespace MyStore.Pages.Suppliers
{
    [Authorize(Roles = "Admin,Manager")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        public IndexModel(ApplicationDbContext context) { _context = context; }

        public List<SupplierViewModel> SupplierList { get; set; } = new();

        [BindProperty] public Supplier SupplierInput { get; set; } = new();
        [BindProperty] public int DeleteSupplierId { get; set; }

        public async Task OnGetAsync()
        {
            SupplierList = await _context.Suppliers
                .OrderBy(s => s.SupplierId == 1 ? 0 : 1)
                .ThenBy(s => s.Name)
                .Select(s => new SupplierViewModel
                {
                    SupplierId = s.SupplierId,
                    Name = s.Name,
                    Phone = s.Phone,
                    Address = s.Address,
                    IsActive = s.IsActive,
                    PurchaseCount = s.Purchases != null ? s.Purchases.Count : 0
                })
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostAddAsync()
        {
            if (string.IsNullOrWhiteSpace(SupplierInput.Name))
                return new JsonResult(new { success = false, message = "ناوی دابینکەر پێویستە" });

            var s = new Supplier
            {
                Name = SupplierInput.Name.Trim(),
                Phone = string.IsNullOrWhiteSpace(SupplierInput.Phone) ? null : SupplierInput.Phone.Trim(),
                Address = string.IsNullOrWhiteSpace(SupplierInput.Address) ? null : SupplierInput.Address.Trim(),
                IsActive = true
            };
            _context.Suppliers.Add(s);
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostEditAsync()
        {
            if (string.IsNullOrWhiteSpace(SupplierInput.Name))
                return new JsonResult(new { success = false, message = "ناوی دابینکەر پێویستە" });

            var existing = await _context.Suppliers.FindAsync(SupplierInput.SupplierId);
            if (existing == null)
                return new JsonResult(new { success = false, message = "دابینکەر نەدۆزرایەوە" });

            existing.Name = SupplierInput.Name.Trim();
            existing.Phone = string.IsNullOrWhiteSpace(SupplierInput.Phone) ? null : SupplierInput.Phone.Trim();
            existing.Address = string.IsNullOrWhiteSpace(SupplierInput.Address) ? null : SupplierInput.Address.Trim();
            existing.IsActive = SupplierInput.IsActive;
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostDeleteAsync()
        {
            var supplier = await _context.Suppliers
                .Include(s => s.Purchases)
                .FirstOrDefaultAsync(s => s.SupplierId == DeleteSupplierId);

            if (supplier == null)
                return new JsonResult(new { success = false, message = "دابینکەر نەدۆزرایەوە" });

            // پسوڵەکان بگەڕێنە بۆ "دیارینەکراو"
            if (supplier.Purchases != null)
                foreach (var p in supplier.Purchases)
                    p.SupplierId = 1;

            _context.Suppliers.Remove(supplier);
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnGetGetAsync(int id)
        {
            var s = await _context.Suppliers.FindAsync(id);
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
    }

    public class SupplierViewModel
    {
        public int SupplierId { get; set; }
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public int PurchaseCount { get; set; }
    }
}