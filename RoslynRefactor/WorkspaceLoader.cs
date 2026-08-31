using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynRefactor;

static class WorkspaceLoader
{
    public static async Task<(MSBuildWorkspace Workspace, Solution Solution)> OpenAsync(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension != ".sln" && extension != ".csproj")
        {
            throw new ArgumentException($"Expected a .sln or .csproj path, got: {path}");
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
                var solution = await workspace.OpenSolutionAsync(path);
                return (workspace, solution);
            default:
                throw new ArgumentException($"Expected a .sln or .csproj path, got: {path}");
        }
    }
}
