"""Blender 4.5: export current split deliveries, never save or alter source .blend files.
blender -b --factory-startup --python-exit-code 1 --python tools/art/export_origami.py -- --source-root <art workspace>
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Vector

PROJECT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--source-root', type=Path, required=True)
parser.add_argument('--only', default='')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
SOURCE = args.source_root.resolve()
OUTPUT = PROJECT / 'assets/art/origami'

# Stable asset IDs and explicit name aliases; numeric cell IDs never select art.
SPECS = [
 ('grass-light','基础_浅绿草地',[]),('grass-dark','基础_深绿草地',[]),
 ('sand','基础_沙地',[]),('stone','基础_石地',[]),('crystal-ground','基础_水晶地',[]),('snow','基础_雪地',[]),
 ('sugarcube','糖块屋',['方糖屋','方糖甜点屋','SugarcubeCorner']),
 ('everfree','无尽自由森林',['无尽之森','EverfreeEntrance','EverfreeLeft','EverfreeCenter','EverfreeRight']),
 ('sweet-apple-acres','香甜苹果园',['甜苹果园','SweetAppleAcres']),
 ('fluttershy','小蝶家',['柔柔小屋','FluttershyCottage']),
 ('fashion-shop','时装店',['FashionShop']),('rainbow-house','云宝家',['RainbowDashHouse']),
 ('old-castle','姐妹城堡',['旧城堡','古城遗迹','OldCastle']),
 ('zecora','泽可拉小屋',['泽科拉小屋','ZecoraHut']),('harmony-tree','谐律之树',['韵律之树','TreeOfHarmony']),
 ('twilight-castle','暮光城堡',['TwilightCastle']),
 ('manehattan-district','马哈顿火车站',[]),('appleloosa','苹果鲁萨',['Appleloosa']),
 ('tartarus','塔塔罗斯',['地狱','塔尔塔罗斯','Tartarus']),
 ('friendship-school','友谊学院',[]),('ponyville-market','小马镇集市',['集市']),
 ('ponyville-station','小马谷火车站',['StationPonyville','小马谷车站']),
 ('crystal-castle','水晶城堡',['CrystalCastle']),('crystal-empire','水晶帝国',['CrystalEmpire']),
 ('crystal-market','水晶集市_候选方案',['水晶集市','CrystalMarket']),
 ('crystal-stadium','水晶运动场',['运动场']),('yakyakistan','牦牦斯坦',['耗耗斯坦','Yakyakistan']),
 ('las-pegasus','天马维加斯',['拉斯飞马斯','LasPegasus']),('weather-factory','天气工厂',['WeatherFactory']),
 ('cloudsdale','云中城',[]),('equality-town','平等镇',['EqualityTown']),
 ('rainbow-market','彩虹瀑布镇',['RainbowFallsMarket','彩虹瀑布集市']),('rainbow-falls','彩虹瀑布',[]),
 ('liberty-statue','马哈顿_友谊雕像',['马哈顿','Manehattan']),
 ('hotel-candidate','高档场所_酒店候选方案',['高档场所']),('canterlot','皇城',['坎特洛特','RoyalCity']),
 ('canterlot-station','中心城火车站',[]),('canterlot-market','皇城商业街',['商业街']),
 ('banquet-candidate','宴会厅_开放剖面候选方案',['宴会厅','CanterlotBanquetHall','坎特洛特宴会厅']),
 ('griffonstone','狮鹫岩',['Griffonstone']),('baltimare','巴尔的马',['Baltimare']),
 ('dragon-lands','巨龙之地',['龙之国','DragonLands']),('hippogriff','骏鹰王国',['鹰马王国','HippogriffKingdom']),
]

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def simplify_materials(objects):
    """Preserve image decals; replace unsupported procedural colour/bump with matte paper base.
    Procedural fibres are intentionally not claimed to be baked into the GLB.
    """
    materials = {slot.material for obj in objects for slot in obj.material_slots if slot.material}
    image_materials = []
    for mat in materials:
        if not mat.use_nodes:
            mat.use_nodes = True
        bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if bsdf is None:
            raise RuntimeError(f'Unsupported material without Principled BSDF: {mat.name}')
        base = bsdf.inputs['Base Color']
        if base.is_linked and base.links[0].from_node.type == 'TEX_IMAGE':
            image = base.links[0].from_node.image
            if image is not None and not image.has_data:
                # Packed images are loaded lazily after opening a .blend.
                if image.size[0] > 0:
                    _ = image.pixels[0]
            if image is None or not image.has_data:
                raise RuntimeError(f'Missing decal image: {mat.name}')
            image_materials.append(mat.name)
        elif base.is_linked:
            for link in list(base.links):
                mat.node_tree.links.remove(link)
            base.default_value = mat.diffuse_color
        for socket_name in ['Normal','Roughness','Metallic']:
            for link in list(bsdf.inputs[socket_name].links):
                mat.node_tree.links.remove(link)
        bsdf.inputs['Roughness'].default_value = .92
        bsdf.inputs['Metallic'].default_value = 0
        mat.use_backface_culling = False
    return image_materials

def export_component(source, dest):
    before = sha(source)
    bpy.ops.wm.open_mainfile(filepath=str(source))
    scene = bpy.context.scene
    offset = list(scene.get('assembly_offset_metres', [0,0,0]))
    roots = [o for o in scene.objects if o.type == 'EMPTY' and o.name.startswith('ROOT_') and not o.parent]
    if 'assembly_offset_metres' in scene:
        if len(roots) != 1:
            raise RuntimeError(f'Ambiguous assembly root: {source}')
        roots[0].location = offset
    bpy.context.view_layer.update()
    objects = [o for o in scene.objects if o.type == 'MESH']
    if not objects:
        raise RuntimeError(f'Empty component: {source}')
    points = [o.matrix_world @ Vector(c) for o in objects for c in o.bound_box]
    low = [min(p[i] for p in points) for i in range(3)]
    high = [max(p[i] for p in points) for i in range(3)]
    if not all(math.isfinite(v) for v in low+high):
        raise RuntimeError(f'Non-finite geometry: {source}')
    decals = simplify_materials(objects)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.hide_set(False)
        obj.select_set(True)
        parent = obj.parent
        while parent:
            parent.hide_set(False)
            parent.select_set(True)
            parent = parent.parent
    dest.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=str(dest), export_format='GLB', use_selection=True,
        export_yup=True, export_apply=True, export_cameras=False, export_lights=False,
        export_animations=False, export_extras=False, export_materials='EXPORT')
    if sha(source) != before:
        raise RuntimeError(f'Source unexpectedly changed: {source}')
    return {'source':source.relative_to(SOURCE).as_posix(),'sourceSha256':before,
        'path':'res://'+dest.relative_to(PROJECT).as_posix(),'sha256':sha(dest),
        'bytes':dest.stat().st_size,'assemblyOffsetBlender':offset,
        'boundsBlender':{'min':low,'max':high},'meshObjects':len(objects),'decalMaterials':decals}

OUTPUT.mkdir(parents=True,exist_ok=True)
catalog_path = OUTPUT/'catalog.json'
previous = json.loads(catalog_path.read_text(encoding='utf-8')) if catalog_path.exists() else {}
entries = {e['id']:e for e in previous.get('assets',[])}
for asset_id,name,aliases in SPECS:
    if args.only and asset_id not in args.only.split(','):
        continue
    folder = SOURCE/'交付/地块'/name
    components = {}
    for prefix,kind in [('01_','terrain'),('02_','building'),('03_','decoration')]:
        sources = sorted(folder.glob(prefix+'*.blend'))
        if len(sources)>1:
            raise RuntimeError(f'Ambiguous {kind}: {folder}')
        if sources:
            components[kind] = export_component(sources[0], OUTPUT/asset_id/(kind+'.glb'))
    if 'terrain' not in components:
        raise RuntimeError(f'Missing terrain: {folder}')
    entries[asset_id] = {'id':asset_id,'name':name,'aliases':[name]+aliases,
        'status':'candidate' if '候选' in name else ('technical-ready' if name.startswith('基础_') else 'visual-review-pending'),
        'sourceReadme':(folder/'README.md').relative_to(SOURCE).as_posix(),
        'components':components}
    print('EXPORTED',asset_id,flush=True)
catalog = {'schemaVersion':1,'sourceUnits':'metres','sourceRadius':1.0,'sourceThickness':0.2,
    'godotRotationDegreesY':30,'materialPolicy':'Matte base colours and embedded image decals; procedural Blender fibres are not baked.',
    'unavailable':[{'name':'市政厅','reason':'制作源尚未标准化和独立拆分，暂用可交互基础地块'}],
    'assets':[entries[s[0]] for s in SPECS if s[0] in entries]}
catalog_path.write_text(json.dumps(catalog,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
for entry in catalog['assets']:
    components = entry['components']
    lines = [f'[gd_scene load_steps={len(components)+1} format=3]', '']
    for kind, component in components.items():
        lines.append(f'[ext_resource type="PackedScene" path="{component["path"]}" id="{kind}"]')
    lines += ['', '[node name="OrigamiAsset" type="Node3D"]', 'rotation_degrees = Vector3(0, 30, 0)']
    for kind in components:
        lines += ['', f'[node name="{kind.title()}" parent="." instance=ExtResource("{kind}")]']
    (OUTPUT/entry['id']/'visual.tscn').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print('CATALOG',len(catalog['assets']),flush=True)
