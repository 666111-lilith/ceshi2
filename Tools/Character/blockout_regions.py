"""Preserve the original face and hair regions in the neutral blockout.

Both clips mix flattened artwork and separate shapes. A part name is therefore
not a colour region: FullEyes15 includes white sclera, iris, lashes and eyebrows;
even several Idle Head_Detail textures contain more than one authored colour.
All head textures use this shared source-colour -> neutral palette, so a region
has the same shade whether it was baked into one bitmap or split into meshes.
Output is absolute linear grayscale. Source alpha and affine poses are untouched.
"""
from pathlib import Path
import json
import numpy as np
from PIL import Image


# Canonical colours identify authored regions, not output colours. Values are
# deliberately flat; there is no source RGB or source lighting in the output.
REGIONS = [
    ((255, 203, 194), .82),  # face
    ((201, 66, 95), .18),    # fringe
    ((192, 62, 95), .16),    # fringe second authored region
    ((198, 128, 148), .44),  # short side hair
    ((163, 75, 101), .16),   # side-hair marks
    ((238, 165, 166), .65),  # cheek
    ((238, 145, 146), .60),  # nose
    ((213, 123, 126), .51),  # nose underside
    ((209, 138, 146), .60),  # ear
    ((222, 154, 153), .67),  # eyelid (must not become a black eye)
    ((255, 255, 255), .98),  # eye white
    ((45, 76, 84), .075),   # iris
    ((45, 15, 24), .025),   # eyelashes and mouth
    ((140, 46, 68), .12),   # eyebrows
    ((51, 51, 51), .035),   # earring
]

HAIR_REGIONS = [
    ((138, 40, 61), .10),
    ((143, 41, 66), .115),
    ((150, 43, 69), .13),
    ((158, 45, 73), .15),
    ((167, 48, 81), .18),
    ((171, 49, 79), .19),
]


def write_regions(root: Path):
    data = json.loads((root / 'Data/JennAnimations.json').read_text(encoding='utf8'))
    destination = root / 'Textures/Blockout'
    destination.mkdir(exist_ok=True)
    head_bitmaps = set()
    for clip in data['clips']:
        for frame in clip['frames']:
            for draw in frame['parts']:
                name = data['parts'][draw['part']]['name']
                if any(key in name for key in ('Head', 'Hair', 'FullEyes', 'Mouth')):
                    head_bitmaps.add(draw['bitmap'])
    manifest = []
    for index, bitmap in enumerate(data['bitmaps']):
        if index not in head_bitmaps:
            continue
        source = np.asarray(Image.open(root / 'Textures' / bitmap['file']).convert('RGBA'))
        regions = HAIR_REGIONS if bitmap['source'].startswith('_ItemHair') else REGIONS
        canonical = np.array([color for color, _ in regions], dtype=np.float32)
        distance = ((source[:, :, None, :3].astype(np.float32) - canonical) ** 2).sum(axis=3)
        classes = distance.argmin(axis=2)
        grays = np.array([round(value * 255) for _, value in regions], dtype=np.uint8)
        shade = grays[classes]
        result = np.stack((shade, shade, shade, np.full_like(shade, 255)), axis=2)
        path = destination / (Path(bitmap['file']).stem + '_regions.png')
        Image.fromarray(result).save(path)
        opaque = source[:, :, 3] > 250
        manifest.append({'bitmap': index, 'source': bitmap['source'],
                         'symbolId': bitmap['symbolId'], 'file': path.name,
                         'linearGrayClasses': np.unique(shade[opaque]).tolist()})
    if len(manifest) != 21:
        raise RuntimeError('Expected 21 face/hair bitmaps across Idle and Walk, got ' + str(len(manifest)))
    (destination/'Regions.json').write_text(json.dumps({
        'encoding': 'absolute linear grayscale; original alpha remains in Shape texture',
        'palette': REGIONS, 'hairPalette': HAIR_REGIONS, 'bitmaps': manifest,
    },indent=2),encoding='utf8')
    print('Blockout regions:',len(manifest),'head/hair textures; shared anatomical palette across both clips.')


if __name__ == '__main__':
    write_regions(Path(__file__).resolve().parents[2] / 'Assets/Art/Characters/Jenn')
