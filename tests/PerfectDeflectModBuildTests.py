"""Validate the five numeric-only UE 5.7.4 difficulty resources."""
import argparse
import importlib.util
import json
from pathlib import Path
import struct
import unittest

ROOT=Path(__file__).resolve().parents[1]
SPEC=importlib.util.spec_from_file_location('mod_builder',ROOT/'build/make-perfect-deflect-mod.py')
builder=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(builder)
OPTIONS=None


def chunks(folder):
    toc=(folder/(builder.STEM+'.utoc')).read_bytes()
    cas=(folder/(builder.STEM+'.ucas')).read_bytes()
    header,count,blocks=struct.unpack_from('<III',toc,20)
    block_size=struct.unpack_from('<I',toc,44)[0]
    block_at=header+count*22+struct.unpack_from('<I',toc,84)[0]*4+struct.unpack_from('<I',toc,96)[0]*4
    result={}
    for i in range(blocks):
        b=toc[block_at+i*12:block_at+(i+1)*12]
        physical=int.from_bytes(b[:5],'little');compressed=int.from_bytes(b[5:8],'little')
        if b[11]!=0 or physical+((compressed+15)//16)*16>len(cas):raise AssertionError('Invalid physical read boundary')
    for i in range(count):
        identity=toc[header+i*12:header+(i+1)*12].hex()
        record=header+count*12+i*10
        logical=int.from_bytes(toc[record:record+5],'big');length=int.from_bytes(toc[record+5:record+10],'big')
        block=logical//block_size;b=toc[block_at+block*12:block_at+(block+1)*12]
        physical=int.from_bytes(b[:5],'little')+logical%block_size
        result[identity]=cas[physical:physical+length]
    return result


class PerfectDeflectModBuildTests(unittest.TestCase):
    def test_five_data_assets_and_native_header_layout(self):
        folder=ROOT/'assets/perfect-deflect-mod'
        layout=json.loads((folder/'layout.json').read_text())
        resources=chunks(folder)
        self.assertEqual(len(resources),6)
        self.assertEqual(len(layout['RequiredChunks']),5)
        self.assertEqual({p['Path'] for p in layout['Packages']},{'/PBZCombatAsset/Config/GlobalConfig_Combat_'+d for d in builder.DIFFICULTIES})
        for identity,info in zip(layout['RequiredChunks'],layout['Packages']):
            data=resources[identity]
            self.assertEqual(len(data),info['Bytes'])
            offset=info['CooldownOffset']
            self.assertEqual(data[offset:offset+8],bytes(8))
            restored=data[:offset]+struct.pack('<d',0.4)+data[offset+8:]
            self.assertEqual(builder.cooldown_offset(restored),offset)
            exports,bundles=struct.unpack_from('<II',data,32)
            cells,cell_end=struct.unpack_from('<II',data,52)
            self.assertEqual(cells,bundles);self.assertEqual(cell_end,bundles)
            self.assertEqual(cells-exports,72)
            package=builder.e.Package(data)
            self.assertEqual(len(package.exports),1)
            self.assertEqual(package.exports[0][0],info['Path'].rsplit('/',1)[1])

    def test_only_cooldown_numeric_bytes_differ_from_original(self):
        if not OPTIONS or not OPTIONS.game_exe:self.skipTest('Pass local archive arguments for original-package comparison')
        folder=ROOT/'assets/perfect-deflect-mod';layout=json.loads((folder/'layout.json').read_text());resources=chunks(folder)
        archive=builder.e.Archive(OPTIONS.game_exe,OPTIONS.paks,OPTIONS.oodle,OPTIONS.build)
        try:
            for identity,info in zip(layout['RequiredChunks'],layout['Packages']):
                original=archive.resource(archive.packages[info['Path'].casefold()]);patched=resources[identity]
                offset=builder.cooldown_offset(original)
                self.assertEqual(original[offset:offset+8],struct.pack('<d',0.4))
                self.assertEqual(patched,original[:offset]+bytes(8)+original[offset+8:])
                self.assertEqual(len(original),len(patched))
                print(info['Path'],len(patched),'bytes; only double at',offset,'changed 0.4 -> 0')
        finally:archive.close()


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--game-exe',type=Path);parser.add_argument('--paks',type=Path);parser.add_argument('--oodle',type=Path)
    parser.add_argument('--build',default='CL-302256')
    OPTIONS,remaining=parser.parse_known_args()
    unittest.main(argv=[__file__,*remaining])
