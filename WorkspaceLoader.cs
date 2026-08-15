using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynRefactor;

static class WorkspaceLoader
{
    public static async Task<(MSBuildWorkspace Workspace, Solution Solution)> OpenAsync(string path)
    {
        var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                Console.Error.WriteLine($"warning: {e.Diagnostic.Message}");
        });

        Solution solution;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".sln")
        {
            solution = await workspace.OpenSolutionAsync(path);
        }
        else if (ext == ".csproj")
        {
            var project = await workspace.OpenProjectAsync(path);
            solution = project.Solution;
        }
        else
        {
            throw new ArgumentException($"Expected a .sln or .csproj path, got: {path}");
        }

        return (workspace, solution);
    }
}
