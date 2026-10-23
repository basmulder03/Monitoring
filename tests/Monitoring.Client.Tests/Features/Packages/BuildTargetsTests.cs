using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Monitoring.Client.Features.Packages;
using Shouldly;
using Xunit;

namespace Monitoring.Client.Tests.Features.Packages;

/// <summary>Builds a small application that uses the build targets of the client, as an application referencing the NuGet package does.</summary>
public class BuildTargetsTests
{
    [Fact]
    public async Task An_application_built_with_the_targets_carries_the_list_of_its_packages()
    {
        // Arrange
        using var project = new TempProject().WithPackage("Microsoft.Extensions.Hosting.Abstractions", "10.0.13");

        // Act
        var (exitCode, output) = await project.BuildAsync("Monitoring.Client.targets");
        var report = PackageList.Read(Assembly.LoadFrom(project.Assembly));

        // Assert
        exitCode.ShouldBe(0, output);
        var hosting = report.Packages.Single(p => p.Name == "Microsoft.Extensions.Hosting.Abstractions");
        hosting.Direct.ShouldBeTrue();
        hosting.Version.ShouldBe("10.0.13");
        report.Packages.Single(p => p.Name == "Microsoft.Extensions.DependencyInjection.Abstractions").Direct.ShouldBeFalse();
    }

    [Fact]
    public async Task A_web_application_carries_the_list_too_although_its_sdk_names_the_resource_differently()
    {
        // Arrange
        using var project = new TempProject().WithSdk("Microsoft.NET.Sdk.Web").WithPackage("Serilog", "4.2.0");

        // Act
        var (exitCode, output) = await project.BuildAsync("Monitoring.Client.targets");
        var report = PackageList.Read(Assembly.LoadFrom(project.Assembly));

        // Assert
        exitCode.ShouldBe(0, output);
        report.Packages.Single(p => p.Name == "Serilog").Direct.ShouldBeTrue();
    }

    [Fact]
    public async Task The_list_is_written_with_the_other_intermediate_files_and_not_into_the_project()
    {
        // Arrange
        using var project = new TempProject().WithPackage("Microsoft.Extensions.Hosting.Abstractions", "10.0.13");

        // Act
        var (exitCode, output) = await project.BuildAsync("Monitoring.Client.targets");

        // Assert
        exitCode.ShouldBe(0, output);
        File.Exists(Path.Combine(project.Directory, "monitoring.packages.txt")).ShouldBeFalse();
        Directory.EnumerateFiles(Path.Combine(project.Directory, "obj"), "monitoring.packages.txt", SearchOption.AllDirectories).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task An_application_can_turn_the_list_off()
    {
        // Arrange
        using var project = new TempProject().WithPackage("Microsoft.Extensions.Hosting.Abstractions", "10.0.13").WithProperty("MonitoringReportPackages", "false");

        // Act
        var (exitCode, output) = await project.BuildAsync("Monitoring.Client.targets");
        var report = PackageList.Read(Assembly.LoadFrom(project.Assembly));

        // Assert
        exitCode.ShouldBe(0, output);
        report.Packages.ShouldBeEmpty();
    }

    private static string RepositoryRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "..", ".."));

    private sealed class TempProject : IDisposable
    {
        private readonly string _directory = System.IO.Directory.CreateTempSubdirectory("packages-").FullName;
        private string _sdk = "Microsoft.NET.Sdk";

        public string Directory => _directory;

        public TempProject WithSdk(string sdk)
        {
            _sdk = sdk;
            return this;
        }
        private readonly List<string> _packages = [];
        private readonly List<string> _properties = [];

        // A name of its own, because an assembly name can only be loaded once into the test process.
        private readonly string _name = $"app{Guid.NewGuid():N}";

        public string Assembly => Path.Combine(_directory, "out", $"{_name}.dll");

        public TempProject WithPackage(string name, string version)
        {
            _packages.Add($"<PackageReference Include=\"{name}\" Version=\"{version}\" />");
            return this;
        }

        public TempProject WithProperty(string name, string value)
        {
            _properties.Add($"<{name}>{value}</{name}>");
            return this;
        }

        public async Task<(int ExitCode, string Output)> BuildAsync(string targets)
        {
            var targetsFile = Path.Combine(RepositoryRoot(), "src", "Monitoring.Client", "build", targets);
            await File.WriteAllTextAsync(Path.Combine(_directory, "app.csproj"), $"""
                <Project Sdk="{_sdk}">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>{_name}</AssemblyName>
                    {string.Join("\n    ", _properties)}
                  </PropertyGroup>
                  <ItemGroup>{string.Join("\n    ", _packages)}</ItemGroup>
                  <Import Project="{targetsFile}" />
                </Project>
                """, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(_directory, "Program.cs"), "System.Console.WriteLine();", TestContext.Current.CancellationToken);

            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", "build app.csproj -o out --nologo")
            {
                WorkingDirectory = _directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            return (process.ExitCode, await output + await errors);
        }

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
                // A temporary folder that stays behind is harmless.
            }
        }
    }
}
