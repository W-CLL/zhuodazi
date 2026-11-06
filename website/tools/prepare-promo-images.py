"""Create web copies without modifying the supplied promotional screenshots."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageOps
FILES = {
    '互动.png': 'hello', '互动猜歌词.png': 'lyrics', '小剧场.png': 'theater',
    '搭子来访.png': 'visit', '摸鱼模式.png': 'break', '桌宠大厅和其他人互动.png': 'hall',
    '桌宠性格满屏幕走跟随鼠标.png': 'personality', '答对收下分数.png': 'score',
    '答题互动.png': 'quiz', '自定义词包.png': 'words', '设置点外卖提醒.png': 'reminder',
}
def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    args = parser.parse_args()
    target = Path(__file__).resolve().parents[1] / 'source/images/promo'
    target.mkdir(parents=True, exist_ok=True)
    manifest = []
    for original, slug in FILES.items():
        source = args.source / original
        with Image.open(source) as image:
            image = ImageOps.exif_transpose(image).convert('RGB')
            image.thumbnail((1440, 1100), Image.Resampling.LANCZOS)
            output = target / f'{slug}.webp'
            image.save(output, 'WEBP', quality=88, method=6)
            manifest.append({'source': original, 'sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                             'file': output.name, 'width': image.width, 'height': image.height,
                             'originalBytes': source.stat().st_size, 'bytes': output.stat().st_size})
    (target / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f"{len(manifest)} images: {sum(x['originalBytes'] for x in manifest):,} -> {sum(x['bytes'] for x in manifest):,} bytes")
if __name__ == '__main__':
    main()
