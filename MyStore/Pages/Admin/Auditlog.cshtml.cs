using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;

namespace MyStore.Pages.Admin
{
    [Authorize(Roles = "Admin")]
    public class AuditLogModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        public AuditLogModel(ApplicationDbContext context) => _context = context;

        public List<AuditLog> Logs { get; set; } = new();
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }

        [BindProperty(SupportsGet = true)] public int CurrentPage { get; set; } = 1;
        [BindProperty(SupportsGet = true)] public string? FilterCategory { get; set; }
        [BindProperty(SupportsGet = true)] public string? FilterUser { get; set; }
        [BindProperty(SupportsGet = true)] public string? FilterDate { get; set; }
        [BindProperty(SupportsGet = true)] public string? SearchTerm { get; set; }

        public int PageSize = 30;

        public async Task OnGetAsync()
        {
            var query = _context.AuditLogs.AsQueryable();

            if (!string.IsNullOrEmpty(FilterCategory))
                query = query.Where(l => l.Category == FilterCategory);

            if (!string.IsNullOrEmpty(FilterUser))
                query = query.Where(l => l.UserName!.Contains(FilterUser));

            if (!string.IsNullOrEmpty(SearchTerm))
                query = query.Where(l => l.Description.Contains(SearchTerm));

            if (!string.IsNullOrEmpty(FilterDate))
            {
                if (DateTime.TryParse(FilterDate, out var dt))
                {
                    query = query.Where(l => l.CreatedAt.Date == dt.Date);
                }
            }

            TotalCount = await query.CountAsync();
            TotalPages = (int)Math.Ceiling(TotalCount / (double)PageSize);
            if (CurrentPage < 1) CurrentPage = 1;
            if (CurrentPage > TotalPages && TotalPages > 0) CurrentPage = TotalPages;

            Logs = await query
                .OrderByDescending(l => l.CreatedAt)
                .ThenByDescending(l => l.Id)
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();
        }
    }
}