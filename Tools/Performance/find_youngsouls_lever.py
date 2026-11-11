from pathlib import Path
import sys, re, json
sys.path.insert(0,str(Path('tmp/camera-research').resolve()))
import unity_camera_probe as probe

root=Path(r'D:/SteamLibrary/steamapps/common/Young Souls/YoungSouls_Data')
found=[]
for file in sorted(root.glob('level*')):
    if file.suffix: continue
    data=file.read_bytes()
    if not re.search(b'lever|levier',data,re.I): continue
    a=probe.asset(file)
    byid={o['pathID']:o for o in a['objects']}
    for o in a['objects']:
        if o['type']['classID']!=1: continue
        g=probe.gameobject(o['data'])
        if not re.search('lever|levier',g['name'],re.I): continue
        record=dict(file=file.name,id=o['pathID'],name=g['name'],components=[])
        for fi, pid in g['components']:
            c=byid.get(pid)
            if c: record['components'].append(dict(id=pid,type=c['type']['classID'],raw=c['data'].hex()))
        found.append(record)
        print(file.name,o['pathID'],g['name'],[(c['id'],c['type']) for c in record['components']])
    if len(found)>15:break
Path('tmp/youngsouls-levers.json').write_text(json.dumps(found,indent=2),encoding='utf8')
