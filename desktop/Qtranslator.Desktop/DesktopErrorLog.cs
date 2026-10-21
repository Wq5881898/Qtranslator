using System.IO;
using System.Text;

namespace Qtranslator.Desktop;

internal static class DesktopErrorLog
{
    private static readonly object Gate = new();

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Qtranslator",
        "logs",
        "desktop.log");

    public static void Write(string context, Exception exception)
        => WriteInfo(context, exception.ToString());

    public static void WriteInfo(string context, string detail)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_048_576)
                    File.Move(LogPath, LogPath + ".previous", overwrite: true);
                var entry = new StringBuilder()
                    .AppendLine($"[{DateTimeOffset.Now:O}] {context}")
                    .AppendLine(detail)
                    .AppendLine();
                File.AppendAllText(LogPath, entry.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never become another application failure.
        }
    }
}
