#!/usr/bin/env bash
# Build/provisioning only. The application never downloads weights.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
python3 - "$root" "${1:-$root/models}" <<'PY'
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import urllib.request

root, destination = map(Path, sys.argv[1:])
model = json.loads((root / 'models/manifest.json').read_text())['models'][0]
destination.mkdir(parents=True, exist_ok=True)
target = destination / model['file']
def valid(path):
    if not path.is_file() or path.stat().st_size != model['size']:
        return False
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest() == model['sha256']
if valid(target):
    print('Model already verified: ' + model['name'])
    sys.exit(0)
fd, staging = tempfile.mkstemp(prefix='.model-', suffix='.tmp', dir=destination)
try:
    with os.fdopen(fd, 'wb') as output, urllib.request.urlopen(model['sourceUrl'], timeout=120) as source:
        while chunk := source.read(1024 * 1024):
            output.write(chunk)
        output.flush()
        os.fsync(output.fileno())
    if not valid(Path(staging)):
        raise RuntimeError('Model size or SHA-256 mismatch; download discarded.')
    os.replace(staging, target)
    print('Model verified: ' + model['name'])
finally:
    Path(staging).unlink(missing_ok=True)
PY
