# Translator 第二阶段设计方案

文档版本：1.1  
最后更新：2026-07-29  
维护基线：`Wq5881898/translator`

## 1. 阶段边界

第二阶段是独立运行的 Windows 桌面程序，不破坏第一阶段浏览器插件的独立性。两者通过受控的 Chrome Native Messaging 桥接协作：

```text
屏幕框选
  → 内存截图
  → 桌面端本地 OCR
  → 用户校对英文
  → Named Pipe
  → Chrome Native Host
  → 第一阶段扩展
  → Chrome 本地 Translator API
  → 中文结果返回桌面端
```

截图永远只进入桌面端本地 OCR。Native Messaging、翻译 Provider、云端备用服务和日志均不得接收截图。

## 2. 第一批次最终 OCR 方案

### 2.1 Provider 结构

- `IOcrProvider` 是稳定接口；
- `PackagedEnglishOcrProvider` 是默认实现；
- `WindowsOcrProvider` 仅在打包引擎异常时备用；
- OCR 结果包含文本、Provider、耗时和平均置信度；
- 截图通过 `MemoryStream` 传递，用完立即释放，不保存到磁盘或剪贴板。

### 2.2 随包模型

测试包包含 Apache-2.0 的 Tesseract `tessdata_fast`：

- `eng.traineddata`：英文识别和英文字母校正；
- `chi_sim.traineddata`：识别简体中文边界，避免中文笔画被强行猜成英文。

用户不需要安装 Windows 语言包、注册账号、配置 Key 或联网。模型来源与 SHA-256 记录在 `THIRD_PARTY_NOTICES.md`。

### 2.3 双引擎识别

每次截图在本地执行：

1. 英文模型识别一次，取得较准确的英文字母；
2. 英文加简体中文模型识别一次，确定中文和英文边界；
3. 若画面没有中文，优先使用英文结果；
4. 若画面包含中文，对齐两组 token，使用双语结果限制边界、英文结果校正字母；
5. 最终编辑框只保留可翻译的英文、相关数字和英文标点。

例如：

```text
GitHub CI 通过。  → GitHub CI
已处理 4m 58s    → 空结果并提示没有可靠英文
```

### 2.4 质量判定与清理

轻量校验包括：

- OCR 平均置信度；
- 拉丁字母数量；
- 是否至少包含英文单词，而不只是数字、时间或单位字母；
- 异常孤立单字母比例；
- 基本元音比例；
- 图标产生的前导纯符号和无元音短小写伪单词；
- 技术缩写中常见的 `I/l` 混淆校正。

不可靠结果必须清空编辑框，并在当前窗口下方说明原因和恢复方法，不能把伪英文交给翻译 Provider。

### 2.5 已处理问题

| 问题 | 根因 | 最终处理 |
|---|---|---|
| 所有截图返回空结果 | WPF 逻辑坐标与物理像素混用 | Per-Monitor V2、`PointToScreen` 和物理像素截图 |
| 中文后再次截英文失败 | 系统 OCR 跟随中文用户语言 | 随包英文模型优先 |
| 英文出现异常空格和大小写 | 中文 OCR 强行识别英文 | Tesseract 英文模型 |
| 图标变成 `fll` | 图形线条被当作字符 | 前导图形噪声清理和短伪单词拒绝 |
| 中文变成 `iat`、`EXME` | 英文模型无法理解中文边界 | 随包中文模型和双引擎对齐 |
| `CI` 变成 `Cl` | 大写 I 与小写 l 字形接近 | 技术缩写上下文校正 |

### 2.6 回归样本

自动及人工回归覆盖：

- 内存生成英文图片；
- 内存生成中文图片；
- Merriam-Webster 当日单词页面长段落；
- 中英文混合句子；
- 中文状态加时间；
- 图标加英文文件名；
- 取消截图和连续多次截图；
- Windows 缩放及物理像素坐标。

