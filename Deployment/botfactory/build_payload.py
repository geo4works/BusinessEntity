#!/usr/bin/env python3
"""Freeze the current app sources, including uncommitted work, without local secrets."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'artifacts' / 'botfactory-businessentity'
SOURCE_ROOTS = {'BusinessEntity', 'BlazorServerWebLogger', 'BusinessEntity.Resources'}
ROOT_FILES = {'global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props'}
EXCLUDED = {'bin', 'obj', '.objtmp', 'node_modules', '.git', '.vs', 'logs', 'storage', 'identity-keys', '__pycache__'}


def allowed(name):
    """Use explicit source roots; dotenv, certificate and runtime files never enter delivery."""
    path = Path(name)
    return ((path.parts[0] in SOURCE_ROOTS or name in ROOT_FILES)
            and not any(p in EXCLUDED for p in path.parts)
            and not path.name.startswith('.env')
            and path.name not in {'identity.initial.json', 'identity-secrets.env', 'appsettings.Development.json'}
            and path.suffix.lower() not in {'.pfx', '.p12', '.key', '.pem', '.crt', '.log', '.dll', '.exe'})


def build():
    """Create an immutable source snapshot and a manifest for the bot's pinned delivery."""
    OUTPUT.mkdir(parents=True, exist_ok=True)
    # Existing Git rules hide BackupRestoreMiniApp; the build uses actual source files.
    names = []
    for source_root in sorted(SOURCE_ROOTS):
        for directory, subdirs, filenames in os.walk(ROOT / source_root, followlinks=False):
            subdirs[:] = [name for name in subdirs if name not in EXCLUDED]
            for name in filenames:
                names.append((Path(directory) / name).relative_to(ROOT).as_posix())
    names.extend(name for name in ROOT_FILES if (ROOT / name).is_file())
    files = {}
    archive = OUTPUT / 'source.zip'
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as output:
        for name in sorted(set(names)):
            if not name or not allowed(name):
                continue
            path = ROOT / name
            if not path.exists():
                continue
            if path.is_symlink():
                raise ValueError('Source symlink is unsupported: ' + name)
            data = path.read_bytes()
            files[name] = hashlib.sha256(data).hexdigest()
            # Fixed metadata makes an unchanged source snapshot reproducible.
            info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            output.writestr(info, data)
    required = ['BusinessEntity/Dockerfile', 'BusinessEntity/BusinessEntity.csproj',
                'BlazorServerWebLogger/Dockerfile', 'BusinessEntity.Resources/DataFill_01.txt']
    if not all(name in files for name in required):
        raise ValueError('Incomplete source snapshot')
    for name, source in [('host_install.py', 'host_install.py'), ('request.json', 'install-request.example.json')]:
        shutil.copyfile(ROOT / 'Deployment/botfactory' / source, OUTPUT / name)
    manifest = {'schemaVersion': 1, 'baseCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT).decode().strip(),
                'sourceKind': 'working-tree-snapshot', 'sourceFiles': files,
                'files': {name: hashlib.sha256((OUTPUT / name).read_bytes()).hexdigest()
                          for name in ('source.zip', 'host_install.py', 'request.json')}}
    (OUTPUT / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'output': str(OUTPUT), 'sourceFiles': len(files), 'sourceSha256': manifest['files']['source.zip']}))


if __name__ == '__main__':
    build()
