# 参与贡献 / Contributing

感谢你愿意帮这个 Mod 变得更好！无论你是会写 C#、会测游戏、会翻译、会做美术，还是只是发现了 BUG，都欢迎参与。

---

## 我能怎么帮忙？

| 方式 | 说明 |
|---|---|
| 🐛 报 BUG | 用 [Bug report](../../issues/new?template=bug_report.md) 模板，附版本/复现步骤/日志 |
| 💡 提建议 | 用 [Feature request](../../issues/new?template=feature_request.md) 模板 |
| 🧪 帮忙测试 | 在新档/旧档、不同游戏版本上跑，反馈平衡性和崩溃 |
| 💻 写代码 | 看 [架构文档](docs/ARCHITECTURE.md)，从 `good first issue` 开始 |
| 🌐 翻译 | 把中文文案补成英/日/俄等（见下方「本地化」） |
| 🎨 美术 | 预览图、UI 图标、物品贴图 |
| 📖 文档 | 补 README、写教程、录演示视频 |

---

## 开发环境

1. **游戏本体**：Steam 版《Probably Stolen》0.46D（或其他兼容版本）。
2. **编译器**：Windows 自带的 .NET Framework `csc.exe`（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`），**不需要安装 .NET SDK**。
3. **构建**：

   ```bat
   :: 修改 build.bat 顶部的 GAME_DIR 为你的游戏目录
   build.bat
   ```

   或在仓库根目录手动执行 README 里的 `csc` 命令。

4. **测试**：把编译出的 `AppraisalExpansion.dll` 覆盖到
   `<游戏>\Mods\appraisal_expansion\`，重启游戏，在「模组」菜单启用。

---

## 代码约定（很重要）

本项目使用**旧版 C# 5 编译器**，以下语法**不可用**：

- ❌ 字符串插值 `$"..."`
- ❌ 空条件运算符 `?.` / `?[]`
- ❌ `nameof(...)`
- ❌ 表达式体成员 `=>`
- ❌ `switch` 表达式、元组 `(a, b)`
- ❌ 目标类型 `new()`

另外，游戏里的几个方法有**字符串 + 泛型重载**，必须显式传第二个参数：

```csharp
DirectoryMaster.Item("x", true);                 // 不能只传 "x"
item.ModifyTag("x", delegate(TagState s) { ... }, false);
item.EnableTag("x", false);
```

> 这些都是旧版 `csc` 的重载解析/BUG 规避技巧，改了会编译失败，别"优化"掉。

代码风格：4 空格缩进、中文注释、方法按模块分区（拾荒/商人/电话/对质/杰克逊）。

---

## 从哪里开始？

- 看 [Issues](../../issues) 里标了 `good first issue` 或 `help wanted` 的条目；
- 想加新物品/新供货商/新问题项，参考 [架构文档](docs/ARCHITECTURE.md) 的「如何扩展」章节；
- 不确定怎么做？先在 [Discussions](../../discussions) 里开个帖聊聊。

---

## 提交 PR

1. Fork 本仓库，新建分支：`feature/你的功能` 或 `fix/问题简述`；
2. 改源码 → `build.bat` 编译通过；
3. 尽量在游戏里实测一遍，并在 PR 描述里写清：**改了什么、为什么、怎么测的**；
4. 提交 PR，我会尽快 review。

> 如果只改文档/翻译，可以不编译，直接提 PR。

---

## 本地化

当前文案以中文硬编码在 `src/AppraisalExpansion.cs` 中（对白、问题项标签等）。
想加多语言，建议：

1. 把文案抽到 `LootTables/` 旁的 JSON 或 `manifest` 里；
2. 用 `LocHelper.GetLocalizedUIOrDefault(key, fallback)` 读取；
3. 欢迎先提一个 issue 讨论方案，不要直接大改。

---

## 行为准则

保持友善、对事不对人。不接受人身攻击、政治/歧视言论。

---

## 许可

提交的代码默认以本仓库的 [MIT License](LICENSE) 授权。