## 3. 第二批次设计

第二批次完成真实翻译闭环：

- 桌面端通过固定名称的本地 Named Pipe 与 Native Host 通信；
- 扩展 Service Worker 使用持久 `runtime.connectNative()` 连接 Host；
- Host 只转发版本化 JSON 文本消息；
- 扩展调用 Chrome Translator API 执行 `en → zh`；
- 中文结果、Provider、请求 ID 和错误返回桌面端；
- 单次请求限制 5000 字符并设置超时；
- 浏览器、扩展、Host、模型或语言包不可用时显示可恢复错误。

第二批次仍不实现全局快捷键、收藏共享、发音、Azure、正式安装器或开机启动。

## 4. 隐私与安全

- 桌面端发送的唯一用户内容是校对后的英文；
- 协议没有截图字段；
- Native Host 校验协议版本、消息类型、请求 ID 和长度；
- Chrome Host 清单只允许精确扩展 ID，不使用通配符；
- Windows 注册使用当前用户 `HKCU`，无需管理员权限；
- 不记录 OCR 全文、译文、截图或收藏；
- 所有错误信息写入界面或 `stderr`，Native Host 的 `stdout` 只允许协议帧。

## 5. 维护规则

1. OCR、桥接和翻译 Provider 保持接口隔离；
2. 新模型必须记录来源、许可证、哈希和体积；
3. 新的识别修正必须有回归测试，避免针对单张截图硬编码完整文本；
4. 错误必须显示在用户当前操作窗口；
5. 每个批次保持独立分支、测试报告和可下载产物；
6. 第一阶段插件继续可以脱离桌面程序独立运行。

## 6. 第二批次桥接安装与诊断补充

Chrome 在 Windows 上从以下注册表键的“默认值”读取 Native Messaging 清单路径：

```text
HKCU\Software\Google\Chrome\NativeMessagingHosts\com.wq5881898.translator.stage2
```

PowerShell Registry Provider 中，名为 `(default)` 的属性并不等于注册表默认值。安装脚本必须使用 `Set-Item -Value` 写入真正的默认值，并在退出前通过 `GetValue("")` 回读验证。发布 CI 还要解析生成的 JSON 清单，检查 Host 绝对路径和精确扩展来源。

桌面端开始翻译前先执行最长约 4 秒的桥接健康检查：

- 桥接未注册、扩展未加载或 Chrome 未启动时，立即显示具体恢复步骤；
- 健康检查通过后才发送正文；
- 首次语言包准备和真实翻译有独立超时；
- 任一层断开都必须在当前桌面窗口结束等待并显示错误，不能无限保持“正在发送”。

本规则来自第二批首次人工测试暴露的问题：模拟 Host 回包测试虽然通过，但未验证 Windows 注册表默认值，也没有验证真实 Chrome 中是否加载了固定 ID 扩展。

## 7. 第三批次全局快捷截图流程

第三批次将原来分开的“框选”和“翻译”操作合并为一个用户流程：

```text
全局 Ctrl+Shift+X
→ 防止重复任务
→ 隐藏结果窗口
→ 多显示器框选
→ 内存截图与本地 OCR
→ 可靠性判断
→ 桥接健康检查
→ Chrome 本地翻译
→ 恢复并激活结果窗口
```

### 7.1 快捷键

- Windows `RegisterHotKey` 注册 `Ctrl+Shift+X`；
- 使用 `MOD_NOREPEAT` 避免按键长按产生重复截图；
- 只在当前进程生命周期有效，退出时必须注销；
- 注册失败时不终止程序，在状态区提示快捷键冲突，并保留按钮入口；
- 捕获或翻译进行中再次触发时只显示“任务正在运行”，不能并发启动截图。

### 7.2 简洁界面

主界面只保留：

- `Select screen region`；
- `Translate again`；
- `Copy Chinese`；
- 可编辑英文、只读中文和单一状态区。

