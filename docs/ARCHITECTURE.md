# 架构与扩展指南

面向想改代码的贡献者。读完你应该能：看懂 Mod 怎么跑、找到要改的位置、加一个新物品/新供货商/新对质问题项。

---

## 1. 入口与生命周期

`src/AppraisalExpansion.cs` 是单文件 Mod，实现游戏的 `IMod`：

```csharp
public class AppraisalExpansionMod : IMod
{
    public void Init(ModManifest manifest);   // 注册工厂、Harmony 补丁、事件
    public void OnEnable();                    // （当前为空）
    public void OnDisable();                   // 卸载 Harmony / 清理回调
}
```

**`Init` 里做了这些事（顺序重要）：**

1. `Harmony` 实例 + 补丁：
   - `StorePhoneClient.GetLocalizedDisplayName` → 后置修复电话本名字；
   - `InspectionUIManager.HandleRibwich/HandleAppHand/HandlePlasma` → 后置打开对质面板；
   - `InspectionUIManager.OnConfront` → 前置接管对质分发。
2. `RegisterConfrontActions()`：把改价 action 塞进 `ItemConditionActionList`。
3. `PreBuiltItemHelper.preBuiltItems[...]`：注册三种拾荒物品工厂。
4. `StoreClientListDict.storeClientDict[...]`：注册居民与供货商；
   `StoreClientList.generalPool/upperLevelPool`：让居民混入每日随机池。
5. `PhoneClientList.DialogFuncs[...]`：注册供货商电话对话。
6. 订阅 `ModHook` 事件（载入/新档/开店/深夜/入睡/物品目录初始化）。

---

## 2. 模块划分（文件内分区）

| 分区 | 主要方法 |
|---|---|
| 拾荒物品 | `CreateScavHand/Ribwich/Plasma`、`MakeHand/MakeRibwich/MakePlasmaBrand`、`FinalizeItem` |
| 供货商 | `CreateMerchant`、`BuildMerchantPhoneDialog`、`PlaceMerchantGoods`、`GetMerchantCredit`、`GetMerchantTier` |
| 排期 | `OnGenerateCustomers`、`InsertMerchant`、`OnCertificateOvernight` |
| 名片/电话 | `GiveCard`、`MakeBusinessCard`、`RegisterCardItems`、`RegisterPhoneContacts`、`PhoneNamePostfix` |
| 对质小游戏 | `RegisterConfrontActions`、`AttachConfrontFeature`、`OpenConfrontPanel`、`TryConfrontDispatch`、`IsFaulty` |
| 奢侈品判定 | `IsLuxuryRibwich`、`IsLuxuryHand`、`IsLuxuryPlasma` |
| 杰克逊 | `OnAfterhourArrive`、`MakeUnmarkedPlasma`、`MakePlasmaRawMaterial` |
| 居民 | `CreateLowerResident`、`CreateUpperResident` |

---

## 3. 关键游戏 API

| 用途 | API |
|---|---|
| 创建物品 | `DirectoryMaster.Item(id, true)` / `ItemDirectory.CreateEmptyItem(id)` |
| 自定义物品工厂 | `PreBuiltItemHelper.preBuiltItems[id] = Func<GameItem>` |
| 物品标签 | `item.IsTag(s)`、`GetTagReadonly(s).GetString()/GetInt()`、`ModifyTag(s, delegate, false)`、`EnableTag(s, false)` |
| 物品类型 | `item.SetGameItemType("FOOD"/"TOOL"/"MATERIAL"/"LUXURY_ITEM")` |
| 价格 | `item.unitValue`、`*Helper.GetCorrectValue(item)` |
| 客户端注册 | `StoreClientListDict.storeClientDict[id] = Func<StoreClient>` |
| 客户来源标记 | `ModHelper.SetClientSource(client, modId)` |
| 电话 | `ps.PhoneClientDict`、`PhoneClientList.DialogFuncs`、`StorePhoneClient.PhoneState` |
| 事件 | `ModHook.OnGameLoadedNormal/OnNewGameStarted/OnShutterOpenedEarly/OnAfterhourArriveLate/OnGenerateCustomerVeryLate/OnGoingSleepEarly/OnModItemDirectoryInit` |
| 委员会/证书 | `PlayerStore.isAppraisalIntroduced`、`appraisalIntroducedDay`、`AppraisalRankHelper.GetDisplayedCertificate()` |
| 对质 | `ItemConditionActionList.ItemConditionActionDict`、`new ItemCondition`、`new ItemFeature(cat)`、`inspectable.FindItemFeatureByCategory(cat)`、`InspectableHelper.OnConfrontSuccess/IsConfrontCorrect`、`InspectionUIManager.IsConfrontLocked()` |
| 音效/UI 对话 | `DialogUIManager.Instance.StartDialogue(new Dialogue()...)` |

