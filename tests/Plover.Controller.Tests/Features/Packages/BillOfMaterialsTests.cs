using Plover.Controller.Features.Packages;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Packages;

public class BillOfMaterialsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 30, 15, TimeSpan.Zero);

    [Fact]
    public void ForService_describes_the_service_and_lists_its_packages_with_package_urls()
    {
        // Arrange
        var service = new ServicePackages("shop", "orders", "1.2.3", [new PackageReport("Serilog", "4.0.0", true), new PackageReport("Json", "13.0.1", false)]);

        // Act
        var bom = BillOfMaterials.ForService(service, Now);

        // Assert
        bom.BomFormat.ShouldBe("CycloneDX");
        bom.SpecVersion.ShouldBe("1.5");
        bom.Metadata.Timestamp.ShouldBe("2026-11-02T08:30:15Z");
        bom.Metadata.Component.ShouldBe(new CycloneDxComponent("application", "orders", "1.2.3", "service:shop/orders"));
        bom.Components.Select(c => c.Purl).ShouldBe(["pkg:nuget/Json@13.0.1", "pkg:nuget/Serilog@4.0.0"]);
        bom.Components.ShouldAllBe(c => c.Type == "library" && c.Scope == "required");
        bom.SerialNumber.ShouldStartWith("urn:uuid:");
    }

    [Fact]
    public void ForLandscape_lists_each_package_and_version_once_whoever_uses_it()
    {
        // Arrange
        var services = new[]
        {
            new ServicePackages("shop", "orders", "1", [new PackageReport("Serilog", "4.0.0", true), new PackageReport("Json", "13.0.1", false)]),
            new ServicePackages("shop", "payments", "1", [new PackageReport("serilog", "4.0.0", true), new PackageReport("Serilog", "3.1.0", true)]),
        };

        // Act
        var bom = BillOfMaterials.ForLandscape(services, Now);

        // Assert
        bom.Metadata.Component.Type.ShouldBe("platform");
        bom.Components.Select(c => $"{c.Name} {c.Version}").ShouldBe(["Json 13.0.1", "Serilog 3.1.0", "Serilog 4.0.0"]);
    }

    [Fact]
    public void Each_document_gets_its_own_serial_number()
    {
        // Arrange
        var service = new ServicePackages("shop", "orders", "1", [new PackageReport("Serilog", "4.0.0", true)]);

        // Act
        var first = BillOfMaterials.ForService(service, Now);
        var second = BillOfMaterials.ForService(service, Now);

        // Assert
        first.SerialNumber.ShouldNotBe(second.SerialNumber);
    }
}
