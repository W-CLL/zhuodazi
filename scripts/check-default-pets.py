"""Check the shared default collection and the exact files copied into a package."""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def verify(directory):
    manifest = json.loads((ROOT / 'assets/default-pets/manifest.json').read_text(encoding='utf-8'))
    expected = {item['file']: item for item in manifest['files']}
    if len(expected) != 20:
        raise SystemExit('The default collection must contain 20 distinct pets')
    actual = {path.relative_to(directory).as_posix(): path for path in directory.rglob('*.gif')}
    if actual.keys() != expected.keys():
        raise SystemExit(f'Default pet files differ: missing={sorted(expected.keys()-actual.keys())}, extra={sorted(actual.keys()-expected.keys())}')
    for name, metadata in expected.items():
        data = actual[name].read_bytes()
        if data[:6] not in (b'GIF87a', b'GIF89a'):
            raise SystemExit(f'Invalid GIF: {name}')
        if len(data) != metadata['size'] or hashlib.sha256(data).hexdigest() != metadata['sha256']:
            raise SystemExit(f'Default pet content differs: {name}')
    print(f'Verified 20 default pets in {directory}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, default=ROOT / 'assets/default-pets')
    verify(parser.parse_args().root)
