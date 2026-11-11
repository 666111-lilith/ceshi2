from pathlib import Path
import sys,json,re,base64,struct,shutil,hashlib
sys.path.insert(0,str(Path('tmp/unitypy-assets').resolve()))
import UnityPy
from PIL import Image,ImageDraw

OUT=Path('Assets/Art/Stage/Textures')
PROOF=Path('output/performance-assets')
OUT.mkdir(parents=True,exist_ok=True);PROOF.mkdir(parents=True,exist_ok=True)
requests=json.loads(Path('tmp/cotl-bundles.json').read_text(encoding='utf8'))
env=UnityPy.load(*[a['dependencies'][0] for a in requests])
need=re.compile('BoneKey|^Key$|ScienceMachine|FireMachine|Lantern|Lever|Telescope|Cannon',re.I)
records=[]
for obj in env.objects:
    if obj.type.name!='Sprite':continue
    data=obj.read()
    if not need.search(data.m_Name):continue
    try:
        image=data.image
        filename=re.sub('[^A-Za-z0-9_-]','_',data.m_Name)+'.png'
        image.save(PROOF/filename)
        records.append(dict(name=data.m_Name,file=filename,size=image.size,path_id=obj.path_id,assets_file=obj.assets_file.name))
    except Exception as exc: print('FAILED',data.m_Name,str(exc)[:200])
print(json.dumps(records,indent=2))
Path('tmp/cotl-asset-candidates.json').write_text(json.dumps(records,indent=2),encoding='utf8')
if records:
    columns=4; cell=240
    sheet=Image.new('RGB',(columns*cell,((len(records)+columns-1)//columns)*cell),'#c9cdd0');draw=ImageDraw.Draw(sheet)
    for i,r in enumerate(records):
        image=Image.open(PROOF/r['file']).convert('RGBA');image.thumbnail((220,200))
        x=(i%columns)*cell+(cell-image.width)//2;y=(i//columns)*cell+18
        sheet.paste(image,(x,y),image);draw.text(((i%columns)*cell+5,(i//columns)*cell+220),r['name'][:32],fill='black')
    sheet.save(PROOF/'candidates.png')

# Preserve the original pixels; white blockout and key accents belong to materials.
selected = [('BoneKey', 'Key.png', 1), ('Icon_DST_ScienceMachine', 'Projector.png', 0)]
sources = []
for source_name, destination, request_index in selected:
    record = next(r for r in records if r['name'] == source_name)
    shutil.copy2(PROOF / record['file'], OUT / destination)
    asset_image = Image.open(OUT / destination).convert('RGBA')
    sources.append({
        'file': destination,
        'source_game': 'Cult of the Lamb',
        'source_sprite': source_name,
        'source_bundle': requests[request_index]['dependencies'][0],
        'serialized_file': record['assets_file'],
        'path_id': str(record['path_id']),
        'size': record['size'],
        'alpha_bounds': asset_image.getchannel('A').getbbox(),
        'sha256': hashlib.sha256((OUT / destination).read_bytes()).hexdigest(),
        'processing': 'UnityPy Sprite.image atlas extraction only. Original source colors and alpha retained; no drawing or recoloring.',
        'suggested_pixels_per_unit': 100,
        'suggested_pivot': [0.5, 0.5] if destination == 'Key.png' else [0.5, 0.0],
    })
provenance = OUT.parent / 'Sources.json'
existing = json.loads(provenance.read_text(encoding='utf8')) if provenance.exists() else []
replaced = {source['file'] for source in sources}
sources = [source for source in existing if source.get('file') not in replaced] + sources
provenance.write_text(json.dumps(sources, ensure_ascii=False, indent=2), encoding='utf8')
print('FINAL_ASSETS', json.dumps(sources, ensure_ascii=False))
