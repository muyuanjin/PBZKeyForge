"""Write the reviewed ability-source inventory from extract-ability-sources output."""
import argparse
import collections
import json
import pathlib


def shortest_paths(records):
    by_path = {row["package"].casefold(): row for row in records}
    paths = {key: [row["package"]] for key, row in by_path.items() if row["playerRoot"]}
    queue = collections.deque(sorted(paths))
    while queue:
        key = queue.popleft()
        for reference in by_path[key]["references"]:
            target = reference["package"].casefold()
            if target in by_path and target not in paths:
                paths[target] = paths[key] + [reference["package"]]
                queue.append(target)
    return paths


def summarize(source, catalog, gaps=None):
    records = source["records"]
    by_path = {row["package"].casefold(): row for row in records}
    paths = shortest_paths(records)
    catalog_tags = {key[len("ability:"):] for key in catalog["entries"] if key.startswith("ability:")}
    groups = collections.defaultdict(list)
    for row in source["abilityUniverse"]:
        groups[row["classification"]].append(row["package"])
    grants, upgrade_effects, candidates = [], [], []
    components = collections.Counter()
    for row in records:
        for component in row.get("components", []):
            components[component["class"].rsplit(".", 1)[-1]] += 1
            for grant in component.get("grants", []):
                target = by_path.get(grant.get("ability", "").casefold(), {})
                tags = target.get("abilityTagCandidates", [])
                grants.append({"effect": row["package"], "component": component["class"],
                               "activation": component["activation"], "grant": grant,
                               "sourceChain": paths.get(row["package"].casefold()),
                               "abilityTagCandidates": tags,
                               "catalogTagMatches": [tag for tag in tags if tag in catalog_tags]})
            tags = component.get("targetTags", {}).get("addedTags", [])
            if tags and ("Upgrade" in row["package"] or any("WeaponUpgrade" in tag for tag in tags)):
                upgrade_effects.append({"effect": row["package"], "addedTags": tags,
                                        "sourceChain": paths.get(row["package"].casefold())})
        if row["assetKind"] == "ability" and row["referencedFromPlayerRoot"]:
            tags = row.get("abilityTagCandidates", [])
            timelines = row.get("timelines", [])
            candidates.append({"ability": row["package"], "nativeParentClass": row["nativeParentClass"],
                               "abilityTagCandidates": tags, "catalogTagMatches": [tag for tag in tags if tag in catalog_tags],
                               "localTimelines": timelines,
                               "sourceChain": paths[row["package"].casefold()]})
    examples = []
    selectors = [
        ("adult-player", "ActorConfig_Hun", ("/Unarm/", "/SpecialMove/", "Execution", "BuJinShui", "PhantomPassive", "QTE")),
        ("young-player", "ActorConfig_Hun_Young", ("/Unarm/", "Jump", "Medicine", "Deflect")),
        ("inventory", "DT_InventoryData", ()),
        ("accessories", "DT_Accessory", ("RandomDrink", "Electrified", "Ignite", "GhostFire")),
        ("training", "DT_AbilityNodesTable", ()),
        ("shadow-weapon-upgrades", "DT_ShaqiWeaponData", ("ThunderSpear",)),
    ]
    for label, basename, filters in selectors:
        for row in records:
            if row["package"].rsplit("/", 1)[-1] != basename:
                continue
            examples.append({"source": label, "provider": row["package"],
                             "references": [ref for ref in row["references"] if not filters or any(text in ref["package"] for text in filters)]})
    unknowns = [{"package": row["package"], "components": row.get("componentUnknowns", []),
                 "timelines": row.get("timelineUnknowns", [])} for row in records
                if row.get("componentUnknowns") or row.get("timelineUnknowns")]
    result = {
        "build": source["build"], "runtimeVerified": False,
        "evidenceType": "Complete registry class enumeration, serialized resource references, and decoded native GameplayEffect grant/tag components",
        "counts": {"registryAssets": source["registryAssets"], "registryAbilityClasses": source["registryAbilityClasses"],
                   "dataTablesEnumerated": source["dataTablesEnumerated"], "rootPackages": source["rootPackages"],
                   "inspectedPackages": source["inspectedPackages"],
                   "abilityClassification": {key: len(value) for key, value in sorted(groups.items())},
                   "nativeEffectComponents": dict(sorted(components.items())),
                   "serializedAbilityGrantEntries": len(grants),
                   "actualAbilityGrants": sum(bool(row["grant"].get("ability")) for row in grants),
                   "playerReferencedAbilities": len(candidates)},
        "boundaries": [
            "The universe includes every registry GAS/Lego generated class and native PBZ GA subclass; AI RunSubtreeAbility controllers are not gameplay ability classes.",
            "All DataTable classes are enumerated without filtering by package directory. Player/NPC native class and player row-structure metadata define reference roots.",
            "Soft object paths and hard object pointers are followed. Unstructured pointer/name matches remain explicitly labeled reference candidates.",
            "A reference chain is discovery evidence. Only decoded GameplayEffect grants establish grants; neither establishes independent activation or an attack phase.",
            "Source-unassigned classes remain in the inventory. NPC-looking directories are not excluded from player reference chains.",
            "Catalog tag matches compare ability tags only. Shared or inherited tags do not prove all phases have bindings.",
        ],
        "abilityUniverse": dict(sorted(groups.items())), "actualAbilityGrants": grants,
        "nativeGrantedAbilityClasses": sorted({row["grant"]["ability"] for row in grants if row["grant"].get("ability", "").startswith("/Script/")}),
        "upgradeEffects": upgrade_effects, "otherSourceExamples": examples,
        "playerReferencedAbilityCandidates": candidates,
        "missingPackages": source["missingPackages"], "decodeUnknowns": unknowns,
        "limitations": [
            "Reference reachability includes target-role and contextual templates. It must not be presented as the set of playable attacks.",
            "DataTable row semantics, inherited defaults, native passive callbacks, costs and state-dependent branches need individual evidence before bindings are added.",
            "No independent consumable, mount or traversal attack endpoint has been established by this inventory.",
            "Static extraction does not verify execution in the game.",
        ],
    }
    if gaps:
        result["contextualSourceReview"] = {
            "candidateCount": gaps["candidateCount"], "groups": gaps["candidateGroups"],
            "reverseSourcePackagesScanned": gaps["reverseSourcePackagesScanned"],
            "candidatesWithActualReferences": sum(bool(row["reverseSources"]) for row in gaps["candidates"]),
            "withoutSourceReferences": [row["package"] for row in gaps["candidates"] if not row["reverseSources"]],
            "evidence": "docs/player-ability-source-gaps.json",
            "counterRecipes": "docs/contextual-counter-recipes.json",
        }
    return result


