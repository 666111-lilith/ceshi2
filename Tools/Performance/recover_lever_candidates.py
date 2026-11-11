from pathlib import Path
import base64, json, re, struct, sys
sys.path.insert(0, str(Path('tmp/unitypy-assets').resolve()))
import UnityPy
from PIL import Image, ImageDraw

ROOT = Path(r'D:/SteamLibrary/steamapps/common/Cult of the Lamb/Cult Of The Lamb_Data/StreamingAssets/aa')
CAT = json.loads((ROOT / 'catalog.json').read_text(encoding='utf-8-sig'))
ENT = base64.b64decode(CAT['m_EntryDataString'])
BUC = base64.b64decode(CAT['m_BucketDataString'])
entries = [struct.unpack_from('<7i', ENT, 4 + i * 28) for i in range(struct.unpack_from('<i', ENT)[0])]
buckets = []
position = 4
for i in range(struct.unpack_from('<i', BUC)[0]):
    offset, count = struct.unpack_from('<2i', BUC, position)
    position += 8
    buckets.append(struct.unpack_from('<' + str(count) + 'i', BUC, position))
    position += count * 4

matches = []
for index, path in enumerate(CAT['m_InternalIds']):
    if ('/Art/Sprite/' in path or '/Art/UI/Base/DecorationIcons/' in path) and re.search(r'Bell\.spriteatlas|Sunken Machines Atlas\.spriteatlas|DST_ScienceMachine.spriteatlas|AlchemyEngine|Decoration.*Icons\.spriteatlas|Hourglass_Bell\.png|Well\.png', path):
        row = next((e for e in entries if e[0] == index and e[2] >= 0), None)
        if row:
            dependency = entries[buckets[row[2]][0]][0]
            bundle = CAT['m_InternalIds'][dependency]
            # Addressables player placeholders resolve to the local aa directory.
            bundle = str(ROOT / 'StandaloneWindows64' / Path(bundle).name)
            matches.append({'asset': path, 'bundle': bundle})
Path('tmp/lever-bundles.json').write_text(json.dumps(matches, indent=2), encoding='utf8')
print(json.dumps(matches, indent=2))

env = UnityPy.load(*sorted({m['bundle'] for m in matches}))
proof = Path('output/performance-assets/lever-candidates')
proof.mkdir(parents=True, exist_ok=True)
records = []
for obj in env.objects:
    if obj.type.name != 'Sprite':
        continue
    sprite = obj.read()
    if not re.search(r'bell|lever|switch|crank|handle|well|sunken|alchemy|machine|pump|windmill', sprite.m_Name, re.I):
        continue
    try:
        image = sprite.image
        name = re.sub('[^A-Za-z0-9_-]', '_', sprite.m_Name) + '.png'
        if (proof / name).exists():
            continue
        image.save(proof / name)
        records.append(dict(name=sprite.m_Name, file=name, size=image.size, path_id=str(obj.path_id), serialized_file=obj.assets_file.name))
    except Exception as exc:
        print('FAIL', sprite.m_Name, str(exc)[:150])
Path('tmp/lever-candidates.json').write_text(json.dumps(records, indent=2), encoding='utf8')
print('EXTRACTED', len(records))
cols, cell = 5, 220
for start in range(0, len(records), 30):
    page = records[start:start+30]
    sheet = Image.new('RGB', (cols*cell, ((len(page)+cols-1)//cols)*cell), '#b7bfc3')
    draw = ImageDraw.Draw(sheet)
    for i, record in enumerate(page):
        picture = Image.open(proof / record['file']).convert('RGBA')
        picture.thumbnail((210, 185))
        x, y = (i % cols)*cell + (cell-picture.width)//2, (i//cols)*cell + 10
        sheet.paste(picture, (x, y), picture)
        draw.text(((i%cols)*cell+5, (i//cols)*cell+198), record['name'][:31], fill='black')
    sheet.save(proof / f'contact-{start//30+1}.png')
