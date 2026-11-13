# 深空鉴定拓展 / Appraisal Expansion

《Probably Stolen》（深空当铺：可能是偷的）的社区 Mod。

> A community mod for *Probably Stolen* that expands the appraisal / pawn-shop gameplay.

---

## 简介

在原版的「鉴定物品」系统基础上，扩展出一整套供应链与对质玩法：

- **拾荒掉落**：夜晚出门拾荒有概率捡到机械手、肋排三明治、等离子燃料三种鉴定物品；赝品/次品占多数，正品稀有。
- **放大镜对质**：捡到的赝品/次品显示「虚高原价」，可用放大镜逐个核对属性、指控问题项；指控正确则揭穿、价格掉到真实价值并再打 85 折，指控错误会扣声望。
- **六位供货商**：三明治 / 机械手 / LL / 中华 / 共和国 / 点火实验室等离子，各自一位供货商。
- **加入鉴定委员会**：只有加入鉴定委员会、并把鉴定证书放进左侧展示栏过夜，才会吸引供货商上门；拿到证书第 14 天六位供货商一起上门（唯一固定时间），此后每周随机一名。
- **名片电话**：每位供货商到店会留下一张 1×2 的名片，号码印在名片上；手动拨打号码后才会存进电话本，之后可电话预约次日上门。
- **杰克逊博士夜晚店铺**：夜间可去他的店铺购买无标识等离子燃料；随进度解锁高级违禁品原料与制作机器。
- **物品标签**：三明治=食物、机械手=工具+材料、等离子燃料=材料；满足增值条件的额外打奢侈品标签。
- **居民买家**：新增下层居民与上层收藏家，会收购这三类物品。

## 安装

### 方式一：手动安装（本地 Mod）

1. 下载 [dist/appraisal_expansion_v0.3.0.zip](dist/appraisal_expansion_v0.3.0.zip) 并解压；
2. 把解压出的 `appraisal_expansion` 文件夹放到：
   ```
   <游戏安装目录>\Mods\appraisal_expansion\
   ```
   目录内应包含 `manifest.xml`、`AppraisalExpansion.dll`、`LootTables\scav_appraisal.json`。
3. 启动游戏 → 主菜单「模组」→ 启用「深空鉴定拓展」。

### 方式二：Steam 创意工坊

> 上传中 / 待补充。

## 从源码构建

需要 .NET Framework 自带的 `csc.exe`（Windows 10/11 自备），无需安装 .NET SDK。

```bat
set M=<游戏安装目录>\Probably Stolen_Data\Managed
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:library -langversion:5 -codepage:65001 ^
  -out:AppraisalExpansion.dll ^
  -r:"%M%\netstandard.dll" -r:"%M%\System.Runtime.dll" ^
  -r:"%M%\Assembly-CSharp.dll" -r:"%M%\Assembly-CSharp-firstpass.dll" ^
  -r:"%M%\UnityEngine.CoreModule.dll" ^
  -r:"%M%\0Harmony.dll" -r:"%M%\UnityEngine.UI.dll" -r:"%M%\Unity.TextMeshPro.dll" ^
  src\AppraisalExpansion.cs
```

编译产物覆盖到 `mod\AppraisalExpansion.dll` 即可。

> 注意：`DirectoryMaster.Item("x", true)` / `ModifyTag("x", ..., false)` / `EnableTag("x", false)` 必须带第二个/第三个参数，否则旧版 `csc` 会误匹配泛型重载。

## 项目结构

```
ProbablyStolen-AppraisalExpansion/
├── src/
│   └── AppraisalExpansion.cs      # Mod 源码（实现 IMod）
├── mod/                           # 可分发的 Mod 文件
│   ├── manifest.xml
│   ├── AppraisalExpansion.dll
│   └── LootTables/scav_appraisal.json
├── dist/
│   └── appraisal_expansion_v0.3.0.zip  # 打包好的 Mod
└── build.bat                      # 一键构建脚本
```

## 兼容性

- 游戏版本：`0.46D`
- 引擎：Unity 2021.3 / Mono，API 级别 .NET Standard 2.1
- 依赖：Harmony（游戏自带 `0Harmony.dll`）

## 许可

MIT License，详见 [LICENSE](LICENSE)。

## 声明

本 Mod 为非官方社区作品，与游戏开发者「Questing Goose Studio」无隶属关系。游戏本体与其中素材版权归原作者所有。
