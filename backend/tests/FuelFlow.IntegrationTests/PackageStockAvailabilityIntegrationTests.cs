        (await availability.FindPackageAsync(false, "okko", "okko-95", 10m))!.PackageId
    .Should().Be(PackageIds.For("okko-95", DataKinds.Real, 10m));
        (await availability.FindPackageAsync(true, "okko", "okko-95", 10m))!.PackageId
            .Should().Be(PackageIds.For("okko-95", DataKinds.Qa, 10m));
        var chosen = await availability.FindPackageAsync(false, "okko", "okko-95", 20m);
        chosen!.PackageId.Should().Be(PackageIds.For("okko-95", DataKinds.Real, 20m));
        fuel.SupplierPricePerLiter = 30m;
        fuel.UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-updatedMinutesAgo);
