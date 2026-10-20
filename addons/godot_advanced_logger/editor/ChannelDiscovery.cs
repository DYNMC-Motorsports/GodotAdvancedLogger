#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using GodotAdvancedLogger.addons.godot_advanced_logger.core;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.editor;

/// <summary>
/// Finds the channels a project actually uses, so they can be muted with a click instead
/// of typed from memory. Two sources are combined: a scan of the project's scripts and
/// the most recent log files written by the File and JSON Lines outputs.
/// </summary>
internal static class ChannelDiscovery
{
    private const int MaxSourceFiles = 5_000;
    private const long MaxSourceFileBytes = 512 * 1024;
    private const int MaxLogFiles = 6;
    private const int MaxLogLinesPerFile = 50_000;
    private const int MaxLocationsPerChannel = 5;

    /// <summary>C#: <c>new ContextLogger("Combat")</c>, <c>: ContextLogger("Combat")</c>, <c>channel: "Combat"</c>.</summary>
    private static readonly Regex[] CSharpPatterns =
    {
        new(@"\bContextLogger\s*\(\s*""((?:[^""\\]|\\.)+)""", RegexOptions.Compiled),
        new(@"\bchannel\s*:\s*""((?:[^""\\]|\\.)+)""", RegexOptions.Compiled),
    };

    /// <summary>GDScript: the trailing string argument of <c>GodotLogger.info("msg", {...}, "Combat")</c>, and <c>mute_channel("Combat")</c>.</summary>
    private static readonly Regex[] GdScriptPatterns =
    {
        new(@"\bGodotLogger\s*\.\s*(?:debug|info|warning|error)\s*\(.+,\s*[""']([^""'\r\n]+)[""']\s*\)", RegexOptions.Compiled),
        new(@"\bGodotLogger\s*\.\s*(?:un)?mute_channel\s*\(\s*[""']([^""'\r\n]+)[""']", RegexOptions.Compiled),
    };

    /// <summary>C# subclass of ContextLogger: <c>public CombatLogger() : base("Combat") { }</c>.</summary>
    private static readonly Regex DerivesFromContextLogger = new(@":\s*(?:[\w.]+\.)?ContextLogger\b", RegexOptions.Compiled);
    private static readonly Regex BaseConstructorChannel = new(@"\)\s*:\s*base\s*\(\s*""((?:[^""\\]|\\.)+)""\s*\)", RegexOptions.Compiled);

    private static readonly Regex JsonChannel = new(@"""channel""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    private static readonly Regex TextChannel = new(
        @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\t(?:DEBUG|INFO|WARNING|ERROR)\t([^\t]+)\t",
        RegexOptions.Compiled);

    public sealed class ChannelInfo
    {
        public string Name { get; init; }
        public List<string> CodeLocations { get; } = new();
        public int CodeReferences { get; set; }
        public int LogEntries { get; set; }
        public bool IsDefault => string.Equals(Name, LogManager.DefaultChannel, StringComparison.OrdinalIgnoreCase);
    }

    public sealed class Result
    {
        public Dictionary<string, ChannelInfo> Channels { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int SourceFilesScanned { get; set; }
        public int LogFilesScanned { get; set; }
        public DateTime ScannedAt { get; } = DateTime.Now;

        public ChannelInfo GetOrAdd(string name)
        {
            if (!Channels.TryGetValue(name, out var info))
            {
                info = new ChannelInfo { Name = name };
                Channels[name] = info;
            }
            return info;
        }
    }

    public static Result Scan()
    {
        var result = new Result();
        result.GetOrAdd(LogManager.DefaultChannel);

        try { ScanSources(result); }
        catch (Exception e) { GD.PushWarning($"[GodotAdvancedLogger] Channel scan of scripts failed: {e.Message}"); }

        try { ScanLogs(result); }
        catch (Exception e) { GD.PushWarning($"[GodotAdvancedLogger] Channel scan of log files failed: {e.Message}"); }

        return result;
    }

    private static void ScanSources(Result result)
    {
        string root = ProjectSettings.GlobalizePath("res://");
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0 && result.SourceFilesScanned < MaxSourceFiles)
        {
            string dir = pending.Pop();

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                string name = Path.GetFileName(sub);
                // .godot, .git, .idea, build output and anything Godot is told to ignore.
                if (name.StartsWith('.') || name is "bin" or "obj" or "node_modules") continue;
                if (File.Exists(Path.Combine(sub, ".gdignore"))) continue;
                pending.Push(sub);
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                bool isCs = file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
                bool isGd = file.EndsWith(".gd", StringComparison.OrdinalIgnoreCase);
                if (!isCs && !isGd) continue;

                var info = new FileInfo(file);
                if (info.Length > MaxSourceFileBytes) continue;

                result.SourceFilesScanned++;
                ScanSourceFile(result, file, root, isCs);
            }
        }
    }

    private static void ScanSourceFile(Result result, string file, string root, bool isCs)
    {
        string content = File.ReadAllText(file);
        string[] lines = content.Split('\n');

        var patterns = isCs ? CSharpPatterns : GdScriptPatterns;
        if (isCs && DerivesFromContextLogger.IsMatch(content))
        {
            patterns = CSharpPatterns.Append(BaseConstructorChannel).ToArray();
        }
        string resPath = "res://" + Path.GetRelativePath(root, file).Replace('\\', '/');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;

            foreach (var pattern in patterns)
            {
                foreach (Match match in pattern.Matches(line))
                {
                    string name = Unescape(match.Groups[1].Value).Trim();
                    if (!IsPlausibleChannel(name)) continue;

                    var channel = result.GetOrAdd(name);
                    channel.CodeReferences++;

                    string location = $"{resPath}:{i + 1}";
                    if (channel.CodeLocations.Count < MaxLocationsPerChannel && !channel.CodeLocations.Contains(location))
                    {
                        channel.CodeLocations.Add(location);
                    }
                }
            }
        }
    }

    private static void ScanLogs(Result result)
    {
        string dir = ProjectSettings.GlobalizePath(LoggerSettings.LogDirectory);
        if (!Directory.Exists(dir)) return;

        var files = new DirectoryInfo(dir).GetFiles("log_*.*")
            .Where(f => f.Extension is ".jsonl" or ".txt")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(MaxLogFiles);

        foreach (var file in files)
        {
            bool json = file.Extension == ".jsonl";
            int lineCount = 0;

            // FileShare.ReadWrite: the game may still be running and appending.
            using var stream = new FileStream(file.FullName, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            string line;
            while ((line = reader.ReadLine()) != null && lineCount++ < MaxLogLinesPerFile)
            {
                var match = json ? JsonChannel.Match(line) : TextChannel.Match(line);
                if (!match.Success) continue;

                string name = (json ? Unescape(match.Groups[1].Value) : match.Groups[1].Value).Trim();
                if (!IsPlausibleChannel(name)) continue;

                result.GetOrAdd(name).LogEntries++;
            }

            result.LogFilesScanned++;
        }
    }

    private static bool IsPlausibleChannel(string name) =>
        name.Length is > 0 and <= 64
        && name.IndexOf(LoggerSettings.ChannelSeparator) < 0
        && !name.Contains('{')
        && !name.Contains('\n');

    private static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0) return value;

        try { return Regex.Unescape(value); }
        catch (ArgumentException) { return value; }
    }
}
#endif
