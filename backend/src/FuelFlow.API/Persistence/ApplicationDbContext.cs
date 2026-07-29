using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Notifications.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Stations.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.SharedModels;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace FuelFlow.Persistence;

public sealed class ApplicationDbContext : DbContext, IImportVouchersDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<FuelVoucher> FuelVouchers => Set<FuelVoucher>();
    public DbSet<QrParameters> QrParameters => Set<QrParameters>();
    public DbSet<VoucherImport> VoucherImports => Set<VoucherImport>();
    public DbSet<VoucherImportError> VoucherImportErrors => Set<VoucherImportError>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Fulfillment> Fulfillments => Set<Fulfillment>();
    public DbSet<OrderLineItem> OrderLineItems => Set<OrderLineItem>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<Station> Stations => Set<Station>();
    public DbSet<StationNode> StationNodes => Set<StationNode>();
    public DbSet<FuelTypeEntity> FuelTypes => Set<FuelTypeEntity>();
    public DbSet<FuelPackage> FuelPackages => Set<FuelPackage>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<UserContract> UserContracts => Set<UserContract>();
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<PriceChangeAudit> PriceChangeAudits => Set<PriceChangeAudit>();
    public DbSet<FuelPackagePriceAudit> FuelPackagePriceAudits => Set<FuelPackagePriceAudit>();
    public DbSet<ProviderEventOutbox> ProviderEventOutbox => Set<ProviderEventOutbox>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        var seedCreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var seedStations = new[]
        {
            new Station { Id = "okko", Name = "OKKO", LogoText = "OKKO", Color = "#22c55e", Lat = 50.4851, Lng = 30.4734, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new Station { Id = "wog", Name = "WOG", LogoText = "WOG", Color = "#10b981", Lat = 50.4501, Lng = 30.5234, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new Station { Id = "upg", Name = "UPG", LogoText = "UPG", Color = "#06b6d4", Lat = 50.4001, Lng = 30.6134, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new Station { Id = "klo", Name = "KLO", LogoText = "KLO", Color = "#eab308", Lat = 50.4101, Lng = 30.4034, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc }
        };

        var seedStationNodes = new[]
        {
            new StationNode { Id = "okko-kyiv-main", StationId = "okko", Name = "OKKO Київ", Address = "42 Чоколівський бульвар", City = "Київ", StationType = "Тип АЗС ОККО-міська", Lat = 50.4310, Lng = 30.4515, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new StationNode { Id = "wog-kyiv-main", StationId = "wog", Name = "WOG Київ", Address = "15-Б проспект Соборності", City = "Київ", StationType = "Тип АЗС WOG-міська", Lat = 50.4482, Lng = 30.6170, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new StationNode { Id = "upg-kyiv-main", StationId = "upg", Name = "UPG Київ", Address = "Проспект Перемоги, 98", City = "Київ", StationType = "Тип АЗС UPG-міська", Lat = 50.4566, Lng = 30.3950, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new StationNode { Id = "klo-kyiv-main", StationId = "klo", Name = "KLO Київ", Address = "Броварський проспект, 11", City = "Київ", StationType = "Тип АЗС KLO-міська", Lat = 50.4578, Lng = 30.5986, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc }
        };

        var seedFuelTypes = new[]
        {
            new FuelTypeEntity { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "okko-95", Name = "A-95", StationId = "okko", BasePrice = 54, DiscountPrice = 51, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "okko-p95", Name = "Pulls 95", StationId = "okko", BasePrice = 60, DiscountPrice = 56, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "okko-pulls-dp", Name = "ДП PULLS", StationId = "okko", BasePrice = 58, DiscountPrice = 55, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "okko-gas", Name = "ГАЗ", StationId = "okko", BasePrice = 29, DiscountPrice = 27, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "wog-dp", Name = "ДП Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "wog-95", Name = "A-95 Mustang", StationId = "wog", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "wog-95-euro", Name = "A 95 EURO", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "wog-100", Name = "Mustang 100", StationId = "wog", BasePrice = 65, DiscountPrice = 61, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "wog-gas", Name = "ГАЗ", StationId = "wog", BasePrice = 29, DiscountPrice = 27, CreatedAtUtc = seedCreatedAtUtc, UpdatedAtUtc = seedCreatedAtUtc }
        };

        // Per-liter pricing: supplier price is discount price - margin, margin is our markup
        // All prices in UAH per liter

        var fuelPricing = new Dictionary<string, (decimal margin, int sort)>
        {
            ["okko-dp"] = (2.0m, 1),
            ["okko-95"] = (2.0m, 2),
            ["okko-p95"] = (3.0m, 3),
            ["okko-pulls-dp"] = (2.5m, 4),
            ["okko-gas"] = (1.5m, 5),
            ["wog-dp"] = (2.0m, 6),
            ["wog-95"] = (2.0m, 7),
            ["wog-95-euro"] = (2.5m, 8),
            ["wog-100"] = (3.0m, 9),
            ["wog-gas"] = (1.5m, 10),
        };

        var seedFuelPackages = seedFuelTypes
            .SelectMany(ft =>
            {
                var margin = fuelPricing.TryGetValue(ft.Id, out var p) ? p.margin : 2.0m;
                var finalPrice = (decimal)ft.DiscountPrice;
                var supplierPrice = finalPrice - margin;
                return new[] { 10m, 20m, 50m }.Select(liters => new FuelPackage
                {
                    Id = $"{ft.Id}-{liters}",
                    StationId = ft.StationId,
                    FuelTypeId = ft.Id,
                    FuelName = ft.Name,
                    Liters = liters,
                    Price = (int)Math.Round(finalPrice * liters),
                    OriginalPrice = (int)Math.Round(supplierPrice * liters),
                    SupplierPricePerLiter = supplierPrice,
                    MarginUahPerLiter = margin,
                    FinalPricePerLiter = finalPrice,
                    CreatedAtUtc = seedCreatedAtUtc,
                    UpdatedAtUtc = seedCreatedAtUtc
                });
            })
            .Append(new FuelPackage
            {
                Id = "okko-dp-2",
                StationId = "okko",
                FuelTypeId = "okko-dp",
                FuelName = "ДП ЄВРО",
                Liters = 2m,
                Price = 104,
                OriginalPrice = 100,
                SupplierPricePerLiter = 50m,
                MarginUahPerLiter = 2m,
                FinalPricePerLiter = 52m,
                CreatedAtUtc = seedCreatedAtUtc,
                UpdatedAtUtc = seedCreatedAtUtc
            })
            .Append(new FuelPackage
            {
                Id = "okko-dp-3",
                StationId = "okko",
                FuelTypeId = "okko-dp",
                FuelName = "ДП ЄВРО",
                Liters = 3m,
                Price = 156,
                OriginalPrice = 150,
                SupplierPricePerLiter = 50m,
                MarginUahPerLiter = 2m,
                FinalPricePerLiter = 52m,
                CreatedAtUtc = seedCreatedAtUtc,
                UpdatedAtUtc = seedCreatedAtUtc
            })
            .ToArray();

        modelBuilder.Entity<Station>().HasData(seedStations);
        modelBuilder.Entity<StationNode>().HasData(seedStationNodes);
        modelBuilder.Entity<FuelTypeEntity>().HasData(seedFuelTypes);
        modelBuilder.Entity<FuelPackage>().HasData(seedFuelPackages);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return base.SaveChangesAsync(cancellationToken);
    }
}