技术细节默认不展开。错误必须说明“发生了什么”和“如何恢复”，不能只显示异常类型或长期停留在加载状态。

### 7.3 自动流程异常边界

- 用户取消框选：恢复窗口，不保存、不 OCR；
- OCR 不可靠：清空结果，不调用翻译 Provider；
- Chrome、扩展或 Host 不可用：健康检查约 4 秒内结束；
- 首次语言包或翻译超时：结束忙碌状态，允许原文不丢失地重试；
- 剪贴板不可用：不影响翻译结果，只在状态区提示复制失败；
- 任一异常都必须重新启用按钮和全局快捷键处理。

### 7.4 OCR 英文撇号规范化

英文网页和 PDF 常使用 Unicode 弯引号。OCR 清理层在过滤非英文字符前，先将
`’`、`‘`、`ʼ` 和全角撇号统一为 ASCII `'`，避免 `week’s` 被拆成 `week s`。

若 OCR 引擎已经把撇号识别为空格，清理层会把独立的 `s` 与前一个英文词合并为
所有格形式。该规则只在 OCR 清理流程内使用，不修改用户在英文编辑框内手动输入
的文本。回归样本必须至少覆盖 `week’s`、`Russia’s`、`America’s`、`it's` 和
`don't`。

## 8. Batch E 共享收藏库

桌面端和浏览器插件以 `%LOCALAPPDATA%\Translator\favorites.json` 作为共同数据源。
Native Host 只处理 `favorites.read`、`favorites.write` 和 `favorites.patch`
本地协议消息，不接收
截图，也不上传收藏。写入时先生成临时文件，再原子替换正式文件。

收藏结构与第一阶段保持兼容：`id`、`kind`、`originalText`、
`translatedText`、`firstFavoritedAt` 和可选 `phonetic`。插件启动时把原有
`chrome.storage.local` 收藏与共享文件按稳定 ID 合并，从而自动迁移旧数据。
Native Host 暂不可用时，插件仍保存到浏览器本地，并记录待同步状态。

桌面主界面只显示爱心按钮和 `Favorites` 入口。收藏列表在独立窗口中按需打开，
支持单选或 Ctrl/Shift 多选批量删除、CSV 导入和 CSV 导出。批量删除把所有选中
ID 放进同一次 `favorites.patch`，避免逐条写入产生中间状态。CSV 列名继续与
第一阶段一致：
`Type, English, Phonetic, Chinese translation, First saved`。

### 8.1 收藏一致性与刷新

收藏修改通过 `favorites.patch` 增量协议提交，只携带新增/更新项目和删除 ID，
不再整表覆盖。共享文件写入由跨进程信号量串行化，并继续使用临时文件原子替换。

插件在收藏面板打开、浏览器侧边栏重新获得焦点或从隐藏恢复时读取一次共享库。
其余时间不轮询，因此不会影响划词翻译或常驻性能。桌面收藏窗口每次打开及完成
导入、删除后重新读取共享库。

桌面程序启动时根据自身所在测试包自动定位相邻的 `bridge-host`，在当前用户
`HKCU` 下创建或更新 Chrome Native Messaging 注册项，并把清单写入
`%LOCALAPPDATA%\Translator\bridge`。该过程不需要管理员权限，也不再依赖用户
手动运行 PowerShell。

### 8.2 离线优先与自动恢复状态机

插件保存 `migrationCompleted` 和 `dirty` 两个本地同步标记：

- `migrationCompleted=false`：尚未把原浏览器收藏迁移到共享库；
- `dirty=true`：Bridge 不可用期间浏览器收藏发生过增删或导入；
- `migrationCompleted=true, dirty=false`：共享库是当前权威数据源。

