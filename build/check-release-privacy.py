"""Inspect every outgoing file version and selected packages without printing matched secrets."""
import argparse
import io
import json
import re
import subprocess
import sys
import zipfile
from pathlib import Path

from publishing_common import ROOT, write_json

PATTERNS = {
    'private_path_or_steam_id': re.compile(r'(?:[A-Za-z]:[\\/](?:Users|C#WorkSpace|SteamLibrary)[\\/]|/Users/[A-Za-z0-9_. -]+/|/home/[A-Za-z0-9_.-]+/|7656119\d{10})'),
    'credential': re.compile(r'gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|-----BEGIN (?:RSA |OPENSSH |EC )?PRIVATE KEY|(?:api[_-]?key|access[_-]?token|password)\s*[:=]\s*["\x27][A-Za-z0-9+/=_-]{20,}', re.I),
    'possible_game_key': re.compile(r'(?i)(?<![a-f0-9])(?:0x)?[a-f0-9]{64}(?![a-f0-9])')
}


def inspect_bytes(label, data, findings, images):
    kinds = set()
    for encoding in ('utf-8', 'utf-16-le'):
        text = data.decode(encoding, errors='ignore')
        kinds.update(kind for kind, pattern in PATTERNS.items() if pattern.search(text))
    for kind in sorted(kinds):
        findings.append({'object': label, 'kind': kind})
    if Path(label).suffix.lower() in ('.png', '.jpg', '.jpeg', '.webp', '.gif'):
        images.append(label)
    if zipfile.is_zipfile(io.BytesIO(data)):
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            for member in archive.infolist():
                if not member.is_dir():
                    inspect_bytes(label + '!' + member.filename, archive.read(member), findings, images)


def inspect_outgoing(base, artifacts):
    def git(*args):
        return subprocess.check_output(['git', *args], cwd=ROOT)
    commits = git('rev-list', '--reverse', base + '..HEAD').decode().splitlines()
    findings, images, versions = [], [], 0
    for commit in commits:
        names = git('diff-tree', '-m', '--root', '--no-commit-id', '--no-renames', '--name-only',
                    '--diff-filter=ACMT', '-z', '-r', commit).decode('utf-8').split('\0')
        for name in dict.fromkeys(filter(None, names)):
            inspect_bytes(commit + ':' + name, git('show', commit + ':' + name), findings, images)
            versions += 1
    for artifact in artifacts:
        path = Path(artifact)
        inspect_bytes(str(path), path.read_bytes(), findings, images)
    return {'base': base, 'commits': commits, 'file_versions_inspected': versions,
            'artifacts': [str(p) for p in artifacts], 'findings': findings,
            'images_requiring_visual_review': images}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', default='origin/main')
    parser.add_argument('--artifact', action='append', default=[], help='Repeat for executables or archives; ZIP entries are inspected recursively.')
    parser.add_argument('--report', default='dist/publishing/privacy.json')
    args = parser.parse_args()
    report = inspect_outgoing(args.base, args.artifact)
    write_json(ROOT / args.report, report)
    print(json.dumps(report, indent=2, ensure_ascii=False))
    print('Inspect findings and screenshot contents before pushing. This scanner does not replace visual review.')
    return int(bool(report['findings']))


if __name__ == '__main__':
    sys.exit(main())
