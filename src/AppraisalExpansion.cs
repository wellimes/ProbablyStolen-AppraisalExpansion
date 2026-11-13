using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 深空鉴定拓展 Appraisal Expansion
/// 1) 夜晚拾荒有概率捡到 hand / ribwich / plasma_fuel 三种鉴定物品，越完好越稀有（赝品/次品占多数）。
/// 2) 多位供货商（三明治/机械手/等离子各厂商），各自每周固定一天上门，可电话联系次日上门。
/// 3) 杰克逊博士夜晚店铺补充：无标识等离子燃料 + 自制等离子原料（高级违禁品）+ 机器。
/// 4) 新增购买这些物品的下层居民与上层收藏家。
/// </summary>
public class AppraisalExpansionMod : IMod
{
    // 拾荒掉落用到的自定义物品工厂 id（注册进 PreBuiltItemHelper.preBuiltItems）
    private const string SCAV_HAND = "scav_hand";
    private const string SCAV_RIBWICH = "scav_ribwich";
    private const string SCAV_PLASMA = "scav_plasma";

    // 居民 identifier
    private const string LOWER_RESIDENT_ID = "appr_lower_resident";
    private const string UPPER_RESIDENT_ID = "appr_upper_resident";

    private static string modId = "appraisal_expansion";

    private static Harmony harmony;

    // ---------------- 多位商人定义（数据驱动） ----------------

    private enum MerchantKind
    {
        Ribwich,
        Hand,
        PlasmaLL,
        PlasmaZhongHua,
        PlasmaRepublic,
        PlasmaIgnitionLab
    }

    private class MerchantDef
    {
        public MerchantKind kind;
        public string id;
        public string displayName;
        public long phone;
        public int weekday;   // dayCounter % 7：0=周日 1=周一 ... 6=周六
    }

    private static readonly MerchantDef[] MERCHANTS = new MerchantDef[]
    {
        new MerchantDef { kind = MerchantKind.Ribwich, id = "appr_supplier_ribwich", displayName = "三明治供货商", phone = 55521L, weekday = 1 },
        new MerchantDef { kind = MerchantKind.Hand, id = "appr_supplier_hand", displayName = "机械手供货商", phone = 55522L, weekday = 2 },
        new MerchantDef { kind = MerchantKind.PlasmaLL, id = "appr_supplier_plasma_ll", displayName = "LL等离子供货商", phone = 55523L, weekday = 3 },
        new MerchantDef { kind = MerchantKind.PlasmaZhongHua, id = "appr_supplier_plasma_zhonghua", displayName = "中华等离子供货商", phone = 55524L, weekday = 4 },
        new MerchantDef { kind = MerchantKind.PlasmaRepublic, id = "appr_supplier_plasma_republic", displayName = "共和国等离子供货商", phone = 55525L, weekday = 5 },
        new MerchantDef { kind = MerchantKind.PlasmaIgnitionLab, id = "appr_supplier_plasma_ignition", displayName = "点火实验室等离子供货商", phone = 55526L, weekday = 6 }
    };

