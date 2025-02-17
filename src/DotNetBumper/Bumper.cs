﻿// Copyright (c) Martin Costello, 2024. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Diagnostics;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace MartinCostello.DotNetBumper;

[Command(
    Name = "dotnet bumper",
    FullName = "Upgrades projects to a newer version of .NET.")]
[HelpOption]
[VersionOptionFromMember(MemberName = nameof(GetVersion))]
internal partial class Bumper(ProjectUpgrader upgrader)
{
    [Argument(0, Description = "The path to directory containing a .NET 6+ project to be upgraded. If not specified, the current directory will be used.")]
    public string? ProjectPath { get; set; }

    [Option(
        "-c|--channel <CHANNEL>",
        Description = "The .NET release channel to upgrade to in the format \"major.minor\".")]
    public string? DotNetChannel { get; set; }

    [Option(
        "-t|--upgrade-type <TYPE>",
        Description = "The type of upgrade to perform.",
        ValueName = "TYPE")]
    public UpgradeType? UpgradeType { get; set; }

    [Option(
        "-test|--test",
        Description = "Test the upgrade by running dotnet test on completion.")]
    public bool TestUpgrade { get; set; }

    [Option(
        "-e|--warnings-as-errors",
        Description = "Treat any warnings encountered during the upgrade as errors.")]
    public bool WarningsAsErrors { get; set; }

    public static async Task<int> RunAsync(
        IAnsiConsole console,
        string[] args,
        Func<ILoggingBuilder, ILoggingBuilder> configureLogging,
        CancellationToken cancellationToken)
    {
        var logLevel = GetLogLevel(args);

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddUserSecrets<Bumper>()
            .Build();

        using var app = new CommandLineApplication<Bumper>();
        app.VerboseOption();

        using var serviceProvider = new ServiceCollection()
            .AddSingleton<IModelAccessor>(app)
            .AddSingleton<IPostConfigureOptions<UpgradeOptions>, UpgradePostConfigureOptions>()
            .AddProjectUpgrader(configuration, console, (builder) => configureLogging(builder).SetMinimumLevel(logLevel))
            .BuildServiceProvider();

        app.Conventions
            .UseDefaultConventions()
            .UseConstructorInjection(serviceProvider);

        return await app.ExecuteAsync(args, cancellationToken);
    }

    public static string GetVersion() => ProjectUpgrader.Version;

    public async Task<int> OnExecute(
        IAnsiConsole console,
        ILogger<Bumper> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            int status = await upgrader.UpgradeAsync(cancellationToken);

            stopwatch.Stop();

            console.WriteLine();
            console.MarkupLine($"Duration: [green]{stopwatch.Elapsed}.[/]");

            return status;
        }
        catch (Exception ex)
        {
            Log.UpgradeFailed(logger, ex);

            if (ex is OperationCanceledException oce && oce.CancellationToken == cancellationToken)
            {
                console.WriteLine();
                console.WriteWarningLine("Upgrade cancelled by user.");
                return 2;
            }
            else if (ex is OptionsValidationException)
            {
                console.WriteLine();
                console.WriteWarningLine(ex.Message);
            }
            else
            {
                console.WriteLine();
                console.WriteExceptionLine("Failed to upgrade project.", ex);
            }

            return 1;
        }
    }

    private static LogLevel GetLogLevel(string[] args)
    {
        bool verbose =
            args.Contains("-v", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);

        return verbose ? LogLevel.Debug : LogLevel.None;
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Error,
            Message = "Failed to upgrade project.")]
        public static partial void UpgradeFailed(ILogger logger, Exception exception);
    }
}
