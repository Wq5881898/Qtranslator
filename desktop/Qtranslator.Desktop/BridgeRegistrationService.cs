using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Qtranslator.Desktop;

public static class BridgeRegistrationService
{
    private const string HostName = "com.wq5881898.qtranslator";
    private const string ExtensionId = "kjgoachjhbfkijaopcmallakpfnalkdo";
    private const string RegistryPath =
        @"Software\Google\Chrome\NativeMessagingHosts\" + HostName;

    public static string EnsureRegistered()
    {
        var packageDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        var hostPath = Path.Combine(
            packageDirectory,
            "bridge-host",
            "Qtranslator.BridgeHost.exe");
        if (!File.Exists(hostPath))
        {
            throw new FileNotFoundException(
                "Bridge Host is missing from the test package.",
                hostPath);
        }

        var manifestDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Qtranslator",
            "bridge");
        Directory.CreateDirectory(manifestDirectory);
        var manifestPath = Path.Combine(manifestDirectory, $"{HostName}.json");
        var manifest = new
        {
            name = HostName,
            description = "Qtranslator Stage 2 browser bridge",
            path = hostPath,
            type = "stdio",
            allowed_origins = new[] { $"chrome-extension://{ExtensionId}/" }
        };
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));

        var errors = new List<string>();
        var registered = false;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                using var key = currentUser.CreateSubKey(RegistryPath, true)
                    ?? throw new InvalidOperationException("registry key could not be created");
                key.SetValue("", manifestPath, RegistryValueKind.String);
                key.Flush();
                var savedPath = key.GetValue("") as string;
                if (!string.Equals(savedPath, manifestPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("registry value verification failed");
                }
                registered = true;
            }
            catch (Exception exception)
            {
                errors.Add($"{view}: {exception.Message}");
            }
        }
        if (!registered)
        {
            throw new InvalidOperationException(
                $"Chrome bridge registration failed in both registry views. {string.Join(" | ", errors)}");
        }
        return hostPath;
    }

    public static bool IsRegistered()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
            using var key = currentUser.OpenSubKey(RegistryPath);
            var manifestPath = key?.GetValue("") as string;
            if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
            {
                return true;
            }
        }
        return false;
    }
}