---

## 4. 数据流

```
拾荒 (ModHook 掉落表)
   └─ CreateScavXxx → FinalizeItem(it, scavenged:true)
        ├─ AttachConfrontFeature(it)   // 问题标签 + 真伪 feature
        ├─ SetGameItemType(...)        // 食物/工具/材料/奢侈品
        └─ 定价：有问题 → 保留虚高基础价；否则 GetCorrectValue

放大镜 → HandleXxx postfix → OpenConfrontPanel
        └─ 选问题 → OnConfront prefix → TryConfrontDispatch
             ├─ 正确 → ExposeFeature (虚高→真价) + OnConfrontSuccess (-15%)
             └─ 错误 → OnConfrontFailed (扣声望)

供货商到店 → CreateMerchant → 对话结束 → PlaceMerchantGoods + GiveCard
        └─ 名片印号码；拨号 → phoneState=Regular → 次日上门
```

---

## 5. 如何扩展

### 加一个新物品（第 4 种）

1. 定义常量（如 `SCAV_TOOL = "scav_tool"`）；
2. 写 `CreateScavTool()`：`ItemDirectory.CreateEmptyItem(SCAV_TOOL)` + 初始化标签；
3. 在 `Init` 里 `PreBuiltItemHelper.preBuiltItems[SCAV_TOOL] = CreateScavTool;`
4. 在 `FinalizeItem` 里加类型标签/定价分支；
5. 在 `LootTables/scav_appraisal.json` 加掉落条目；
6. 若要对质，在 `AttachConfrontFeature` + `GetConfrontCat/Reasons/Labels` 加分支。

### 加一位供货商

1. 在 `MERCHANTS` 数组加一项 `MerchantDef`；
2. 在 `BuildMerchantGoodsList` 里加对应货品生成逻辑；
3. （可选）在 `MakePlasmaBrand` 等工厂里处理新厂商；
4. 电话/名片会自动注册。

### 加一个对质问题项

1. 在常量区加 `XXX_ISSUE_YYY`；
2. `AttachConfrontFeature` 判定条件后 `EnableTag(..., false)`；
3. `GetConfrontReasons` + `GetConfrontLabels` 对应位置填上（最多 5 项，对应 5 个 toggle）。

### 调整掉率/品质

- 掉落概率：`LootTables/scav_appraisal.json` 的 `weight`；
- 品质分布：`CreateScavXxx` 里的 `RNG.GetRandomRangeInt` 阈值。

---

## 6. 调试技巧

- 游戏日志：`%USERPROFILE%\AppData\LocalLow\<公司>\Probably Stolen\Player.log`（或游戏目录 `Player.log`）；
- 对质无反应时先确认：`isAppraisalIntroduced`、证书是否在展示栏、`CanOpenConfront` 的四个条件；
- 电话本不显示：确认 `dialogFuncId` 前缀是 `appr_supplier_`（`PhoneNamePostfix` 依赖它）；
- 编译报 `CS0311`：就是重载歧义，按 CONTRIBUTING 里的"三件套"补参数。

---

## 7. 已知限制

- 单文件、无命名空间、无测试；改动集中在 `AppraisalExpansion.cs`；
- 文案中文硬编码，未做本地化；
- 对质面板复用原版 5 个 toggle，问题项 ≤ 5；
- 杰克逊第 40 天的"机器"目前用原版能量转换器占位；
- Harmony 补丁在 `OnDisable` 里统一卸载，若中途异常可能残留。
