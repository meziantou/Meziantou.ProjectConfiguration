using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Meziantou.Framework;
using Xunit.Abstractions;
using static System.Net.Mime.MediaTypeNames;

namespace Meziantou.DotNet.CodingStandard.Tests;

public abstract class CodingStandardTests(PackageFixture fixture, ITestOutputHelper testOutputHelper, string dotnetChannel) : IClassFixture<PackageFixture>
{
    private async Task<string> CreateGlobalJsonContent()
    {
        var version = await DotnetVersions.GetLatestVersionAsync(dotnetChannel);
        return $$"""
        {
            "sdk": {
                "version": "{{version}}"
            }
        }
        """;
    }

    [Fact]
    public async Task BannedSymbolsAreReported()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper, this);
        project.AddCsprojFile();
        project.AddFile("sample.cs", """_ = System.DateTime.Now;""");
        var data = await project.BuildAndGetOutput();
        Assert.True(data.HasWarning("RS0030"));
    }

    [Fact]
    public async Task WarningsAsErrorOnGitHubActions()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper, this);
        project.AddCsprojFile();
        project.AddFile("sample.cs", """_ = System.DateTime.Now;""");
        var data = await project.BuildAndGetOutput(["/p:GITHUB_ACTIONS=true"]);
        Assert.True(data.HasError("RS0030"));
    }

    [Fact]
    public async Task NamingConvention_Invalid()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper, this);
        project.AddCsprojFile();
        project.AddFile("sample.cs", """
            _ = "";

            class Sample
            {
                private readonly int field;

                public Sample(int a) => field = a;

                public int A() => field;
            }
            """);
        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);
        Assert.True(data.HasError("IDE1006"));
    }

    [Fact]
    public async Task NamingConvention_Valid()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper, this);
        project.AddCsprojFile();
        project.AddFile("sample.cs", """
            _ = "";

            class Sample
            {
                private int _field;
            }
            """);
        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);
        Assert.False(data.HasError("IDE1006"));
        Assert.False(data.HasWarning("IDE1006"));
    }

    [Fact]
    public async Task CodingStyle_UseExpression()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper, this);
        project.AddCsprojFile();
        project.AddFile("Program.cs", """
            A();

            static void A()
            {
                System.Console.WriteLine();
            }
            """);
        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);
        Assert.False(data.HasWarning());
        Assert.False(data.HasError());
    }

    [Fact]
    public async Task CodingStyle_ExpressionIsNeverUsed()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper, this);
        project.AddCsprojFile();
        project.AddFile("Program.cs", """
            var sb = new System.Text.StringBuilder();
            sb.AppendLine();

            """);
        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);
        Assert.False(data.HasWarning());
        Assert.False(data.HasError());
    }

    [Fact]
    public async Task LocalEditorConfigCanOverrideSettings()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper, this);
        project.AddCsprojFile();
        project.AddFile("Program.cs", """
            _ = "";

            class Sample
            {
                public static void A()
                {
                    B();

                    static void B()
                    {
                        System.Console.WriteLine();
                    }
                }
            }
            
            """);
        project.AddFile(".editorconfig", """
            [*.cs]      
            csharp_style_expression_bodied_local_functions = true:warning
            """);

        var data = await project.BuildAndGetOutput(["--configuration", "Debug"]);
        Assert.True(data.HasWarning());
        Assert.False(data.HasError());
    }

    [Fact]
    public async Task NuGetAuditIsReportedAsErrorOnGitHubActions()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper, this);
        project.AddCsprojFile(nuGetPackages: [("System.Net.Http", "4.3.3")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput(["/p:GITHUB_ACTIONS=true"]);
        Assert.True(data.OutputContains("error NU1903", StringComparison.Ordinal));
        Assert.Equal(1, data.ExitCode);
    }

    [Fact]
    public async Task NuGetAuditIsReportedAsWarning()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper, this);
        project.AddCsprojFile(nuGetPackages: [("System.Net.Http", "4.3.3")]);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        var data = await project.BuildAndGetOutput();
        Assert.True(data.OutputContains("warning NU1903", StringComparison.Ordinal));
        Assert.True(data.OutputDoesNotContain("error NU1903", StringComparison.Ordinal));
        Assert.Equal(0, data.ExitCode);
    }

    [Fact]
    public async Task MSBuildWarningsAsError()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper, this);
        project.AddFile("Program.cs", """
            System.Console.WriteLine();
            
            """);
        project.AddCsprojFile(additionalProjectElements: [
            new XElement("Target", new XAttribute("Name", "Custom"), new XAttribute("BeforeTargets", "Build"),
                new XElement("Warning", new XAttribute("Text", "CustomWarning")))]);
        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);

        Assert.True(data.OutputContains("error : CustomWarning"));
    }

    [Fact]
    public async Task MSBuildWarningsAsError_NotEnableOnDebug()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper, this);
        project.AddFile("Program.cs", """System.Console.WriteLine();""");
        project.AddCsprojFile(additionalProjectElements: [
            new XElement("Target", new XAttribute("Name", "Custom"), new XAttribute("BeforeTargets", "Build"),
                new XElement("Warning", new XAttribute("Text", "CustomWarning")))]);
        var data = await project.BuildAndGetOutput(["--configuration", "Debug"]);

        Assert.True(data.OutputContains("warning : CustomWarning"));
    }

    [Fact]
    public async Task CA1708_NotReportedForFileLocalTypes()
    {
        await using var project = new ProjectBuilder(fixture, testOutputHelper, this);
        project.AddCsprojFile();
        project.AddFile("Sample1.cs", """
            System.Console.WriteLine()l

            class A {}
            
            file class Sample
            {
            }
            """);
        project.AddFile("Sample2.cs", """
            class B {}

            file class sample
            {
            }
            """);
        var data = await project.BuildAndGetOutput(["--configuration", "Release"]);
        Assert.False(data.HasError("CA1708"));
        Assert.False(data.HasWarning("CA1708"));
    }

    private sealed class ProjectBuilder : IAsyncDisposable
    {
        private const string SarifFileName = "BuildOutput.sarif";

        private readonly TemporaryDirectory _directory;
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly CodingStandardTests _test;

        public ProjectBuilder(PackageFixture fixture, ITestOutputHelper testOutputHelper, CodingStandardTests test)
        {
            _testOutputHelper = testOutputHelper;
            _test = test;
            _directory = TemporaryDirectory.Create();
            _directory.CreateTextFile("NuGet.config", $"""
                <configuration>
                  <config>
                    <add key="globalPackagesFolder" value="{fixture.PackageDirectory}/packages" />
                  </config>
                  <packageSources>
                    <clear />    
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                    <add key="TestSource" value="{fixture.PackageDirectory}" />
                  </packageSources>
                  <packageSourceMapping>
                    <packageSource key="nuget.org">
                        <package pattern="*" />
                    </packageSource>
                    <packageSource key="TestSource">
                        <package pattern="Meziantou.DotNet.CodingStandard" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>
                """);
        }

        public ProjectBuilder AddFile(string relativePath, string content)
        {
            File.WriteAllText(_directory.FullPath / relativePath, content);
            return this;
        }

        public ProjectBuilder AddCsprojFile((string Name, string Value)[] properties = null, (string Name, string Version)[] nuGetPackages = null, XElement[] additionalProjectElements = null)
        {
            var propertiesElement = new XElement("PropertyGroup");
            if (properties != null)
            {
                foreach (var prop in properties)
                {
                    propertiesElement.Add(new XElement(prop.Name), prop.Value);
                }
            }

            var packagesElement = new XElement("ItemGroup");
            if (nuGetPackages != null)
            {
                foreach (var package in nuGetPackages)
                {
                    packagesElement.Add(new XElement("PackageReference", new XAttribute("Include", package.Name), new XAttribute("Version", package.Version)));
                }
            }

            var content = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <ComputeNETCoreBuildOutputFiles>false</ComputeNETCoreBuildOutputFiles>
                    <OutputType>exe</OutputType>
                    <TargetFramework>net$(NETCoreAppMaximumVersion)</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <ErrorLog>{SarifFileName},version=2.1</ErrorLog>
                  </PropertyGroup>
                  {propertiesElement}
                  {packagesElement}
                  <ItemGroup>
                    <PackageReference Include="Meziantou.DotNet.CodingStandard" Version="*" />
                  </ItemGroup>
                  {string.Join('\n', additionalProjectElements?.Select(e => e.ToString()) ?? [])}
                </Project>                
                """;

            File.WriteAllText(_directory.FullPath / "test.csproj", content);
            return this;
        }

        public async Task<BuildResult> BuildAndGetOutput(string[] buildArguments = null)
        {
            string dotnetVersion = await _test.CreateGlobalJsonContent();
            _testOutputHelper.WriteLine("Global.json version:\n" + dotnetVersion);
            _directory.CreateTextFile("global.json", dotnetVersion);

            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = _directory.FullPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("build");
            if (buildArguments != null)
            {
                foreach (var arg in buildArguments)
                {
                    psi.ArgumentList.Add(arg);
                }
            }

            // Remove parent environment variables
            psi.Environment.Remove("CI");
            psi.Environment.Remove("GITHUB_ACTIONS");

            var result = await psi.RunAsTaskAsync();
            _testOutputHelper.WriteLine("Process exit code: " + result.ExitCode);
            _testOutputHelper.WriteLine(result.Output.ToString());

            var bytes = File.ReadAllBytes(_directory.FullPath / SarifFileName);
            var sarif = JsonSerializer.Deserialize<SarifFile>(bytes);
            _testOutputHelper.WriteLine("Sarif result:\n" + string.Join("\n", sarif.AllResults().Select(r => r.ToString())));
            return new BuildResult(result.ExitCode, result.Output, sarif);
        }

        public ValueTask DisposeAsync() => _directory.DisposeAsync();
    }

    private sealed record BuildResult(int ExitCode, ProcessOutputCollection ProcessOutput, SarifFile SarifFile)
    {
        public bool OutputContains(string value, StringComparison stringComparison = StringComparison.Ordinal) => ProcessOutput.Any(line => line.Text.Contains(value, stringComparison));
        public bool OutputDoesNotContain(string value, StringComparison stringComparison = StringComparison.Ordinal) => !ProcessOutput.Any(line => line.Text.Contains(value, stringComparison));

        public bool HasError() => SarifFile.AllResults().Any(r => r.Level == "error");
        public bool HasError(string ruleId) => SarifFile.AllResults().Any(r => r.Level == "error" && r.RuleId == ruleId);
        public bool HasWarning() => SarifFile.AllResults().Any(r => r.Level == "warning");
        public bool HasWarning(string ruleId) => SarifFile.AllResults().Any(r => r.Level == "warning" && r.RuleId == ruleId);
        public bool HasNote(string ruleId) => SarifFile.AllResults().Any(r => r.Level == "note" && r.RuleId == ruleId);
    }

    private sealed class SarifFile
    {
        [JsonPropertyName("runs")]
        public SarifFileRun[] Runs { get; set; }

        public IEnumerable<SarifFileRunResult> AllResults() => Runs.SelectMany(r => r.Results);
    }

    private sealed class SarifFileRun
    {
        [JsonPropertyName("results")]
        public SarifFileRunResult[] Results { get; set; }
    }

    private sealed class SarifFileRunResult
    {
        [JsonPropertyName("ruleId")]
        public string RuleId { get; set; }

        [JsonPropertyName("level")]
        public string Level { get; set; }

        [JsonPropertyName("message")]
        public SarifFileRunResultMessage Message { get; set; }

        public override string ToString()
        {
            return $"{Level}:{RuleId} {Message}";
        }
    }

    private sealed class SarifFileRunResultMessage
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }

        public override string ToString()
        {
            return Text;
        }
    }
}

public sealed class CodingStandardTestsNet8_0(PackageFixture fixture, ITestOutputHelper testOutputHelper) : CodingStandardTests(fixture, testOutputHelper, "8.0")
{

}

public sealed class CodingStandardTestsNet9_0(PackageFixture fixture, ITestOutputHelper testOutputHelper) : CodingStandardTests(fixture, testOutputHelper, "9.0")
{
}