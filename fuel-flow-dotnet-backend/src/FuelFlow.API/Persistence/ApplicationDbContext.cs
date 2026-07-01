using FuelFlow.Features.Auth.SharedModels;
using FuelFlow.Features.Orders.SharedModels;
using FuelFlow.Features.Stations.SharedModels;
using FuelFlow.Features.Vouchers;
using FuelFlow.Features.Vouchers.Import;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Persistence;

public sealed class ApplicationDbContext : DbContext, IImportVouchersDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<FuelVoucher> FuelVouchers => Set<FuelVoucher>();
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FuelVoucher>(entity =>
        {
            entity.ToTable("fuel_vouchers");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.Provider)
                .HasColumnName("provider")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.FuelTypeId)
                .HasColumnName("fuel_type_id")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.Liters)
                .HasColumnName("liters")
                .HasColumnType("numeric(10,2)")
                .IsRequired();

            entity.Property(e => e.ExpirationDate)
                .HasColumnName("expiration_date")
                .IsRequired();

            entity.Property(e => e.VoucherNumber)
                .HasColumnName("voucher_number")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.QrPayload)
                .HasColumnName("qr_payload")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .IsRequired();

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(e => e.FuelSubtype)
                .HasColumnName("fuel_subtype")
                .HasMaxLength(50);

            entity.Property(e => e.RedemptionRules)
                .HasColumnName("redemption_rules")
                .HasColumnType("text");

            entity.Property(e => e.ImageUrl)
                .HasColumnName("image_url")
                .HasColumnType("text");

            entity.Property(e => e.AssignedToUserId)
                .HasColumnName("assigned_to_user_id")
                .HasMaxLength(100);

            entity.Property(e => e.ImportJobId)
                .HasColumnName("import_job_id");

            entity.Property(e => e.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .IsRequired();

            // Indexes
            entity.HasIndex(e => e.VoucherNumber)
                .IsUnique();

            entity.HasIndex(e => e.QrPayload)
                .IsUnique();

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ExpirationDate);
            entity.HasIndex(e => e.Provider);
            entity.HasIndex(e => e.FuelTypeId);
            entity.HasIndex(e => e.AssignedToUserId);
            entity.HasIndex(e => new { e.Provider, e.FuelTypeId, e.Liters, e.Status });
        });

        modelBuilder.Entity<VoucherImport>(entity =>
        {
            entity.ToTable("voucher_imports");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.FileName)
                .HasColumnName("file_name")
                .IsRequired();

            entity.Property(e => e.PageCount)
                .HasColumnName("page_count")
                .IsRequired();

            entity.Property(e => e.StartedAtUtc)
                .HasColumnName("started_at_utc")
                .IsRequired();

            entity.Property(e => e.CompletedAtUtc)
                .HasColumnName("completed_at_utc");

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .IsRequired();

            entity.Property(e => e.ImportedCount)
                .HasColumnName("imported_count")
                .IsRequired();

            entity.Property(e => e.DuplicateCount)
                .HasColumnName("duplicate_count")
                .IsRequired();

            entity.Property(e => e.FailedCount)
                .HasColumnName("failed_count")
                .IsRequired();
        });

        modelBuilder.Entity<VoucherImportError>(entity =>
        {
            entity.ToTable("voucher_import_errors");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.ImportId)
                .HasColumnName("import_id")
                .IsRequired();

            entity.Property(e => e.PageNumber)
                .HasColumnName("page_number")
                .IsRequired();

            entity.Property(e => e.VoucherNumber)
                .HasColumnName("voucher_number")
                .HasMaxLength(100);

            entity.Property(e => e.ErrorMessage)
                .HasColumnName("error_message")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.RawText)
                .HasColumnName("raw_text")
                .HasColumnType("text");

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .IsRequired();

            entity.HasOne<VoucherImport>()
                .WithMany()
                .HasForeignKey(d => d.ImportId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.PhoneNumber)
                .HasColumnName("phone_number")
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(e => e.RoleId)
                .HasColumnName("role_id");

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .IsRequired();

            entity.Property(e => e.LastLoginAtUtc)
                .HasColumnName("last_login_at_utc");

            entity.Property(e => e.UpdatedAtUtc)
                .HasColumnName("updated_at_utc")
                .IsRequired();

            entity.Property(e => e.ReferralCode)
                .HasColumnName("referral_code")
                .HasMaxLength(50);

            entity.Property(e => e.ReferredBy)
                .HasColumnName("referred_by")
                .HasMaxLength(50);

            entity.Property(e => e.BonusBalance)
                .HasColumnName("bonus_balance")
                .HasDefaultValue(0)
                .IsRequired();

            entity.HasOne(e => e.Role)
                .WithMany()
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.PhoneNumber)
                .IsUnique();

            entity.HasIndex(e => e.ReferralCode)
                .IsUnique()
                .HasFilter("referral_code IS NOT NULL");
        });

        modelBuilder.Entity<Device>(entity =>
        {
            entity.ToTable("devices");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(e => e.DeviceId)
                .HasColumnName("device_id")
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(e => e.PublicKey)
                .HasColumnName("public_key")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.DeviceModel)
                .HasColumnName("device_model")
                .HasMaxLength(255);

            entity.Property(e => e.OsVersion)
                .HasColumnName("os_version")
                .HasMaxLength(255);

            entity.Property(e => e.AppVersion)
                .HasColumnName("app_version")
                .HasMaxLength(255);

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            entity.Property(e => e.LastSeenAt)
                .HasColumnName("last_seen_at")
                .IsRequired();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.DeviceId)
                .IsUnique();

            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(e => e.Token)
                .HasColumnName("token")
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.ExpiresAtUtc)
                .HasColumnName("expires_at_utc")
                .IsRequired();

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .IsRequired();

            entity.Property(e => e.IsRevoked)
                .HasColumnName("is_revoked")
                .IsRequired();

            entity.Property(e => e.RevokedAtUtc)
                .HasColumnName("revoked_at_utc");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.Token)
                .IsUnique();

            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<VerificationCode>(entity =>
        {
            entity.ToTable("verification_codes");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.PhoneNumber)
                .HasColumnName("phone_number")
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(e => e.Code)
                .HasColumnName("code")
                .HasMaxLength(10)
                .IsRequired();

            entity.Property(e => e.ExpiresAtUtc)
                .HasColumnName("expires_at_utc")
                .IsRequired();

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .IsRequired();

            entity.Property(e => e.IsUsed)
                .HasColumnName("is_used")
                .IsRequired();

            entity.Property(e => e.UsedAtUtc)
                .HasColumnName("used_at_utc");

            entity.HasIndex(e => e.PhoneNumber);
            entity.HasIndex(e => e.ExpiresAtUtc);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.Name)
                .HasColumnName("name")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .IsRequired();

            entity.HasIndex(e => e.Name)
                .IsUnique();
        });

        // Order Entity Configuration
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.ProductType)
                .HasColumnName("product_type")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.Provider)
                .HasColumnName("provider")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.FuelTypeId)
                .HasColumnName("fuel_type_id")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.Liters)
                .HasColumnName("liters")
                .IsRequired();

            entity.Property(e => e.Quantity)
                .HasColumnName("quantity")
                .HasDefaultValue(1)
                .IsRequired();

            entity.Property(e => e.Price)
                .HasColumnName("price")
                .IsRequired();

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(e => e.MonobankInvoiceId)
                .HasColumnName("monobank_invoice_id")
                .HasMaxLength(100);

            entity.Property(e => e.MonobankStatus)
                .HasColumnName("monobank_status")
                .HasConversion<string>()
                .HasMaxLength(50);

            entity.Property(e => e.IdempotencyKey)
                .HasColumnName("idempotency_key")
                .HasMaxLength(100);

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .IsRequired();

            entity.Property(e => e.FulfilledAtUtc)
                .HasColumnName("fulfilled_at_utc");

            // Indexes
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAtUtc);
            entity.HasIndex(e => e.IdempotencyKey)
                .IsUnique()
                .HasFilter("idempotency_key IS NOT NULL");
        });

        // Fulfillment Entity Configuration
        modelBuilder.Entity<Fulfillment>(entity =>
        {
            entity.ToTable("fulfillments");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.OrderId)
                .HasColumnName("order_id")
                .IsRequired();

            entity.Property(e => e.VoucherId)
                .HasColumnName("voucher_id")
                .IsRequired();

            entity.Property(e => e.FulfilledAtUtc)
                .HasColumnName("fulfilled_at_utc")
                .IsRequired();

            // Indexes
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.VoucherId);
        });

        // OutboxEvent Entity Configuration
        modelBuilder.Entity<OutboxEvent>(entity =>
        {
            entity.ToTable("outbox_events");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id");

            entity.Property(e => e.EventType)
                .HasColumnName("event_type")
                .HasConversion<string>()
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.Payload)
                .HasColumnName("payload")
                .HasColumnType("jsonb")
                .IsRequired();

            entity.Property(e => e.Processed)
                .HasColumnName("processed")
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(e => e.ProcessedAtUtc)
                .HasColumnName("processed_at_utc");

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .IsRequired();

            // Indexes
            entity.HasIndex(e => e.Processed);
            entity.HasIndex(e => e.CreatedAtUtc);
        });

        modelBuilder.Entity<Station>(entity =>
        {
            entity.ToTable("stations");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasColumnType("text");

            entity.Property(e => e.Name)
                .HasColumnName("name")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.Color)
                .HasColumnName("color")
                .HasColumnType("text")
                .HasDefaultValue("#00ff80")
                .IsRequired();

            entity.Property(e => e.LogoText)
                .HasColumnName("logo_text")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.Address)
                .HasColumnName("address")
                .HasColumnType("text");

            entity.Property(e => e.Phone)
                .HasColumnName("phone")
                .HasColumnType("text");

            entity.Property(e => e.StationType)
                .HasColumnName("station_type")
                .HasColumnType("text");

            entity.Property(e => e.Lat)
                .HasColumnName("lat")
                .HasColumnType("text");

            entity.Property(e => e.Lng)
                .HasColumnName("lng")
                .HasColumnType("text");

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.HasMany<StationNode>()
                .WithOne()
                .HasForeignKey(e => e.StationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StationNode>(entity =>
        {
            entity.ToTable("station_nodes");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasColumnType("text");

            entity.Property(e => e.StationId)
                .HasColumnName("station_id")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.Name)
                .HasColumnName("name")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.Address)
                .HasColumnName("address")
                .HasColumnType("text");

            entity.Property(e => e.Phone)
                .HasColumnName("phone")
                .HasColumnType("text");

            entity.Property(e => e.City)
                .HasColumnName("city")
                .HasColumnType("text");

            entity.Property(e => e.StationType)
                .HasColumnName("station_type")
                .HasColumnType("text");

            entity.Property(e => e.Lat)
                .HasColumnName("lat")
                .HasColumnType("text");

            entity.Property(e => e.Lng)
                .HasColumnName("lng")
                .HasColumnType("text");

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.HasIndex(e => e.StationId);
        });

        modelBuilder.Entity<FuelTypeEntity>(entity =>
        {
            entity.ToTable("fuel_types");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasColumnType("text");

            entity.Property(e => e.Name)
                .HasColumnName("name")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.StationId)
                .HasColumnName("station_id")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.BasePrice)
                .HasColumnName("base_price")
                .IsRequired();

            entity.Property(e => e.DiscountPrice)
                .HasColumnName("discount_price")
                .IsRequired();

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.HasIndex(e => e.StationId);
        });

        modelBuilder.Entity<FuelPackage>(entity =>
        {
            entity.ToTable("fuel_packages");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasColumnType("text");

            entity.Property(e => e.StationId)
                .HasColumnName("station_id")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.FuelTypeId)
                .HasColumnName("fuel_type_id")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.FuelName)
                .HasColumnName("fuel_name")
                .HasColumnType("text")
                .IsRequired();

            entity.Property(e => e.Liters)
                .HasColumnName("liters")
                .IsRequired();

            entity.Property(e => e.Price)
                .HasColumnName("price")
                .IsRequired();

            entity.Property(e => e.OriginalPrice)
                .HasColumnName("original_price")
                .IsRequired();

            entity.Property(e => e.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.HasIndex(e => e.StationId);
            entity.HasIndex(e => e.FuelTypeId);
        });

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


