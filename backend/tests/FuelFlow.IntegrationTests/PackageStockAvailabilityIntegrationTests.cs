        var chosen = await availability.FindPackageAsync(false, "okko", "okko-95", 20m);
        chosen!.PackageId.Should().Be(PackageIds.For("okko-95", DataKinds.Real, 20m));
