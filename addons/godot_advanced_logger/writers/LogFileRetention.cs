using System;
using System.IO;
using Godot;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.writers;

/// <summary>
/// Shared retention policy for the file-based writers: keeps the newest N files
/// matching a pattern and deletes the rest.
/// </summary>
internal static class LogFileRetention
{
    /// <summary>
    /// Deletes the oldest files in <paramref name="directory"/> matching
    /// <paramref name="searchPattern"/> until at most <paramref name="maxFiles"/> remain.
    /// </summary>
    /// <remarks>
    /// Ordering uses <see cref="FileSystemInfo.LastWriteTimeUtc"/> rather than creation
    /// time: on NTFS, "file tunneling" makes a recreated file inherit the creation
    /// timestamp of a same-named predecessor, which would make the newest log look old
    /// and get it deleted first.
    /// </remarks>
    public static void Apply(string directory, string searchPattern, int maxFiles, string writerName)
    {
        if (maxFiles <= 0) return;

        try
        {
            var info = new DirectoryInfo(directory);
            if (!info.Exists) return;

            var files = info.GetFiles(searchPattern);
            if (files.Length <= maxFiles) return;

            Array.Sort(files, static (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

            for (int i = maxFiles; i < files.Length; i++)
            {
                try
                {
                    files[i].Delete();
                }
                catch (Exception e)
                {
                    // A single locked file (open in an editor, held by another instance)
                    // must not abort retention for the rest.
                    GD.PushWarning($"[{writerName}] Could not delete old log '{files[i].Name}': {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[{writerName}] Log retention failed: {e.Message}");
        }
    }
}
