# Qtranslator 1.2.1：翻译与 AI 对话

Qtranslator 是基于 Translator v1.1.8 代码开发的独立程序，可以与旧 Translator 并行使用。Windows 主窗口分为“翻译”和“AI 对话”两个标签页；新程序、浏览器扩展、配置和收藏均独立，不读取或迁移旧程序的数据。

## 运行与使用

1. 完整解压 `Qtranslator-v1.2.1-win-x64.zip`，保留 desktop、bridge-host、extension 三个文件夹的相对位置。
2. 运行 `desktop/Qtranslator.exe`。无需安装 .NET，无需退出或卸载旧 Translator。
3. Qtranslator 的翻译标签页需要 Chrome 打开并加载本包 `extension` 中名为 Qtranslator 的独立扩展。保留旧 Translator 扩展及其加载目录，不用替换。新插件默认关闭自动网页划词捕获，避免抢占旧插件的侧栏；可通过 Qtranslator 设置主动开启。AI 对话独立运行，不依赖 Chrome。
4. 打开“AI 对话 → API 配置”，选择或新增服务档案，输入 API Key，核对 URL、API 格式和模型，然后保存。
5. 输入消息后点击发送或按 Enter。Shift+Enter 换行；停止取消当前请求；重试重新发送失败的一轮；新建对话清空当前消息。
6. Qtranslator 的截图翻译默认为 `Ctrl+Alt+X`，AI 截图默认为 `Ctrl+Shift+D`。旧 Translator 的 `Ctrl+Shift+X` 留给旧程序，Qtranslator 不允许将该组合配置为翻译快捷键。AI 截图识别中英文并追加到草稿，不自动发送。若其他应用已占用快捷键，Qtranslator 只提示注册失败，不会释放或改变其他程序的快捷键，可用截图按钮。

## 与旧程序隔离

| 项目 | Qtranslator |
| --- | --- |
| 桌面程序 | `Qtranslator.exe` |
| 桥接程序 | `Qtranslator.BridgeHost.exe` |
| 配置、收藏和日志 | `%LOCALAPPDATA%/Qtranslator/` |
| 默认安装目录 | `%LOCALAPPDATA%/Programs/Qtranslator/` |
| Native Messaging 主机 | `com.wq5881898.qtranslator` |
| 默认命名管道 | `wq5881898.qtranslator` |
| 浏览器扩展 ID | `kjgoachjhbfkijaopcmallakpfnalkdo` |

安装器使用独立 AppId 和卸载项；安装、更新、卸载只操作 Qtranslator 的程序和注册项。旧 Translator 的扩展 ID、桥接注册项、目录及收藏不被替换。之前的 1.2.0 测试包未完成隔离，已撤回；请使用 1.2.1 或更新版本。

## 服务配置

预置 qwen、minimax、zai、sub2api、grok 五个示例档案，URL、模型和数值沿用需求示例，不保证第三方服务提供或允许这些模型。所有字段可编辑，Key 默认为空。模型 ID 按原样发送，不自动替换。

| API 格式 | 请求路径 | 输出上限字段 |
| --- | --- | --- |
| openai-completions | Base URL + `/chat/completions` | `max_tokens` |
| openai-responses | Base URL + `/responses` | `max_output_tokens` |
| anthropic-messages | Base URL + `/v1/messages`（Base 已以 `/v1` 结束时仅加 `/messages`） | `max_tokens`，必填 |

支持直接填写完整端点，不重复追加同一协议路径。例如 MiniMax 示例最终调用 `https://api.minimaxi.com/anthropic/v1/messages`。

支持 `Authorization: Bearer KEY`、`x-api-key: KEY` 和自定义 authHeader 名称及前缀。Anthropic 自动携带 `anthropic-version: 2023-06-01`。URL 接受 HTTPS；本机回环地址也可使用 HTTP。对于只能提供非流式 JSON 响应的服务可关闭“流式显示”。默认请求超时为三分钟。

sub2api 的模型可改为 `gpt-5.6-sol`、`gpt-5.6-terra`、`gpt-6-astra` 或服务实际支持的其他 ID。若服务要求名为 `authHeader` 的头，选择自定义鉴权，名称填写 `authHeader`，前缀按服务要求填写或留空。

上下文和输出上限留空表示服务默认值，Anthropic 输出上限除外。上下文检查采用每个 Unicode 字符计一个 Token 加消息开销的保守估算，可能提前提示超限；不是模型的精确 tokenizer。超过配置上限时拒绝发送，不删除历史。失败、中断和取消的轮次保留在界面用于复制或重试，但不会进入下一轮上下文。切换服务后，当前对话的成功轮次会随下一次发送给新选择的服务。

## 数据与限制

- AI 配置（包括 Key）用 Windows DPAPI 当前用户加密，文件在 `%LOCALAPPDATA%/Qtranslator/ai-settings.bin`。配置不能直接搬到另一个 Windows 用户；重新填写即可。
- Key 不进入仓库、运行包或日志。API 调用只连接所选配置的端点，不自动切换服务，不自动重试收费请求，不跟随 HTTP 重定向。
- 截图在内存中完成 OCR，AI 接收的是确认发送的文字和对话上下文，不上传截图。
- 对话在本次窗口会话的内存中，关闭应用后不会恢复；尚不提供历史会话列表、文件上传、联网工具或富文本 Markdown 渲染。
- AI 服务的错误和 HTTP 状态会显示在窗口中；第三方接口和模型的真实可用性需要使用你自己的 Key 验证。本次测试使用模拟服务，没有调用收费模型。

## 开发验证

```powershell
dotnet build desktop/Qtranslator.slnx --configuration Release
dotnet run --project desktop/Qtranslator.TechnicalValidation --configuration Release -- --chat
dotnet run --project desktop/Qtranslator.TechnicalValidation --configuration Release -- --chat-render artifacts/ai-preview
```

协议实现参考 [OpenAI Chat Completions](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create)、[OpenAI Responses](https://developers.openai.com/api/docs/guides/migrate-to-responses)、[Anthropic 流式消息](https://platform.claude.com/docs/en/build-with-claude/streaming) 和 [MiniMax Anthropic 接口](https://platform.minimax.cn/docs/api-reference/text-anthropic-api)。
