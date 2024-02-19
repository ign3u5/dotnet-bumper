﻿// Copyright (c) Martin Costello, 2024. All rights reserved.
// Licensed under the Apache 2.0 license. See the LICENSE file in the project root for full license information.

using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace MartinCostello.DotNetBumper.Upgraders;

internal sealed partial class TargetFrameworkUpgrader(
    IAnsiConsole console,
    IOptions<UpgradeOptions> options,
    ILogger<TargetFrameworkUpgrader> logger) : FileUpgrader(console, options, logger)
{
    protected override string Action => "Upgrading target frameworks";

    protected override string InitialStatus => "Update TFMs";

    protected override IReadOnlyList<string> Patterns => ["*.csproj", "*.fsproj"];

    protected override async Task<UpgradeResult> UpgradeCoreAsync(
        UpgradeInfo upgrade,
        IReadOnlyList<string> fileNames,
        StatusContext context,
        CancellationToken cancellationToken)
    {
        Log.UpgradingTargetFramework(logger);

        UpgradeResult result = UpgradeResult.None;
        XmlWriterSettings? writerSettings = null;

        foreach (var filePath in fileNames)
        {
            var name = RelativeName(filePath);

            context.Status = StatusMessage($"Parsing {name}...");

            (var project, var encoding) = await LoadProjectAsync(filePath, cancellationToken);

            if (project is null)
            {
                result = result.Max(UpgradeResult.Warning);
                continue;
            }

            bool edited = false;
            string newTfm = upgrade.Channel.ToTargetFramework();

            var property = project
                .Root?
                .Element("PropertyGroup")?
                .Element("TargetFramework");

            if (property is not null &&
                !string.Equals(property.Value, newTfm, StringComparison.Ordinal) &&
                CanUpgrade(property.Value, upgrade.Channel))
            {
                property.SetValue(newTfm);
                edited = true;
            }

            property = project
                .Root?
                .Element("PropertyGroup")?
                .Element("TargetFrameworks");

            if (property is not null &&
                !property.Value.Contains(newTfm, StringComparison.Ordinal) &&
                CanUpgrade(property.Value, upgrade.Channel))
            {
                property.SetValue($"{property.Value};{newTfm}");
                edited = true;
            }

            if (edited)
            {
                context.Status = StatusMessage($"Updating {name}...");

                // Ensure that the user's own formatting is preserved
                string xml = project.ToString(SaveOptions.DisableFormatting);

                writerSettings ??= new XmlWriterSettings()
                {
                    Async = true,
                    Indent = true,
                    OmitXmlDeclaration = true,
                };

                await File.WriteAllTextAsync(
                    filePath,
                    xml,
                    encoding ?? Encoding.UTF8,
                    cancellationToken);

                result = result.Max(UpgradeResult.Success);
            }
        }

        return result;
    }

    private static bool CanUpgrade(string targetFrameworks, Version candidate)
    {
        var tfms = targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var tfm in tfms)
        {
            var version = tfm.ToVersion();

            if (version is not null && version > candidate)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<(XDocument? Project, Encoding? Encoding)> LoadProjectAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        try
        {
            var project = await XDocument.LoadAsync(reader, LoadOptions.PreserveWhitespace, cancellationToken);
            return (project, reader.CurrentEncoding);
        }
        catch (Exception ex)
        {
            Log.FailedToLoadProject(logger, filePath, ex);
            return default;
        }
    }

    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    private static partial class Log
    {
        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Debug,
            Message = "Upgrading target framework moniker.")]
        public static partial void UpgradingTargetFramework(ILogger logger);

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Warning,
            Message = "Failed to parse project file {FileName}.")]
        public static partial void FailedToLoadProject(ILogger logger, string fileName, Exception exception);
    }
}
