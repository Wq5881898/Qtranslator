using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Qtranslator.Core;

public enum ChatApiFormat { OpenAiCompletions, OpenAiResponses, AnthropicMessages }
public enum ChatAuthentication { Bearer, ApiKey, Custom }

public sealed record ChatProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "自定义服务";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public ChatApiFormat Api { get; set; } = ChatApiFormat.OpenAiCompletions;
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int? ContextTokens { get; set; }
    public int? MaxTokens { get; set; }
    public string SystemPrompt { get; set; } = "";
    public ChatAuthentication Authentication { get; set; } = ChatAuthentication.Bearer;
    public string AuthHeader { get; set; } = "Authorization";
    public string AuthPrefix { get; set; } = "Bearer ";
    public bool Stream { get; set; } = true;

    public Uri Endpoint()
    {
        if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("URL 必须是 HTTPS（本机服务可用 HTTP），且不能带凭据、查询或片段。");
        var suffix = Api switch
        {
            ChatApiFormat.OpenAiCompletions => "/chat/completions",
            ChatApiFormat.OpenAiResponses => "/responses",
            _ => "/messages"
        };
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(suffix, StringComparison.Ordinal)) return new Uri(uri.GetLeftPart(UriPartial.Authority) + path);
        if (Api == ChatApiFormat.AnthropicMessages && !path.EndsWith("/v1", StringComparison.Ordinal)) suffix = "/v1/messages";
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + path + suffix);
    }

    public void Validate(bool requireKey = true)
    {
        _ = Endpoint();
        if (!Enum.IsDefined(Api) || !Enum.IsDefined(Authentication)) throw new InvalidOperationException("无效的 API 或鉴权方式。");
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Model)) throw new InvalidOperationException("请填写服务名称和模型。");
        if (requireKey && string.IsNullOrWhiteSpace(ApiKey)) throw new InvalidOperationException("请在 API 配置中填写 Key。");
        if (ApiKey.Any(char.IsControl)) throw new InvalidOperationException("Key 不能包含换行或控制字符。");
        if (ContextTokens is <= 0 || MaxTokens is <= 0) throw new InvalidOperationException("Token 上限必须是正整数，或留空使用服务默认值。");
        if (Api == ChatApiFormat.AnthropicMessages && MaxTokens is null) throw new InvalidOperationException("Anthropic Messages 必须填写最大输出 Token。");
        if (ContextTokens is not null && MaxTokens >= ContextTokens) throw new InvalidOperationException("输出上限必须小于上下文上限。");
        if (Authentication == ChatAuthentication.Custom &&
            (string.IsNullOrWhiteSpace(AuthHeader) || !AuthHeader.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ||
             AuthPrefix.Any(char.IsControl)))
            throw new InvalidOperationException("自定义鉴权头名称或前缀无效。");
    }
}

public sealed record ChatMessage(string Role, string Content);
public sealed record ChatSettings(List<ChatProfile> Profiles, string SelectedId)
{
    public static ChatSettings Defaults()
    {
        var profiles = new List<ChatProfile>
        {
            new() { Name = "qwen", BaseUrl = "https://api.xauusd.news/v1", Model = "ARC4NUM/Qwen3.8-Flash-Next-Uncensored-MLX-Serve-4bit", ContextTokens = 262144, MaxTokens = 8192 },
            new() { Name = "minimax", BaseUrl = "https://api.minimaxi.com/anthropic", Api = ChatApiFormat.AnthropicMessages, Model = "MiniMax-M2.1", ContextTokens = 131072, MaxTokens = 8192, Authentication = ChatAuthentication.ApiKey },
            new() { Name = "zai", BaseUrl = "https://api.z.ai/api/coding/paas/v4", Model = "glm-4.5", ContextTokens = 131072, MaxTokens = 8192 },
            new() { Name = "sub2api", BaseUrl = "https://api.151077.xyz/v1", Api = ChatApiFormat.OpenAiResponses, Model = "gpt-5.6-luna" },
            new() { Name = "grok", BaseUrl = "https://api.151077.xyz/v1", Api = ChatApiFormat.OpenAiResponses, Model = "grok-4.6" }
        };
        return new(profiles, profiles[0].Id);
    }
}

