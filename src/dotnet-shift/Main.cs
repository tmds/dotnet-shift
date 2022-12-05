using System.CommandLine;
using Microsoft.Build.Locator;

MSBuildLocator.RegisterDefaults();

new DotnetShiftCommand().Invoke(args);
