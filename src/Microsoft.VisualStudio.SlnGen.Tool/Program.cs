// Copyright (c) Microsoft Corporation.
//
// Licensed under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Microsoft.VisualStudio.SlnGen
{
    /// <summary>
    /// Represents the main logic for the program.
    /// </summary>
    public static class Program
    {
        private static readonly IEnvironmentProvider EnvironmentProvider = SystemEnvironmentProvider.Instance;

        static Program()
        {
            if (EnvironmentProvider.GetCommandLineArgs().Any(i => i.Equals("--debug", StringComparison.OrdinalIgnoreCase)))
            {
                Debugger.Launch();
            }
        }

        private enum ExitCode
        {
            Success = 0,
            DevelopmentEnvironmentNotFound = 1,
            UnsupportedNETSdk = 2,
            UnknownNETSdk = 3,
            SlnGenNotFound = 4,
            UnsupportedMSBuild = 5,
            UnhandledException = -1,
        }

        /// <summary>
        /// Gets or sets a <see cref="TextWriter" /> to write errors to.
        /// </summary>
        internal static TextWriter Error { get; set; } = Console.Error;

        /// <summary>
        /// Gets or sets a <see cref="TextWriter" /> to write output to.
        /// </summary>
        internal static TextWriter Out { get; set; } = Console.Out;

        /// <summary>
        /// Executes the program.
        /// </summary>
        /// <param name="args">The command-line arguments.</param>
        /// <returns>0 if the program ran successfully, otherwise a non-zero number.</returns>
        public static int Main(string[] args)
        {
            try
            {
                // Determine the path to the current slngen.exe
                FileInfo thisAssemblyFileInfo = new FileInfo(Assembly.GetExecutingAssembly().Location);

                DevelopmentEnvironment developmentEnvironment = DevelopmentEnvironment.LoadCurrentDevelopmentEnvironment(EnvironmentProvider);

                if (!developmentEnvironment.Success)
                {
                    foreach (string error in developmentEnvironment.Errors)
                    {
                        Utility.WriteError(Error, error);
                    }

                    // If the development environment couldn't be determined, then we can't proceed since we have no idea what framework to use or MSBuild/dotnet are not available.
                    return (int)ExitCode.DevelopmentEnvironmentNotFound;
                }

                bool useDotnet = developmentEnvironment.MSBuildExe == null;

                // Default to .NET Framework on Windows if MSBuild.exe is on the PATH
                string framework = Utility.RunningOnWindows && !useDotnet ? "net472" : string.Empty;

                if (useDotnet)
                {
                    switch (developmentEnvironment.DotNetSdkMajorVersion)
                    {
                        case "3":
                        case "5":
                        case "6":
                        case "7":
                            Utility.WriteError(Error, "The currently configured .NET SDK {0} is not supported, SlnGen requires .NET SDK 8 or greater.", developmentEnvironment.DotNetSdkVersion);

                            return (int)ExitCode.UnsupportedNETSdk;

                        case "8":
                            framework = "net8.0";
                            break;

                        case "9":
                            framework = "net9.0";
                            break;

                        case "10":
                            framework = "net10.0";
                            break;

                        default:
                            Utility.WriteError(Error, "SlnGen does not currently support the .NET SDK {0} defined by in global.json.  Please update to the latest version and if you still get this error message, file an issue at https://github.com/microsoft/slngen/issues/new so it can be added.", developmentEnvironment.DotNetSdkVersion);

                            return (int)ExitCode.UnknownNETSdk;
                    }
                }
                else
                {
                    FileVersionInfo msBuildVersionInfo = FileVersionInfo.GetVersionInfo(developmentEnvironment.MSBuildExe.FullName);

                    if (msBuildVersionInfo.FileMajorPart < 17)
                    {
                        Utility.WriteError(Error, "The currently configured MSBuild {0} is not supported, SlnGen requires MSBuild 17.0 or greater.", msBuildVersionInfo.FileMajorPart);

                        return (int)ExitCode.UnsupportedMSBuild;
                    }

                    framework = "net472";
                }

                FileInfo slnGenFileInfo = new FileInfo(Path.Combine(thisAssemblyFileInfo.DirectoryName!, "..", thisAssemblyFileInfo.DirectoryName!.EndsWith("any") ? ".." : string.Empty, "slngen", framework, useDotnet ? "slngen.dll" : "slngen.exe"));

                if (!slnGenFileInfo.Exists)
                {
                    Utility.WriteError(Error, $"SlnGen not found: {slnGenFileInfo.FullName}");

                    return (int)ExitCode.SlnGenNotFound;
                }

                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = useDotnet ? "dotnet" : slnGenFileInfo.FullName,
                    },
                };

                if (useDotnet)
                {
                    process.StartInfo.ArgumentList.Add(slnGenFileInfo.FullName);
                }

                foreach (string argument in EnvironmentProvider.GetCommandLineArgs().Skip(1))
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }

                process.Start();

                process.WaitForExit();

                return process.ExitCode;
            }
            catch (Exception e)
            {
                Utility.WriteError(Error, $"Unhandled exception: {e}");

                return (int)ExitCode.UnhandledException;
            }
        }
    }
}