def markdown(audit):
    counts = audit["counts"]
    lines = ["# 能力来源审计", "", "游戏构建 `%s`。清点依据为全量 AssetRegistry、实际序列化软/硬引用及原生 GameplayEffect 组件；未进行游戏运行验证。" % audit["build"], "",
             "全表扫描覆盖 %d 个 DataTable、%d 个来源根和 %d 个引用资源。能力类全集为 %d，保留所有未归属项。" %
             (counts["dataTablesEnumerated"], counts["rootPackages"], counts["inspectedPackages"], counts["registryAbilityClasses"]), "",
             "| 来源分类 | 类数 |", "| --- | ---: |"]
    names = {"player-referenced": "玩家来源引用", "player-and-npc-referenced": "玩家与 NPC 来源均引用", "npc-referenced": "NPC 来源引用", "provider-only": "其他配置引用", "source-unassigned": "来源尚未归属"}
    lines += ["| %s | %d |" % (names[key], value) for key, value in counts["abilityClassification"].items()]
    lines += ["", "引用链用于发现候选，不能据此认定玩家能独立施放。目录不作为排除依据：魂的 ActorConfig 实际引用了 `Mobs/054_ZuoShang/Ability/Abilities/GA_ZuoShang_CA_03_QTE`。AI 的 RunSubtreeAbility 行为控制器不计入 GAS/Lego 能力类。", "",
              "已解码 %d 条真实能力授予，组件分别为 %s。JSON 保存授予效果、实际组件类型、能力、激活方式和最短玩家来源链。未声明为授予的对象仍只是引用候选。" %
              (counts["actualAbilityGrants"], "、".join("%s %d" % (key, value) for key, value in counts["nativeEffectComponents"].items())), "",
              "武器之外的实际来源包括：", "",
              "- `DT_Accessory` 的软对象路径指向饰品 GE；GE 的 GrantAndActivateAbilities 实际授予 RandomDrink、GhostFire 和原生状态类被动。饰品行为见 [饰品审计](accessory-move-audit.md)。",
              "- `DT_AbilityNodesTable` 保留训练节点对象来源；`DT_ShaqiWeaponData` 实际软引用雷霆枪升级 GE，其组件添加 CA、DashAtk、HuiMaQiang、LightningChain 和 Lv1–3 标签，招式条件必须检查这些真实标签。",
              "- 成年魂与幼年魂的 ActorConfig 引用不持械闪避、处决、跳跃、特殊地形动作及不同药葫芦能力。它们属于玩家来源，不能用主武器清单代表。",
              "- `DT_InventoryData` 实际引用 `GE_SecondWind`、`GE_ShachiCore`、`BP_ReturnToBell`；药物还包含不尽水被动。现有证据对应恢复、增益和返回功能。", "",
              "JSON 同时保留全部玩家引用能力候选、局部时间轴及目录标签匹配。标签存在不能证明阶段齐全：共用标签的武器大招必须继续检查时间轴、分支和消耗入口。", "",
              "对未归属类再按完整继承链检查，得到 53 个玩家相关候选；反向搜索 104190 个全目录 Blueprint、能力、效果、子弹、配置、表格及 World 包（包括流送单元），49 个候选有真实序列化来源。逐项证据见 [玩家来源差集](player-ability-source-gaps.json)。其中 31 个是玩家情境完美闪避反制，9 个是被执行方，2 个是剧情/敌招中的成对执行，3 个是移动，2 个是教程/UI 换武器，3 个是被动升级，3 个是 NPC 继承的玩家动作。", "",
              "31 个反制由敌招发出的实际反制帧临时授予。绑定通过原生 `TryActivateExternalActionAbilityByClass` 精确选择已有能力，保留输入和优先级；原生资格检查继续要求真实反制帧及匹配的 CounterAbility。配方不授予能力、不跳入时间轴，也不调用被执行方。详见 [情境反制配方](contextual-counter-recipes.json)。", "",
              "反向搜索未找到来源的 4 项为 `GA_Hun_Double_C_JueYing_Summon`、`GA_Hun_BEx_ZuoSHang_P2_Skill_20`、`GA_Hun_Double_SWT_Tutorial2`、`GA_ASM_ClimLadder_2`，保留真实类和继承证据。游龙叉水下被动、鬼头刀花增益、狂暴被动、束缚中的冲刺及翻障已找到各自 GE 或移动 Volume 来源。", "",
              "## 已知边界", "",
              "全类表仍保留来源根闭包以外的类，并单独记录已核对的 53 个玩家相关差集；NPC 控制类不据目录名称推断为玩家动作。原生被动的真实事件 payload、继承默认值、DataTable 行字段与状态/消耗条件需要逐项证据。当前清点未确立可独立绑定的消耗品、坐骑或攀爬攻击终点。", "",
              "生成：`build/extract-ability-sources.py` 读取游戏资源，`build/summarize-ability-sources.py --source <原始输出> --output docs/ability-source-audit.json` 生成清点与本文。资源断言：`tests/AbilitySourceResourceTests.py`。", ""]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=pathlib.Path, required=True)
    parser.add_argument("--catalog", type=pathlib.Path, default=pathlib.Path("assets/key-enhancement/action-details.json"))
    parser.add_argument("--gaps", type=pathlib.Path, default=pathlib.Path("docs/player-ability-source-gaps.json"))
    parser.add_argument("--output", type=pathlib.Path, default=pathlib.Path("docs/ability-source-audit.json"))
    args = parser.parse_args()
    audit = summarize(json.loads(args.source.read_text(encoding="utf-8")), json.loads(args.catalog.read_text(encoding="utf-8")),
                      json.loads(args.gaps.read_text(encoding="utf-8")) if args.gaps.exists() else None)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(audit, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    args.output.with_suffix(".md").write_text(markdown(audit), encoding="utf-8")
    print("Wrote %d ability classes, %d actual grants and %d player-reference candidates" %
          (audit["counts"]["registryAbilityClasses"], audit["counts"]["actualAbilityGrants"], audit["counts"]["playerReferencedAbilities"]))


if __name__ == "__main__":
    main()