侧边栏启动后立即尝试连接，并按 `0/1/3/6/10` 秒延迟最多尝试五次，总窗口约
20 秒。全部失败后立即停止，不设置定时轮询，也不影响翻译和浏览器本地收藏。
Bridge 恢复后，由以下离散事件触发单次重连：侧边栏重新聚焦、页面从隐藏恢复、
打开收藏面板、收藏增删或导入，以及用户点击 `Sync now`。
自动事件之间有 30 秒冷却时间；打开收藏面板和 `Sync now` 属于用户明确操作，可
跳过冷却时间。冷却只比较时间戳，不创建后台计时器。

首次迁移或 `dirty=true` 时，将浏览器收藏与共享收藏按稳定 ID 去重合并，再通过
一次 `favorites.patch` 写回；成功后设置 `migrationCompleted=true`、
`dirty=false`。已经完成迁移且没有离线修改时，直接以共享库为准，从而保留桌面端
删除操作，不会被浏览器旧快照重新添加。同步失败只把状态切换为
`Browser storage · sync paused`；成功则显示 `Shared with the Windows app`。
并发触发共享同一个进行中的同步任务，避免重复 Native Messaging 请求。

Windows 程序不会持续唤醒 Chrome。其职责是启动时自动恢复 Native Host 注册；
插件在下一次上述用户事件中发现 Bridge 已可用并自动完成合并。这种事件触发方案
保证只使用浏览器插件时没有无限重试或常驻资源消耗。

### 8.3 主界面操作布局

英文和中文文本框的标题行右上角各放置独立复制按钮。英文按钮复制 OCR 后可编辑
文本，中文按钮复制翻译结果。设置入口使用带工具提示的齿轮图标，避免长文本按钮
占用操作区宽度。

`Favorites` 按钮显示共享收藏总数，并在窗口激活、收藏增删和关闭收藏列表后更新。

## 9. Batch E 最终交付补充

### 9.1 Bridge 注册可靠性

安装程序在当前用户的 64 位和 32 位注册表视图中写入 Native Messaging Host，
桌面程序启动、重新激活及翻译前也会执行回读验证和自恢复。注册成功必须同时满足：

1. 注册表默认值存在；
2. 默认值指向的 JSON 清单存在；
3. 清单中的 Host 绝对路径存在；
4. `allowed_origins` 只包含固定扩展 ID。

清单已经生成但注册表写入失败时，桌面状态区显示具体注册错误。插件收藏页的
`Sync now` 不再只保留 `sync paused`，而是在当前页面显示 Chrome 返回的 Native
Messaging 错误，方便区分注册缺失、扩展 ID 不一致和 Host 无法启动。

### 9.2 单词词典式翻译

句子和段落继续直接使用 Chrome 本地 `en → zh` 翻译。单个单词先调用免费开放的
Dictionary API 获取音标、词形和英文释义；常见 `-ed`、`-ing`、复数等词形会优先
回查词典原形。随后仅把词典原形和最多两个英文释义交给 Chrome 本地模型翻译，
去重后展示最多三个中文常用义。

例如 `granted` 会优先按原形 `grant` 得到“授予、批准”等核心义，再补充词典释义，
不会只把脱离上下文的过去分词解释成连接语“倘若”。词典不可访问时自动退回原有
Chrome 单词翻译，不阻塞基本功能。该流程不使用付费 API，也不上传网页或截图。

### 9.3 CSV 日期

共享 JSON 内仍保存完整首次收藏时间，用于去重和历史兼容；CSV 导出时
`First saved` 统一截取为 `YYYY-MM-DD`。导入同时兼容旧版完整 ISO 时间和新版
日期格式。

### 9.4 单文件安装程序

CI 使用 Inno Setup 生成 `Translator-Setup.exe`。安装程序按当前用户安装到
`%LOCALAPPDATA%\Programs\Translator`，包含桌面程序、OCR 模型、Bridge Host、
配套浏览器插件和文档，并完成 Bridge 双注册表视图注册、开始菜单及桌面快捷方式。
不要求管理员权限，也不运行 PowerShell。

