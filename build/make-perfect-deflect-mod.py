"""Build a numeric-only rapid-guard penalty patch from local UE 5.7.4 resources.

Only five difficulty data assets are overridden. The application embeds the
finished containers; Python, Oodle and retoc are development tools only.
"""
import argparse
import importlib.util
import json
import pathlib
import shutil
import struct
import subprocess
import tempfile

ROOT=pathlib.Path(__file__).resolve().parents[1]
SPEC=importlib.util.spec_from_file_location('input_extract',ROOT/'build/extract-input-defaults.py')
e=importlib.util.module_from_spec(SPEC);SPEC.loader.exec_module(e)
STEM='PBZKeyForge_PerfectDeflect_P'
DIFFICULTIES=('Easy','Normal','Hard','Nightmare','Handicapped')


def cooldown_offset(data):
    package=e.Package(data)
    if len(package.exports)!=1:raise ValueError('Expected a difficulty data asset')
    payload=package.exports[0][1]
    reader=e.Reader(payload,package.names)
    fields=reader.header()
    if any(fields.get(i,True) for i in range(13)):raise ValueError('Missing explicit combat configuration fields')
    reader.read(8*8+4+4*2)
    count=reader.number()
    if not 1<=count<=32:raise ValueError('Unexpected quickclick multiplier count')
    reader.read(count*8)
    offset=reader.position
    if reader.number('<d')!=0.4:raise ValueError('Unexpected quickclick cooldown')
    header=struct.unpack_from('<I',data,4)[0]
    export_map=struct.unpack_from('<I',data,32)[0]
    serial_offset=struct.unpack_from('<Q',data,export_map)[0]
    return header+serial_offset+offset


def pack(args,packages,metadata,source_header):
    output=args.output
    output.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='perfect-deflect-') as temporary:
        work=pathlib.Path(temporary);raw=work/'raw';chunks=raw/'chunks';chunks.mkdir(parents=True)
        manifest={'chunk_paths':{metadata[path]['chunk'].hex():metadata[path]['path'] for path in packages},'version':'ReplaceIoChunkHashWithIoHash','mount_point':'../../../'}
        (raw/'manifest.json').write_text(json.dumps(manifest),encoding='utf8')
        toc=output/(STEM+'.utoc')
        def run(*command):subprocess.run([str(args.retoc),*map(str,command)],check=True,cwd=work)
        run('pack-raw',raw,toc)
        container_id=toc.read_bytes()[56:64]
        count=struct.unpack_from('<I',source_header,16)[0];ids=struct.unpack_from('<'+'Q'*count,source_header,20)
        stores=[];imports=[];package_ids=[]
        for path,package in packages.items():
            data=package;info=metadata[path];package_id=struct.unpack_from('<Q',info['chunk'])[0]
            package_ids.append(package_id)
            record=24+count*8+ids.index(package_id)*16
            n,relative,shaders,_=struct.unpack_from('<iiii',source_header,record)
            if shaders:raise ValueError('Unexpected shader dependencies')
            imports.append(source_header[record+relative:record+relative+n*8]);stores.append(n)
            (chunks/info['chunk'].hex()).write_bytes(data)
        store=bytearray();tail=bytearray()
        for i,(n,blob) in enumerate(zip(stores,imports)):
            store+=struct.pack('<iiii',n,len(stores)*16+len(tail)-i*16,0,0);tail+=blob
        store+=tail
        container_header=struct.pack('<II',0x496f436e,5)+container_id+struct.pack('<I',len(package_ids))+b''.join(struct.pack('<Q',p) for p in package_ids)+struct.pack('<I',len(store))+store+struct.pack('<IIIIIqq',0,0,0,0,0,0,0)
        (chunks/(container_id+bytes([0,0,0,6])).hex()).write_bytes(container_header)
        run('pack-raw',raw,toc)
        companion=ROOT/'assets/perfect-deflect-mod/companion.pak'
        shutil.copyfile(companion,toc.with_suffix('.pak'))
        toc_data=toc.read_bytes()
        entries,blocks=struct.unpack_from('<II',toc_data,24);seeds=struct.unpack_from('<I',toc_data,84)[0];overflow=struct.unpack_from('<I',toc_data,96)[0]
        block_base=144+entries*22+seeds*4+overflow*4;block_size=struct.unpack_from('<I',toc_data,44)[0]
        # IOStore readers align each block's read length, including unencrypted data.
        # retoc may place a block at an unaligned physical offset.
        read_end=max(int.from_bytes(toc_data[block_base+i*12:block_base+i*12+5],'little')+
                     ((int.from_bytes(toc_data[block_base+i*12+5:block_base+i*12+8],'little')+15)//16)*16 for i in range(blocks))
        with toc.with_suffix('.ucas').open('ab') as stream:stream.write(bytes(max(0,read_end-stream.tell())))
        run('verify',toc)
        layout={'RequiredChunks':[metadata[path]['chunk'].hex() for path in packages],
                'SourceBuild':args.build,'Packages':[{'Path':path,'Bytes':len(data),'CooldownOffset':metadata[path]['offset']} for path,data in packages.items()]}
        (output/'layout.json').write_text(json.dumps(layout,indent=2)+'\n',encoding='utf8')
        print('PerfectDeflect:',len(packages),'data assets; cooldown 0.4 -> 0; all other package bytes unchanged')


def main(args):
    args.output=args.output.resolve();args.retoc=args.retoc.resolve()
    archive=e.Archive(args.game_exe,args.paks,args.oodle,args.build)
    packages={};metadata={}
    try:
        source_header=archive.resource(next(i for i in range(archive.entries) if archive.toc[144+i*12+11]==6))
        for difficulty in DIFFICULTIES:
            path='/PBZCombatAsset/Config/GlobalConfig_Combat_'+difficulty
            entry=archive.packages[path.casefold()]
            source=archive.resource(entry);offset=cooldown_offset(source)
            patched=source[:offset]+struct.pack('<d',0)+source[offset+8:]
            packages[path]=patched
            metadata[path]={'chunk':archive.toc[144+entry*12:156+entry*12],
                            'path':next(p for p,i in archive.files.items() if i==entry),'offset':offset}
    finally:archive.close()
    pack(args,packages,metadata,source_header)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-exe',type=pathlib.Path,required=True)
    parser.add_argument('--paks',type=pathlib.Path,required=True)
    parser.add_argument('--oodle',type=pathlib.Path,required=True)
    parser.add_argument('--retoc',type=pathlib.Path,required=True)
    parser.add_argument('--build',choices=e.KEY_PART_OFFSETS,default='CL-302256')
    parser.add_argument('--output',type=pathlib.Path,default=ROOT/'assets/perfect-deflect-mod')
    main(parser.parse_args())
