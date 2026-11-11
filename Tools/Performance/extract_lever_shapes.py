from pathlib import Path
import base64,json,re,struct,sys
sys.path.insert(0,str(Path('tmp/unitypy-assets').resolve()))
import UnityPy
from PIL import Image,ImageDraw
ROOT=Path(r'D:/SteamLibrary/steamapps/common/Cult of the Lamb/Cult Of The Lamb_Data/StreamingAssets/aa')
OUT=Path('output/performance-assets/lever-shapes');OUT.mkdir(parents=True,exist_ok=True)
CAT=json.loads((ROOT/'catalog.json').read_text(encoding='utf-8-sig'))
ENT=base64.b64decode(CAT['m_EntryDataString']);BUC=base64.b64decode(CAT['m_BucketDataString'])
entries=[struct.unpack_from('<7i',ENT,4+i*28) for i in range(struct.unpack_from('<i',ENT)[0])]
buckets=[];position=4
for i in range(struct.unpack_from('<i',BUC)[0]):
    offset,count=struct.unpack_from('<2i',BUC,position);position+=8
    buckets.append(struct.unpack_from('<'+str(count)+'i',BUC,position));position+=count*4
wanted=re.compile(r'Toolshed_Atlas\.spriteatlas|FishingRod\.png|FirstEntrySignpost[1-4]\.png|SignPost\.png|D6_HammerRoom\.spriteatlas|Icon_Weapon_(Hammer|Axe)\.png',re.I)
requests=[]
for index,path in enumerate(CAT['m_InternalIds']):
    if not wanted.search(path):continue
    row=next((e for e in entries if e[0]==index and e[2]>=0),None)
    if row:
        dep=entries[buckets[row[2]][0]][0]
        bundle=ROOT/'StandaloneWindows64'/Path(CAT['m_InternalIds'][dep]).name
        if bundle.exists():requests.append(dict(asset=path,bundle=str(bundle)))
(OUT/'bundles.json').write_text(json.dumps(requests,indent=2),encoding='utf8')
print('OWNING BUNDLES',len({r['bundle'] for r in requests}),flush=True)
env=UnityPy.load(*sorted({r['bundle'] for r in requests}))
atlas_names=set()
for obj in env.objects:
    if obj.type.name!='SpriteAtlas':continue
    atlas=obj.read()
    if not re.search('Toolshed|HammerRoom',atlas.m_Name,re.I):continue
    print('ATLAS',atlas.m_Name,flush=True)
    for pointer in getattr(atlas,'m_PackedSprites',[]):
        try:atlas_names.add(pointer.read().m_Name)
        except Exception:pass
names=re.compile(r'toolshed|shovel|spade|broom|rake|pickaxe|hammer|axe|fishingrod|signpost|sign_post|wrench|spanner|plunger|staff|handle|crank',re.I)
records=[];seen=set()
for obj in env.objects:
    if obj.type.name!='Sprite':continue
    sprite=obj.read();name=sprite.m_Name
    if name in seen or (name not in atlas_names and not names.search(name)):continue
    seen.add(name)
    if re.search(r'Particle|Projectile|Slash|Charged|Controller|Effect|ScrollBar',name,re.I):continue
    try:
        img=sprite.image.convert('RGBA');box=img.getchannel('A').getbbox()
        if not box:continue
        w,h=box[2]-box[0],box[3]-box[1]
        if max(w,h)<32:continue
        filename=re.sub('[^A-Za-z0-9_-]','_',name)+'.png';img.save(OUT/filename)
        records.append(dict(name=name,file=filename,size=list(img.size),alpha_bounds=box,ratio=round(max(w,h)/max(1,min(w,h)),2),path_id=str(obj.path_id),serialized_file=obj.assets_file.name))
    except Exception as exc:print('FAIL',name,str(exc)[:80])
def rank(r):
    n=r['name'];score=0 if re.search('shovel|spade|broom|rake|pickaxe|fishingrod|signpost|handle|tool',n,re.I) else 5
    score+=8 if re.search('Godly|Legendary|Critical|Healing|Poison|Necromancy|Fervor',n,re.I) else 0
    score+=5 if r['ratio']<1.5 else 0
    return score,n
records.sort(key=rank)
(OUT/'candidates.json').write_text(json.dumps(records,indent=2),encoding='utf8')
print('CANDIDATES',len(records),[r['name'] for r in records],flush=True)
for start in range(0,min(len(records),36),12):
    page=records[start:start+12];cols=4;cell=230
    sheet=Image.new('RGB',(cols*cell,((len(page)+cols-1)//cols)*cell),'#bdc5c8');draw=ImageDraw.Draw(sheet)
    for i,r in enumerate(page):
        img=Image.open(OUT/r['file']).convert('RGBA');img.thumbnail((210,190))
        x=(i%cols)*cell+(cell-img.width)//2;y=(i//cols)*cell+8
        sheet.paste(img,(x,y),img);draw.text(((i%cols)*cell+5,(i//cols)*cell+207),r['name'][:32],fill='black')
    sheet.save(OUT/f'contact-{start//12+1}.png')
print('READY',str((OUT/'contact-1.png').resolve()),flush=True)
