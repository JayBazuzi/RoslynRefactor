using System.Diagnostics;

namespace RoslynRefactor.Tests;

/// <summary>
/// Runs the built RoslynRefactor tool as a separate process against a scratch copy of the
/// Fixtures/Sample solution, so tests exercise the exact same code path (MSBuildWorkspace,
/// reflection into Roslyn-internal providers, TryApplyChanges) that a real user hits.
/// </summary>
static class ProcessTestHost
{
    // The ProjectReference to RoslynRefactor.csproj guarantees this assembly is built and
    // available alongside the test assembly; using its location avoids hardcoding a path.
    static readonly string ToolDllPath = typeof(RoslynRefactor.ICommand).Assembly.Location.Replace(".dll", OperatingSystem.IsWindows() ? ".exe" : "");

    public static readonly string FixturesSourceDir = FindFixturesSourceDir();

    public static SampleCopy CreateSampleCopy()
    {
        var dest = Path.Combine(Path.GetTempPath(), "RoslynRefactorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        File.Copy(Path.Combine(FixturesSourceDir, "Sample.sln"), Path.Combine(dest, "Sample.sln"));
        CopyDirectory(Path.Combine(FixturesSourceDir, "Sample"), Path.Combine(dest, "Sample"));
        return new SampleCopy(Path.Combine(dest, "Sample.sln"), Path.Combine(dest, "Sample", "Program.cs"));
    }

    /// <summary>
    /// Writes <paramref name="source"/> to Program.cs in a scratch project of its own, for tests
    /// that need a specific bit of C# rather than the shared Fixtures/Sample program.
    /// </summary>
    public static ScratchProject CreateProjectWithSource(string source)
    {
        var dest = Path.Combine(Path.GetTempPath(), "RoslynRefactorTests_" + Guid.NewGuid().ToString("N"), "Sample");
        Directory.CreateDirectory(dest);

        // A library, not an exe, so sources that have no Main are still a legal project.
        var projectPath = Path.Combine(dest, "Sample.csproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>

            </Project>
            """);

        var programFilePath = Path.Combine(dest, "Program.cs");
        File.WriteAllText(programFilePath, source);

        return new ScratchProject(projectPath, programFilePath);
    }

    public static async Task<ProcessResult> RunAsync(params string[] args)
    {
        var result = await RunAllowingFailureAsync(args);
        Assert.Equal(0, result.ExitCode);

        return result;
    }

    /// <summary>
    /// Like <see cref="RunAsync"/>, but hands back the result even when the tool exits non-zero, so
    /// tests can assert on refusals (exit code plus the message on stderr) as well as on successes.
    /// </summary>
    public static async Task<ProcessResult> RunAllowingFailureAsync(params string[] args)
    {
        ProcessStartInfo psi = CreateRoslynRefactorProcessStartInfo(args);
        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static ProcessStartInfo CreateRoslynRefactorProcessStartInfo(string[] args)
    {
        var psi = new ProcessStartInfo(ToolDllPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    static string FindFixturesSourceDir()
    {
        // AppContext.BaseDirectory is .../Tests/bin/<Config>/<TFM>/; walk up to the Tests dir.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "Tests")
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException($"Could not locate the 'Tests' directory above {AppContext.BaseDirectory}");
        }

        return Path.Combine(dir.FullName, "Fixtures");
    }

    static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(subDir, Path.Combine(destDir, Path.GetFileName(subDir)));
        }
    }
}

record ProcessResult(int ExitCode, string StdOut, string StdErr);

record SampleCopy(string SolutionPath, string ProgramFilePath);

record ScratchProject(string ProjectPath, string ProgramFilePath);
