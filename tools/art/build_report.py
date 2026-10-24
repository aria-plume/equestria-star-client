import json
from pathlib import Path
root=Path(__file__).resolve().parents[2]
catalog=json.loads((root/'assets/art/origami/catalog.json').read_text(encoding='utf-8'))
snapshot=json.loads((root/'tests/fixtures/main-map-20261024.json').read_text(encoding='utf-8-sig'))['data']
lines=['# 素材清单与地图匹配','', '2026-10-24快照，仅供检查；在线地图始终从服务器读取。', '',
    '![素材总览](gallery.png)', '', '| 资产 | 独立组件 | 当前地图格位 | 状态 |','|---|---|---|---|']
for entry in catalog['assets']:
    codes=[c['cellCode'] for c in snapshot['cells'] if c['cellName'] in entry['aliases'] or c.get('cellNameText') in entry['aliases']]
    status='候选，仅预览' if entry['status']=='candidate' else ('基础资产' if entry['status']=='technical-ready' else '已接入，外观沿用原稿待审阅状态')
    lines.append('| '+entry['name']+' | '+', '.join(entry['components'])+' | '+(', '.join(codes) or '资源库备用')+' | '+status+' |')
lines+=['','市政厅制作源尚未标准化，本轮保留原基础地块。三个候选资产虽有名称对应格位，正式地图仍不启用。',
    '', '115个GLB共47,953,872字节，源库累计约1,000,979三角面；这是单份资产总和，不是单帧地图渲染面数。尚未制定手机端面数与材质批次预算。',
    '', '![时装店贴纸与纸边近景](fashion-shop.png)']
(root/'docs/art/ASSETS.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
