using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynRefactor;

static class WorkspaceLoader
{
    const string ExpectedPathMessage = "Expected a .sln, .slnx or .csproj path";

    public static async Task<(MSBuildWorkspace Workspace, Solution Solution)> OpenAsync(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension != ".sln" && extension != ".slnx" && extension != ".csproj")
        {
            throw new ArgumentException($"{ExpectedPathMessage}, got: {path}");
        }

        var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                Console.Error.WriteLine($"warning: {e.Diagnostic.Message}");
        });

        switch (extension)
        {
            case ".csproj":
                var project = await workspace.OpenProjectAsync(path);
                return (workspace, project.Solution);
            case ".sln":
            case ".slnx":
                var solution = await workspace.OpenSolutionAsync(path);
                return (workspace, solution);
            default:
                throw new ArgumentException($"{ExpectedPathMessage}, got: {path}");
        }
    }
}
