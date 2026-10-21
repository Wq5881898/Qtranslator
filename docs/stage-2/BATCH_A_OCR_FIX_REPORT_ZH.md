# 第一批次内置英文 OCR 修正报告

日期：2026-07-29

## 问题

用户在 Merriam-Webster 页面框选英文后，Windows 中文 OCR 把正常单词识别成异常空格、大小写和相似字符，例如 `someone is to` 被拆散或误识别。

## 修正

- 新增 `PackagedEnglishOcrProvider`，优先使用随程序打包的 Tesseract 英文模型；
- 保留 `WindowsOcrProvider`，仅在内置引擎异常时备用；
- OCR Provider 接口未改变，后续仍可替换；
- 图片继续只在内存中传递，不上传、不写入磁盘或剪贴板；
- 用户无需安装 Windows 英文语言包、注册账号、配置 Key 或联网。
- 对 OCR 平均置信度、英文字母数量、异常单字母比例和基本英文形态进行轻量校验；
- 中文或其他不可靠内容不会作为伪英文输出，编辑框会清空，具体原因显示在下方状态区。
- 图标与文件名同时框选时，清理图标产生的前导纯符号和无元音短小写伪单词；只框选图标产生的短伪单词会被拒绝。
- 新增随包提供的简体中文模型，用于确定中英文边界；英文模型与双语模型进行轻量对齐，保留真实英文并丢弃中文产生的伪英文字母。
- 用户提供的混合截图回归：`GitHub CI 通过` 输出 `GitHub CI`；`已处理 4m 58s` 返回空结果并提示无可靠英文。

## 自动验证

```text
Build succeeded.
0 warnings
0 errors
PASS  packaged English OCR on in-memory image
PASS  non-English OCR is rejected with a reason
PASS  Windows local OCR on in-memory image
7/7 technical checks passed.
```

## 实际页面回归

使用 `https://www.merriam-webster.com/word-of-the-day` 的实际英文页面截图验证，识别结果正确包含：

```text
What It Means
To inveigle someone is to persuade them in a clever or deceptive way to do something.
Inveigle can also mean “to get something in a clever or deceptive way.”
```

正式中文翻译仍不属于第一批次；`Run mock translation` 继续验证 Provider 接口，不应被视为正式翻译。
