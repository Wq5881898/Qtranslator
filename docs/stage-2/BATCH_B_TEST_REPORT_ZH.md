# 第二阶段第二批次测试报告

报告状态：通过  
测试日期：2026-07-29

## 1. 首次人工测试问题

现象：点击翻译后界面长时间停留在发送状态，没有中文结果。

已确认的根因：

1. 安装脚本写入了名为 `(default)` 的普通注册表值，而不是 Chrome Native Messaging 要求的真正默认值；
2. 当前 Chrome 配置没有加载固定 ID `djbkcmlpogpnafgifiocehmkkghnhjjb` 的本批次扩展；
3. 桌面端没有在发送正文前做桥接健康检查，因此只能等待正文超时。

修复：

- 改用 `Set-Item -Value` 写入真正的注册表默认值；
- 安装结束立即用 `GetValue("")` 回读验证；
- CI 解析 Host 清单并验证程序绝对路径和精确扩展来源；
- 桌面端翻译前增加约 4 秒桥接健康检查；
- 增加自包含的 `--bridge-health`、`--bridge-translate` 和图片 OCR 回归入口。

## 2. 自动化结果

- TypeScript/Vitest：32/32 通过；
- Chrome MV3 扩展构建：通过；
- Windows 技术检查：8/8 通过；
- 自包含 Desktop、Host 和 Validation 发布：通过；
- Windows 注册表默认值写入及回读：通过；
- Native Messaging 清单路径和扩展来源验证：通过；
- 构建产物 SHA-256：`79a953d1c3793d2843836741f02aa9ce9bb9b1d65f5dadbf7d7cd8f6daffcca2`。

## 3. 真实网页与 PDF OCR 回归

| 样本 | 类型 | 结果 |
|---|---|---|
| Wikipedia English language | 普通网页长段落 | 通过，识别出正文英文 |
| MDN JavaScript Introduction | 技术文档网页 | 通过，识别出标题、列表与正文 |
| Merriam-Webster Word of the Day | 中英混合/自动翻译网页 | 通过可靠性入口；页面已被 Chrome 自动翻译，英文内容有限 |
| Economist PDF（GitHub PDF Viewer） | 多段落 PDF | 通过，识别出新闻正文 |
| W3C dummy.pdf | 简单 PDF | 通过，识别为 `Dummy PDF file` |

## 4. 异常恢复回归

在固定 ID 扩展未加载的实际 Chrome 环境中执行桥接健康检查：

- 约 4.2 秒结束；
- 返回“Chrome bridge is not connected”；
- 不再进入 45 秒无反馈等待。

## 5. 真实 Chrome 本地翻译闭环

固定 ID 扩展加载后，使用发布包内自包含验证程序连接真实 Chrome，而非 Mock：

| 输入 | 类型 | Chrome 本地返回 | 结果 |
|---|---|---|---|
| `Hello world.` | 短句 | `你好世界。` | 通过 |
| `inveigle` | 单词 | `因维格尔` | 链路通过，译义质量有限 |
| `Learning a language becomes easier...` | 复杂句 | 返回完整中文句子 | 通过 |
| Economist 新闻两句长段落 | 段落 | 返回两句完整中文 | 通过 |

额外恢复测试：

1. 真实桥接健康检查返回 `Chrome bridge is connected.`；
2. 强制结束 Native Host；
3. 扩展按设计自动重新连接；
4. 4 秒后健康检查再次通过；
5. `The translation bridge recovered successfully.` 返回 `翻译桥恢复成功。`。

截图、OCR、可编辑英文、Named Pipe、Native Host、扩展离屏页面、Chrome 本地 Translator API 和中文回程各层均已分别或组合验证。

## 6. 已知质量边界

- Chrome 本地 Translator API 对常见句子和段落可给出可用中文；
- 对低频单词可能返回音译而不是词典释义，例如 `inveigle → 因维格尔`；
- 这是本地模型的翻译质量边界，不是 OCR 或桥接故障；
- 后续应按既定 Provider 架构增加专业词典优先策略，但不阻塞本批次真实链路验收。
