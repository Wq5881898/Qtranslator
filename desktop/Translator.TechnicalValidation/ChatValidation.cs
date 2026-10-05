using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Translator.Core;
using Translator.Desktop;

internal static class ChatValidation
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        _checks++; Console.WriteLine("PASS: " + name);
    }
    private static async Task Fails(Func<Task> action, string name)
    {
        try { await action(); }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or JsonException)
        { Check(true, name); return; }
        throw new Exception("Expected failure: " + name);
    }
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private static ChatProfile Profile(ChatApiFormat format) => new()
    {
        Name = "test", BaseUrl = "https://service.example/v1", Api = format,
        Model = "exact/configured-model", ApiKey = "fake-test-key", MaxTokens = 100,
        ContextTokens = 10000, SystemPrompt = "Be helpful.",
        Authentication = format == ChatApiFormat.AnthropicMessages ? ChatAuthentication.ApiKey : ChatAuthentication.Bearer
    };
    private static HttpResponseMessage Reply(string body, string mediaType = "application/json", HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
    private static string Event(string json) => "data: " + json + "\n\n";
    public static async Task<int> RunAsync()
    {
        var history = new List<ChatMessage> { new("user", "first"), new("assistant", "answer"), new("user", "第二轮") };
        foreach (var format in Enum.GetValues<ChatApiFormat>())
        {
            var p = Profile(format);
            using var http = new HttpClient(new FakeHandler(async (request, token) =>
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                var root = body.RootElement;
                Check(root.GetProperty("model").GetString() == p.Model, format + " preserves configured model");
                var items = root.GetProperty(format == ChatApiFormat.OpenAiResponses ? "input" : "messages");
                Check(items.GetArrayLength() == (format == ChatApiFormat.AnthropicMessages ? 3 : 4) &&
                      items[items.GetArrayLength() - 1].GetProperty("content").GetString() == "第二轮", format + " multi-turn history");
                Check(request.RequestUri == p.Endpoint(), format + " configured URL and endpoint");
                if (format == ChatApiFormat.AnthropicMessages)
                    Check(request.Headers.GetValues("x-api-key").Single() == p.ApiKey && request.Headers.GetValues("anthropic-version").Single() == "2023-06-01" &&
                          root.GetProperty("system").GetString() == p.SystemPrompt, "Anthropic headers and system prompt");
                else Check(request.Headers.Authorization?.Parameter == p.ApiKey, format + " Bearer authentication");
                Check(root.GetProperty(format == ChatApiFormat.OpenAiResponses ? "max_output_tokens" : "max_tokens").GetInt32() == 100, format + " output limit");
                return Reply(format switch
                {
                    ChatApiFormat.OpenAiCompletions => "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"你好\"}}]}",
                    ChatApiFormat.OpenAiResponses => "{\"status\":\"completed\",\"output\":[{\"type\":\"reasoning\",\"summary\":[]},{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"你好\"}]}]}",
                    _ => "{\"content\":[{\"type\":\"thinking\",\"thinking\":\"hidden\"},{\"type\":\"text\",\"text\":\"你好\"}],\"stop_reason\":\"end_turn\"}"
                });
            }));
            var deltas = new StringBuilder();
            Check(await new ChatClient(http).SendAsync(p, history, s => deltas.Append(s), CancellationToken.None) == "你好" && deltas.ToString() == "你好", format + " JSON fallback");
        }
        foreach (var format in Enum.GetValues<ChatApiFormat>())
        {
            var stream = format switch
            {
                ChatApiFormat.OpenAiCompletions => ": keepalive\n\n" + Event("{\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}") +
                    Event("{\"choices\":[{\"delta\":{\"content\":\"你\"}}]}") + Event("{\"choices\":[{\"delta\":{\"content\":\"好\"},\"finish_reason\":\"stop\"}]}") + Event("[DONE]"),
                ChatApiFormat.OpenAiResponses => Event("{\"type\":\"response.created\"}") + Event("{\"type\":\"response.output_text.delta\",\"delta\":\"你\"}") +
                    Event("{\"type\":\"response.output_text.delta\",\"delta\":\"好\"}") + Event("{\"type\":\"response.completed\"}"),
                _ => "event: content_block_start\n" + Event("{\"type\":\"content_block_start\",\"content_block\":{\"type\":\"text\",\"text\":\"你\"}}") +
                    Event("{\"type\":\"ping\"}") + Event("{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"好\"}}") + Event("{\"type\":\"message_stop\"}")
            };
            using var http = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Reply(stream, "text/event-stream"))));
            var deltas = new List<string>();
            Check(await new ChatClient(http).SendAsync(Profile(format), history, deltas.Add, CancellationToken.None) == "你好" && deltas.Count(s => s.Length > 0) == 2, format + " incremental SSE");
        }
        using (var http = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Reply("fake-test-key secret-server-body", status: HttpStatusCode.Unauthorized)))))
        {
            try { await new ChatClient(http).SendAsync(Profile(ChatApiFormat.OpenAiCompletions), history, null, CancellationToken.None); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("401") && !ex.Message.Contains("fake-test-key") && !ex.Message.Contains("secret-server-body"), "HTTP errors do not expose key or server body"); }
        }
        var cases = new[]
        {
            (ChatApiFormat.OpenAiResponses, Event("{\"type\":\"error\",\"message\":\"secret\"}"), "text/event-stream", "SSE API error"),
            (ChatApiFormat.OpenAiResponses, Event("{\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}"), "text/event-stream", "truncated stream"),
            (ChatApiFormat.OpenAiResponses, Event("{\"type\":\"response.incomplete\"}"), "text/event-stream", "incomplete response"),
            (ChatApiFormat.AnthropicMessages, Event("{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"max_tokens\"}}"), "text/event-stream", "Anthropic output limit"),
            (ChatApiFormat.OpenAiCompletions, "{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"partial\"}}]}", "application/json", "completion output limit"),
            (ChatApiFormat.OpenAiResponses, "{\"output\":[]}", "application/json", "empty output"),
            (ChatApiFormat.OpenAiResponses, "not-json", "application/json", "malformed response")
        };
        foreach (var (format, body, media, name) in cases)
        {
            using var http = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Reply(body, media))));
            await Fails(() => new ChatClient(http).SendAsync(Profile(format), history, null, CancellationToken.None), name);
        }
        using (var http = new HttpClient(new FakeHandler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Reply(""); })))
        using (var cts = new CancellationTokenSource(30))
            await Fails(() => new ChatClient(http).SendAsync(Profile(ChatApiFormat.OpenAiResponses), history, null, cts.Token), "request cancellation");
        var custom = Profile(ChatApiFormat.OpenAiResponses) with { Authentication = ChatAuthentication.Custom, AuthHeader = "authHeader", AuthPrefix = "", MaxTokens = null };
        using (var request = ChatClient.CreateRequest(custom, history))
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(request.Headers.GetValues("authHeader").Single() == custom.ApiKey && !body.RootElement.TryGetProperty("max_output_tokens", out _) &&
                !body.RootElement.GetProperty("store").GetBoolean(), "custom authHeader, service default token limit, store false");
        }
        Check((custom with { BaseUrl = "https://service.example/v1/responses/" }).Endpoint().AbsolutePath == "/v1/responses", "full endpoint is not duplicated");
        Check(ChatSettings.Defaults().Profiles.Single(p => p.Name == "minimax").Endpoint().AbsolutePath == "/anthropic/v1/messages", "MiniMax Anthropic base URL appends v1/messages");
        await Fails(() => { (custom with { BaseUrl = "https://service.example/v1?key=x" }).Validate(); return Task.CompletedTask; }, "invalid URL rejected");
        await Fails(() => { (custom with { ApiKey = "x\r\nInjected: y" }).Validate(); return Task.CompletedTask; }, "header injection rejected");
        await Fails(() => { (custom with { Model = "" }).Validate(); return Task.CompletedTask; }, "missing model rejected");
        using (var http = new HttpClient(new FakeHandler((_, _) => throw new Exception("Must not send"))))
            await Fails(() => new ChatClient(http).SendAsync(Profile(ChatApiFormat.OpenAiResponses) with { ContextTokens = 101 }, history, null, CancellationToken.None), "context overflow rejected before network");
        var original = Encoding.UTF8.GetBytes("fake-key-and-settings");
        var protectedBytes = ChatSettingsStore.Protect(original);
        Check(!protectedBytes.AsSpan().SequenceEqual(original) && ChatSettingsStore.Unprotect(protectedBytes).AsSpan().SequenceEqual(original), "Windows DPAPI encryption roundtrip");
        Check(ChatSettings.Defaults().Profiles.All(p => p.ApiKey == ""), "preset profiles contain no credentials");
        Console.WriteLine($"Chat validation passed: {_checks} checks.");
        return 0;
    }

    public static int Render(string directory)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                var settings = ChatSettings.Defaults();
                var window = new ChatSettingsWindow(settings);
                window.Width = 780; window.Height = 760;
                Snapshot(window, Path.Combine(directory, "ai-settings.png"));
                // Switching providers must retain edits in the previous profile.
                ((TextBox)window.FindName("Model")).Text = "edited-model";
                var list = (ListBox)window.FindName("ProfilesList");
                list.SelectedIndex = 1; list.SelectedIndex = 0;
                Check(((TextBox)window.FindName("Model")).Text == "edited-model", "configuration survives provider switching");
                var chat = new ChatView();
                chat.AppendCapturedText("请解释这段文字：\nArtificial intelligence helps us understand language.");
                Snapshot(chat, Path.Combine(directory, "ai-chat.png"), 820, 680);
                var bubbles = (ItemsControl)chat.FindName("MessagesList");
                var collection = (System.Collections.ObjectModel.ObservableCollection<ChatBubble>)bubbles.ItemsSource;
                collection.Add(new("user", "请解释 neural network 这个词。", "你"));
                collection.Add(new("assistant", "Neural network（神经网络）是一种从示例中学习规律的模型。\n\n你可以把它想象成很多互相连接的小计算单元：\n1. 输入信息\n2. 学习有用的特征\n3. 生成预测或回答", "qwen · 模型响应示例"));
                ((Border)chat.FindName("Welcome")).Visibility = Visibility.Collapsed;
                ((TextBox)chat.FindName("PromptInput")).Clear();
                Snapshot(chat, Path.Combine(directory, "ai-conversation.png"), 820, 680);
                chat.Close();
                window.Close();
                var main = new MainWindow();
                ((TabControl)main.FindName("MainTabs")).SelectedIndex = 1;
                Snapshot(main, Path.Combine(directory, "qtranslator-tabs.png"), 860, 760);
                ((ChatView)main.FindName("ChatPanel")).Close();
                main.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw failure;
        return 0;
    }
    private static void Snapshot(FrameworkElement element, string path, double width = 780, double height = 760)
    {
        var target = element is Window window ? (FrameworkElement)window.Content : element;
        target.Measure(new System.Windows.Size(width, height));
        target.Arrange(new Rect(0, 0, width, height)); target.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(target);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
