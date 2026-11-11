import pathlib,struct,json,sys
ROOT=pathlib.Path('D:/SteamLibrary/steamapps/common/Cult of the Lamb/Cult Of The Lamb_Data')
class Reader:
    def __init__(self,b,p=0):self.b,self.p=b,p
    def read(self,fmt):
        v=struct.unpack_from('<'+fmt,self.b,self.p); self.p+=struct.calcsize('<'+fmt)
        return v[0] if len(v)==1 else v
    def align(self):self.p=(self.p+3)&~3
    def z(self):
        e=self.b.index(0,self.p);v=self.b[self.p:e].decode('utf8');self.p=e+1;return v
    def string(self):
        n=self.read('i');assert 0<=n<1000000
        v=self.b[self.p:self.p+n].decode('utf8');self.p+=n;self.align();return v
def asset(path):
    b=path.read_bytes();ms,fs,ver,offset=struct.unpack_from('>4I',b)
    pos=20
    if ver>=22:ms,fs,offset,unknown=struct.unpack_from('>IQQQ',b,20);pos=48
    assert fs==len(b),(fs,len(b),ver)
    r=Reader(b,pos);unity=r.z();platform=r.read('i');tree=r.read('B')
    assert tree==0,'type tree present'
    types=[]
    for _ in range(r.read('i')):
        cid,strip,script=r.read('iBh')
        if cid==114:r.p+=16
        r.p+=16
        types.append(cid)
    objects=[]
    for _ in range(r.read('i')):
        r.align();pid=r.read('q');start=r.read('q' if ver>=22 else 'I');size,ti=r.read('Ii')
        assert 0<=ti<len(types)
        objects.append(dict(pathID=pid,start=start+offset,size=size,type=types[ti],data=b[start+offset:start+offset+size]))
    scripts=[]
    for _ in range(r.read('i')):
        fi=r.read('i');r.align();scripts.append((fi,r.read('q')))
    exts=[]
    for _ in range(r.read('i')):
        r.z();r.p+=16;r.read('i');exts.append(r.z())
    return dict(path=str(path),unity=unity,objects=objects,externals=exts)
if __name__=='__main__':
    out=[]
    for file in ['resources.assets','globalgamemanagers.assets','sharedassets0.assets']:
        a=asset(ROOT/file);print(file,a['unity'],len(a['objects']))
        for o in a['objects']:
            if o['type'] not in [28,213,687078895]:continue
            try:name=Reader(o['data']).string()
            except:continue
            out.append(dict(file=file,pathID=o['pathID'],type=o['type'],name=name,size=o['size']))
    pathlib.Path('tmp/cotl-objects.json').write_text(json.dumps(out,indent=2),encoding='utf8')
    for o in out:
        if any(word in o['name'].lower() for word in ['key','machine','lantern','lever','camera','telescope','cannon']):print(o)
