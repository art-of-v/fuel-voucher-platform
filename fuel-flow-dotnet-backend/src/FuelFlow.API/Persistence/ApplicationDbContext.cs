using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Contracts.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Purchases.SharedModels;
using FuelFlow.SharedKernel.Domain;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Import;
using FuelFlow.Features.Vouchers.SharedModels;
using Microsoft.EntityFrameworkCore;

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
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    public DbSet<Station> Stations => Set<Station>();
    public DbSet<StationNode> StationNodes => Set<StationNode>();
    public DbSet<FuelTypeEntity> FuelTypes => Set<FuelTypeEntity>();
    public DbSet<FuelPackage> FuelPackages => Set<FuelPackage>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<UserContract> UserContracts => Set<UserContract>();
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        var seedCreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var seedStations = new[]
        {
            new Station { Id = "okko", Name = "OKKO", LogoText = "OKKO", Color = "#22c55e", Lat = "50.4851", Lng = "30.4734", CreatedAtUtc = seedCreatedAtUtc },
            new Station { Id = "wog", Name = "WOG", LogoText = "WOG", Color = "#10b981", Lat = "50.4501", Lng = "30.5234", CreatedAtUtc = seedCreatedAtUtc },
            new Station { Id = "upg", Name = "UPG", LogoText = "UPG", Color = "#06b6d4", Lat = "50.4001", Lng = "30.6134", CreatedAtUtc = seedCreatedAtUtc },
            new Station { Id = "klo", Name = "KLO", LogoText = "KLO", Color = "#eab308", Lat = "50.4101", Lng = "30.4034", CreatedAtUtc = seedCreatedAtUtc }
        };

        var seedStationNodes = new[]
        {
            new StationNode { Id = "okko-kyiv-main", StationId = "okko", Name = "OKKO Київ", Address = "42 Чоколівський бульвар", City = "Київ", StationType = "Тип АЗС ОККО-міська", Lat = "50.4310", Lng = "30.4515", CreatedAtUtc = seedCreatedAtUtc },
            new StationNode { Id = "wog-kyiv-main", StationId = "wog", Name = "WOG Київ", Address = "15-Б проспект Соборності", City = "Київ", StationType = "Тип АЗС WOG-міська", Lat = "50.4482", Lng = "30.6170", CreatedAtUtc = seedCreatedAtUtc },
            new StationNode { Id = "upg-kyiv-main", StationId = "upg", Name = "UPG Київ", Address = "Проспект Перемоги, 98", City = "Київ", StationType = "Тип АЗС UPG-міська", Lat = "50.4566", Lng = "30.3950", CreatedAtUtc = seedCreatedAtUtc },
            new StationNode { Id = "klo-kyiv-main", StationId = "klo", Name = "KLO Київ", Address = "Броварський проспект, 11", City = "Київ", StationType = "Тип АЗС KLO-міська", Lat = "50.4578", Lng = "30.5986", CreatedAtUtc = seedCreatedAtUtc }
        };

        var seedFuelTypes = new[]
        {
            new FuelTypeEntity { Id = "okko-dp", Name = "ДП ЄВРО", StationId = "okko", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "okko-95", Name = "A-95", StationId = "okko", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "okko-p95", Name = "Pulls 95", StationId = "okko", BasePrice = 62, DiscountPrice = 58, CreatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "okko-pulls-dp", Name = "ДП PULLS", StationId = "okko", BasePrice = 58, DiscountPrice = 55, CreatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "okko-gas", Name = "ГАЗ", StationId = "okko", BasePrice = 30, DiscountPrice = 28, CreatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "wog-dp", Name = "ДП Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "wog-95", Name = "A-95 Mustang", StationId = "wog", BasePrice = 56, DiscountPrice = 53, CreatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "wog-95-euro", Name = "A 95 EURO", StationId = "wog", BasePrice = 55, DiscountPrice = 52, CreatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "wog-100", Name = "Mustang 100", StationId = "wog", BasePrice = 65, DiscountPrice = 60, CreatedAtUtc = seedCreatedAtUtc },
            new FuelTypeEntity { Id = "wog-gas", Name = "ГАЗ", StationId = "wog", BasePrice = 30, DiscountPrice = 28, CreatedAtUtc = seedCreatedAtUtc }
        };

        var seedFuelPackages = seedFuelTypes
            .SelectMany(ft => new[] { 10, 20, 50 }.Select(liters => new FuelPackage
            {
                Id = $"{ft.Id}-{liters}",
                StationId = ft.StationId,
                FuelTypeId = ft.Id,
                FuelName = ft.Name,
                Liters = liters,
                Price = ft.DiscountPrice * liters,
                OriginalPrice = ft.BasePrice * liters,
                CreatedAtUtc = seedCreatedAtUtc
            }))
            .Append(new FuelPackage
            {
                Id = "okko-dp-2",
                StationId = "okko",
                FuelTypeId = "okko-dp",
                FuelName = "ДП ЄВРО",
                Liters = 2,
                Price = 104,
                OriginalPrice = 110,
                CreatedAtUtc = seedCreatedAtUtc
            })
            .Append(new FuelPackage
            {
                Id = "okko-dp-3",
                StationId = "okko",
                FuelTypeId = "okko-dp",
                FuelName = "ДП ЄВРО",
                Liters = 3,
                Price = 156,
                OriginalPrice = 165,
                CreatedAtUtc = seedCreatedAtUtc
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


