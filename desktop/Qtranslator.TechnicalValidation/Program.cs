using System.Text.Json;
using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using Qtranslator.Core;
using Qtranslator.Desktop;

if (args is ["--bridge", var hostPath])
    return await BridgeValidation.RunAsync(Path.GetFullPath(hostPath));

if (args is ["--chat"])
    return await ChatValidation.RunAsync();

if (args is ["--chat-render", var previewDirectory])
    return ChatValidation.Render(previewDirectory);

if (args is ["--image", var imagePath])
{
    await using var image = File.OpenRead(imagePath);
    var provider = new EnglishOcrProvider();
    var result = await provider.RecognizeAsync(image, CancellationToken.None);
    Console.WriteLine(result.Text);
    return string.IsNullOrWhiteSpace(result.Text) ? 1 : 0;
}

if (args is ["--image-crop", var sourcePath, var xText, var yText, var widthText, var heightText])
{
    using var source = new Bitmap(sourcePath);
    var cropBounds = new Rectangle(
        int.Parse(xText),
        int.Parse(yText),
        int.Parse(widthText),
        int.Parse(heightText));
    using var cropped = source.Clone(cropBounds, source.PixelFormat);
    await using var image = new MemoryStream();
    cropped.Save(image, System.Drawing.Imaging.ImageFormat.Png);
    image.Position = 0;
    var provider = new EnglishOcrProvider();
    var result = await provider.RecognizeAsync(image, CancellationToken.None);
    Console.WriteLine($"Raw: {result.Text}");
    Console.WriteLine($"English: {TextRules.CleanEnglishOcrArtifacts(TextRules.ExtractEnglishOcrContent(result.Text))}");
    Console.WriteLine($"Confidence: {result.Confidence:P0}");
    return 0;
}

