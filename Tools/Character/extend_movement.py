"""Append original Jenn movement clips without replacing existing poses or part indices."""
from pathlib import Path
import json
import sys
import hashlib
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parent.parent
sys.path.insert(0, str(HERE))
import render_pbmc_preview as r
from blockout_regions import REGIONS, HAIR_REGIONS

ROOT = PROJECT / 'Assets/Art/Characters/Jenn'
if not ROOT.exists():
    ROOT = PROJECT / 'Assets/Art/Characters/Jenn'
path = PROJECT / 'Assets/Animations/Jenn/Data/JennAnimations.json'
data = json.loads(path.read_text(encoding='utf8'))
old_clips = {c['name']: json.dumps(c, separators=(',', ':')) for c in data['clips'] if c['name'] in ('Idle', 'Walk')}
original_parts, original_bitmaps = len(data['parts']), len(data['bitmaps'])
parts = {p['name']: i for i, p in enumerate(data['parts'])}
bitmaps = {(p['source'], p['symbolId']): i for i, p in enumerate(data['bitmaps'])}
new_clips = [
    ('Run', 'Jenn_SwordMoveSet.swf.swf', 'Run'),
    ('Dash', 'Jenn_SwordMoveSet.swf.swf', 'Dash02'),
    ('JumpIdle', 'Jenn_NoWeaponMoveSet.swf.swf', 'JumpIdle_noWeapon'),
    ('JumpMove', 'Jenn_NoWeaponMoveSet.swf.swf', 'JumpWalk_noWeapon'),
    ('JumpPrepareIdle', 'Jenn_NoWeaponMoveSet.swf.swf', 'JumpPreparationIdle_noWeapon'),
    ('JumpPrepareMove', 'Jenn_NoWeaponMoveSet.swf.swf', 'JumpPreparationWalk_noWeapon'),
    ('LandIdle', 'Jenn_NoWeaponMoveSet.swf.swf', 'ReceptionIdle_noWeapon'),
    ('LandMove', 'Jenn_NoWeaponMoveSet.swf.swf', 'ReceptionWalk_noWeapon'),
]
head_bitmaps = set()
for name, source, original in new_clips:
    r.load_asset(source)
    frames = []
    rec = r.ASSETS[source]['names'][original]
    for frame, items in enumerate(r.frames(source, rec['id'])):
        draws = []
        occurrences = {}
        for item in items:
            t = item['transform']
            if not item['visible'] or t[-1] <= .001:
                continue
            if item['name'].startswith(('Weapon', 'Shield', 'swordDummy')):
                continue
            target = r.resolve_costume(item, source)
            if target is None:
                continue
            asset, sid = target
            key = item['name']
            if key.startswith('instance'):
                key = r.ASSETS[asset]['records'][sid]['name'].replace('_shared', '')
            for detail, (file, symbol, matrix, alpha) in enumerate(r.leaves(asset, sid, frame, r.matrix(t), t[-1], 1)):
                part = key + ('' if detail == 0 else '_Detail' + str(detail))
                occurrence = occurrences.get(part, 0)
                occurrences[part] = occurrence + 1
                if occurrence:
                    part += '_Layer' + str(occurrence)
                if part not in parts:
                    parts[part] = len(data['parts'])
                    data['parts'].append({'name': part})
                bitmap_key = (file, symbol)
                crop, (_, _, width, height) = r.bitmap(file, symbol)
                if bitmap_key not in bitmaps:
                    index = len(data['bitmaps'])
                    bitmaps[bitmap_key] = index
                    filename = f'part_{index:03d}.png'
                    crop.save(ROOT / 'Textures' / filename)
                    data['bitmaps'].append({'file': filename, 'source': file, 'symbolId': symbol})
                index = bitmaps[bitmap_key]
                if any(k in part for k in ('Head', 'Hair', 'FullEyes', 'Mouth')):
                    head_bitmaps.add(index)
                corners = []
                for x, y in ((0, 0), (width, 0), (width, height), (0, height)):
                    point = matrix @ np.array([x, y, 1.0])
                    assert np.isfinite(point).all()
                    corners.extend(round(float(v), 6) for v in point[:2])
                draws.append({'part': parts[part], 'bitmap': index, 'corners': corners, 'alpha': round(float(alpha), 6)})
        assert len(set(d['part'] for d in draws)) == len(draws), (name, frame, 'duplicate parts')
        assert any('Hair' in data['parts'][d['part']]['name'] for d in draws), (name, frame, 'missing hair')
        assert any('Head' in data['parts'][d['part']]['name'] for d in draws), (name, frame, 'missing face')
        frames.append({'parts': draws})
    data['clips'] = [c for c in data['clips'] if c['name'] != name]
    data['clips'].append({'name': name, 'source': source + ':' + original, 'frames': frames})
    print(name, len(frames), 'frames')

# Apply exactly the existing neutral anatomical palette to newly used head bitmaps.
# Source texture pixels and all existing shade maps remain untouched.
new_maps = []
for index in sorted(head_bitmaps):
    bitmap = data['bitmaps'][index]
    destination = ROOT / 'Textures/Blockout' / (Path(bitmap['file']).stem + '_regions.png')
    if destination.exists():
        continue
    source = np.asarray(Image.open(ROOT / 'Textures' / bitmap['file']).convert('RGBA'))
    regions = HAIR_REGIONS if bitmap['source'].startswith('_ItemHair') else REGIONS
    canonical = np.array([c for c, _ in regions], dtype=np.float32)
    classes = ((source[:, :, None, :3].astype(np.float32) - canonical) ** 2).sum(axis=3).argmin(axis=2)
    grays = np.array([round(v * 255) for _, v in regions], dtype=np.uint8)[classes]
    Image.fromarray(np.stack((grays, grays, grays, np.full_like(grays, 255)), axis=2)).save(destination)
    new_maps.append(destination.name)

for clip in data['clips']:
    if clip['name'] in old_clips:
        assert json.dumps(clip, separators=(',', ':')) == old_clips[clip['name']], 'existing clip changed'
path.write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':')), encoding='utf8')
report = {'clips': [{'name': c['name'], 'source': c['source'], 'frames': len(c['frames'])} for c in data['clips']],
          'partsBefore': original_parts, 'partsAfter': len(data['parts']),
          'bitmapsBefore': original_bitmaps, 'bitmapsAfter': len(data['bitmaps']),
          'newHeadMaps': new_maps, 'existingIdleWalkUnchanged': True,
          'weaponSocketsExcluded': True}
(PROJECT / 'Assets/Animations/Jenn/Data/MovementSources.json').write_text(json.dumps(report, indent=2), encoding='utf8')
print('Parts', original_parts, '->', len(data['parts']), 'bitmaps', original_bitmaps, '->', len(data['bitmaps']))
