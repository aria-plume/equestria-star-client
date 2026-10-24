"""Validate exported binaries, embedded dependencies, and (optionally) source freshness."""
import argparse, hashlib, json, math, struct
from pathlib import Path
parser=argparse.ArgumentParser()
parser.add_argument('--source-root',type=Path)
args=parser.parse_args()
root=Path(__file__).resolve().parents[2]
catalog=json.loads((root/'assets/art/origami/catalog.json').read_text(encoding='utf-8'))
aliases=set(); components=0; size=0; triangles=0
for entry in catalog['assets']:
    assert (root/'assets/art/origami'/entry['id']/'visual.tscn').exists(),entry['id']
    for alias in entry['aliases']:
        assert alias.casefold() not in aliases,alias
        aliases.add(alias.casefold())
    for kind,component in entry['components'].items():
        path=root/component['path'].removeprefix('res://')
        binary=path.read_bytes()
        assert hashlib.sha256(binary).hexdigest()==component['sha256'],path
        magic,version,length=struct.unpack_from('<III',binary)
        assert (magic,version,length)==(0x46546c67,2,len(binary)),path
        chunk_length,chunk_type=struct.unpack_from('<II',binary,12)
        assert chunk_type==0x4e4f534a,path
        gltf=json.loads(binary[20:20+chunk_length])
        assert gltf.get('meshes'),path
        assert not gltf.get('cameras'),path
        assert all('uri' not in image for image in gltf.get('images',[])),f'External texture: {path}'
        assert all('uri' not in buf for buf in gltf.get('buffers',[])),f'External buffer: {path}'
        for mesh in gltf['meshes']:
            for primitive in mesh['primitives']:
                accessor=gltf['accessors'][primitive['attributes']['POSITION']]
                assert all(math.isfinite(v) for v in accessor['min']+accessor['max']),path
                triangles+=gltf['accessors'][primitive['indices']]['count']//3
        if args.source_root:
            source=args.source_root/component['source']
            assert hashlib.sha256(source.read_bytes()).hexdigest()==component['sourceSha256'],f'Source changed: {source}'
        components+=1;size+=len(binary)
print(json.dumps({'assets':len(catalog['assets']),'components':components,'bytes':size,'triangles':triangles,'sourceHashesChecked':bool(args.source_root)}))