if (args is ["--capture-window", var titleFragment])
{
    var windowBounds = FindVisibleWindow(titleFragment)
        ?? throw new InvalidOperationException($"No visible window contains title: {titleFragment}");
    await using var image = ScreenRegionCapture.CaptureRegion(windowBounds);
    var provider = new WindowsOcrProvider();
    var result = await provider.RecognizeAsync(image, CancellationToken.None);
    Console.WriteLine($"Captured physical region: {windowBounds}");
    Console.WriteLine(result.Text);
    return result.Text.Any(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z') ? 0 : 1;
}

if (args is ["--bridge-health"])
{
    var provider = new BrowserBridgeTranslationProvider();
    var health = await provider.CheckHealthAsync(CancellationToken.None);
    Console.WriteLine(health.Message);
    return health.IsAvailable ? 0 : 1;
}

if (args is ["--bridge-translate", var text])
{
    var provider = new BrowserBridgeTranslationProvider();
    var result = await provider.TranslateAsync(
        new TranslationRequest(Guid.NewGuid().ToString("N"), text),
        CancellationToken.None);
    Console.WriteLine(result.TranslatedText);
    return string.IsNullOrWhiteSpace(result.TranslatedText) ? 1 : 0;
}

var checks = new (string Name, Func<Task> Run)[]
{
    ("text rules", ValidateTextRulesAsync),
    ("translation display", ValidateTranslationDisplayAsync),
    ("shared favorites contract", ValidateSharedFavoritesAsync),
    ("mock provider", ValidateMockAsync),
    ("native frame UTF-8 round-trip", ValidateFramingAsync),
    ("invalid native frame rejection", ValidateInvalidLengthAsync),
    ("desktop and native host translation relay", ValidateNativeHostRelayAsync),
    ("global shortcut contract", ValidateGlobalShortcutContractAsync),
    ("packaged English OCR on in-memory image", ValidatePackagedEnglishOcrAsync),
    ("non-English OCR is rejected with a reason", ValidateNonEnglishOcrAsync),
    ("chat OCR preserves Chinese input", ValidateChatOcrAsync),
    ("Windows local OCR on in-memory image", ValidateWindowsOcrAsync),
};
var failed = 0;
foreach (var check in checks)
{
    try { await check.Run(); Console.WriteLine($"PASS  {check.Name}"); }
    catch (Exception exception) { failed++; Console.Error.WriteLine($"FAIL  {check.Name}: {exception}"); }
}
Console.WriteLine($"{checks.Length - failed}/{checks.Length} technical checks passed.");
return failed == 0 ? 0 : 1;

static Task ValidateTextRulesAsync()
{
    Assert(TextRules.Normalize("  Hello\r\n world ") == "Hello world", "Whitespace normalization failed.");
    Assert(TextRules.Classify("Hello") == TextKind.Word, "Word classification failed.");
    Assert(TextRules.Classify("Hello world.") == TextKind.Sentence, "Sentence classification failed.");
    Assert(TextRules.AssessEnglishOcr("To learn English well.", 0.92f).IsReliable,
        "Valid English OCR was rejected.");
    Assert(!TextRules.AssessEnglishOcr("S O m e O n e", 0.40f).IsReliable,
        "Low-confidence OCR gibberish was accepted.");
    Assert(TextRules.CleanEnglishOcrArtifacts("| fll Qtranslator.exe") == "Qtranslator.exe",
        "Leading icon artifacts were not removed.");
    Assert(!TextRules.AssessEnglishOcr("fll", 0.80f).IsReliable,
        "An icon-only pseudo-word was accepted.");
    Assert(TextRules.AssessEnglishOcr("Qtranslator.exe", 0.80f).IsReliable,
        "A valid file name was rejected.");
    Assert(TextRules.ExtractEnglishOcrContent("技术检查 7/7，GitHub CI 通过。") == "7/7 GitHub CI",
        "English was not extracted from mixed Chinese text.");
    Assert(TextRules.AssessEnglishOcr("技术检查 7/7，GitHub CI 通过。", 0.80f).IsReliable,
        "English in mixed Chinese text was rejected.");
    Assert(!TextRules.AssessEnglishOcr("已处理 4m 58s >", 0.90f).IsReliable,
        "A Chinese status containing only time units was accepted as English.");
    Assert(TextRules.MergeEnglishFromBilingualOcr(
            "GitHub CI iat,",
            "GitHub Cl 通过。") == "GitHub CI",
        "English-only and bilingual OCR results were not reconciled.");
    Assert(TextRules.CleanEnglishOcrArtifacts("GitHub Cl") == "GitHub CI",
        "A likely technical acronym I/l confusion was not corrected.");
    Assert(
        TextRules.ExtractEnglishOcrContent("this week’s cartoon, Russia’s attack, America’s plan") ==
        "this week's cartoon, Russia's attack, America's plan",
        "Curly OCR apostrophes were not normalized.");
    Assert(
        TextRules.CleanEnglishOcrArtifacts("this week s cartoon Russia s attack America s plan") ==
        "this week's cartoon Russia's attack America's plan",
        "Detached possessive markers were not repaired.");
    return Task.CompletedTask;
}

static Task ValidateSharedFavoritesAsync()
{
    var favorite = new FavoriteEntry(
        "word:hello",
        "word",
        "Hello",
        "你好",
        "2026-07-30T00:00:00.000Z",
        "/həˈləʊ/");
    Assert(SharedFavoriteStore.IsValid(favorite), "A valid favorite was rejected.");
    var csv = FavoritesCsv.Serialize([favorite]);
    Assert(csv.Contains("2026-07-30") && !csv.Contains("2026-07-30T"),
        "Favorites CSV must export First saved as YYYY-MM-DD.");
    var parsed = FavoritesCsv.Parse(csv);
    Assert(
        parsed.Count == 1 &&
        parsed[0] == favorite with { FirstFavoritedAt = "2026-07-30" },
        "Favorites CSV did not round-trip.");
    return Task.CompletedTask;
}

static Task ValidateTranslationDisplayAsync()
{
    var word = new TranslationResult(
        "word-result",
        "granted",
        "授予；批准",
        TextKind.Word,
        "chrome-local-bridge",
        "/ˈɡrɑːntɪd/");
    Assert(
        TranslationDisplay.Format(word) ==
        $"/ˈɡrɑːntɪd/{Environment.NewLine}授予；批准",
        "A word result must display its phonetic before the Chinese translation.");

    var sentence = word with
    {
        OriginalText = "Permission was granted.",
        TextKind = TextKind.Sentence,
    };
    Assert(
        TranslationDisplay.Format(sentence) == "授予；批准",
        "Sentence results must not prepend a word phonetic.");
    return Task.CompletedTask;
}

static async Task ValidateMockAsync()
{
    var provider = new MockTranslationProvider();
    Assert((await provider.CheckHealthAsync(CancellationToken.None)).IsAvailable, "Mock provider unavailable.");
    var result = await provider.TranslateAsync(new TranslationRequest("r1", "Hello world."), CancellationToken.None);
    Assert(result.RequestId == "r1" && result.Provider == "mock", "Mock result invalid.");
}
static async Task ValidateFramingAsync()
{
    var expected = new BridgeEnvelope(BridgeEnvelope.CurrentVersion, "bridge.health", "r2", DateTimeOffset.UtcNow,
        JsonSerializer.SerializeToElement(new { text = "Hello 世界", phonetic = "/ˌkɒnsəlˈteɪʃən/" }));
    await using var stream = new MemoryStream();
    await NativeMessageFraming.WriteAsync(stream, expected, CancellationToken.None);
    stream.Position = 0;
    var actual = await NativeMessageFraming.ReadAsync(stream, CancellationToken.None);
    Assert(actual?.Payload.GetProperty("text").GetString() == "Hello 世界", "UTF-8 payload failed.");
    Assert(actual?.Payload.GetProperty("phonetic").GetString() == "/ˌkɒnsəlˈteɪʃən/",
        "IPA payload was corrupted during the UTF-8 bridge round-trip.");
}
static async Task ValidateInvalidLengthAsync()
{
    await using var stream = new MemoryStream(BitConverter.GetBytes(int.MaxValue));
    try { _ = await NativeMessageFraming.ReadAsync(stream, CancellationToken.None); }
    catch (InvalidDataException) { return; }
    throw new InvalidOperationException("Invalid frame was accepted.");
}
static async Task ValidateNativeHostRelayAsync()
{
    var previousPipeName = Environment.GetEnvironmentVariable(
        BridgeEnvelope.PipeNameEnvironmentVariable);
    var isolatedPipeName = $"{BridgeEnvelope.DefaultPipeName}.validation.{Guid.NewGuid():N}";
    Environment.SetEnvironmentVariable(
        BridgeEnvelope.PipeNameEnvironmentVariable,
        isolatedPipeName);
    var desktopRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        ".."));
    var hostPath = Path.Combine(
        desktopRoot,
        "Qtranslator.BridgeHost",
        "bin",
        "Release",
        "net10.0-windows10.0.19041.0",
        "Qtranslator.BridgeHost.exe");
    Assert(File.Exists(hostPath), $"Bridge Host was not built: {hostPath}");
    using var process = Process.Start(CreateBridgeHostStartInfo(hostPath, isolatedPipeName))
        ?? throw new InvalidOperationException("Bridge Host could not be started.");
    try
    {
        using var favoritesProcess = Process.Start(
            CreateBridgeHostStartInfo(hostPath, isolatedPipeName))
            ?? throw new InvalidOperationException("Concurrent favorites Bridge Host could not be started.");
        var favoritesRequest = BridgeEnvelope.Create(
            "favorites.read",
            "concurrent-favorites-read",
            new { });
        await NativeMessageFraming.WriteAsync(
            favoritesProcess.StandardInput.BaseStream,
            favoritesRequest,
            CancellationToken.None);
        var favoritesResponse = await NativeMessageFraming.ReadAsync(
            favoritesProcess.StandardOutput.BaseStream,
            CancellationToken.None)
            ?? throw new InvalidOperationException("Concurrent favorites Bridge Host returned no response.");
        Assert(
            favoritesResponse.MessageType == "favorites.result",
            "Concurrent favorites request failed while the translation host was active.");
        favoritesProcess.StandardInput.Close();
        await favoritesProcess.WaitForExitAsync();

        var provider = new BrowserBridgeTranslationProvider();
        var translationTask = provider.TranslateAsync(
            new TranslationRequest("relay-test", "Hello world."),
            CancellationToken.None);
        var forwarded = await NativeMessageFraming.ReadAsync(
            process.StandardOutput.BaseStream,
            CancellationToken.None)
            ?? throw new InvalidOperationException("Host closed before forwarding a request.");
        Assert(forwarded.MessageType == "translation.request", "Host did not forward translation request.");
        Assert(
            forwarded.Payload.GetProperty("text").GetString() == "Hello world.",
            "Host changed the translation text.");
        var response = BridgeEnvelope.Create(
            "translation.result",
            forwarded.RequestId,
            new
            {
                originalText = "Hello world.",
                translatedText = "你好，世界。",
                textKind = "sentence",
                provider = "chrome-local",
            });
        await NativeMessageFraming.WriteAsync(
            process.StandardInput.BaseStream,
            response,
            CancellationToken.None);
        var result = await translationTask;
        Assert(result.TranslatedText == "你好，世界。", "Desktop did not receive translated text.");
        Assert(result.Provider == "chrome-local", "Desktop did not preserve provider metadata.");
    }
    finally
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        Environment.SetEnvironmentVariable(
            BridgeEnvelope.PipeNameEnvironmentVariable,
            previousPipeName);
    }
}

