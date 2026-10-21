# 第二阶段第三批次测试说明

## 本批次目标

实现任意 Windows 应用中的快捷截图翻译：

```text
Ctrl+Shift+X
→ 屏幕框选
→ 本地 OCR
→ 自动检查 Chrome 桥接
→ Chrome 本地翻译
→ 桌面窗口显示中英文
```

截图只在内存中交给本地 OCR。进入桥接和 Chrome 的只有识别后、可编辑的英文文字。

## 安装

1. 解压测试包，不要直接在 ZIP 内运行。
2. 在 `chrome://extensions` 开启开发者模式。
3. 删除或停用旧的第二阶段 Translator 测试扩展。
4. 加载测试包中的 `extension` 文件夹。
5. 确认扩展 ID 为 `djbkcmlpogpnafgifiocehmkkghnhjjb`。
6. 在解压目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\bridge\install-bridge.ps1
```

7. 完全退出并重新打开 Chrome。
8. 运行 `desktop\Translator.Desktop.exe`。

## 基本测试

1. 保持 Translator 桌面程序运行。
2. 切换到网页、浏览器 PDF、Word 或 PowerPoint。
3. 按 `Ctrl+Shift+X`，框选清晰英文。
4. 确认桌面窗口自动恢复并显示英文和中文。
5. 修改英文后点击 `Translate again`，确认中文更新。
6. 点击 `Copy Chinese`，确认译文进入剪贴板。
7. 连续执行三次快捷截图，确认每次结果都会更新。

## 异常测试

- 按 Esc 取消框选：窗口恢复并提示取消；
- 框选中文、图标或空白：不翻译伪英文，并提示重新框选；
- 完全关闭 Chrome：约 4 秒内提示桥接不可用；
- 禁用扩展：提示启用扩展并重试；
- 快捷键被占用：界面提示冲突，按钮仍可使用；
- 翻译过程中再次按快捷键：提示等待，不启动第二个任务；
- 首次语言包尚未就绪：状态区说明可能等待，超时后可重试。

## 本批次不包含

共享收藏库、发音、专业词典优先、开机启动和正式安装器继续放在后续批次。
