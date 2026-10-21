# Qtranslator 1.2.2：翻译与 AI 对话

Qtranslator 是基于 Translator v1.1.8 代码开发的独立程序，可以与旧 Translator 并行使用。Windows 主窗口分为“翻译”和“AI 对话”两个标签页；新程序、浏览器扩展、配置和收藏均独立，不读取或迁移旧程序的数据。

## 运行与使用

1. 完整解压 `Qtranslator-v1.2.2-win-x64.zip`，保留 desktop、bridge-host、extension 三个文件夹的相对位置。
2. 在 Windows 文件资源管理器中直接双击 `desktop/Qtranslator.exe`。无需安装 .NET，无需退出或卸载旧 Translator。
3. Qtranslator 的翻译标签页需要 Chrome 打开并加载本包 `extension` 中名为 Qtranslator 的独立扩展。保留旧 Translator 扩展及其加载目录，不用替换。新插件默认关闭自动网页划词捕获，避免抢占旧插件的侧栏；可通过 Qtranslator 设置主动开启。AI 对话独立运行，不依赖 Chrome。
4. 打开“AI 对话 → API 配置”，选择或新增服务档案，输入 API Key，核对 URL、API 格式和模型，然后保存。
5. 输入消息后点击发送或按 Enter。Shift+Enter 换行；停止取消当前请求；重试重新发送失败的一轮；新建对话清空当前消息。
6. Qtranslator 的截图翻译默认为 `Ctrl+Alt+X`，AI 截图默认为 `Ctrl+Shift+D`。旧 Translator 的 `Ctrl+Shift+X` 留给旧程序，Qtranslator 不允许将该组合配置为翻译快捷键。AI 截图识别中英文并追加到草稿，不自动发送。若其他应用已占用快捷键，Qtranslator 只提示注册失败，不会释放或改变其他程序的快捷键，可用截图按钮。

## 插件连接与启动排查（1.2.2）

桌面端启动时会检查完整连接，空闲时每 15 秒检查一次。点击“检查连接”可重新注册并检查；点击“插件文件夹”可打开本包配套扩展目录。插件设置的“桌面连接”区域显示连接状态、最近检查时间、Chrome 原始错误，并提供“检查并重连”。插件断线后会自动重试，间隔从 2 秒逐步增加到最多 10 秒。

桌面端显示“已连接”需要 Chrome 中的 Qtranslator 插件实际回应，不能仅靠注册表存在或桥接进程存在来判断。插件设置中的连接成功只表示浏览器与本机桥接连通；翻译语言模型是否就绪，仍需发送一次翻译确认。

如果错误为 `Specified native messaging host not found`：

1. 从文件资源管理器直接双击本包的 `desktop/Qtranslator.exe`。程序会检查桥接文件，并注册当前用户的 32 位和 64 位 Chrome 主机项。
2. 确认 Chrome 加载的是 Qtranslator 扩展，ID 为 `kjgoachjhbfkijaopcmallakpfnalkdo`。保留旧 Translator 的扩展和注册项。
3. 在 Qtranslator 插件设置点击“检查并重连”，再回到桌面点击“检查连接”。如果刚替换了扩展文件，先在 Chrome 扩展管理页重新加载 Qtranslator。
4. 仍未连通时，查看插件的“Chrome 错误详情”和 `%LOCALAPPDATA%/Qtranslator/logs/desktop.log`，根据具体错误检查文件位置或浏览器策略。不要仅凭历史错误记录反复重启 Chrome；以新的连接检查及实际翻译结果为准。

本机排查曾观察到：开发工具启动时注册检查正常，但 Chrome 找不到主机；从资源管理器启动后连接和翻译恢复。证据说明启动环境会影响结果，尚未确认具体的底层隔离机制；因此不能保证仅重启 Chrome 就能修复。

升级前先复制当前 Qtranslator 对话中需要保留的内容；关闭旧版 Qtranslator 会丢失本次内存对话。新旧 Qtranslator 版本共用同一套产品配置、快捷键和桥接注册，请避免长期同时运行。旧产品 Translator 则保持独立。

回退：关闭新版本 Qtranslator，从资源管理器打开保留的旧版 Qtranslator.exe，让其恢复自身桥接路径；恢复对应旧版扩展文件并重新加载。不会改变旧产品 Translator。

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
dotnet run --project desktop/Qtranslator.TechnicalValidation --configuration Release -- --bridge desktop/Qtranslator.BridgeHost/bin/Release/net10.0-windows10.0.19041.0/Qtranslator.BridgeHost.exe
dotnet run --project desktop/Qtranslator.TechnicalValidation --configuration Release -- --chat
dotnet run --project desktop/Qtranslator.TechnicalValidation --configuration Release -- --chat-render artifacts/ai-preview
```

协议实现参考 [OpenAI Chat Completions](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create)、[OpenAI Responses](https://developers.openai.com/api/docs/guides/migrate-to-responses)、[Anthropic 流式消息](https://platform.claude.com/docs/en/build-with-claude/streaming) 和 [MiniMax Anthropic 接口](https://platform.minimax.cn/docs/api-reference/text-anthropic-api)。
