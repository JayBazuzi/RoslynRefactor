using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynRefactor;

static class WorkspaceLoader
{
    public static async Task<(MSBuildWorkspace Workspace, Solution Solution)> OpenAsync(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() != ".sln")
        {
            throw new ArgumentException($"Expected a .sln path, got: {path}");
        }

        var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                Console.Error.WriteLine($"warning: {e.Diagnostic.Message}");
        });

        var solution = await workspace.OpenSolutionAsync(path);
        return (workspace, solution);
    }
}
