# Translator 第二阶段 v1.1.8 安装使用说明

## 1. 交付内容

第二阶段由 Windows 截图翻译程序、Native Messaging Bridge 和配套 Chrome 扩展组成。安装器自动安装三个组件并注册桥接；用户不需要运行 PowerShell 或手动修改注册表。

## 2. 安装与升级

1. 运行 `Translator-Setup-v1.1.8.exe`。
2. 首次安装后打开 `chrome://extensions`，开启“开发者模式”，点击“加载已解压的扩展程序”。
3. 选择 `%LOCALAPPDATA%\Programs\Translator\extension`。
4. 升级后点击 Translator 的“重新加载”，或完全退出并重新启动 Chrome。
5. 从桌面快捷方式启动 Translator。

Chrome 的开发者模式扩展必须由用户确认加载，这是浏览器安全限制；其余 Bridge 配置由安装器完成。

当前候选安装器尚未使用商业代码签名证书。若 Windows 应用程序控制明确阻止运行，请不要关闭系统安全功能；应改用经过组织允许或后续正式签名的安装包。

## 3. 日常使用

- 按 `Ctrl+Alt+A` 或点击“Select screen region”，框选网页、PDF、Word、PPT 等画面中的英文。
- OCR 完成后自动翻译；英文可编辑，修改后点击“Translate Chinese”重新翻译。
- 英文与中文区域分别提供复制按钮。
- 单词结果显示经过格式识别和字体兼容处理的 IPA；词性跟随对应释义显示（如 `noun：地址`、`verb：处理`）；“Read aloud”朗读英文原文。
- 插件侧边栏和 Windows 主窗口均显示实际运行版本号，安装后应确认为 `1.1.8`。
- 爱心按钮收藏当前内容；“Favorites (数量)”打开共享收藏库。
- 设置按钮可修改截图快捷键和翻译 Provider。

## 4. 翻译与重试规则

自动翻译和手动翻译走同一条请求链。主词典短暂失败时只快速重试一次；主请求变慢或失败时自动使用备用词典补充 IPA、词性和多个定义。词头和释义合并为一次 Chrome 本地翻译调用，避免逐条等待。浏览器翻译页面的通信端口失活时，页面会主动重连；Bridge 仍会重建页面并自动重发一次请求。快速连续划词时，只保留最后一个尚未开始的查询，旧查询不会在后台继续排队阻塞最新单词。

Chrome 本地语言包需要由浏览器在用户操作后初始化；看到相关提示时点击一次“Translate Chinese”。扩展刚升级时先重新加载扩展或重启 Chrome。

## 5. 收藏与隐私

桌面端与配套扩展通过 Bridge 合并收藏。Bridge 暂时不可用时，两端先保留本地数据，并采用有限重试和后续事件触发同步，不进行无限高频轮询。CSV 可用于导入导出；首次收藏时间导出为 `YYYY-MM-DD`。

截图仅在本机内存中交给本地 OCR，识别后释放；发送给翻译 Provider 的是可编辑英文文字，不上传完整截图。

## 6. 故障排查

- 桌面端不能翻译、插件正常：重新加载配套扩展或重启 Chrome，再重试一次；确认加载的是安装目录中的扩展。
- 提示 Native Host 通信失败：重新运行同版本安装器执行修复安装，然后重新加载扩展。
- 程序显示可恢复错误：查看状态区；详细日志位于 `%LOCALAPPDATA%\Translator\logs\desktop.log`。
- 安装升级提示文件被占用：关闭 Translator 和 Chrome 后重试；安装器正常情况下会主动结束旧 Bridge 进程。
- `Translator.TechnicalValidation.exe` 不属于正式交付物，不应作为日常程序运行。

卸载不会主动删除 `%LOCALAPPDATA%\Translator` 中的学习收藏，避免误删用户数据。
