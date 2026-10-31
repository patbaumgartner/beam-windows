using System.Xml.Linq;

namespace Beam.Architecture.Tests;

/// <summary>
/// Guards the dependency rules that make the codebase testable. These tests read the
/// project files (not the compiled metadata) so a reference cannot sneak back in.
/// </summary>
public class DependencyRuleTests
{
    private static string RepoRoot
    {
        get
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Beam.sln")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName
                ?? throw new InvalidOperationException("Could not locate Beam.sln above the test output directory.");
        }
    }

    private static string ProjectPath(string name) =>
        Path.Combine(RepoRoot, "src", name, name + ".csproj");

    private static string[] References(string projectName)
    {
        XDocument doc = XDocument.Load(ProjectPath(projectName));
        return doc.Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Path.GetFileNameWithoutExtension(v!.Replace('\\', '/')))
            .OrderBy(v => v)
            .ToArray();
    }

    [Fact]
    public void Core_HasNoProjectReferences()
    {
        Assert.Empty(References("Beam.Core"));
    }

    [Fact]
    public void App_ReferencesOnlyCore()
    {
        Assert.Equal(new[] { "Beam.Core" }, References("Beam.App"));
    }

    [Fact]
    public void Windows_ReferencesOnlyCore()
    {
        Assert.Equal(new[] { "Beam.Core" }, References("Beam.Windows"));
    }

    [Fact]
    public void Tray_ReferencesCoreWindowsAndApp()
    {
        Assert.Equal(
            new[] { "Beam.App", "Beam.Core", "Beam.Windows" },
            References("Beam.Tray"));
    }

    [Fact]
    public void Cli_ReferencesCoreWindowsAndApp_ButNeverTray()
    {
        string[] references = References("Beam.Cli");

        Assert.Equal(new[] { "Beam.App", "Beam.Core", "Beam.Windows" }, references);
        Assert.DoesNotContain("Beam.Tray", references);
    }

    [Theory]
    [InlineData("Beam.Core")]
    [InlineData("Beam.App")]
    public void PortableLayers_TargetPlainNet(string projectName)
    {
        XDocument doc = XDocument.Load(ProjectPath(projectName));
        string? framework = doc.Descendants("TargetFramework").FirstOrDefault()?.Value;

        Assert.Equal("net10.0", framework);
    }

    [Theory]
    [InlineData("Beam.Core")]
    [InlineData("Beam.App")]
    public void PortableLayers_ContainNoWindowsOnlySource(string projectName)
    {
        string dir = Path.GetDirectoryName(ProjectPath(projectName))!;

        foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            Assert.DoesNotContain("System.Windows.Forms", text, StringComparison.Ordinal);
            Assert.DoesNotContain("[DllImport", text, StringComparison.Ordinal);
            Assert.DoesNotContain("[LibraryImport", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.Win32", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WindowsAdapter_DoesNotDependOnWinForms()
    {
        string dir = Path.Combine(RepoRoot, "src", "Beam.Windows");

        foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(dir, file);
            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(file);

            // Match real usage, not documentation mentions of the WinForms type name.
            Assert.DoesNotContain("using System.Windows.Forms;", text, StringComparison.Ordinal);
            Assert.DoesNotContain("UseWindowsForms", text, StringComparison.Ordinal);
        }
    }
}
