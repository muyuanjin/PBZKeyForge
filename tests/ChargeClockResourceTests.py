"""Check charge clocks against native signatures and installed Blueprint inputs."""
import argparse
import mmap
from pathlib import Path
import struct

from CompleteMoveSignatureResourceTests import NativeFunctions, blueprint_parameters, combat_art


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("game-exe", "paks", "oodle"):
        parser.add_argument("--" + name, type=Path, required=True)
    args = parser.parse_args()
    with args.game_exe.open("rb") as stream, mmap.mmap(stream.fileno(), 0, access=mmap.ACCESS_READ) as binary:
        assert [("OwningAbility", 0x12), ("ExplicitInput", 0x12), ("RequiredHoldTime", 0xa)] in NativeFunctions(binary).signatures("CreateTask")
    archive = combat_art.e.Archive(args.game_exe, args.paks, args.oodle, "CL-302256")
    symbols = combat_art.ScriptObjects(args.paks)
    try:
        paths = [
            "/PBZCombatAsset/ActorData/Players/Hun/Abilities/BronzeSword/GA_Hun_BronzeSword_B_Hold",
            "/PBZCombatAsset/ActorData/Players/Hun/Abilities/BaiShanFengLeiJian/Hold/GA_Hun_FengLeiJian_B_Hold",
        ]
        for path in paths:
            package = combat_art.e.Package(archive.resource(archive.packages[path.casefold()]))
            index = next(i for i, (name, _) in enumerate(package.exports) if name == "Main")
            timeline = combat_art.timeline(package, index)
            table = struct.unpack_from("<I", package.data, 32)[0]
            for action in timeline["actions"]:
                assert action["actionPointer"] > 0
                cls = struct.unpack_from("<Q", package.data, table + (action["actionPointer"] - 1) * 72 + 32)[0]
                assert not any(word in symbols.resolve(package, cls) for word in ("Bullet", "Damage", "HitBox")), "Charge timeline contains an attack: " + path
            graph = next(data for name, data in package.exports if name == "ExecuteUbergraph_" + path.rsplit("/", 1)[1])
            assert struct.pack("<f", .5) in graph, "Native hold threshold changed"
            if "FengLeiJian" in path:
                callback = "OnTick_F49F244647F3D911EDC36F8CD34569D0"
                payload = next(data for name, data in package.exports if name == callback)
                assert blueprint_parameters(package, payload) == [("DeltaTime", "FloatProperty")]
                assert struct.pack("<II", package.names.index(callback), 0) in graph
                assert struct.pack("<d", 350) in graph and struct.pack("<d", .34) in graph
    finally:
        archive.close()
    print("ChargeClockResourceTests PASS: native hold duration, float Tick input, charge-only Main timelines and storm fee/progress coefficients")


if __name__ == "__main__":
    main()