由于开发者模式扩展不能由普通安装程序静默装入 Chrome，用户仍需在
`chrome://extensions` 中加载一次固定目录：

```text
%LOCALAPPDATA%\Programs\Translator\extension
```

以后升级只需运行新版安装程序，不再重新选择临时下载目录。卸载程序删除应用和
Bridge 注册，但默认保留 `%LOCALAPPDATA%\Translator\favorites.json`，避免误删
学习记录。CI 必须静默安装、验证文件与注册表，再静默卸载后才发布安装包。
### 9.5 收藏即时反馈、Bridge 自愈与音标展示

浏览器收藏采用“本地先成功、共享库后同步”的交互：用户点击爱心后，先写入
`chrome.storage.local` 并立即刷新爱心和数量；随后把 `upsert/removeIds` 放入串行后台队列提交给
Native Host。Bridge 暂时不可用时保留 `dirty=true`，不回滚用户刚才的操作；后续聚焦、打开收藏页或
`Sync now` 再补交。只有最后一次排队变更完成后才把共享库快照设为权威数据，避免快速连续点击时旧响应覆盖新状态。

安装器除 `[Registry]` 双视图声明外，在安装完成阶段再次使用 Inno Setup Registry API 分别写入并验证
64 位和 32 位 HKCU Native Messaging 项；写入失败时安装明确报错，不发布“文件存在但 Bridge 未注册”的半完成状态。
桌面程序启动、重新激活及翻译前仍保留二次自检和自愈。

浏览器主翻译结果中的音标与中文翻译使用相同的字号、字重和绿色，避免音标过小。桌面端收到单词结果且
Provider 返回音标时，中文结果框按“音标换行中文释义”展示；句子和段落不添加单词音标。收藏数据仍分别保存
`phonetic` 与 `translatedText`，不会把展示换行写入 CSV 或共享 JSON。

### 9.6 v1.1.1 音标、会话与安装稳定性

词典音标只取自 Dictionary API 返回的 IPA，不从 OCR 或中文翻译反推。Provider 会依次检查
`phonetic` 和 `phonetics[].text`，拒绝 Unicode 替换字符、控制字符和私用区字符。部分词典使用
音节辅音组合写法，例如 `consultation` 的 `l̩`；该组合符号在部分 Windows 字体中会显示为方框，
因此显示层之前将 `l̩/n̩/m̩` 规范化为音值等价、字体兼容性更高的 `əl/ən/əm`。最终显示为
`/ˌkɒnsəlˈteɪʃən/`，普通 IPA 不做改写。Windows 音标栏固定使用 Segoe UI，Bridge 使用 UTF-8
JSON 帧并对完整 IPA 做往返测试。

Chrome 本地 Translator 会话不再并发处理同一个单词的多个词典释义；翻译任务进入串行队列，
会话出现 generic failure 时立即销毁，并用新会话重试一次。Windows 请求优先使用已打开侧边栏的
可用会话；侧边栏不存在时才使用 offscreen 会话。若浏览器要求首次用户点击下载语言包，错误提示
明确引导用户打开侧边栏运行本地检查，不再让用户反复点击 Windows 按钮。

安装器版本统一为 1.1.1。安装路径使用当前用户 `LOCALAPPDATA` 环境变量，规避部分 Windows

### 9.7 v1.1.2 首次翻译一致性与音标来源

单词的自动 OCR 翻译与“翻译中文”按钮共用同一 Provider 流程。词典查询遇到一次瞬时网络错误时先进行一次短间隔重试，成功后再启动 Chrome 本地翻译，避免第一次只显示基础翻译、第二次才补齐音标和多释义。

词形还原仅在原词不存在或原词没有可用释义时启用。原词有完整词条时禁止用猜测词根覆盖，避免把 `during` 错判为无关的 `dur`。

