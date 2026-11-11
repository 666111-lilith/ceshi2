"""Export verified PBMC character parts, retaining every authored affine pose.

Run with the bundled Python (Pillow + numpy). Source resources are read-only.
Only IdleCool_noWeapon and Walk_noWeapon are exported for the MVP controller.
"""
from pathlib import Path
import json
import sys
import numpy as np

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parent.parent
sys.path.insert(0, str(HERE))
import render_pbmc_preview as r
from blockout_regions import write_regions

DEST = PROJECT / 'Assets/Art/Characters/Jenn'
TEXTURES = DEST / 'Textures'
DATA = PROJECT / 'Assets/Animations/Jenn/Data'
TEXTURES.mkdir(parents=True, exist_ok=True)
DATA.mkdir(parents=True, exist_ok=True)
SOURCE = 'Jenn_NoWeaponMoveSet.swf.swf'
PARTS, BITMAPS, part_ids, bitmap_ids = [], [], {}, {}

def named_leaves(name, frame):
    rec = r.ASSETS[SOURCE]['names'][name]
    for item in r.frames(SOURCE, rec['id'])[frame]:
        t = item['transform']
        if not item['visible'] or t[-1] <= .001: continue
        target = r.resolve_costume(item, SOURCE)
        if target is None: continue
        asset, sid = target
        key = item['name']
        if key.startswith('instance'):
            key = r.ASSETS[asset]['records'][sid]['name'].replace('_shared', '')
            if key == 'IdleCool_noWeapon_0.79': key = 'Head'
        for index, leaf in enumerate(r.leaves(asset, sid, frame, r.matrix(t), t[-1], 1)):
            yield key + ('' if index == 0 else '_Detail'+str(index)), leaf

def export_frame(name, frame):
    draws = []
    for key, (asset, sid, matrix, alpha) in named_leaves(name, frame):
        if key not in part_ids:
            part_ids[key] = len(PARTS); PARTS.append({'name': key})
        bitmap_key = (asset, sid)
        crop, (_, _, width, height) = r.bitmap(asset, sid)
        if bitmap_key not in bitmap_ids:
            index = len(BITMAPS); bitmap_ids[bitmap_key] = index
            filename = f'part_{index:03d}.png'
            crop.save(TEXTURES / filename)
            BITMAPS.append({'file': filename, 'source': asset, 'symbolId': sid})
        corners = []
        for x, y in ((0,0), (width,0), (width,height), (0,height)):
            point = matrix @ np.array([x,y,1.0])
            assert np.isfinite(point).all()
            corners.extend(round(float(v), 6) for v in point[:2])
        draws.append({'part': part_ids[key], 'bitmap': bitmap_ids[bitmap_key],
                      'corners': corners, 'alpha': round(float(alpha), 6)})
    assert len({p['part'] for p in draws}) == len(draws), 'Duplicate named part'
    return {'parts': draws}

clips = []
for name, source in [('Idle','IdleCool_noWeapon'), ('Walk','Walk_noWeapon')]:
    count = len(r.frames(SOURCE, r.ASSETS[SOURCE]['names'][source]['id']))
    frames = [export_frame(source, frame) for frame in range(count)]
    clips.append({'name':name, 'source':source, 'frames':frames})
    print(name, count, 'frames;', min(len(f['parts']) for f in frames),
          'to', max(len(f['parts']) for f in frames), 'visible parts')

ground_y = max(p['corners'][i] for p in clips[0]['frames'][0]['parts'] for i in (1,3,5,7))
all_y = [p['corners'][i] for c in clips for f in c['frames'] for p in f['parts'] for i in (1,3,5,7)]
data = {'version':1, 'unitsPerPixel': .0075, 'sourceGroundY': ground_y,
        'framesPerSecond':24, 'parts':PARTS, 'bitmaps':BITMAPS, 'clips':clips}
(DATA/'JennAnimations.json').write_text(json.dumps(data, ensure_ascii=False,
                                               separators=(',',':')), encoding='utf8')
write_regions(DEST)
assert [len(c['frames']) for c in clips] == [99,24]
print('Exported',len(PARTS),'named parts,',len(BITMAPS),'bitmap crops;',
      'ground',ground_y,'height',(max(all_y)-min(all_y))*.0075,
      'FPS=24 is a tunable preview setting, not verified source playback speed.')