/// <summary>One explicit request, no provider fallback or automatic paid retry.</summary>
public sealed class ChatClient(HttpClient http)
{
    public async Task<string> SendAsync(ChatProfile profile, IReadOnlyList<ChatMessage> history,
        Action<string>? onDelta, CancellationToken token)
    {
        profile.Validate();
        if (history.Count == 0 || history[^1].Role != "user") throw new InvalidOperationException("请先输入消息。");
        if (history.Any(m => m.Role is not ("user" or "assistant"))) throw new InvalidOperationException("对话角色无效。");
        // Conservative upper estimate: one token per Unicode scalar, plus message overhead.
        // This is deliberately not represented as the provider's exact tokenizer.
        var estimate = (long)profile.SystemPrompt.EnumerateRunes().Count() + 16;
        estimate += history.Sum(m => (long)m.Content.EnumerateRunes().Count() + 16);
        if (profile.ContextTokens is int limit && estimate + (profile.MaxTokens ?? 0) > limit)
            throw new InvalidOperationException("对话已超过保守估算的上下文上限。请新建对话、缩短输入或调整上限；历史不会被自动删除。");

        using var request = CreateRequest(profile, history);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"API 返回 HTTP {(int)response.StatusCode}。" + ((int)response.StatusCode switch
            {
                401 or 403 => "请检查 Key、鉴权方式和账号权限。",
                404 => "请检查 URL、API 格式和模型名称。",
                429 => "服务限流或额度不足，请稍后重试。",
                _ => "请检查服务状态和配置。"
            }));