音标仍来自免费词典接口，不由程序猜测或生成。存在多套发音时优先采用接口明确标记的美式发音；显示层将 `ɹ`、`ɚ`、可选 `(j)` 和音节分隔点规范为学习词典常见形式，例如 `proud` 显示 `/praʊd/`，`during` 显示 `/ˈdjʊərɪŋ/`。朗读仍以英文原文为输入，不朗读音标字符串。

### 9.8 v1.1.3 组合连音符兼容

部分 IPA 数据使用 Unicode 组合连音符 U+0361 或 U+035C 连接塞擦音。该符号在 Windows Segoe UI 中可能显示为悬浮在音标上方或下方的小弧线。显示规范化层移除连音组合符但保留两侧音素，因此 `crouch /kɹaʊt͡ʃ/` 显示为 `/kraʊtʃ/`，`judge /d͜ʒʌdʒ/` 显示为 `/dʒʌdʒ/`；音值和朗读内容不受影响。

### 9.9 v1.1.4 翻译确定性与桌面进程保护

每个 Chrome 翻译 Worker 对成功的单词词典查询进行会话内缓存。同一单词的自动翻译和连续手动翻译复用同一完整词条，避免重复请求在网络波动时分别得到基础翻译和完整词典翻译。词典网络错误在一次用户操作内最多尝试三次；三次均失败时返回明确的可恢复错误，不再把缺少音标和释义的结果伪装成成功。

Windows 端将 Bridge 注册、健康检查、翻译请求和界面更新全部包含在同一异常边界内。命名管道重连或 Chrome 重载导致的异常只更新状态栏，不再从 `async void` 事件逸出并终止 WPF 进程。应用层另设 Dispatcher 和未观察任务异常保护，并将诊断写入 `%LOCALAPPDATA%\\Translator\\logs\\desktop.log`，日志写入本身失败时不会影响主程序。
兼容模式下 Shell 文件夹解析失败。覆盖升级开始时暂时移除 32/64 位 Native Messaging 注册，
再循环终止旧 Desktop 与 Bridge Host，防止 Chrome 立即重启 Host 并锁住运行库；文件替换完成后
安装器和桌面端共同恢复并验证注册。该流程已在 Chrome 同时运行两个 Bridge Host 的状态下通过
实际覆盖安装回归。

开发验证工具与正式产品必须隔离。`Translator.TechnicalValidation` 只在源码和 CI 环境执行，不进入最终安装器。Windows 应用控制可能阻止开发暂存目录中的未签名 DLL，并以 `0xe0434352` 显示运行库异常；这类事件通过 Windows 事件日志确认，不能与正式 `Translator.Desktop` 的运行状态混为一谈。正式桌面端的未处理 UI、任务和 AppDomain 异常统一记录到 `%LOCALAPPDATA%\Translator\logs\desktop.log`，可恢复异常在状态区展示而不直接终止进程。

### 9.10 v1.1.5 浏览器翻译页面自愈与词典降级

Bridge 健康检查只能证明 Native Messaging 通道存在，不能证明实际执行 Chrome Translator API 的 runtime Port 仍然可用。Chrome 休眠或重启扩展 Worker 后，offscreen 文档可能仍被报告为存在，但旧 Port 已经失效。v1.1.5 不再把 `hasDocument()` 当作健康证明：发现“文档存在但没有活动 Port”时关闭孤立文档并重新创建；请求期间发生断连或超时，则重建后只重发一次同一个幂等翻译请求，仍失败才返回明确错误。

免费词典属于增强 Provider，不再是基础翻译的单点故障。词典超时或连续网络失败时，单词直接降级为 Chrome 本地基础翻译，Provider 标记为 `chrome-local-dictionary-fallback`；桌面状态栏提示本次可能缺少音标和扩展释义。等待状态改为描述“连接 Chrome、查询词典并本地翻译”，不再把所有等待误报成首次语言包下载。

### 9.11 v1.1.6 连续查询、端口重连与确定性降级

