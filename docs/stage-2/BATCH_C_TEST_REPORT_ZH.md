# 第二阶段第三批次测试报告

报告状态：通过  
测试日期：2026-07-29

## 1. 自动化与构建

- TypeScript/Vitest：32/32 通过；
- Chrome MV3 扩展构建：通过；
- Windows 技术检查：9/9 通过；
- Desktop、Native Host、Validation 自包含发布：通过；
- Native Messaging 注册表默认值及清单校验：通过；
- GitHub 通用 CI、Batch A、Batch B、Batch C：全部通过。

## 2. 真实 GUI 完整流程

在 Windows 桌面环境启动候选程序并执行：

```text
Ctrl+Shift+X
→ 框选固定英文测试窗口
→ 本地 OCR
→ Chrome 桥接
→ Chrome 本地翻译
→ Translator 窗口自动恢复
```

结果：

- 快捷键状态显示 `Ctrl+Shift+X ready`；
- OCR 返回：
  `Learning a language becomes easier when new words are reviewed in meaningful contexts.`
- Chrome 返回：
  `当在有意义的上下文中复习新单词时，学习一门语言变得更容易。`
- 状态区显示 `Translation complete.`；
- 点击 `Copy Chinese` 后，剪贴板内容与中文译文完全一致；
- 修改英文为 `The PDF contains several paragraphs of English text.` 并点击重新翻译，返回 `PDF 包含几段英文文本。`。

## 3. 网页与 PDF OCR 回归

使用本批次候选包内自包含 OCR 程序复测：

| 样本 | 类型 | 结果 |
|---|---|---|
| Wikipedia English language | 普通长网页 | 通过 |
| MDN JavaScript Introduction | 技术文档网页 | 通过 |
| Economist PDF（GitHub Viewer） | 多段落 PDF | 通过 |
| W3C dummy.pdf | 简单 PDF | 通过，识别 `Dummy PDF file` |

Word、PowerPoint 和普通桌面窗口使用同一屏幕像素框选入口，不读取第三方应用文件或对象模型。本地固定英文桌面窗口已完成真实快捷框选闭环。

## 4. 异常与恢复

| 场景 | 结果 |
|---|---|
| 框选时按 Esc | 窗口恢复，显示 `Capture cancelled. Nothing was saved.` |
| 启动第二个程序实例 | 第二实例显示 `Shortcut unavailable` |
| 快捷键冲突 | 明确说明 `Ctrl+Shift+X` 已被占用，框选按钮仍可使用 |
| 重复触发保护 | 忙碌状态下不启动第二个捕获任务 |
| 无中文可复制 | 状态区提示无可复制译文 |
| Chrome/桥接不可用 | 沿用已验证的约 4 秒健康检查和恢复提示 |

## 5. 界面检查

界面只保留：

- `Select screen region`；
- `Translate again`；
- `Copy Chinese`；
- 英文编辑框；
- 中文只读框；
- 单一状态区及快捷键状态。

未在主界面展示协议、Provider、清单或技术日志。