        var output = new StringBuilder();
        void Append(string delta) { if (delta.Length == 0) return; output.Append(delta); onDelta?.Invoke(delta); }
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream);
            var data = new StringBuilder();
            var complete = false;
            while (await reader.ReadLineAsync(token) is { } line)
            {
                if (line.Length == 0)
                {
                    if (data.Length == 0) continue;
                    var payload = data.ToString(); data.Clear();
                    if (payload == "[DONE]") { complete = true; break; }
                    using var json = JsonDocument.Parse(payload);
                    var root = json.RootElement;
                    ThrowIfApiError(root);
                    Append(StreamText(root, profile.Api));
                    var kind = String(root, "type");
                    if (kind == "response.incomplete") throw new InvalidOperationException("输出未完成，请提高输出上限后重试。");
                    if (kind is "response.completed" or "message_stop") { complete = true; break; }
                    if (profile.Api == ChatApiFormat.OpenAiCompletions && root.TryGetProperty("choices", out var choices))
                        foreach (var choice in choices.EnumerateArray())
                            if (String(choice, "finish_reason") is { Length: > 0 } finish)
                            {
                                if (finish != "stop") throw new InvalidOperationException($"输出提前结束（{finish}），请调整输出上限或提示词。");
                                complete = true;
                            }
                    if (kind == "message_delta" && root.TryGetProperty("delta", out var stopDelta) && String(stopDelta, "stop_reason") == "max_tokens")
                        throw new InvalidOperationException("输出达到 Token 上限，请提高上限后重试。");
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (data.Length > 0) data.Append('\n');
                    data.Append(line[5..].TrimStart(' '));
                }
            }
            if (!complete) throw new InvalidOperationException("响应流中断，未收到完成标记。可重试本轮消息。");
        }
        else
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            ThrowIfApiError(json.RootElement);
            var root = json.RootElement;
            if (String(root, "status") == "incomplete" || String(root, "stop_reason") == "max_tokens")
                throw new InvalidOperationException("输出达到上限或未完成，请调整输出上限后重试。");
            if (profile.Api == ChatApiFormat.OpenAiCompletions && root.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 && String(choices[0], "finish_reason") is "length" or "content_filter")
                throw new InvalidOperationException("输出提前结束，请调整输出上限或提示词。");
            Append(ResponseText(root, profile.Api));
        }
        if (string.IsNullOrWhiteSpace(output.ToString())) throw new InvalidOperationException("API 未返回文本。请检查模型和 API 格式。");
        return output.ToString();
    }

    public static HttpRequestMessage CreateRequest(ChatProfile p, IReadOnlyList<ChatMessage> history)
    {
        var body = new Dictionary<string, object?> { ["model"] = p.Model.Trim(), ["stream"] = p.Stream };
        var messages = history.Select(m => new { role = m.Role, content = m.Content }).ToList();
        if (p.Api == ChatApiFormat.AnthropicMessages)
        {
            body["messages"] = messages;
            body["max_tokens"] = p.MaxTokens;
            if (!string.IsNullOrWhiteSpace(p.SystemPrompt)) body["system"] = p.SystemPrompt;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(p.SystemPrompt)) messages.Insert(0, new { role = "system", content = p.SystemPrompt });
            body[p.Api == ChatApiFormat.OpenAiResponses ? "input" : "messages"] = messages;
            if (p.Api == ChatApiFormat.OpenAiResponses) body["store"] = false;
            if (p.MaxTokens is not null) body[p.Api == ChatApiFormat.OpenAiResponses ? "max_output_tokens" : "max_tokens"] = p.MaxTokens;
        }
        var request = new HttpRequestMessage(HttpMethod.Post, p.Endpoint())
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        if (p.Authentication == ChatAuthentication.Bearer) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", p.ApiKey.Trim());
        else request.Headers.Add(p.Authentication == ChatAuthentication.ApiKey ? "x-api-key" : p.AuthHeader.Trim(),
            (p.Authentication == ChatAuthentication.Custom ? p.AuthPrefix : "") + p.ApiKey.Trim());
        if (p.Api == ChatApiFormat.AnthropicMessages) request.Headers.Add("anthropic-version", "2023-06-01");
        return request;
    }

    private static string String(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static void ThrowIfApiError(JsonElement root)
    {
        if (String(root, "type") is "error" or "response.failed" || root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("API 报告生成失败，请检查服务和配置后重试。");
    }
    private static string ContentText(JsonElement content) => content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" :
        content.ValueKind == JsonValueKind.Array ? string.Concat(content.EnumerateArray().Where(b => String(b, "type") is "text" or "output_text").Select(b => String(b, "text"))) : "";
    private static string StreamText(JsonElement root, ChatApiFormat api)
    {
        if (api == ChatApiFormat.OpenAiResponses) return String(root, "type") == "response.output_text.delta" ? String(root, "delta") : "";
        if (api == ChatApiFormat.AnthropicMessages)
        {
            if (String(root, "type") == "content_block_delta" && root.TryGetProperty("delta", out var delta) && String(delta, "type") == "text_delta") return String(delta, "text");
            if (String(root, "type") == "content_block_start" && root.TryGetProperty("content_block", out var block) && String(block, "type") == "text") return String(block, "text");
            return "";
        }
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 && choices[0].TryGetProperty("delta", out var d) && d.TryGetProperty("content", out var c)) return ContentText(c);
        return "";
    }
    private static string ResponseText(JsonElement root, ChatApiFormat api)
    {
        if (api == ChatApiFormat.OpenAiCompletions && root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)) return ContentText(content);
        if (api == ChatApiFormat.AnthropicMessages && root.TryGetProperty("content", out var blocks)) return ContentText(blocks);
        if (api == ChatApiFormat.OpenAiResponses && root.TryGetProperty("output", out var output))
            return string.Concat(output.EnumerateArray().Where(o => String(o, "type") == "message" && String(o, "role") == "assistant")
                .Select(o => o.TryGetProperty("content", out var c) ? ContentText(c) : ""));
        return "";
    }
}
