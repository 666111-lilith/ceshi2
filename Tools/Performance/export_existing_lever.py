"""Reuse an existing COTL wheel control sprite without drawing or compositing."""
from pathlib import Path
import hashlib, json, shutil
from PIL import Image

proof = Path('output/performance-assets/lever-candidates')
out = Path('Assets/Art/Stage/Textures/Lever.png')
record = next(r for r in json.loads(Path('tmp/lever-candidates.json').read_text(encoding='utf8')) if r['name'] == 'Icon_DST_AlchemyEngine')
shutil.copy2(proof / record['file'], out)
image = Image.open(out).convert('RGBA')
source = {
    'file': out.name,
    'source_game': 'Cult of the Lamb',
    'source_sprite': record['name'],
    'source_bundle': r'D:\SteamLibrary\steamapps\common\Cult of the Lamb\Cult Of The Lamb_Data\StreamingAssets\aa\StandaloneWindows64\cb6c235e6b422e75dcbb560045e4e105.bundle',
    'serialized_file': record['serialized_file'],
    'path_id': record['path_id'],
    'size': list(image.size),
    'alpha_bounds': image.getchannel('A').getbbox(),
    'sha256': hashlib.sha256(out.read_bytes()).hexdigest(),
    'processing': 'UnityPy Sprite.image atlas extraction only. Original source colors and alpha retained. No drawing, recoloring, or compositing.',
    'role': 'Existing wheel-operated mechanism used as the performance control. This is not an original pull-lever sprite.',
    'suggested_pixels_per_unit': 100,
    'suggested_pivot': [0.5, 0.0],
}
provenance = out.parent.parent / 'Sources.json'
entries = json.loads(provenance.read_text(encoding='utf8')) if provenance.exists() else []
entries = [e for e in entries if e.get('file') != out.name] + [source]
provenance.write_text(json.dumps(entries, ensure_ascii=False, indent=2), encoding='utf8')
print(json.dumps(source, ensure_ascii=False, indent=2))
