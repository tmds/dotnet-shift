using Microsoft.Build.Locator;
using Cli;

MSBuildLocator.RegisterDefaults();

var commandLine = AppCommandLine.Create();

return await commandLine.InvokeAsync(args);
