"""Refresh searchable ability descriptions and original per-device input keys.

Audited labels and sections remain in action-details.json. This command refreshes
resource search aliases and raw InputAction factory keys, and can record evidence.
Archive keys stay in memory. It neither launches the game nor changes its files.
"""
import argparse
import json
import pathlib
import sys


ROOT = pathlib.Path(__file__).resolve().parents[1]


def descriptions(archive, binding_reader, catalog):
    entries = {entry["tag"]: {"descriptions": [], "groupDescriptions": [], "sources": []}
               for entry in catalog["abilities"]}
    assets = sorted({source["asset"] for entry in catalog["abilities"] for source in entry["sources"]})
    for asset in assets:
        package = binding_reader.e.Package(archive.resource(archive.packages[asset.casefold()]))
        document = binding_reader.BindingDocument(package.exports[0][1], package.names)
        for group, item in enumerate(document.items):
            blocks = [("normal", item.get("NormalData"))] + [
                (pair["tag"], pair["value"])
                for pair in (item.get("TagConditionData") or {}).get("pairs", [])]
            for condition, data in blocks:
                if not data:
                    continue
                for kind in ("Actions", "Combos", "InterruptLower"):
                    for trigger in data.get(kind) or []:
                        entry = entries.get(trigger.get("TriggerTag"))
                        if entry is None:
                            continue
                        description = trigger.get("Description") or ""
                        group_description = item.get("Description") or ""
                        for key, value in (("descriptions", description), ("groupDescriptions", group_description)):
                            if value and value not in entry[key]:
                                entry[key].append(value)
                        entry["sources"].append({
                            "asset": asset, "group": group, "priority": item.get("ActionPriority"),
                            "groupDescription": group_description, "condition": condition, "kind": kind,
                            "description": description,
                            "requiredTags": trigger.get("RequiredTags") or [],
                            "ignoredTags": trigger.get("IgnoredTags") or []})
    return {"build": catalog["build"],
            "evidence": "Static InputActionBinding descriptions, conditions and trigger sections",
            "assets": len(assets), "entries": entries}


def factory_inputs(archive, reader, details):
    for identity, entry in details["entries"].items():
        if identity.startswith("action:"):
            entry["factoryKeyboardKeys"] = []
            entry["factoryGamepadKeys"] = []
            entry["nativeMapping"] = False
    count = 0
    for path, resource in sorted(archive.files.items()):
        if not path.endswith(".uasset") or not path.rsplit("/", 1)[-1].startswith("IMC_"):
            continue
        context = reader.Package(archive.resource(resource)).context(
            path.removeprefix("../../../"), archive.input_action_settings)
        count += 1
        for mapping in context["mappings"]:
            entry = details["entries"].get("action:" + mapping["inputAction"])
            if entry is None:
                continue
            entry["nativeMapping"] = True
            field = "factoryGamepadKeys" if mapping["device"] == "Gamepad" else "factoryKeyboardKeys"
            if mapping["key"] not in entry[field]:
                entry[field].append(mapping["key"])
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-exe", required=True, type=pathlib.Path)
    parser.add_argument("--paks", required=True, type=pathlib.Path)
    parser.add_argument("--oodle", required=True, type=pathlib.Path)
    parser.add_argument("--build", default="CL-302256")
    parser.add_argument("--catalog", type=pathlib.Path, default=ROOT / "prototype/input-router/actions.json")
    parser.add_argument("--output", type=pathlib.Path, default=ROOT / "assets/key-enhancement/action-details.json")
    parser.add_argument("--evidence", type=pathlib.Path)
    args = parser.parse_args()
    catalog = json.loads(args.catalog.read_text(encoding="utf-8"))
    details = json.loads(args.output.read_text(encoding="utf-8"))
    if catalog["build"] != args.build or details["build"] != args.build:
        raise ValueError("The action relationships belong to a different game build")
    sys.path.insert(0, str(ROOT / "prototype/input-router"))
    import special_dash
    archive = special_dash.e.Archive(args.game_exe, args.paks, args.oodle, args.build)
    try:
        evidence = descriptions(archive, special_dash, catalog)
        contexts = factory_inputs(archive, special_dash.e, details)
    finally:
        archive.close()
    for tag, source in evidence["entries"].items():
        entry = details["entries"]["ability:" + tag]
        entry["aliases"] = list(dict.fromkeys(
            source["descriptions"] + source["groupDescriptions"] + [entry["variant"]]))
    args.output.write_text(json.dumps(details, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if args.evidence:
        args.evidence.parent.mkdir(parents=True, exist_ok=True)
        args.evidence.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Refreshed %d ability aliases from %d InputActionBinding assets" %
          (len(evidence["entries"]), evidence["assets"]))
    print("Refreshed factory input keys from %d InputMappingContext assets" % contexts)


if __name__ == "__main__":
    main()
