"""Add player-mappable identities from the full input-defaults extraction to the action catalog."""
import argparse
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--defaults", type=Path, required=True)
    parser.add_argument("--catalog", type=Path, default=Path("prototype/input-router/actions.json"))
    args = parser.parse_args()
    defaults = json.loads(args.defaults.read_text(encoding="utf-8-sig"))
    catalog = json.loads(args.catalog.read_text(encoding="utf-8-sig"))
    if defaults["build"] != catalog["build"]:
        raise ValueError("Input defaults and catalog builds differ")
    actions = {entry["path"]: entry for entry in catalog["inputActions"]}
    names = {path: set() for path in actions}
    for context in defaults["contexts"]:
        for mapping in context["mappings"]:
            if mapping.get("action"):
                names[mapping["inputAction"]].add(mapping["action"])
    for path, entry in actions.items():
        entry["mappingNames"] = sorted(names[path])
    args.catalog.write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
