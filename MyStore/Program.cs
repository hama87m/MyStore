using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Models;

var builder = WebApplication.CreateBuilder(args);

// گەڕان بەدوای Connection String
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// ناساندنی ApplicationDbContext بۆ بەکارهێنانی SQL Server بەبێ سیستەمی Retry بۆ ڕێگریکردن لە کێشەی فرۆشتن
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// زیادکردنی سێرڤسی Identity و ڕۆڵەکان
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 4;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

// Add services to the container.
builder.Services.AddRazorPages();

// [نوێ] زیادکردنی PermissionService - پێش Build()
builder.Services.AddScoped<IPermissionService, PermissionService>();
// زیادکردنی سنووری ناردنی داتا بۆ فۆڕمەکان بۆ چارەسەرکردنی کێشەی جەردکردنی کاڵای زۆر
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.ValueCountLimit = 900000000; // زیادکردنی سنوور بۆ ٥٠ هەزار داتا
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages(); // تەنها یەک جار!

// --- دروستکردنی ئەدمین لە کاتی ئیشپێکردن ---
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        await DbSeeder.Initialize(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "هەڵەیەک ڕوویدا لە کاتی دروستکردنی ئەدمین.");
    }
}

app.Run();