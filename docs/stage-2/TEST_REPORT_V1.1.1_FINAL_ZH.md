# Translator v1.1.1 最终回归测试报告

测试日期：2026-08-21
目标：Windows x64 自包含正式安装版

## 结论

v1.1.1 已通过自动化、技术集成、安装和覆盖升级回归，可作为当前最终可安装版本交付。

## 结果

| 范围 | 结果 |
|---|---|
| TypeScript 类型检查 | 通过 |
| 插件自动化测试 | 46/46 通过 |
| Chrome MV3 生产构建 | 通过 |
| 发布权限、Host、固定扩展 ID、版本检查 | 通过 |
| Windows Release 编译 | 0 错误 |
| Windows 技术检查 | 11/11 通过 |
| Desktop/Bridge/Validation win-x64 自包含发布 | 通过 |
| Inno Setup v1.1.1 编译 | 通过 |
| 当前用户实际安装 | 通过 |
| Chrome 运行 2 个 Bridge Host 时覆盖升级 | 通过 |
| 32/64 位 Native Messaging 注册恢复 | 通过 |
| Desktop 启动与收藏文件保留 | 通过 |

## 重点缺陷回归

- `consultation`：词典原始音标 `/ˌkɒnsl̩ˈteɪʃən/` 规范化为字体稳定且音值等价的
  `/ˌkɒnsəlˈteɪʃən/`；无方框或损坏字符。
- `whoosh`：词典多释义改为串行翻译；generic failure 后销毁会话并只重试一次，不污染后续请求。
- Windows 偶发语言包错误：优先路由至已工作的侧边栏会话，后台会话仅作备用。
- 覆盖安装 DLL 拒绝访问：升级前临时撤销桥接注册并终止旧 Host，避免 Chrome 立即重启锁文件。
- 收藏：安装与覆盖升级均保留 `%LOCALAPPDATA%\Translator\favorites.json`。

## 交付限制

Chrome 不允许普通 Windows 安装器静默安装开发者模式扩展。首次使用仍需在
`chrome://extensions` 加载 `%LOCALAPPDATA%\Programs\Translator\extension`；后续升级只需运行新版安装器并在扩展页点击一次“重新加载”。
