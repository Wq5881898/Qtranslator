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
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var entry = new StringBuilder()
                    .AppendLine($"[{DateTimeOffset.Now:O}] {context}")
                    .AppendLine(exception.ToString())
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
