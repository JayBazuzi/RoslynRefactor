using Microsoft.Build.Locator;

// Must run before any MSBuild/Roslyn workspace types are touched.
MSBuildLocator.RegisterDefaults();

return await RoslynRefactor.CommandLine.RunAsync(args);
