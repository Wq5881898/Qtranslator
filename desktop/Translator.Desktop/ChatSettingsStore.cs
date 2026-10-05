using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Translator.Core;

namespace Translator.Desktop;

/// <summary>DPAPI protects the entire configuration for the current Windows user.</summary>
public static class ChatSettingsStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Qtranslator");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "ai-settings.bin");

    public static ChatSettings Load()
    {
        if (!File.Exists(SettingsPath)) return ChatSettings.Defaults();
        var settings = JsonSerializer.Deserialize<ChatSettings>(Unprotect(File.ReadAllBytes(SettingsPath)))
            ?? throw new InvalidOperationException("AI 配置无法读取。");
        if (settings.Profiles is null || settings.Profiles.Count == 0 || settings.Profiles.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id)) ||
            settings.Profiles.Select(p => p.Id).Distinct().Count() != settings.Profiles.Count)
            throw new InvalidOperationException("AI 配置档案无效。");
        foreach (var profile in settings.Profiles) profile.Validate(requireKey: false);
        return settings;
    }

    public static void Save(ChatSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllBytes(temporary, Protect(JsonSerializer.SerializeToUtf8Bytes(settings)));
        File.Move(temporary, SettingsPath, true);
    }

    public static byte[] Protect(byte[] data) => Transform(data, true);
    public static byte[] Unprotect(byte[] data) => Transform(data, false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new DataBlob { Size = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            DataBlob output;
            // CRYPTPROTECT_UI_FORBIDDEN: never trigger an unexpected credential dialog.
            var ok = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new InvalidOperationException("无法加密或读取本机 AI 配置。请使用保存配置时的 Windows 账号。");
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, output.Size);
                return result;
            }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
