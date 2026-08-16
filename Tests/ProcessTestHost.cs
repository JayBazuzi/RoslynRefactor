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
    static readonly string ToolDllPath = typeof(RoslynRefactor.ICommand).Assembly.Location;

    static readonly string FixturesSourceDir = FindFixturesSourceDir();

    public static SampleCopy CreateSampleCopy()
    {
        var dest = Path.Combine(Path.GetTempPath(), "RoslynRefactorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        File.Copy(Path.Combine(FixturesSourceDir, "Sample.sln"), Path.Combine(dest, "Sample.sln"));
        CopyDirectory(Path.Combine(FixturesSourceDir, "Sample"), Path.Combine(dest, "Sample"));
        return new SampleCopy(Path.Combine(dest, "Sample.sln"), Path.Combine(dest, "Sample", "Program.cs"));
    }

    public static async Task<ProcessResult> RunAsync(params string[] args)
    {
        var psi = new ProcessStartInfo(@".\bin\Debug\net10.0\RoslynRefactor.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
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
