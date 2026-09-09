using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyStore.Models;

namespace MyStore.Data
{
    public static class DbSeeder
    {
        public static async Task Initialize(IServiceProvider serviceProvider)
        {
            var userManager = serviceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();

            // ١. ڕۆڵەکان
            string[] roleNames = { "Admin", "Manager", "Cashier" };
            foreach (var roleName in roleNames)
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                    await roleManager.CreateAsync(new IdentityRole(roleName));
            }

            // ٢. یەکەم ئەدمین
            var adminEmail = "admin@mystore.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                var newAdmin = new IdentityUser
                {
                    UserName = "admin",
                    Email = adminEmail,
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(newAdmin, "1234");
                if (result.Succeeded)
                    await userManager.AddToRoleAsync(newAdmin, "Admin");
            }

            // ٣. دابینکەری "دیارینەکراو" — ID=1 پێویستە هەمیشە هەبێت
            if (!await context.Suppliers.AnyAsync(s => s.SupplierId == 1))
            {
                context.Suppliers.Add(new Supplier
                {
                    SupplierId = 1,
                    Name = "دیارینەکراو",
                    Phone = null,
                    Address = null,
                    IsActive = true
                });
                await context.SaveChangesAsync();
            }
        }
    }
}