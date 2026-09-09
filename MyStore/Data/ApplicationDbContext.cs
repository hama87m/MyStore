// Data/ApplicationDbContext.cs
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MyStore.Models;

namespace MyStore.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // خشتەکانی هەبوو

        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<Purchase> Purchases { get; set; } = null!;
        public DbSet<PurchaseItem> PurchaseItems { get; set; } = null!;
        public DbSet<Sale> Sales { get; set; } = null!;
        public DbSet<SaleItem> SaleItems { get; set; } = null!;
        public DbSet<Customer> Customers { get; set; } = null!;
        public DbSet<CustomerPayments> CustomerPayments { get; set; } = null!;
        public DbSet<SalePayment> SalePayments { get; set; } = null!;
        public DbSet<StockAdjustment> StockAdjustments { get; set; } = null!;
        public DbSet<SystemSettings> SystemSettings { get; set; }
        public DbSet<UserSettings> UserSettings { get; set; }


        // ⭐ خشتەی نوێ
        public DbSet<CustomerLedger> CustomerLedgers { get; set; } = null!;
        public DbSet<Supplier> Suppliers { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<CashTransaction> CashTransactions { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ڕێکخستنەکانی کۆن
            modelBuilder.Entity<SalePayment>()
                .HasOne(sp => sp.Sale)
                .WithMany()
                .HasForeignKey(sp => sp.SaleId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SalePayment>()
                .HasOne(sp => sp.CustomerPayment)
                .WithMany(cp => cp.SalePayments)
                .HasForeignKey(sp => sp.CustomerPaymentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Performance Indexes
            modelBuilder.Entity<PurchaseItem>().HasIndex(p => p.ProductId);
            modelBuilder.Entity<SaleItem>().HasIndex(s => s.ProductId);
            modelBuilder.Entity<Sale>().HasIndex(s => s.CustomerId);

            // ⭐ Indexes نوێ بۆ Ledger
            modelBuilder.Entity<CustomerLedger>()
                .HasIndex(l => l.CustomerId);

            modelBuilder.Entity<CustomerLedger>()
                .HasIndex(l => l.TransactionDate);

            modelBuilder.Entity<CustomerLedger>()
                .HasIndex(l => new { l.ReferenceType, l.ReferenceId });

            // Supplier → Purchases
            modelBuilder.Entity<Purchase>()
                .HasOne(p => p.Supplier)
                .WithMany(s => s.Purchases)
                .HasForeignKey(p => p.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            // ⭐ AuditLog Indexes
            modelBuilder.Entity<AuditLog>().HasIndex(a => a.CreatedAt);
            modelBuilder.Entity<AuditLog>().HasIndex(a => a.Category);
            modelBuilder.Entity<AuditLog>().HasIndex(a => a.UserId);

            // ⭐ CashTransaction Indexes
            modelBuilder.Entity<CashTransaction>().HasIndex(c => c.CreatedAt);
            modelBuilder.Entity<CashTransaction>().HasIndex(c => c.FlowType);
        }
    }
}