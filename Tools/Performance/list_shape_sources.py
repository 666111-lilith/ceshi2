from pathlib import Path
import json,re
root=Path(r'D:/SteamLibrary/steamapps/common/Cult of the Lamb/Cult Of The Lamb_Data/StreamingAssets/aa')
catalog=json.loads((root/'catalog.json').read_text(encoding='utf-8-sig'))
pattern=re.compile(r'lever|handle|hammer|shovel|spade|pickaxe|fishing|rake|torch|axe|tool|staff|signpost|spear',re.I)
paths=[p for p in catalog['m_InternalIds'] if pattern.search(p) and ('/Art/' in p or '.spriteatlas' in p)]
Path('tmp/shape-source-paths.json').write_text(json.dumps(paths,ensure_ascii=False,indent=2),encoding='utf8')
print('\n'.join(paths[:180]))
print('TOTAL',len(paths))
