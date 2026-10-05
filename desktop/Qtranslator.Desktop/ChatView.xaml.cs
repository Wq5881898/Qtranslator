using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Qtranslator.Core;

namespace Qtranslator.Desktop;

public sealed class ChatBubble(string role, string text, string label) : INotifyPropertyChanged
{
    private string _text = text;
    private string _note = "";
    public string Label { get; } = label;
    public string Background { get; } = role == "user" ? "#E7F0EB" : "#FFFFFF";
    public string Text { get => _text; set { _text = value; PropertyChanged?.Invoke(this, new(nameof(Text))); } }
    public string Note { get => _note; set { _note = value; PropertyChanged?.Invoke(this, new(nameof(Note))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class ChatView : System.Windows.Controls.UserControl
{
    private readonly ObservableCollection<ChatBubble> _bubbles = [];
    private readonly List<ChatMessage> _history = [];
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private ChatSettings _settings = ChatSettings.Defaults();
    private CancellationTokenSource? _request;
    private string? _failedPrompt;
    private ChatBubble? _failedBubble;
    private bool _initializing;
    private bool _closed;
    public event EventHandler? CaptureRequested;

    public ChatView()
    {
        InitializeComponent();
        MessagesList.ItemsSource = _bubbles;
        try { _settings = ChatSettingsStore.Load(); }
        catch (Exception ex) { ChatStatus.Text = ex.Message + " 请打开 API 配置重新保存。"; }
        RefreshProfiles();
    }

    private void RefreshProfiles()
    {
        _initializing = true;
        ProviderPicker.ItemsSource = _settings.Profiles;
        ProviderPicker.SelectedItem = _settings.Profiles.FirstOrDefault(p => p.Id == _settings.SelectedId) ?? _settings.Profiles.First();
        _initializing = false;
        UpdateModelHint();
    }
    private void UpdateModelHint()
    {
        if (ProviderPicker.SelectedItem is ChatProfile p)
            ModelHint.Text = $"{p.Model} · {p.Api} · {(Uri.TryCreate(p.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : "URL 无效，请修改配置")}";
    }
    private void ProviderPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ProviderPicker.SelectedItem is not ChatProfile profile) return;
        _settings = _settings with { SelectedId = profile.Id };
        UpdateModelHint();
        // The active choice is saved with the configuration dialog; changing it needs no disk write.
        ChatStatus.Text = "已选择服务。发送时会将本轮消息和当前对话历史发给此服务。";
    }
    private void Configure_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ChatSettingsWindow(_settings) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        _settings = dialog.Settings; RefreshProfiles(); ChatStatus.Text = "配置已加密保存在本机。";
    }
    private void NewChat_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear(); _bubbles.Clear(); PromptInput.Clear(); _failedPrompt = null; _failedBubble = null;
        RetryButton.Visibility = Visibility.Collapsed; Welcome.Visibility = Visibility.Visible;
        ChatStatus.Text = "已新建对话。"; PromptInput.Focus();
    }
    public void AppendCapturedText(string text)
    {
        PromptInput.Text = string.IsNullOrWhiteSpace(PromptInput.Text) ? text : PromptInput.Text + "\n\n" + text;
        PromptInput.CaretIndex = PromptInput.Text.Length;
        PromptInput.Focus();
        ChatStatus.Text = "截图文字已放入输入框，可修改后发送。";
    }
    public void SetCaptureStatus(string status) => ChatStatus.Text = status;
    public void SetCaptureBusy(bool busy) => CaptureChatButton.IsEnabled = !busy;
    private void Capture_Click(object sender, RoutedEventArgs e) => CaptureRequested?.Invoke(this, EventArgs.Empty);
    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync(false);
    private async void Retry_Click(object sender, RoutedEventArgs e) => await SendAsync(true);
    private void Stop_Click(object sender, RoutedEventArgs e) => _request?.Cancel();
    private async void PromptInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && !e.IsRepeat)
        {
            e.Handled = true;
            await SendAsync(false);
        }
    }
    private async Task SendAsync(bool retry)
    {
        if (_request is not null || _closed) return;
        var prompt = retry ? _failedPrompt : PromptInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt)) { ChatStatus.Text = "请先输入文字。"; return; }
        if (ProviderPicker.SelectedItem is not ChatProfile selected) return;
        var profile = selected with { };
        try { profile.Validate(); }
        catch (Exception ex) { ChatStatus.Text = ex.Message; return; }

        if (retry && _failedBubble is not null) _bubbles.Remove(_failedBubble);
        else { _bubbles.Add(new("user", prompt, "你")); PromptInput.Clear(); }
        _failedPrompt = null; _failedBubble = null;
        var assistant = new ChatBubble("assistant", "", $"{profile.Name} · {profile.Model}");
        _bubbles.Add(assistant); Welcome.Visibility = Visibility.Collapsed;
        _request = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var request = _request;
        SetSending(true); ChatStatus.Text = "正在连接并生成…"; ConversationScroll.ScrollToEnd();
        try
        {
            var history = _history.Append(new ChatMessage("user", prompt)).ToList();
            var client = new ChatClient(_http);
            var result = await client.SendAsync(profile, history, delta =>
            {
                if (_closed) return;
                var follow = ConversationScroll.ScrollableHeight - ConversationScroll.VerticalOffset < 60;
                assistant.Text += delta;
                if (follow) { ConversationScroll.UpdateLayout(); ConversationScroll.ScrollToEnd(); }
            }, request.Token);
            _history.Add(new("user", prompt)); _history.Add(new("assistant", result));
            ChatStatus.Text = "响应完成。";
        }
        catch (Exception ex)
        {
            if (_closed) return;
            assistant.Note = ex is OperationCanceledException
                ? "已停止或请求超时。本轮未加入后续上下文，可重试。"
                : ex is HttpRequestException ? "连接失败，请检查网络、URL 和服务状态。本轮可重试。"
                : ex is System.Text.Json.JsonException ? "响应格式无法解析，请检查 API 格式。本轮可重试。"
                : ex.Message;
            // No raw server body, key, or prompt is sent to application logs.
            ChatStatus.Text = assistant.Note;
            _failedPrompt = prompt; _failedBubble = assistant;
        }
        finally
        {
            _request = null; request.Dispose();
            if (!_closed) { SetSending(false); PromptInput.Focus(); }
        }
    }
    private void SetSending(bool sending)
    {
        SendButton.IsEnabled = !sending; ProviderPicker.IsEnabled = !sending;
        ConfigureButton.IsEnabled = !sending; NewChatButton.IsEnabled = !sending;
        StopButton.Visibility = sending ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = !sending && _failedPrompt is not null ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is System.Windows.Controls.Button { Tag: ChatBubble bubble } && !string.IsNullOrWhiteSpace(bubble.Text))
            { System.Windows.Clipboard.SetText(bubble.Text); ChatStatus.Text = "已复制。"; }
        }
        catch { ChatStatus.Text = "复制失败，请重试。"; }
    }
    public void Close()
    {
        _closed = true; _request?.Cancel(); _http.Dispose();
    }
}