    public void Init(ModManifest manifest)
    {
        modId = manifest.ID;

        // Harmony 补丁：修复电话本显示名（去掉 "Translation Error ... in clientName"）
        harmony = new Harmony("mod." + modId);
        harmony.Patch(
            AccessTools.Method(typeof(StorePhoneClient), "GetLocalizedDisplayName", Type.EmptyTypes, (Type[])null),
            (HarmonyMethod)null,
            new HarmonyMethod(typeof(AppraisalExpansionMod), "PhoneNamePostfix", (Type[])null),
            (HarmonyMethod)null,
            (HarmonyMethod)null);

        // 对质小游戏：注册改价 action + Harmony 补丁（查看时开对质面板 + 对质按钮分发）
        RegisterConfrontActions();
        harmony.Patch(AccessTools.Method(typeof(InspectionUIManager), "HandleRibwich", new Type[] { typeof(GameItem) }, (Type[])null), (HarmonyMethod)null, new HarmonyMethod(typeof(AppraisalExpansionMod), "HandleRibwichPostfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
        harmony.Patch(AccessTools.Method(typeof(InspectionUIManager), "HandleAppHand", new Type[] { typeof(GameItem) }, (Type[])null), (HarmonyMethod)null, new HarmonyMethod(typeof(AppraisalExpansionMod), "HandleAppHandPostfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
        harmony.Patch(AccessTools.Method(typeof(InspectionUIManager), "HandlePlasma", new Type[] { typeof(GameItem) }, (Type[])null), (HarmonyMethod)null, new HarmonyMethod(typeof(AppraisalExpansionMod), "HandlePlasmaPostfix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null);
        harmony.Patch(AccessTools.Method(typeof(InspectionUIManager), "OnConfront", Type.EmptyTypes, (Type[])null), new HarmonyMethod(typeof(AppraisalExpansionMod), "OnConfrontPrefix", (Type[])null), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);

        // 1) 拾荒：注册自定义物品工厂
        PreBuiltItemHelper.preBuiltItems[SCAV_HAND] = CreateScavHand;
        PreBuiltItemHelper.preBuiltItems[SCAV_RIBWICH] = CreateScavRibwich;
        PreBuiltItemHelper.preBuiltItems[SCAV_PLASMA] = CreateScavPlasma;

        // 4) 居民：命名注册 + 混入每日随机池
        StoreClientListDict.storeClientDict[LOWER_RESIDENT_ID] = CreateLowerResident;
        StoreClientListDict.storeClientDict[UPPER_RESIDENT_ID] = CreateUpperResident;
        StoreClientList.generalPool.Add(CreateLowerResident, 120);
        StoreClientList.upperLevelPool.Add(CreateUpperResident, 120);

        // 2) 商人：命名注册 + 电话对话注册 + 名片物品工厂
        foreach (MerchantDef def in MERCHANTS)
        {
            MerchantDef captured = def;
            StoreClientListDict.storeClientDict[captured.id] = delegate { return CreateMerchant(captured); };
            PhoneClientList.DialogFuncs[captured.id + "_phone"] = delegate(StorePhoneClient client) { return BuildMerchantPhoneDialog(captured, client); };
        }
        ModHook.OnGenerateCustomerVeryLate += OnGenerateCustomers;
        ModHook.OnModItemDirectoryInit += RegisterCardItems;

        // 电话联系人：新游戏/读档/每天开店三重幂等注册
        ModHook.OnGameLoadedNormal += RegisterPhoneContacts;
        ModHook.OnNewGameStarted += OnNewGameStarted;
        ModHook.OnShutterOpenedEarly += RegisterPhoneContacts;

        // 3) 杰克逊博士夜晚店铺补货 + 证书在展示栏过夜检测
        ModHook.OnAfterhourArriveLate += OnAfterhourArrive;
        ModHook.OnGoingSleepEarly += OnCertificateOvernight;
    }

    public void OnEnable()
    {
    }

    public void OnDisable()
    {
        if (harmony != null)
        {
            harmony.UnpatchAll(harmony.Id);
            harmony = null;
        }
    }

    public static void PhoneNamePostfix(StorePhoneClient __instance, ref string __result)
    {
        if (__instance != null && !string.IsNullOrEmpty(__instance.dialogFuncId) && __instance.dialogFuncId.StartsWith("appr_supplier_"))
        {
            __result = __instance.displayName;
        }
    }

    // ---------------- 商人上门排期（加入委员会后、证书展示栏过夜才吸引；第14天六位一起，之后每周随机一名） ----------------

    private static void OnGenerateCustomers(StoreClientManager manager)
    {
        PlayerStore ps = PlayerStore.Instance;
        if (ps == null || !ps.isAppraisalIntroduced)
        {
            return;   // 未加入鉴定委员会
        }
        if (ModHelper.GetModDataInt(modId, "cert_shown_overnight", 0) != 1)
        {
            return;   // 证书尚未在展示栏过夜
        }
        int day = StoreStation.GetDayCounter();
        int certDay = ps.appraisalIntroducedDay;
        if (day == certDay + 14)
        {
            // 唯一固定上门时间：六位供货商一起上门
            foreach (MerchantDef def in MERCHANTS)
            {
                InsertMerchant(manager, def);
            }
        }
        else if (day > certDay + 14 && (day - certDay) % 7 == 0)
        {
            // 之后每周：有且只有一名随机供货商
            InsertMerchant(manager, RNG.GetRandomElement(MERCHANTS));
        }
    }

    private static void InsertMerchant(StoreClientManager manager, MerchantDef def)
    {
        StoreClient merchant = CreateMerchant(def);
        ModHelper.SetClientSource(merchant, modId);
        int count = manager.clientStack.Count;
        int pos = RNG.GetRandomInt(0, count);   // 0..count，随机第几位
        manager.clientStack.Insert(pos, merchant);
    }

    private static void OnCertificateOvernight()
    {
        PlayerStore ps = PlayerStore.Instance;
        if (ps == null || !ps.isAppraisalIntroduced)
        {
            return;
        }
        if (AppraisalRankHelper.GetDisplayedCertificate() != null)
        {
            ModHelper.SetModDataInt(modId, "cert_shown_overnight", 1);
        }
    }

    // ---------------- 电话联系人（拨号 → 下一游戏日上门） ----------------

    private static void OnNewGameStarted(string scenarioId)
    {
        RegisterPhoneContacts();
    }

    private static void RegisterPhoneContacts()
    {
        PlayerStore ps = PlayerStore.Instance;
        if (ps == null || ps.PhoneClientDict == null)
        {
            return;
        }
        bool migrated = ModHelper.GetModDataInt(modId, "phone_unlock_v2", 0) == 1;
        foreach (MerchantDef def in MERCHANTS)
        {
            if (ps.PhoneClientDict.ContainsKey(def.phone))
            {
                // 旧存档迁移：旧版本号码直接可见，重置为"未拿号码"状态（打不通 + 不进电话本），只做一次
                if (!migrated)
                {
                    ps.PhoneClientDict[def.phone].phoneState = StorePhoneClient.PhoneState.NotResponding;
                    ps.PhoneClientDict[def.phone].dialedBefore = false;
                }
                continue;
            }
            StorePhoneClient c = new StorePhoneClient();
            c.phoneClientType = StorePhoneClient.PhoneClientType.None;
            c.phoneState = StorePhoneClient.PhoneState.NotResponding;   // 未拿号码前：拨不通 + 不可见
            c.displayName = def.displayName;
            c.locID = def.displayName;   // 缺失本地化 key 时回退返回 key 本身，直接显示中文名
            c.dialogFuncId = def.id + "_phone";
            c.cooldownDuration = 0;
            c.currentCooldown = 0;
            c.dialedBefore = false;   // 只有玩家手动拨通后，原版 StartPhoneDialog 才会置 true
            ps.PhoneClientDict[def.phone] = c;
        }
        if (!migrated)
        {
            ModHelper.SetModDataInt(modId, "phone_unlock_v2", 1);
        }
    }

    private static void GiveCard(MerchantDef def)
    {
        PlayerStore ps = PlayerStore.Instance;
        if (ps == null)
        {
            return;
        }
        // 给一张印着号码的 1×2 名片
        GameItem card = DirectoryMaster.Item("appr_card_" + def.id, true);
        ps.AddDirectSellingItemToTable(card, true);
        // 同时解锁电话（能拨通，但仍不进电话本）
        if (ps.PhoneClientDict != null && ps.PhoneClientDict.ContainsKey(def.phone))
        {
            ps.PhoneClientDict[def.phone].phoneState = StorePhoneClient.PhoneState.Regular;
        }
    }

    private static void RegisterCardItems(ModItemDirectory dir)
    {
        foreach (MerchantDef def in MERCHANTS)
        {
            MerchantDef captured = def;
            dir.Add("appr_card_" + captured.id, delegate { return MakeBusinessCard(captured); });
        }
    }

    private static GameItem MakeBusinessCard(MerchantDef def)
    {
        GameItem card = DirectoryMaster.Item("card_pharma", true);
        card.identifier = "appr_card_" + def.id;
        card.SetName("名片（" + def.displayName + "）");
        card.flavorText = "电话 " + def.phone;
        card.ModifyTag("CARD_PHONE_TAG", delegate(TagState s) { s.Enable().SetString(def.phone.ToString()); }, false);
        card.onActivateSlotItemFunc = delegate(GameItem item, GameInventory inv, SlotMarker marker)
        {
            Dialogue d = new Dialogue();
            d.SetText(def.displayName, "电话：" + def.phone);
            DialogUIManager.Instance.StartDialogue(d);
        };
        return card;
    }

    private static Dialogue BuildMerchantPhoneDialog(MerchantDef def, StorePhoneClient client)
    {
        Dialogue root = new Dialogue();
        root.SetText(def.displayName, "需要我明天上门补货吗？");
        Dialogue yes = new Dialogue();
        yes.SetText("玩家", "好的，明天过来。");
        Dialogue no = new Dialogue();
        no.SetText("玩家", "不用了。");
        root.AddChoice(DirectoryMaster.Item("$", true), "好的，明天过来", "", yes, delegate
        {
            PlayerStore.Instance.QueueFuturClient(def.id, 1);
            yes.NextDialogue().SetText(def.displayName, "好，明天见。")
                .SetEndAction(delegate { PhoneUIManager.Instance.OnPhoneDialogEnd(); });
        });
        root.AddChoice(DirectoryMaster.Item("$", true), "不用了", "", no, delegate
        {
            no.NextDialogue().SetText(def.displayName, "好，再见。")
                .SetEndAction(delegate { PhoneUIManager.Instance.OnPhoneDialogEnd(); });
        });
        return root;
    }

    // ---------------- 拾荒物品工厂（正品稀有，赝品/次品常见） ----------------

    private static GameItem CreateScavHand()
    {
        GameItem it = DirectoryMaster.Item("hand", true);
        int r = RNG.GetRandomInt(1, 100);
        if (r <= 1)
        {
            AppHandHelper.InitDeluxe(it);       // 1% 顶级正品
        }
        else if (r <= 4)
        {
            AppHandHelper.InitTactical(it);     // 3% 高级正品
        }
        else if (r <= 9)
        {
            AppHandHelper.InitPremium(it);      // 5% 中档正品
        }
        else if (r <= 20)
        {
            AppHandHelper.InitCombat(it);       // 11% 基础正品
        }
        else if (r <= 60)
        {
            AppHandHelper.InitRandom(it);       // 40% 次品（随机成色）
        }
        else
        {
            AppHandHelper.InitJunk(it);         // 40% 次品（磨损/损坏）
        }
        return FinalizeItem(it, true);
    }

    private static GameItem CreateScavRibwich()
    {
        GameItem it = DirectoryMaster.Item("ribwich", true);
        RibwichHelper.Init(it);
        int r = RNG.GetRandomInt(1, 100);
        if (r <= 2)
        {
            RibwichHelper.InitCorrectDoubleMeat(it);      // 2% 顶级正品
        }
        else if (r <= 10)
        {
            RibwichHelper.InitCorrectDeluxeEdition(it);   // 8% 高级正品
        }
        else if (r <= 30)
        {
            // 20% 普通正品（保持 InitCorrectNormal）
        }
        else if (r <= 50)
        {
            RibwichHelper.InitOpened(it);                 // 20% 次品（已开封）
        }
        else
        {
            RibwichHelper.InitCounterfeit(it);            // 50% 赝品（假货）
        }
        return FinalizeItem(it, true);
    }

    private static GameItem CreateScavPlasma()
    {
        GameItem it = DirectoryMaster.Item("plasma_fuel", true);
        PlasmaHelper.InitPlasmaItem(it);
        int r = RNG.GetRandomInt(1, 100);
        if (r <= 3)
        {
            PlasmaHelper.InitLabPlasma(it);     // 3% 顶级正品
        }
        else if (r <= 11)
        {
            PlasmaHelper.InitZhongHuaPlasma(it); // 8% 正品（中华）
        }
        else if (r <= 19)
        {
            PlasmaHelper.InitRepublicPlasma(it); // 8% 正品（共和国）
        }
        else if (r <= 40)
        {
            PlasmaHelper.InitLLPlasma(it);       // 21% 基础正品（LL，随机磨损）
        }
        else
        {
            PlasmaHelper.InitRandomPlasma(it);   // 60% 次品/赝品（含无标识 NONE 封条）
        }
        return FinalizeItem(it, true);
    }

    // ---------------- 下层 / 上层居民（买家） ----------------

    private static StoreClient CreateLowerResident()
    {
        StoreClient c = new StoreClient();
        c.identifier = LOWER_RESIDENT_ID;
        c.displayName = "下层居民";
        c.spriteName = SpriteDict.GetRandomScavSpriteName();
        StoreClientFactionSetup.InitLowerLevelCitizen(c);
        c.clientIntent = StoreClient.ClientIntent.BUY;
        c.SetBudget(RNG.GetRandomInt(120, 300) + StoreStation.GetDayCounter());
        c.clientBuyingTagList.Add("HAND_TAG");
        c.clientBuyingTagList.Add("RIBWICH_TAG");
        c.clientBuyingTagList.Add("PLASMA_CELL_TAG");
        c.mainDialogue
            .SetText(c.displayName, "听说你这里能收到机械手、肋排三明治和等离子燃料？我随便看看成色。")
            .NextDialogue()
            .SetText(c.displayName, "捡来的货我也收，按成色给价。")
            .SetEndAction(delegate { });
        c.AddBasicDialog();
        c.CompleteClientCreation();
        return c;
    }

    private static StoreClient CreateUpperResident()
    {
        StoreClient c = new StoreClient();
        c.identifier = UPPER_RESIDENT_ID;
        c.displayName = "上层收藏家";
        c.spriteName = SpriteDict.GetRandomUpperSpriteName();
        StoreClientFactionSetup.InitUpperLevelCitizen(c);
        c.clientIntent = StoreClient.ClientIntent.BUY;
        c.SetBudget(RNG.GetRandomInt(500, 1000) + StoreStation.GetDayCounter() * 2);
        c.clientBuyingTagList.Add("HAND_TAG");
        c.clientBuyingTagList.Add("RIBWICH_TAG");
        c.clientBuyingTagList.Add("PLASMA_CELL_TAG");
        c.mainDialogue
            .SetText(c.displayName, "我在收集稀有机械义肢和等离子燃料电池。")
            .NextDialogue()
            .SetText(c.displayName, "只要是真品、成色好，价钱好商量。")
            .SetEndAction(delegate { });
        c.AddBasicDialog();
        c.CompleteClientCreation();
        return c;
    }

    // ---------------- 供货商（卖家） ----------------

    private static int GetMerchantCredit(MerchantDef def)
    {
        return ModHelper.GetModDataInt(modId, "credit_" + def.id, 0);
    }

    private static StoreClient CreateMerchant(MerchantDef def)
    {
        StoreClient c = new StoreClient();
        c.identifier = def.id;
        c.displayName = def.displayName;
        c.spriteName = SpriteDict.GetRandomMerchantSpriteName();
        c.clientFaction = "FACTION_LOWER_LEVEL";
        c.clientIntent = StoreClient.ClientIntent.SELL;
        c.cooldown = 3;
        c.isOfferWholesale = true;
        c.wholesaleDiscount = -10;

        int credit = GetMerchantCredit(def);
        c.mainDialogue
            .SetText(c.displayName, def.displayName + "上门了。当前合作额度：" + credit + "。")
            .NextDialogue()
            .SetText(c.displayName, "买得越多，额度越高，能解锁的品质越好。这是我的名片，缺货了随时打上面的电话。")
            .SetEndAction(delegate
            {
                PlaceMerchantGoods(def);
                GiveCard(def);   // 给名片（印号码）+ 解锁电话
            });

        c.onItemSold = delegate(GameItem item, int cost)
        {
            int newCredit = GetMerchantCredit(def) + cost;
            ModHelper.SetModDataInt(modId, "credit_" + def.id, newCredit);
        };

        c.AddBasicDialog();
        c.CompleteClientCreation();
        return c;
    }

    private static string GetTier(int credit)
    {
        return (credit >= 300) ? "premium" : "basic";
    }

    private static void PlaceMerchantGoods(MerchantDef def)
    {
        int credit = GetMerchantCredit(def);
        string tier = GetTier(credit);
        for (int i = 0; i < 3; i++)
        {
            GameItem it = MakeItem(def, tier);
            if (it != null)
            {
                PlayerStore.Instance.AddDirectSellingItemToTable(FinalizeItem(it, false), false);
            }
        }
    }

    private static GameItem MakeItem(MerchantDef def, string tier)
    {
        switch (def.kind)
        {
            case MerchantKind.Ribwich:
                return MakeRibwich(tier);
            case MerchantKind.Hand:
                return MakeHand(tier);
            case MerchantKind.PlasmaLL:
                return MakePlasmaBrand("LL", tier);
            case MerchantKind.PlasmaZhongHua:
                return MakePlasmaBrand("ZHONGHUA", tier);
            case MerchantKind.PlasmaRepublic:
                return MakePlasmaBrand("REPUBLIC", tier);
            case MerchantKind.PlasmaIgnitionLab:
                return MakePlasmaBrand("IGNITION_LAB", tier);
        }
        return null;
    }

    private static GameItem MakeHand(string tier)
    {
        GameItem it = DirectoryMaster.Item("hand", true);
        if (tier == "premium")
        {
            AppHandHelper.InitDeluxe(it);
        }
        else
        {
            AppHandHelper.InitCombat(it);
        }
        return it;
    }

    private static GameItem MakeRibwich(string tier)
    {
        GameItem it = DirectoryMaster.Item("ribwich", true);
        RibwichHelper.Init(it);
        if (tier == "premium")
        {
            RibwichHelper.InitCorrectDoubleMeat(it);
        }
        // basic 保持 InitCorrectNormal（普通正品）
        return it;
    }

    private static GameItem MakePlasmaBrand(string brand, string tier)
    {
        GameItem it = DirectoryMaster.Item("plasma_fuel", true);
        PlasmaHelper.InitPlasmaItem(it);
        if (brand == "LL")
        {
            PlasmaHelper.InitLLPlasma(it);
        }
        else if (brand == "ZHONGHUA")
        {
            PlasmaHelper.InitZhongHuaPlasma(it);
        }
        else if (brand == "REPUBLIC")
        {
            PlasmaHelper.InitRepublicPlasma(it);
        }
        else
        {
            PlasmaHelper.InitLabPlasma(it);
        }
        if (tier == "premium" && brand != "IGNITION_LAB")
        {
            ForcePristinePlasma(it);   // 高级档：无磨损/无裂痕/无裸露 + 加固
        }
        return it;
    }

    private static void ForcePristinePlasma(GameItem it)
    {
        it.ModifyTag("PLASMA_CASING_CONDITION_WEAR", delegate(TagState s) { s.Enable().SetString("NONE"); }, false);
        it.ModifyTag("PLASMA_CASING_CONDITION_CRACK", delegate(TagState s) { s.Enable().SetString("NONE"); }, false);
        it.ModifyTag("PLASMA_CASING_CONDITION_UNCOVERED", delegate(TagState s) { s.Enable().SetString("NONE"); }, false);
        it.ModifyTag("PLASMA_CASING_CONDITION_REINFORCED", delegate(TagState s) { s.Enable().SetString("REINFORCED"); }, false);
    }

    private static GameItem FinalizeItem(GameItem it, bool scavenged)
    {
        // 对质小游戏：标记问题标签 + 挂真伪 feature（需求6）
        AttachConfrontFeature(it);

        // 物品类型标签（问题3/4）
        if (it.IsTag("RIBWICH_TAG"))
        {
            it.SetGameItemType("FOOD");
            if (IsLuxuryRibwich(it))
            {
                it.SetGameItemType("LUXURY_ITEM");
            }
        }
        else if (it.IsTag("HAND_TAG"))
        {
            it.SetGameItemType("TOOL");
            it.SetGameItemType("MATERIAL");
            if (IsLuxuryHand(it))
            {
                it.SetGameItemType("LUXURY_ITEM");
            }
        }
        else if (it.IsTag("PLASMA_CELL_TAG"))
        {
            it.SetGameItemType("MATERIAL");
            if (IsLuxuryPlasma(it))
            {
                it.SetGameItemType("LUXURY_ITEM");
            }
        }
        // 价格（问题2/6）：拾荒/小偷带来的"有问题"物品保留虚高原价（Init 基础价），揭穿后才变真价；其余按真实价值
        if (scavenged && IsFaulty(it))
        {
            return it;
        }
        if (it.IsTag("HAND_TAG"))
        {
            it.unitValue = AppHandHelper.GetCorrectValue(it);
        }
        else if (it.IsTag("RIBWICH_TAG"))
        {
            it.unitValue = RibwichHelper.GetCorrectValue(it);
        }
        else if (it.IsTag("PLASMA_CELL_TAG"))
        {
            it.unitValue = PlasmaHelper.GetCorrectValue(it);
        }
        return it;
    }

    private static bool IsLuxuryRibwich(GameItem it)
    {
        // 开封/假货不算奢侈品
        if (RibwichHelper.IsOpened(it) || RibwichHelper.IsCounterfeit(it))
        {
            return false;
        }
        // 双层肉 / 豪华版 / 高品质包装厂
        if (it.GetTagReadonly("RIBWICH_MEAT_TAG").GetInt() == 1)
        {
            return true;
        }
        if (RibwichHelper.IsDeluxeEdition(it))
        {
            return true;
        }
        if (RibwichHelper.GetFacilityBonus(it.GetTagReadonly("RIBWICH_FACILITY_NUMBER_TAG").GetInt()) > 0)
        {
            return true;
        }
        return false;
    }

    private static bool IsLuxuryHand(GameItem it)
    {
        // 第一层判断：有磨损（WEAR/DAMAGE）则不打奢侈品
        string condition = it.GetTagReadonly("HAND_CONDITION").GetString();
        if (condition != "NONE")
        {
            return false;
        }
        string iface = it.GetTagReadonly("HAND_INTERFACE").GetString();
        string material = it.GetTagReadonly("HAND_MATERIAL").GetString();
        string armored = it.GetTagReadonly("HAND_ADDON_ARMORED").GetString();
        string bladed = it.GetTagReadonly("HAND_ADDON_BLADED").GetString();
        if (iface == "EMG_PLUS")
        {
            return true;
        }
        if (material == "GOLD_PLATED")   // 黄铜/镀金材质
        {
            return true;
        }
        if (armored == "ARMORED")        // 外置装甲
        {
            return true;
        }
        if (bladed == "BLADED")          // 外置武器（刀刃）
        {
            return true;
        }
        return false;
    }

    private static bool IsLuxuryPlasma(GameItem it)
    {
        // 第一层：有磨损则不打奢侈品
        string wear = it.GetTagReadonly("PLASMA_CASING_CONDITION_WEAR").GetString();
        if (wear != "NONE")
        {
            return false;
        }
        // 无磨损：含最顶级增值（点火实验室封条）才算奢侈品
        string seal = it.GetTagReadonly("PLASMA_SEAL").GetString();
        return seal == "IGNITION_LAB";
    }

    // ---------------- 杰克逊博士夜晚店铺补货 ----------------

    private static void OnAfterhourArrive(string locationId)
    {
        if (locationId != "dr_jackson")
        {
            return;
        }
        int day = StoreStation.GetDayCounter();
        // 无标识（NONE 封条）= 杰克逊博士自制的等离子燃料，估价无封条溢价
        PlayerStore.Instance.AddDirectSellingItemToTable(FinalizeItem(MakeUnmarkedPlasma(), false), false);
        PlayerStore.Instance.AddDirectSellingItemToTable(FinalizeItem(MakeUnmarkedPlasma(), false), false);

        if (day >= 20)
        {
            // 自制等离子原料（高级违禁品）
            PlayerStore.Instance.AddDirectSellingItemToTable(MakePlasmaRawMaterial(), false);
        }
        if (day >= 40)
        {
            // 制作机器（占位：原版能量转换器，后续换专属机器）
            PlayerStore.Instance.AddDirectSellingItemToTable("current_converter", false);
        }
    }

    private static GameItem MakeUnmarkedPlasma()
    {
        GameItem it = DirectoryMaster.Item("plasma_fuel", true);
        PlasmaHelper.InitPlasmaItem(it);
        it.ModifyTag("PLASMA_SEAL", delegate(TagState s) { s.Enable().SetString("NONE"); }, false);
        it.ModifyTag("PLASMA_CASING_CONDITION_WEAR", delegate(TagState s) { s.Enable().SetString(RNG.Roll(40) ? "NONE" : (RNG.Roll(50) ? "WEAR" : "WEAR_HEAVY")); }, false);
        it.ModifyTag("PLASMA_CASING_CONDITION_CRACK", delegate(TagState s) { s.Enable().SetString(RNG.Roll(20) ? "CRACK" : "NONE"); }, false);
        it.ModifyTag("PLASMA_CASING_CONDITION_UNCOVERED", delegate(TagState s) { s.Enable().SetString(RNG.Roll(20) ? "UNCOVERED" : "NONE"); }, false);
        it.ModifyTag("PLASMA_CASING_CONDITION_REINFORCED", delegate(TagState s) { s.Enable().SetString("NONE"); }, false);
        it.ModifyTag("PLASMA_COLOR", delegate(TagState s) { s.Enable().SetString(RNG.Roll(50) ? "POOR1" : "STANDARD"); }, false);
        return it;
    }

    private static GameItem MakePlasmaRawMaterial()
    {
        GameItem it = ItemDirectory.CreateEmptyItem("appr_plasma_raw");
        it.SetName("等离子原料（自制）");
        it.SetSpriteAndShape("Items/items_ship", "plasma_fuel_cell");
        ContrabandHelper.InitContrabandItem(it, 3);   // 3 = CONTRABAND_LEVEL_HIGH 高级违禁品
        return it;
    }

    // ---------------- 对质小游戏（需求6） ----------------

    private const string CAT_RIBWICH = "CATEGORY_GENUINE_RIBWICH";
    private const string CAT_HAND = "CATEGORY_GENUINE_HAND";
    private const string CAT_PLASMA = "CATEGORY_GENUINE_PLASMA";

    private const string RIBWICH_ISSUE_FAKE = "RIBWICH_ISSUE_FAKE";
    private const string RIBWICH_ISSUE_OPEN = "RIBWICH_ISSUE_OPEN";
    private const string HAND_ISSUE_WEAR = "HAND_ISSUE_WEAR";
    private const string HAND_ISSUE_DAMAGE = "HAND_ISSUE_DAMAGE";
    private const string HAND_ISSUE_INTERFACE = "HAND_ISSUE_INTERFACE";
    private const string PLASMA_ISSUE_CRACK = "PLASMA_ISSUE_CRACK";
    private const string PLASMA_ISSUE_WEAR = "PLASMA_ISSUE_WEAR";
    private const string PLASMA_ISSUE_COLOR = "PLASMA_ISSUE_COLOR";
    private const string PLASMA_ISSUE_UNCOVERED = "PLASMA_ISSUE_UNCOVERED";

    private static void RegisterConfrontActions()
    {
        ItemConditionActionList.ItemConditionActionDict["appr_genuine_ribwich"] = delegate(GameItem i) { i.unitValue = RibwichHelper.GetCorrectValue(i); };
        ItemConditionActionList.ItemConditionActionDict["appr_faulty_ribwich"] = delegate(GameItem i) { i.unitValue = RibwichHelper.GetCorrectValue(i); };
        ItemConditionActionList.ItemConditionActionDict["appr_genuine_hand"] = delegate(GameItem i) { i.unitValue = AppHandHelper.GetCorrectValue(i); };
        ItemConditionActionList.ItemConditionActionDict["appr_faulty_hand"] = delegate(GameItem i) { i.unitValue = AppHandHelper.GetCorrectValue(i); };
        ItemConditionActionList.ItemConditionActionDict["appr_genuine_plasma"] = delegate(GameItem i) { i.unitValue = PlasmaHelper.GetCorrectValue(i); };
        ItemConditionActionList.ItemConditionActionDict["appr_faulty_plasma"] = delegate(GameItem i) { i.unitValue = PlasmaHelper.GetCorrectValue(i); };
    }

    private static ItemCondition MakeGenuineCond(string cat, string id)
    {
        ItemCondition c = new ItemCondition();
        c.category = cat;
        c.display = LocHelper.GetLocalizedMechanicOrDefault("mech_condition_genuine", "Genuine");
        c.modValue = 0;
        c.modValueDisplay = 0;
        c.isTransformative = true;
        c.identifier = id;
        c.compareValue = 0;
        c.onActivateActionId = id;
        return c;
    }

    private static ItemCondition MakeFaultyCond(string cat, string id)
    {
        ItemCondition c = new ItemCondition();
        c.category = cat;
        c.display = LocHelper.GetLocalizedMechanicOrDefault("mech_condition_counterfeit", "Counterfeit");
        c.modValue = 0;
        c.modValueDisplay = -50;
        c.isTransformative = true;
        c.identifier = id;
        c.compareValue = -50;
        c.onActivateActionId = id;
        return c;
    }

    private static ItemFeature MakeConfrontFeature(string cat, string genuineId, string faultyId, bool faulty)
    {
        ItemFeature f = new ItemFeature(cat);
        f.identifier = cat;
        f.SetConditions(MakeGenuineCond(cat, genuineId), faulty ? MakeFaultyCond(cat, faultyId) : MakeGenuineCond(cat, genuineId));
        f.isFeatureMatch = !faulty;
        return f;
    }

    private static bool IsFaulty(GameItem it)
    {
        if (it.IsTag("RIBWICH_TAG")) return RibwichHelper.IsCounterfeit(it) || RibwichHelper.IsOpened(it);
        if (it.IsTag("HAND_TAG")) return it.GetTagReadonly("HAND_CONDITION").GetString() != "NONE";
        if (it.IsTag("PLASMA_CELL_TAG")) return it.GetTagReadonly("PLASMA_CASING_CONDITION_WEAR").GetString() != "NONE";
        return false;
    }

    private static void AttachConfrontFeature(GameItem it)
    {
        bool faulty = false;
        string cat = "";
        string gid = "";
        string fid = "";
        if (it.IsTag("RIBWICH_TAG"))
        {
            faulty = RibwichHelper.IsCounterfeit(it) || RibwichHelper.IsOpened(it);
            if (RibwichHelper.IsCounterfeit(it)) it.EnableTag(RIBWICH_ISSUE_FAKE, false);
            if (RibwichHelper.IsOpened(it)) it.EnableTag(RIBWICH_ISSUE_OPEN, false);
            cat = CAT_RIBWICH; gid = "appr_genuine_ribwich"; fid = "appr_faulty_ribwich";
        }
        else if (it.IsTag("HAND_TAG"))
        {
            string cond = it.GetTagReadonly("HAND_CONDITION").GetString();
            faulty = cond != "NONE";
            if (cond == "WEAR") it.EnableTag(HAND_ISSUE_WEAR, false);
            if (cond == "DAMAGE") it.EnableTag(HAND_ISSUE_DAMAGE, false);
            if (it.GetTagReadonly("HAND_INTERFACE").GetString() == "UNSPECIFIED") it.EnableTag(HAND_ISSUE_INTERFACE, false);
            cat = CAT_HAND; gid = "appr_genuine_hand"; fid = "appr_faulty_hand";
        }
        else if (it.IsTag("PLASMA_CELL_TAG"))
        {
            faulty = it.GetTagReadonly("PLASMA_CASING_CONDITION_WEAR").GetString() != "NONE";
            if (it.GetTagReadonly("PLASMA_CASING_CONDITION_CRACK").GetString() == "CRACK") it.EnableTag(PLASMA_ISSUE_CRACK, false);
            if (it.GetTagReadonly("PLASMA_CASING_CONDITION_WEAR").GetString() != "NONE") it.EnableTag(PLASMA_ISSUE_WEAR, false);
            string color = it.GetTagReadonly("PLASMA_COLOR").GetString();
            if (color == "POOR1" || color == "POOR2") it.EnableTag(PLASMA_ISSUE_COLOR, false);
            if (it.GetTagReadonly("PLASMA_CASING_CONDITION_UNCOVERED").GetString() == "UNCOVERED") it.EnableTag(PLASMA_ISSUE_UNCOVERED, false);
            cat = CAT_PLASMA; gid = "appr_genuine_plasma"; fid = "appr_faulty_plasma";
        }
        if (cat != "")
        {
            it.AddItemFeature(MakeConfrontFeature(cat, gid, fid, faulty));
        }
    }

    private static string GetConfrontCat(GameItem item)
    {
        if (item.IsTag("RIBWICH_TAG")) return CAT_RIBWICH;
        if (item.IsTag("HAND_TAG")) return CAT_HAND;
        if (item.IsTag("PLASMA_CELL_TAG")) return CAT_PLASMA;
        return null;
    }

    private static bool CanOpenConfront(GameItem item)
    {
        if (GeneralHelper.IsItemOwned(item)) return false;
        if (item.IsTag("ALREADY_CONFRONTED_TAG")) return false;
        if (InspectionUIManager.IsConfrontLocked()) return false;
        return item.IsTag(RIBWICH_ISSUE_FAKE) || item.IsTag(RIBWICH_ISSUE_OPEN)
            || item.IsTag(HAND_ISSUE_WEAR) || item.IsTag(HAND_ISSUE_DAMAGE) || item.IsTag(HAND_ISSUE_INTERFACE)
            || item.IsTag(PLASMA_ISSUE_CRACK) || item.IsTag(PLASMA_ISSUE_WEAR) || item.IsTag(PLASMA_ISSUE_COLOR) || item.IsTag(PLASMA_ISSUE_UNCOVERED);
    }

    private static string[] GetConfrontReasons(string cat)
    {
        if (cat == CAT_RIBWICH) return new string[] { RIBWICH_ISSUE_FAKE, RIBWICH_ISSUE_OPEN, "", "", "" };
        if (cat == CAT_HAND) return new string[] { HAND_ISSUE_WEAR, HAND_ISSUE_DAMAGE, HAND_ISSUE_INTERFACE, "", "" };
        return new string[] { PLASMA_ISSUE_CRACK, PLASMA_ISSUE_WEAR, PLASMA_ISSUE_COLOR, PLASMA_ISSUE_UNCOVERED, "" };
    }

    private static string[] GetConfrontLabels(string cat)
    {
        if (cat == CAT_RIBWICH) return new string[] { "假货", "已开封", "", "", "" };
        if (cat == CAT_HAND) return new string[] { "磨损", "损坏", "接口未标注", "", "" };
        return new string[] { "裂纹", "磨损", "劣质颜色", "螺丝裸露", "" };
    }

    private static void SetToggleLabel(InspectionUIManager m, Toggle t, string label)
    {
        TextMeshProUGUI txt = t.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
        if (txt != null) txt.text = label;
    }

    private static void OpenConfrontPanel(InspectionUIManager m, GameItem item)
    {
        string cat = GetConfrontCat(item);
        if (cat == null) return;
        if (!CanOpenConfront(item)) { m.confrontPanel.SetActive(false); return; }
        m.confrontPanel.SetActive(true);
        m.ResetAllToggle();
        string[] reasons = GetConfrontReasons(cat);
        string[] labels = GetConfrontLabels(cat);
        Toggle[] toggles = new Toggle[] { m.toggle1, m.toggle2, m.toggle3, m.toggle4, m.toggle5 };
        for (int k = 0; k < 5; k++)
        {
            if (reasons[k] != "")
            {
                toggles[k].gameObject.SetActive(true);
                SetToggleLabel(m, toggles[k], labels[k]);
            }
            else
            {
                toggles[k].gameObject.SetActive(false);
            }
        }
        m.OnGenuineClaim();
    }

    private static bool TryConfrontDispatch(InspectionUIManager m)
    {
        GameItem item = m.currentInspectingItem;
        if (item == null) return true;
        string cat = GetConfrontCat(item);
        if (cat == null) return true;
        if (InspectionUIManager.IsConfrontLocked() || PlayerStore.Instance.currentClientInstance == null || !m.IsAtLeastOneIssueSelected())
        {
            return false;
        }
        string reason = "";
        string[] reasons = GetConfrontReasons(cat);
        Toggle[] toggles = new Toggle[] { m.toggle1, m.toggle2, m.toggle3, m.toggle4, m.toggle5 };
        for (int k = 0; k < 5; k++)
        {
            if (toggles[k].isOn) { reason = reasons[k]; break; }
        }
        if (reason == "") return false;
        Dialogue d = new Dialogue();
        d.priority = 6;
        if (InspectableHelper.IsConfrontCorrect(reason, item))
        {
            d.SetText("CLIENT", LocHelper.GetLocalizedMechanicOrDefault("ins_confront_correct", "你说得对，给你打折。"))
             .SetStartAction(delegate
             {
                 ItemFeature feat = item.FindItemFeatureByCategory(cat);
                 if (feat != null) feat.ExposeFeature();   // 揭穿：把虚高原价改为真价 GetCorrectValue
                 InspectableHelper.OnConfrontSuccess(item); // -15% 折扣 + 标记已对质
             });
        }
        else
        {
            d.SetText("CLIENT", LocHelper.GetLocalizedMechanicOrDefault("ins_confront_wrong", "你错了。"))
             .SetStartAction(delegate { InspectionUIManager.Instance.OnConfrontFailed(); });
        }
        DialogUIManager.Instance.StartDialogue(d);
        m.CloseUI();
        return false;
    }

    public static void HandleRibwichPostfix(InspectionUIManager __instance, GameItem gameItem) { OpenConfrontPanel(__instance, gameItem); }
    public static void HandleAppHandPostfix(InspectionUIManager __instance, GameItem gameItem) { OpenConfrontPanel(__instance, gameItem); }
    public static void HandlePlasmaPostfix(InspectionUIManager __instance, GameItem gameItem) { OpenConfrontPanel(__instance, gameItem); }
    public static bool OnConfrontPrefix(InspectionUIManager __instance) { return TryConfrontDispatch(__instance); }
}
