from pathlib import Path
import json,shutil,hashlib
folder=Path('output/performance-assets/lever-shapes')
record=next(r for r in json.loads((folder/'candidates.json').read_text()) if r['name']=='FishingRod')
request=next(r for r in json.loads((folder/'bundles.json').read_text()) if r['asset'].endswith('/FishingRod.png'))
art=Path('Assets/Art/Stage')
target=art/'Textures/LeverShape.png'
shutil.copy2(folder/record['file'],target)
source=dict(file=target.name,source_game='Cult of the Lamb',source_sprite=record['name'],source_bundle=request['bundle'],serialized_file=record['serialized_file'],path_id=record['path_id'],size=record['size'],sha256=hashlib.sha256(target.read_bytes()).hexdigest(),processing='Original FishingRod sprite extracted unchanged; reused for pivot and angled handle silhouette. No drawing, compositing, or recoloring of source pixels.')
path=art/'Sources.json'
sources=json.loads(path.read_text(encoding='utf8'))
sources=[r for r in sources if r.get('file')!=target.name]+[source]
path.write_text(json.dumps(sources,ensure_ascii=False,indent=2),encoding='utf8')
print(source['source_sprite'],source['size'],source['sha256'])
