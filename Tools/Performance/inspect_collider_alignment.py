from pathlib import Path
import re, json
import numpy as np

scene=Path('Assets/Scenes/Palworks_PaperRoom.unity').read_text(encoding='utf-8')
docs={}
types={}
for match in re.finditer(r'^--- !u!(\d+) &(\d+)\s*\n(.*?)(?=^--- !u!|\Z)',scene,re.M|re.S):
    cls,ident,body=match.groups()
    def value(raw):
        raw=raw.strip()
        if raw.startswith('{'):
            return {a.strip():value(b) for a,b in (item.split(':',1) for item in raw.strip('{}').split(','))}
        try: return float(raw) if any(c in raw for c in '.eE') else int(raw)
        except ValueError: return raw.strip('"')
    data={a:value(b) for a,b in re.findall(r'^  (\w+): (.*)$',body,re.M)}
    if not data: continue
    docs[int(ident)]=data
    types[int(ident)]=int(cls)
gos={k:v for k,v in docs.items() if types[k]==1}
trans={k:v for k,v in docs.items() if types[k]==4}
go_trans={v['m_GameObject']['fileID']:k for k,v in trans.items()}
def matrix(ident):
    t=trans[ident]; p=t['m_LocalPosition']; q=t['m_LocalRotation']; s=t['m_LocalScale']
    x,y,z,w=(q[n] for n in ('x','y','z','w'))
    m=np.eye(4)
    m[:3,:3]=np.array([[1-2*y*y-2*z*z,2*x*y-2*z*w,2*x*z+2*y*w],
        [2*x*y+2*z*w,1-2*x*x-2*z*z,2*y*z-2*x*w],
        [2*x*z-2*y*w,2*y*z+2*x*w,1-2*x*x-2*y*y]])@np.diag([s['x'],s['y'],s['z']])
    m[:3,3]=[p['x'],p['y'],p['z']]
    parent=t['m_Father']['fileID']
    return matrix(parent)@m if parent in trans else m
def path(ident):
    t=trans[ident]; name=gos[t['m_GameObject']['fileID']]['m_Name']; parent=t['m_Father']['fileID']
    return path(parent)+'/'+name if parent in trans else name
def coords(value): return np.array([value['x'],value['y'],value['z'],1])
sprites=[]
for ident,doc in docs.items():
    if types[ident]!=212: continue
    tr=go_trans[doc['m_GameObject']['fileID']]
    sprites.append((path(tr),tr,doc))
for ident,doc in docs.items():
    if types[ident] not in (65,64,135,136,143): continue
    tr=go_trans[doc['m_GameObject']['fileID']]
    name=path(tr)
    center=(matrix(tr)@coords(doc.get('m_Center',{'x':0,'y':0,'z':0})))[:3]
    associated=[s for s in sprites if s[0].startswith(name+'/')]
    if name.endswith('Collider'):
        associated += [s for s in sprites if s[0]==name[:-8]]
    row={'path':name,'type':types[ident],'active':gos[doc['m_GameObject']['fileID']].get('m_IsActive'),'enabled':doc.get('m_Enabled'),'trigger':doc.get('m_IsTrigger'),'center':center.round(3).tolist(),'size':doc.get('m_Size'),'visuals':[]}
    for sp,st,sd in associated:
        row['visuals'].append({'path':sp,'position':matrix(st)[:3,3].round(3).tolist(),'local':trans[st]['m_LocalPosition'],'sprite':sd.get('m_Sprite'),'enabled':sd.get('m_Enabled'),'active':gos[sd['m_GameObject']['fileID']].get('m_IsActive')})
    print(json.dumps(row,ensure_ascii=False))
