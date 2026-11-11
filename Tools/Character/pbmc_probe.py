from pathlib import Path
import struct, collections, json, sys

ROOT = Path(r'D:/赛博仓鼠/游戏类/202611皮影/youngsoulsref/TextAsset')

class Reader:
    def __init__(self, data): self.data, self.p = data, 0
    def read(self, fmt):
        size=struct.calcsize('<'+fmt)
        if self.p+size>len(self.data):
            raise ValueError(f"Unexpected end of PBMC at {self.p}; need {size} bytes")
        v = struct.unpack_from('<'+fmt, self.data, self.p)
        self.p += size
        return v[0] if len(v)==1 else v
    def string(self):
        n=self.read('I')
        if self.p+n>len(self.data):
            raise ValueError(f"String exceeds PBMC boundary at {self.p}: {n}")
        s=self.data[self.p:self.p+n]; self.p+=n
        return s.decode('utf8')
    def tag(self):
        typ=self.read('H'); n=self.read('I')
        if self.p+n>len(self.data):
            raise ValueError(f"Tag {typ} exceeds PBMC boundary at {self.p}: {n}")
        s=self.data[self.p:self.p+n]; self.p+=n
        return typ,s

def clip(rec, debug=False):
    r=Reader(rec['payload']); imported=r.read('I'); unk=r.read('I'); count=r.read('I')
    if imported not in (0,1) or unk != 0:
        raise ValueError(f"Unsupported clip header {rec['name']}: {(imported,unk,count)}")
    frames=[]
    for frame in range(count):
        n=r.read('I'); items=[]
        for i in range(n):
            start=r.p
            prefix,op=r.read('2B')
            if prefix != 0:
                raise ValueError(f"{rec['name']}: unsupported prefix {prefix} at frame={frame}, item={i}, offset={start}")
            if op == 3:
                # Compact entry: this depth retains its preceding frame state.
                depth=r.read('i')
                previous=next((x for x in frames[-1] if x['depth']==depth),None) if frames else None
                if previous is None:
                    raise ValueError(f"{rec['name']}: retained depth {depth} has no previous state")
                items.append(dict(previous,start=start,retained=True))
                continue
            if op not in (0,2):
                raise ValueError(f"{rec['name']}: unsupported op={op} at frame={frame}, item={i}, offset={start}")
            subtype=r.read('B'); flags=(prefix,op,subtype)
            if subtype not in (0,2):
                raise ValueError(f"{rec['name']}: unsupported subtype={subtype} at frame={frame}, item={i}, offset={start}")
            sid,depth=r.read('ii'); transform=r.read('9f'); visible=r.read('B')
            ln=r.read('H')
            try:
                name=r.data[r.p:r.p+ln].decode('utf8')
            except UnicodeDecodeError as ex:
                raise ValueError(f"{rec['name']}: frame={frame} item={i}/{n} offset={start} flags={flags} name_length={ln} previous={items[-1:]}") from ex
            r.p+=ln
            extra=r.string()
            rot=r.read('f') if flags[1]==2 else None
            color=r.read('4B')
            items.append(dict(start=start,flags=flags,id=sid,depth=depth,transform=transform,visible=visible,name=name,extra=extra,rot=rot,color=color))
        end=r.read('6B')
        if any(end):
            raise ValueError(f"{rec['name']}: unsupported frame trailer at frame={frame}, offset={r.p-6}: {end}")
        if debug: print('frame',frame,n,'end',r.p,end,'first',items[:1])
        frames.append(items)
    if r.p!=len(r.data):
        raise ValueError(f"Clip size mismatch {rec['name']}: consumed {r.p} of {len(r.data)} bytes")
    return frames

def parse(name):
    b=ROOT.joinpath(name).read_bytes(); r=Reader(b)
    h=[r.read('I'),r.read('I'),r.string(),r.read('I')]
    if h != [3,8,'PBMC',8]:
        raise ValueError(f"Unsupported PBMC container header in {name}: {h}")
    tag,payload=r.tag()
    if tag != 755:
        raise ValueError(f"Unsupported initial PBMC tag in {name}: {tag}")
    strings=[r.string() for _ in range(r.read('I'))]
    n=r.read('I'); records=[]
    try:
        for i in range(n):
            start=r.p
            kind=r.read('I'); name=r.string(); rid=r.read('i'); tag,payload=r.tag()
            records.append(dict(start=start,kind=kind,name=name,id=rid,tag=tag,length=len(payload),payload=payload))
    except Exception as ex:
        raise ValueError(f"PBMC container record {i} at offset {start} failed in {name}") from ex
    # Observed footer: zero uint32 followed by a length-prefixed string. Its
    # semantic purpose is not yet known; MoveSet files have an empty string,
    # while Jenn/SceneBus contain sixteen ASCII zero characters.
    footer_flag=r.read('I')
    footer_text=r.string()
    if footer_flag != 0:
        raise ValueError(f"Unsupported PBMC footer flag in {name}: {footer_flag}")
    if name in ('Jenn_NoWeaponMoveSet.swf.swf','Jenn_SwordMoveSet.swf.swf') and footer_text:
        raise ValueError(f"Unexpected nonempty MoveSet footer in {name}")
    if r.p != len(b):
        raise ValueError(f"PBMC container trailing bytes in {name}: {len(b)-r.p}")
    return h,strings,records,r.p,len(b)

if __name__=='__main__':
    for name in sys.argv[1:]:
        h,s,rs,end,size=parse(name)
        print(name,h,s,len(rs),'END',end,size,'kinds',collections.Counter(r['kind'] for r in rs),'tags',collections.Counter(r['tag'] for r in rs))
        print(json.dumps([{k:v for k,v in r.items() if k!='payload'} for r in rs[:10]],ensure_ascii=False,indent=1))
        print('named clips',[(r['id'],r['name'],r['length']) for r in rs if any(x in r['name'].lower() for x in ('idle','walk','run','attack','jump','root'))][:100])
