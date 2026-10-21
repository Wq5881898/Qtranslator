# Translator 第一阶段最终交付索引

> 第二阶段当前稳定候选为 Windows + 配套 Chrome 扩展 v1.1.8。其设计、使用和测试文档分别位于 `docs/stage-2/STAGE2_DESIGN_ZH.md`、`docs/stage-2/STAGE2_FINAL_USER_GUIDE_ZH.md` 和 `docs/stage-2/TEST_REPORT_V1.1.8_FINAL_ZH.md`。第一阶段浏览器独立版仍保持独立，不被第二阶段覆盖。

交付版本：1.0.0-rc.1  
交付日期：2026-07-29  
代码仓库：`Wq5881898/translator`

## 1. 本地长期文档

本地目录：

```text
C:\Users\qcalg\Documents\Codex\2026-07-20\github-plugin-github-openai-curated-remote\outputs
```

最终维护入口：

- `translator-stage1-final-user-guide.md`：安装、使用、导入导出、设置、隐私和排错；
- `translator-stage1-final-design.md`：架构、模块、数据流、接口、安全、测试和后续方案；
- `translator-stage1-final-delivery-index.md`：版本与文档导航；
- `translator-srs-v1.1.md`：早期完整需求基线；
- `translator-mvp-requirements-v1.md`：早期 MVP 需求；
- `translator-mvp-feature-priorities.md`：早期功能优先级。

发生冲突时，按以下优先顺序判断：

1. 当前版本代码和自动化测试；
2. 第一阶段最终设计方案；
3. 第一阶段最终安装使用说明书；
4. GitHub 测试报告与隐私政策；
5. 早期需求和优先级文件。

早期需求包含后续规划，不代表全部已经实现。

## 2. GitHub 长期文档

- `README.md`：项目总览；
- `docs/FINAL_USER_GUIDE_ZH.md`：最终安装使用说明；
- `docs/FINAL_DESIGN_ZH.md`：最终设计方案；
- `docs/TEST_REPORT_STAGE_1.md`：测试报告和用户缺陷记录；
- `docs/INSTALLATION.md`：简版 Chrome/Edge 安装；
- `docs/RELEASE_CHECKLIST.md`：发布检查表；
- `PRIVACY.md`：隐私政策；
- `CHANGELOG.md`：版本变更；
- `THIRD_PARTY_NOTICES.md`：第三方声明；
- `LICENSE`：MIT 许可证。

## 3. 第一阶段最终范围

完成：

- 网页自动划词与右键翻译；
- Chrome 本地翻译和 Azure 可选备用；
- 音标与本地发音；
- 单词和句子收藏；
- 隐藏收藏弹窗；
- CSV 导入导出；
- 设置、隐私、错误处理；
- 自动化测试、CI 和发布包。

暂缓：

- 翻译次数统计；
- 截图 OCR；
- GPT 自动调用；
- 商业词典；
- 商店发布。

## 4. 最终质量状态

截至 2026-07-29：

- 用户已完成多轮 Chrome 人工测试；
- 已记录并修复划词、测试输出、收藏布局、发音、设置关闭、CSV 和跨网页注入等问题；
- 最新自动化验证包含 9 个测试文件、30 项单元测试；
- TypeScript 类型检查、生产构建、权限核验和发布包检查通过；
- PR #9 作为第一阶段最终合并入口。

## 5. 后续继续开发的最短路径

1. 阅读 `translator-stage1-final-design.md`；
2. 查看 GitHub 当前 `main` 和最新 CI；
3. 根据“后续建议优先级”选择一个独立模块；
4. 建立新分支和 PR；
5. 修改代码、测试及相关文档；
6. 由用户安装 CI 产物进行人工测试；
7. 通过后合并。

不要从聊天记录重新推断架构；以本索引指向的版本化资料为准。