static ProcessStartInfo CreateBridgeHostStartInfo(string hostPath, string pipeName)
{
    var startInfo = new ProcessStartInfo(hostPath)
    {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    startInfo.Environment[BridgeEnvelope.PipeNameEnvironmentVariable] = pipeName;
    return startInfo;
}
static Task ValidateGlobalShortcutContractAsync()
{
    using var shortcut = new GlobalHotKeyService();
    Assert(ShortcutSettings.Default.DisplayName == "Ctrl+Alt+X", "Unexpected default global shortcut.");
    Assert(!ShortcutSettingsStore.IsAllowed(new(ShortcutModifiers.ControlShift, "X")), "Legacy capture shortcut must remain reserved.");
    Assert(!ShortcutSettingsStore.IsAllowed(new(ShortcutModifiers.ControlShift, "D")), "AI capture shortcut must remain reserved.");
    Assert(
        new ShortcutSettings(ShortcutModifiers.ControlAlt, "Q").DisplayName == "Ctrl+Alt+Q",
        "Configurable shortcut display is invalid.");
    return Task.CompletedTask;
}
static async Task ValidateWindowsOcrAsync()
{
    await using var stream = CreateEnglishTestImage();
    var provider = new WindowsOcrProvider();
    var health = await provider.CheckHealthAsync(CancellationToken.None);
    Assert(health.IsAvailable, health.Message);
    for (var attempt = 1; attempt <= 2; attempt++)
    {
        stream.Position = 0;
        var result = await provider.RecognizeAsync(stream, CancellationToken.None);
        Assert(result.Text.Contains("local OCR", StringComparison.OrdinalIgnoreCase),
            $"Unexpected OCR result on attempt {attempt}: {result.Text}");
    }
}
static async Task ValidatePackagedEnglishOcrAsync()
{
    await using var stream = CreateEnglishTestImage();
    var provider = new PackagedEnglishOcrProvider();
    var health = await provider.CheckHealthAsync(CancellationToken.None);
    Assert(health.IsAvailable, health.Message);
    var result = await provider.RecognizeAsync(stream, CancellationToken.None);
    Assert(result.Provider == "packaged-bilingual-ocr", $"Unexpected OCR provider: {result.Provider}");
    Assert(result.Text.Contains("Qtranslator local OCR", StringComparison.Ordinal),
        $"Unexpected OCR result: {result.Text}");
    Assert(TextRules.AssessEnglishOcr(result.Text, result.Confidence).IsReliable,
        $"Packaged OCR result was not considered reliable at {result.Confidence:P0}.");
}
static async Task ValidateNonEnglishOcrAsync()
{
    await using var stream = CreateTestImage("这是一个中文截图测试", "Microsoft YaHei UI");
    var provider = new PackagedEnglishOcrProvider();
    var result = await provider.RecognizeAsync(stream, CancellationToken.None);
    var assessment = TextRules.AssessEnglishOcr(result.Text, result.Confidence);
    Assert(!assessment.IsReliable,
        $"Chinese image produced an accepted English result at {result.Confidence:P0}: {result.Text}");
}
static MemoryStream CreateEnglishTestImage()
{
    return CreateTestImage("Qtranslator local OCR", "Arial");
}
static MemoryStream CreateTestImage(string content, string typeface)
{
    var visual = new DrawingVisual();
    using (var drawing = visual.RenderOpen())
    {
        drawing.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, 900, 180));
        var text = new FormattedText(content, CultureInfo.GetCultureInfo("en-US"),
            System.Windows.FlowDirection.LeftToRight, new Typeface(typeface), 54, System.Windows.Media.Brushes.Black, 1);
        drawing.DrawText(text, new System.Windows.Point(20, 45));
    }
    var bitmap = new RenderTargetBitmap(900, 180, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(visual);
    var stream = new MemoryStream();
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    encoder.Save(stream);
    stream.Position = 0;
    return stream;
}
static async Task ValidateChatOcrAsync()
{
    await using var image = CreateTestImage("这是一个中文截图测试", "Microsoft YaHei UI");
    var result = await new PackagedEnglishOcrProvider(preserveAllText: true).RecognizeAsync(image, CancellationToken.None);
    Assert(result.Text.Any(c => c is >= '\u3400' and <= '\u9FFF'), "AI screenshot draft lost Chinese OCR text.");
}

static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

static Rectangle? FindVisibleWindow(string titleFragment)
{
    Rectangle? result = null;
    EnumWindows((window, parameter) =>
    {
        if (!IsWindowVisible(window)) return true;
        var length = GetWindowTextLength(window);
        if (length == 0) return true;
        var title = new StringBuilder(length + 1);
        GetWindowText(window, title, title.Capacity);
        if (!title.ToString().Contains(titleFragment, StringComparison.OrdinalIgnoreCase)) return true;
        GetWindowThreadProcessId(window, out var processId);
        try
        {
            if (!Process.GetProcessById((int)processId).ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch (ArgumentException)
        {
            return true;
        }
        if (!GetWindowRect(window, out var bounds)) return true;
        result = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        return false;
    }, IntPtr.Zero);
    return result;
}

[DllImport("user32.dll")]
static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
[DllImport("user32.dll")]
static extern bool IsWindowVisible(IntPtr window);
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
[DllImport("user32.dll")]
static extern int GetWindowTextLength(IntPtr window);
[DllImport("user32.dll")]
static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);
[DllImport("user32.dll")]
static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
[StructLayout(LayoutKind.Sequential)]
struct NativeRectangle { public int Left; public int Top; public int Right; public int Bottom; }
