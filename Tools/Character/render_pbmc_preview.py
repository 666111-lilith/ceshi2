from pathlib import Path
import math, struct, sys
from functools import lru_cache
import numpy as np
from PIL import Image, ImageDraw
from pbmc_probe import parse, clip

TEXTURES = Path(r'D:/赛博仓鼠/游戏类/202611皮影/youngsoulsref/Texture2D')
OUTPUT=Path(__file__).parent
FILES=['Jenn.swf.swf','Jenn_NoWeaponMoveSet.swf.swf','SceneBus.swf.swf']
OUTFIT_FILES = [
    '_ItemHairJenn01.swf.swf',
    '_ItemShirtJacket01.swf.swf',
    '_ItemShirtJacket01_girl.swf.swf',
    '_ItemPantsShorty01.swf.swf',
    '_ItemBootsSneakers01.swf.swf',
    '_NakedGlove_girl.swf.swf',
    'SwordJennFlashForward_Weapons.swf.swf',
    'ShieldJennsShield_Shields.swf.swf',
]
ASSETS={}

def load_asset(f):
    if f in ASSETS: return
    h,s,rs,_,_=parse(f)
    ASSETS[f]={'strings':s,'records':{r['id']:r for r in rs},'names':{r['name']:r for r in rs if r['name']}}

for f in FILES + OUTFIT_FILES: load_asset(f)
OUTFIT = {name: (f, rec['id']) for f in OUTFIT_FILES
          for name, rec in ASSETS[f]['names'].items() if rec['tag'] == 107}

def unsided(name):
    for side in ('Left', 'Right'):
        if name.startswith(side): return name[len(side):]
    return name

def resolve_costume(item, asset):
    """Resolve authored costume sockets; the motion always comes from the clip."""
    name = unsided(item['name'])
    if name in OUTFIT:
        return OUTFIT[name]
    # These are the underlying body/guide meshes covered by this outfit.
    if name in {
        'Arm01', 'ForeArm01', 'Chest01', 'Chest02', 'Chest03', 'Belly01',
        'Pelvis01', 'Pelvis02', 'Pelvis03', 'Thigh01',
        'Tibia01', 'Tibia02', 'Tibia03', 'Tibia04',
    }:
        return None
    # An unassigned equipment socket must not expose its coloured placeholder.
    if name.startswith(('Helmet', 'Epaulette', 'Bag', 'Scarf', 'GloveWrist')):
        return None
    rec = ASSETS[asset]['records'].get(item['id'])
    if rec and rec['tag'] == 107 and struct.unpack_from('<I',rec['payload'])[0]:
        return asset, item['id']
    # Idle's face is an authored nested timeline rather than three imported clips.
    if name.startswith('instance') and rec and rec['tag'] == 107:
        return asset, item['id']
    return None

@lru_cache(None)
def frames(asset, sid): return clip(ASSETS[asset]['records'][sid])

@lru_cache(None)
def bitmap(asset,sid):
    p=ASSETS[asset]['records'][sid]['payload']
    kind,x,y,w,h,lx,ly,lw,lh,tex,scale=struct.unpack_from('<I8fIf',p)
    texture=ASSETS[asset]['strings'][tex]
    atlas=Image.open(TEXTURES/(asset[:-4]+'_'+texture+'.png')).convert('RGBA')
    crop=atlas.crop((round(x),round(y),round(x+w),round(y+h)))
    return crop,(lx,ly,lw,lh)

def matrix(t):
    a,b,c,d,x,y=t[:6]
    return np.array([[a,c,x],[b,d,y],[0,0,1]],dtype=float)

def leaves(asset,sid,frame,mat=None,alpha=1,level=0):
    if level>8: return []
    if mat is None: mat=np.eye(3)
    rec=ASSETS[asset]['records'][sid]
    if rec['tag']==102:
        return [(asset,sid,mat,alpha)]
    if rec['tag']!=107: return []
    imported=struct.unpack_from('<I',rec['payload'])[0]
    if imported:
        other=ASSETS[FILES[0]]['names'].get(rec['name'])
        return leaves(FILES[0],other['id'],frame,mat,alpha,level+1) if other else []
    fr=frames(asset,sid)
    if not fr: return []
    out=[]
    for item in fr[frame%len(fr)]:
        t=item['transform']; opacity=alpha*t[-1]
        if opacity<=0.001 or not item['visible']: continue
        childasset=asset; child=item['id']
        if level == 0 and asset.startswith('Jenn_'):
            target = resolve_costume(item, asset)
            if target is None: continue
            childasset, child = target
        out.extend(leaves(childasset,child,frame,mat@matrix(t),opacity,level+1))
    return out

def fit_screen_clips(clips):
    points=[]
    for asset, name, n in clips:
        for f in range(n):
            for file,sid,mat,alpha in leaves(asset,ASSETS[asset]['names'][name]['id'],f):
                crop,(lx,ly,lw,lh)=bitmap(file,sid)
                for x,y in ((0,0),(lw,0),(lw,lh),(0,lh)):
                    points.append((mat@np.array([x,y,1]))[:2])
    lo=np.min(points,axis=0); hi=np.max(points,axis=0)
    scale=min(400/(hi[0]-lo[0]),390/(hi[1]-lo[1]))
    return np.array([[scale,0,250-scale*(lo[0]+hi[0])/2],[0,scale,255-scale*(lo[1]+hi[1])/2],[0,0,1]],dtype=float)

def fit_screen(name,asset,n):
    return fit_screen_clips([(asset,name,n)])

def render(name,frame,asset,screen):
    rec=ASSETS[asset]['names'][name]
    output=Image.new('RGBA',(500,500),(225,229,232,255))
    ls=leaves(asset,rec['id'],frame)
    for file,sid,mat,alpha in ls:
        crop,(lx,ly,lw,lh)=bitmap(file,sid)
        local=np.array([[lw/crop.width,0,0],[0,lh/crop.height,0],[0,0,1]])
        forward=screen@mat@local
        inv=np.linalg.inv(forward)
        transformed=crop.transform(output.size,Image.Transform.AFFINE,tuple(inv[:2].flat),Image.Resampling.BICUBIC)
        if alpha<1: transformed.putalpha(transformed.getchannel('A').point(lambda a:round(a*alpha)))
        output.alpha_composite(transformed)
    draw=ImageDraw.Draw(output)
    draw.text((15,15),name+' | frame '+str(frame+1),fill=(45,53,64))
    draw.text((15,475),'Reconstructed from exported PBMC data',fill=(75,85,100))
    return output.convert('RGB')

if __name__=='__main__':
    name=sys.argv[1] if len(sys.argv)>1 else 'Walk_noWeapon'
    asset=sys.argv[2] if len(sys.argv)>2 else FILES[1]
    load_asset(asset)
    n=len(frames(asset,ASSETS[asset]['names'][name]['id']))
    screen=fit_screen(name,asset,n)
    rendered=[render(name,f,asset,screen) for f in range(n)]
    rendered[0].save(OUTPUT/(name+'.png'))
    rendered[0].save(OUTPUT/(name+'.gif'),save_all=True,append_images=rendered[1:],duration=42,loop=0)
    contact=Image.new('RGB',(1500,1000))
    for index,fr in enumerate([0,n//5,2*n//5,3*n//5,4*n//5,n-1]): contact.paste(rendered[fr],((index%3)*500,(index//3)*500))
    contact.save(OUTPUT/(name+'-contact.png'))
    print('rendered',name,n,'frames',OUTPUT/(name+'.gif'))
