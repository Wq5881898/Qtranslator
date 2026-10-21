# 第二阶段第二批次测试说明

## 本次目标

验证完整本地链路：

```text
桌面截图 → 本地 OCR → 可编辑英文 → Native Host
→ Chrome 扩展 → Chrome 本地翻译 → 中文返回桌面
```

截图不会进入 Native Host 或 Chrome；桥接只传输用户确认后的英文文字。

## 首次安装

1. 解压 `translator-stage2-batch-b`，不要直接在 ZIP 内运行。
2. 打开 Chrome 的 `chrome://extensions`。
3. 开启“开发者模式”，删除此前用于第二阶段测试的旧 Translator 扩展。
4. 点击“加载已解压的扩展程序”，选择解压目录中的 `extension` 文件夹。
5. 确认扩展 ID 是 `djbkcmlpogpnafgifiocehmkkghnhjjb`。
6. 在解压目录打开 PowerShell，执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\bridge\install-bridge.ps1
```

7. 完全退出并重新打开 Chrome。
8. 点击一次 Translator 扩展图标，使扩展和本地语言包准备就绪。
9. 运行 `desktop\Translator.Desktop.exe`。

安装脚本只在当前用户的 Chrome Native Messaging 注册表位置写入 Host 清单，不需要管理员权限。

## 测试步骤

1. 点击 `Select screen region` 并框选英文。
2. 检查 OCR 英文，必要时直接修改。
3. 点击 `Translate in Chrome`。
4. 确认 `Chinese translation` 出现真实中文，不包含 `[模拟翻译]`。
5. 修改英文后再次翻译，确认中文随之更新。
6. 完全关闭 Chrome 后点击翻译，确认状态区提示打开 Chrome且按钮恢复。
7. 重新打开 Chrome后重试，确认能够恢复。

## 已知边界

- Chrome Translator API 要求桌面 Chrome 138 或更高版本；
- 首次使用某语言对时，Chrome 可能下载本地语言包；
- 若提示需要用户点击下载语言包，请先在第一阶段侧边栏翻译一次，再返回桌面重试；
- 本批次不测试收藏、发音、全局快捷键、Azure 或正式安装器。
