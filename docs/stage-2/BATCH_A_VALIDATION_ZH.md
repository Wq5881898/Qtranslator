# Translator 第二阶段第一批次技术验证报告

报告版本：1.0

验证日期：2026-07-29
分支：`stage2/batch-a`

## 1. 批次范围

第一批次只验证高风险技术基础：

- Windows 桌面工程和可执行程序；
- 区域截图与多屏虚拟桌面坐标；
- 截图仅存于内存；
- Windows 本地 OCR；
- Translation Provider 与 Mock Provider；
- Chrome Native Messaging 消息帧、Host 健康响应和扩展侧契约；
- 第一阶段插件回归构建。

本批次完成后暂停，不进入全局快捷键、正式翻译结果界面、收藏、同步或安装器。

## 2. 技术结论

### 桌面技术栈

采用 `.NET 10 + WPF` 继续实施是可行的：

- 可直接使用 Windows 多屏、鼠标和屏幕复制 API；
- Windows 本地 OCR 可通过 WinRT 调用；
- 不需要捆绑 Electron/Chromium 桌面运行时；
- 可生成自包含 `win-x64` 测试包。

当前技术目标为 Windows 10 19041 或更高版本；最终最低版本仍需在发布批次验证。

### 截图

原型使用覆盖虚拟桌面的透明选择层，支持：

- 鼠标拖选；
- `Esc` 取消；
- 负坐标副屏；
- 小于 3 像素的无效选区拒绝。

截图生成 PNG `MemoryStream`，未写入文件或剪贴板。OCR 调用结束后由 `using` 释放。

### OCR

`WindowsOcrProvider` 实现 `IOcrProvider`。自动验证在内存中生成英文图片并成功识别 `local OCR`，证明：

- Windows OCR 引擎可用；
- PNG 内存流解码可用；
- 不需要把图片先写入临时文件；
- OCR Provider 可替换。

实际样例中 `Translator` 曾被识别为 `TransIator`，说明 Windows OCR 可满足首版验证，但必须保留用户校对步骤，后续还需用 PDF、PPT、低分辨率和复杂背景样本评估准确率。

### Provider

`ITranslationProvider` 已隔离健康检查和翻译调用。Mock Provider 可在无网络、无浏览器时验证文本规范化、分类和结果状态。

### 浏览器桥接

已完成：

- 协议版本 `1.0`；
- UTF-8 JSON 和 32 位小端长度帧；
- 1 MiB Host 响应上限；
- 请求 ID；
- Host 健康检查响应；
- 扩展 `nativeMessaging` 权限；
- 扩展侧消息创建、验证和健康检查函数；
- Native Host 清单模板。

尚未完成：

- 安装器写入 Host 绝对路径和实际扩展 ID；
- Chrome 启动 Host 的人工端到端测试；
- Desktop → Named Pipe → Host → Extension → Chrome Translator 的完整翻译往返。

上述剩余项属于浏览器桥接正式集成，而非本批次协议可行性验证。正式往返应在下一批核心实现开始时优先完成。

## 3. 自动验证结果

### Windows 解决方案

```text
Build succeeded.
0 warnings
0 errors
```

### 技术自检

```text
PASS  text rules
PASS  mock provider
PASS  native frame UTF-8 round-trip
PASS  invalid native frame rejection
PASS  Windows local OCR on in-memory image
5/5 technical checks passed.
```

### 浏览器插件

```text
TypeScript strict check: passed
Test files: 10 passed
Tests: 32 passed
Chrome MV3 production build: passed
```

## 4. 已发现问题和处理

| 问题 | 原因 | 处理 |
|---|---|---|
| SDK 首次解压失败 | 工作区完整路径超过部分 Windows 解压路径限制 | SDK 改用短工具目录；代码库不依赖该目录 |
| NuGet 初始还原失败 | Windows SDK 引用包不在本地 | 使用官方 NuGet 源和项目级 `NuGet.Config` |
| OCR 首次解码失败 | 写入 WinRT 内存流后未显式刷新 | 写入后 `FlushAsync` 再解码 |
| OCR 把小写 `l` 识别为大写 `I` | OCR 字形歧义 | 保留用户校对步骤并安排样本评估 |
| 中文截图后再次截取英文不显示 | OCR 跟随用户语言，且遮罩关闭后截屏过早 | 英文引擎优先、系统引擎回退；等待 DWM 遮罩退出；新截图清除旧结果并明确提示空结果 |
| 所有实际框选均返回空结果 | WPF 框选使用逻辑坐标，`CopyFromScreen` 使用物理像素，Windows 缩放后坐标偏移 | 启用 Per-Monitor V2；使用 `PointToScreen` 转换框选端点；通过 Win32 物理像素设置跨屏遮罩 |

## 4.1 Merriam-Webster 实际网页验证

验证网址：

```text
https://www.merriam-webster.com/word-of-the-day
```

在未自动翻译的浏览器中取得当前可见页面，页面包含 `inveigle`、`What It Means` 和多段英文。该实际网页画面经 `WindowsOcrProvider` 识别成功，结果包含：

```text
Word of the Day
inveigle
What It Means
To inveigle someone is to persuade them...
```

同时执行了 Windows 物理屏幕捕获测试，证明 `CopyFromScreen → PNG 内存流 → Windows OCR` 链路能够返回实际屏幕文字。修复后桌面框选使用同一物理像素捕获方法。

## 5. 隐私核对

- 原始截图没有进入 Provider 请求模型；
- Native Messaging 契约没有图片字段；
- 截图未保存到磁盘或剪贴板；
- 自动测试使用内存生成图像；
- 当前原型不记录 OCR 全文和截图。

## 6. 第一批次结论

第一批次技术基础通过，可以进入下一批核心可用版，但需先由用户运行候选程序测试真实屏幕截图和 OCR 操作。当前不建议合并为正式 Stage 2 产品版本。