侧边栏和 offscreen 翻译页不再把首次 `runtime.connect()` 视为永久连接。后台 Worker 休眠、重载或端口断开后，仍存活的翻译页以 250 ms 起步、最高 2 秒的退避间隔主动重连；Bridge 请求侧继续保留一次幂等页面重建与重放。这样 Windows 与浏览器两条入口都不依赖用户反复点击来碰巧恢复连接。

Chrome Translator 会话创建成功后直接复用，不再为每个单词重复调用 `availability()`。快速连续划词采用“当前任务完成、尚未开始的旧任务丢弃、最新任务优先”的串行策略，既避免同一有状态会话并发，也避免多个已过时查询在后台逐个执行。

免费词典查询的前台等待上限缩短为 3 秒。超时或网络失败时清除未完成缓存，并对该单词设置 5 分钟词典冷却；冷却期间稳定使用 Chrome 基础翻译，不会出现第一次无音标、第二次因迟到网络结果突然改变释义的情况。冷却只影响增强词典，Chrome 本地基础翻译继续可用。

### 9.12 v1.1.7 低延迟词典、备用数据源与词性

v1.1.6 的五分钟词典冷却会把一次瞬时故障放大为持续缺少音标和多义项；逐条串行翻译“词头 + 每条定义”又使一个单词触发三次以上 Chrome Translator 调用。v1.1.7 删除该冷却，并将词头和最多三条定义合并成一次本地模型调用。Provider 仍保持请求串行和会话复用，但正常单词查询只占用一次模型调用。

词典采用延迟备用策略。`dictionaryapi.dev` 为主来源；若主请求约 250ms 内未完成或失败，才启动 Datamuse。备用来源请求 `dpr` 元数据及 `ipa=1`，获得定义、词性和 IPA。主来源快速成功时不发送备用请求，避免无意义的双倍流量；任何来源先返回完整可用词条即写入会话缓存。两者都失败时，整个增强查询最多等待约 1.2 秒，然后仅降级基础翻译。

`TranslationResult` 新增 `partsOfSpeech: string[]`。免费词典将每条定义保留为 `{ partOfSpeech, definition }`，Provider 去重词性后通过 Stage 2 Bridge JSON 传递；Windows `TranslationResult` 使用 `IReadOnlyList<string>` 接收。浏览器侧边栏在音标下显示词性，Windows 在音标旁显示词性。句子和段落不显示该字段。

### 9.13 v1.1.8 音标格式识别、释义绑定与版本可见性

Datamuse 的 `pron:` 字段并不保证始终返回 IPA；部分词条即使请求 `ipa=1`，仍返回 CMU ARPAbet，例如 `changes` 返回 `CH EY1 N JH AH0 Z`。v1.1.8 在词典数据入口识别 ARPAbet，将音素、重音和非重读元音转换为学习者可读的 IPA（该示例转换为 `/ˈtʃeɪndʒəz/`）。无法完整解析的全大写音素序列直接丢弃，不再作为音标显示；正常 IPA 继续沿用已有 Unicode 兼容规范化。

词性不再作为与释义分离的展示标签。Provider 给合并翻译输入增加稳定编号，按编号将每个中文释义重新绑定到原词典 sense，并按词性分组生成 `noun：……`、`verb：……` 等结果行。`TranslationResult.meanings` 保留结构化 `{ partOfSpeech, translatedText }`，`translatedText` 同时提供已格式化的兼容文本，因此浏览器和 Windows 通过 Bridge 得到一致的“词性 + 释义”内容。

版本号以 `package.json` / WXT manifest 的 `1.1.8` 和 .NET `Directory.Build.props` 的程序集版本为统一发布标识。浏览器侧边栏与设置页读取实际扩展 manifest，Windows 主窗口读取实际程序集版本；界面显示的是当前真正加载的组件版本，而不是手写说明文字。
