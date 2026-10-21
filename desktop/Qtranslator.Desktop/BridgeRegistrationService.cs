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
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        if (!File.Exists(manifestPath) || File.ReadAllText(manifestPath) != manifestJson)
        {
            // Chrome can reconnect while registration is running. Never expose a
            // truncated JSON file, and leave an unchanged registration untouched.
            var temporaryPath = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, manifestJson, new UTF8Encoding(false));
                File.Move(temporaryPath, manifestPath, overwrite: true);
            }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        }

        var errors = new List<string>();
        var registeredViews = 0;
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                using var key = currentUser.CreateSubKey(RegistryPath, true)
                    ?? throw new InvalidOperationException("registry key could not be created");
                if (!string.Equals(key.GetValue("") as string, manifestPath, StringComparison.OrdinalIgnoreCase))
                    key.SetValue("", manifestPath, RegistryValueKind.String);
                key.Flush();
                var savedPath = key.GetValue("") as string;
                if (!string.Equals(savedPath, manifestPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("registry value verification failed");
                }
                registeredViews++;
            }
            catch (Exception exception)
            {
                errors.Add($"{view}: {exception.Message}");
            }
        }
        if (registeredViews != 2)
        {
            throw new InvalidOperationException(
                $"Chrome bridge registration is incomplete; both registry views must agree. {string.Join(" | ", errors)}");
        }
        return hostPath;
    }

    public static bool IsRegistered()
    {
        try
        {
            var expectedHost = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "bridge-host", "Qtranslator.BridgeHost.exe"));
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                using var currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                using var key = currentUser.OpenSubKey(RegistryPath);
                var manifestPath = key?.GetValue("") as string;
                if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath) ||
                    !IsValidManifest(File.ReadAllText(manifestPath), expectedHost)) return false;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return false; }
    }

    public static bool IsValidManifest(string json, string expectedHost)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.GetProperty("name").GetString() == HostName &&
                root.GetProperty("type").GetString() == "stdio" &&
                string.Equals(root.GetProperty("path").GetString(), expectedHost, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(expectedHost) &&
                root.GetProperty("allowed_origins").EnumerateArray().Any(origin =>
                    origin.GetString() == $"chrome-extension://{ExtensionId}/");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        { return false; }
    }
}
