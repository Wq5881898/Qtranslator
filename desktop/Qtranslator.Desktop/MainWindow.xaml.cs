using System.Windows;
using System.Diagnostics;
using System.Text;
using Qtranslator.Core;

namespace Qtranslator.Desktop;

public partial class MainWindow : Window
{
    private readonly BrowserBridgeTranslationProvider _translation = new();
    private readonly EnglishOcrProvider _ocr = new();
    private readonly EnglishOcrProvider _chatOcr = new(primary: new PackagedEnglishOcrProvider(preserveAllText: true));
    private readonly GlobalHotKeyService _hotKey = new();
    private readonly GlobalHotKeyService _chatHotKey = new(0x5155);
    private ShortcutSettings _shortcutSettings = ShortcutSettingsStore.Load();
    private TranslationResult? _currentTranslation;
    private bool _busy;
    private Process? _speechProcess;
    private DateTimeOffset _lastBridgeRegistrationAttempt = DateTimeOffset.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Activated += OnActivated;
        Closed += OnClosed;
        _hotKey.Pressed += OnHotKeyPressed;
        _chatHotKey.Pressed += OnChatHotKeyPressed;
        ChatPanel.CaptureRequested += OnChatHotKeyPressed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureBridgeRegistration(force: true);
        await RefreshFavoriteCountAsync();
    }

    private async void OnActivated(object? sender, EventArgs e)
    {
        EnsureBridgeRegistration();
        await RefreshFavoriteCountAsync();
    }

    private void EnsureBridgeRegistration(bool force = false)
    {
        if (!force &&
            DateTimeOffset.UtcNow - _lastBridgeRegistrationAttempt < TimeSpan.FromSeconds(30) &&
            BridgeRegistrationService.IsRegistered())
        {
            return;
        }
        _lastBridgeRegistrationAttempt = DateTimeOffset.UtcNow;
        try
        {
            BridgeRegistrationService.EnsureRegistered();
        }
        catch (Exception exception)
        {
            StatusText.Text =
                $"Chrome bridge registration failed: {FriendlyError(exception)} " +
                "Favorites remain safe locally. Reinstall Qtranslator or retry after checking Windows security.";
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        RegisterShortcut();
        if (!_chatHotKey.TryRegister(this, new ShortcutSettings(ShortcutModifiers.ControlShift, "D"), out var error))
            ChatPanel.SetCaptureStatus(error ?? "Ctrl+Shift+D 不可用，请用截图输入按钮。");
    }

    private void RegisterShortcut()
    {
        ShortcutHint.Text =
            $"Press {_shortcutSettings.DisplayName} anywhere to capture, recognize and translate.";
        if (_hotKey.TryRegister(this, _shortcutSettings, out var error))
        {
            HotKeyText.Text = $"{_hotKey.DisplayName} ready";
            StatusText.Text =
                "Ready. The screenshot stays in memory; only recognized English is sent to Chrome local translation.";
        }
        else
        {
            HotKeyText.Text = "Shortcut unavailable";
            StatusText.Text = error;
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var dialog = new ShortcutSettingsWindow(_shortcutSettings) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var previous = _shortcutSettings;
        _shortcutSettings = dialog.SelectedSettings;
        if (_hotKey.TryRegister(this, _shortcutSettings, out var error))
        {
            ShortcutSettingsStore.Save(_shortcutSettings);
            ShortcutHint.Text =
                $"Press {_shortcutSettings.DisplayName} anywhere to capture, recognize and translate.";
            HotKeyText.Text = $"{_shortcutSettings.DisplayName} ready";
            StatusText.Text = "Screenshot shortcut saved and ready.";
            return;
        }

        _shortcutSettings = previous;
        RegisterShortcut();
        StatusText.Text =
            $"{error} The previous shortcut {_shortcutSettings.DisplayName} remains active.";
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hotKey.Pressed -= OnHotKeyPressed;
        _hotKey.Dispose();
        _chatHotKey.Pressed -= OnChatHotKeyPressed;
        _chatHotKey.Dispose();
        ChatPanel.CaptureRequested -= OnChatHotKeyPressed;
        ChatPanel.Close();
        if (_speechProcess is { HasExited: false })
        {
            try { _speechProcess.Kill(entireProcessTree: true); } catch { }
        }
        _speechProcess?.Dispose();
    }

    private async void OnHotKeyPressed(object? sender, EventArgs e)
    {
        if (_busy)
        {
            StatusText.Text = "A capture or translation is already running. Please wait.";
            return;
        }
        await CaptureRecognizeAndTranslateAsync();
    }

    private async void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        await CaptureRecognizeAndTranslateAsync();
    }

    private async Task CaptureRecognizeAndTranslateAsync()
    {
        if (_busy) return;
        MainTabs.SelectedIndex = 0;
        _currentTranslation = null;
        RecognizedText.Clear();
        TranslatedText.Clear();
        SetBusy(true, "Select a region. Press Esc to cancel.");
        Hide();
        try
        {
            await Task.Delay(150);
            using var image = ScreenRegionCapture.CaptureWithOverlay();
            RestoreWindow();
            if (image is null)
            {
                StatusText.Text = "Capture cancelled. Nothing was saved.";
                return;
            }

            StatusText.Text = "Recognizing English locally...";
            var result = await _ocr.RecognizeAsync(image, CancellationToken.None);
            var cleanedText = TextRules.CleanEnglishOcrArtifacts(
                TextRules.ExtractEnglishOcrContent(result.Text));
            var assessment = TextRules.AssessEnglishOcr(cleanedText, result.Confidence);
            if (!assessment.IsReliable)
            {
                StatusText.Text =
                    $"No reliable English text was found. {assessment.Message} " +
                    "Select a clearer or larger English region and retry.";
                return;
            }

            RecognizedText.Text = cleanedText;
            await TranslateCurrentTextAsync();
        }
        catch (Exception exception)
        {
            RestoreWindow();
            StatusText.Text = $"Capture or OCR failed: {FriendlyError(exception)}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }
        SetBusy(true);
        try
        {
            await TranslateCurrentTextAsync();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task TranslateCurrentTextAsync()
    {
        try
        {
            EnsureBridgeRegistration();
            _currentTranslation = null;
            FavoriteButton.Content = "♡";
            PhoneticText.Text = string.Empty;
            SpeakButton.IsEnabled = false;
            TranslatedText.Clear();
            if (string.IsNullOrWhiteSpace(RecognizedText.Text))
            {
                StatusText.Text = "There is no English text to translate.";
                return;
            }

            StatusText.Text = "Checking the Chrome translation bridge...";
            var health = await _translation.CheckHealthAsync(CancellationToken.None);
            if (!health.IsAvailable)
            {
                StatusText.Text =
                    $"Translation could not start: {health.Message} " +
                    "Keep Chrome open, enable the Qtranslator extension, then retry.";
                return;
            }

            StatusText.Text =
                "Connecting to Chrome, checking the dictionary, and translating locally...";
            var result = await _translation.TranslateAsync(
                new TranslationRequest(Guid.NewGuid().ToString("N"), RecognizedText.Text),
                CancellationToken.None);
            PhoneticText.Text = result.TextKind == TextKind.Word && !string.IsNullOrWhiteSpace(result.Phonetic)
                ? result.Phonetic
                : string.Empty;
            TranslatedText.Text = result.TranslatedText;
            _currentTranslation = result;
            SpeakButton.IsEnabled = true;
            await RefreshFavoriteButtonAsync();
            StatusText.Text = result.Provider == "chrome-local-dictionary-fallback"
                ? "Basic translation complete. The dictionary was unavailable, so phonetics and extended meanings may be missing."
                : "Translation complete.";
        }
        catch (Exception exception)
        {
            DesktopErrorLog.Write("Translation operation failed", exception);
            StatusText.Text = $"Translation failed: {FriendlyError(exception)}";
        }
    }

    private async void OnChatHotKeyPressed(object? sender, EventArgs e)
    {
        if (_busy) { ChatPanel.SetCaptureStatus("截图或翻译正在运行，请稍后再试。"); return; }
        MainTabs.SelectedItem = ChatTab;
        SetBusy(true);
        ChatPanel.SetCaptureStatus("请选择截图区域，Esc 取消。");
        Hide();
        try
        {
            await Task.Delay(150);
            using var image = ScreenRegionCapture.CaptureWithOverlay();
            RestoreWindow();
            if (image is null) { ChatPanel.SetCaptureStatus("已取消截图。"); return; }
            ChatPanel.SetCaptureStatus("正在本机识别文字…");
            var result = await _chatOcr.RecognizeAsync(image, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(result.Text))
            { ChatPanel.SetCaptureStatus("没有识别到文字，请选取更清晰的区域。"); return; }
            ChatPanel.AppendCapturedText(result.Text);
        }
        catch (Exception ex)
        {
            RestoreWindow();
            ChatPanel.SetCaptureStatus($"截图识别失败：{FriendlyError(ex)}");
        }
        finally { SetBusy(false); }
    }

    internal void ReportUnexpectedError(Exception exception)
    {
        DesktopErrorLog.Write("Recovered dispatcher exception", exception);
        RestoreWindow();
        SetBusy(false);
        StatusText.Text =
            $"Qtranslator recovered from an unexpected error: {FriendlyError(exception)} Please retry.";
    }

    private async void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTranslation is null) return;
        try
        {
            var favorites = (await SharedFavoriteStore.LoadAsync()).ToList();
            var kind = _currentTranslation.TextKind == TextKind.Word ? "word" : "sentence";
            var normalized = TextRules.Normalize(_currentTranslation.OriginalText);
            var id = $"{kind}:{(kind == "word" ? normalized.ToLowerInvariant() : normalized)}";
            var existing = favorites.FindIndex(item => item.Id == id);
            if (existing >= 0)
            {
                await SharedFavoriteStore.PatchAsync([], [id]);
                StatusText.Text = "Removed from favorites.";
            }
            else
            {
                var item = new FavoriteEntry(
                    id,
                    kind,
                    normalized,
                    _currentTranslation.TranslatedText,
                    DateTimeOffset.UtcNow.ToString("O"),
                    _currentTranslation.Phonetic);
                await SharedFavoriteStore.PatchAsync([item], []);
                StatusText.Text = "Saved to the shared local favorites.";
            }
            await RefreshFavoriteButtonAsync();
            await RefreshFavoriteCountAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Favorite update failed: {FriendlyError(exception)}";
        }
    }

    private async void FavoritesButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new FavoritesWindow { Owner = this };
        window.ShowDialog();
        await RefreshFavoriteButtonAsync();
        await RefreshFavoriteCountAsync();
    }

    private async Task RefreshFavoriteButtonAsync()
    {
        if (_currentTranslation is null)
        {
            FavoriteButton.Content = "♡";
            return;
        }
        var kind = _currentTranslation.TextKind == TextKind.Word ? "word" : "sentence";
        var normalized = TextRules.Normalize(_currentTranslation.OriginalText);
        var id = $"{kind}:{(kind == "word" ? normalized.ToLowerInvariant() : normalized)}";
        var favorites = await SharedFavoriteStore.LoadAsync();
        var saved = favorites.Any(item => item.Id == id);
        FavoriteButton.Content = saved ? "♥" : "♡";
        FavoriteButton.Foreground = saved
            ? System.Windows.Media.Brushes.Crimson
            : System.Windows.Media.Brushes.Black;
    }

    private async Task RefreshFavoriteCountAsync()
    {
        try
        {
            var favorites = await SharedFavoriteStore.LoadAsync();
            FavoritesButton.Content = $"Favorites ({favorites.Count})";
        }
        catch
        {
            FavoritesButton.Content = "Favorites (?)";
        }
    }

    private void CopyEnglishButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(RecognizedText.Text, "English text");
    }

    private void CopyChineseButton_Click(object sender, RoutedEventArgs e)
    {
        CopyText(TranslatedText.Text, "Chinese translation");
    }

    private void SpeakButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTranslation is null || string.IsNullOrWhiteSpace(_currentTranslation.OriginalText))
        {
            StatusText.Text = "There is no English text to read aloud.";
            return;
        }
        try
        {
            if (_speechProcess is { HasExited: false })
            {
                _speechProcess.Kill(entireProcessTree: true);
                SpeakButton.Content = "Read aloud";
                StatusText.Text = "Reading stopped.";
                return;
            }
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(_currentTranslation.OriginalText));
            var script = "$t=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encoded + "')); " +
                         "Add-Type -AssemblyName System.Speech; $s=New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                         "$s.SelectVoiceByHints([System.Speech.Synthesis.VoiceGender]::NotSet, [System.Speech.Synthesis.VoiceAge]::NotSet, 0, [Globalization.CultureInfo]::GetCultureInfo('en-US')); $s.Speak($t)";
            _speechProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            SpeakButton.Content = "Stop reading";
            StatusText.Text = "Reading English aloud locally...";
            _ = WatchSpeechAsync(_speechProcess);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Read aloud failed: {FriendlyError(exception)}";
        }
    }

    private async Task WatchSpeechAsync(Process? process)
    {
        if (process is null) return;
        try { await process.WaitForExitAsync(); } catch { return; }
        if (!Dispatcher.HasShutdownStarted)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                SpeakButton.Content = "Read aloud";
                if (StatusText.Text.StartsWith("Reading English", StringComparison.Ordinal))
                    StatusText.Text = "Reading complete.";
            });
        }
        process.Dispose();
        _speechProcess = null;
    }

    private void CopyText(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            StatusText.Text = $"There is no {description.ToLowerInvariant()} to copy.";
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(value);
            StatusText.Text = $"{description} copied.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Copy failed: {FriendlyError(exception)}";
        }
    }

    private void RestoreWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        ChatPanel.SetCaptureBusy(busy);
        CaptureButton.IsEnabled = !busy;
        TranslateButton.IsEnabled = !busy;
        SettingsButton.IsEnabled = !busy;
        FavoriteButton.IsEnabled = !busy && _currentTranslation is not null;
        SpeakButton.IsEnabled = !busy && _currentTranslation is not null;
        FavoritesButton.IsEnabled = !busy;
        if (status is not null)
        {
            StatusText.Text = status;
        }
    }

    private static string FriendlyError(Exception exception) =>
        exception.Message.Replace("\r", " ").Replace("\n", " ").Trim();
}
