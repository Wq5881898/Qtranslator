# Translator v1.1.8 回归测试报告

## 修复范围

本版本修复浏览器插件与 Windows 端把 ARPAbet 音素代码显示成音标的问题；调整词性与中文释义的对应关系；增加两端实际版本号显示。

## 自动化验证

- TypeScript 类型检查：通过。
- Vitest：12 个测试文件、59 项测试全部通过。
- 插件生产构建与 manifest/权限/版本校验：通过。
- Windows Release 编译：4 个项目通过，0 警告、0 错误。
- Windows 技术验证程序在当前电脑被 Windows 应用控制策略阻止加载未签名开发目录 DLL；这属于本机策略事件，正式自包含安装包仍继续执行安装回归。

## 专项用例

1. Datamuse 返回 `CH EY1 N JH AH0 Z` 时，插件与 Windows 显示 `/ˈtʃeɪndʒəz/`，不显示原始代码。
2. 无法完整解析的 ARPAbet 字符串不进入音标字段。
3. 同一词同时有 noun、verb sense 时，中文结果按 `noun：……`、`verb：……` 分行，词性不再悬空显示。
4. 扩展 manifest、插件侧边栏、Windows 程序程序集均使用版本 `1.1.8`。

## 安装回归

本机实际静默安装回归通过：安装器退出码 0；扩展 manifest 为 `1.1.8`；Windows 文件版本为 `1.1.8.0`；Desktop 与 Bridge Host 文件存在；Native Messaging 的 Registry32、Registry64 注册均指向存在的 manifest。启动安装后的 Windows 程序并观察 5 秒，进程保持运行，随后正常关闭。

最终安装包的 SHA-256 在交付信息中单独记录，避免安装包内文档包含自身哈希而造成循环变化。
