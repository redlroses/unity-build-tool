import sys
import os
import re
import subprocess
import tempfile

def get_guid(meta_path):
    with open(meta_path, 'r', encoding='utf-8', errors='ignore') as f:
        content = f.read()
    m = re.search(r'guid:\s*([0-9a-fA-F]{32})', content)
    return m.group(1) if m else None

def pack(source_dir, output_pkg, dest_prefix='Assets/BuildTool'):
    entries = []

    root_meta = '''fileFormatVersion: 2
guid: 8f7e2d9a3b4c5d6e7f8a9b0c1d2e3f4a
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
'''
    entries.append(('8f7e2d9a3b4c5d6e7f8a9b0c1d2e3f4a', dest_prefix, root_meta.encode('utf-8'), None))

    for dirpath, dirnames, filenames in os.walk(source_dir):
        dirnames[:] = [d for d in dirnames if not d.endswith('~') and not d.startswith('.')]
        rel_dir = os.path.relpath(dirpath, source_dir).replace('\\\\', '/').replace('\\', '/')
        
        if rel_dir != '.':
            dir_meta = dirpath + '.meta'
            if os.path.exists(dir_meta):
                guid = get_guid(dir_meta)
                with open(dir_meta, 'rb') as f:
                    meta_bytes = f.read()
                target_path = f'{dest_prefix}/{rel_dir}'.replace('\\', '/')
                entries.append((guid, target_path, meta_bytes, None))

        for f in filenames:
            if f.endswith('.meta') or f.startswith('.'):
                continue
            full_path = os.path.join(dirpath, f)
            meta_path = full_path + '.meta'
            if not os.path.exists(meta_path):
                continue
            guid = get_guid(meta_path)
            if not guid:
                continue
            with open(meta_path, 'rb') as mf:
                meta_bytes = mf.read()
            with open(full_path, 'rb') as af:
                asset_bytes = af.read()
            
            rel_file = os.path.relpath(full_path, source_dir).replace('\\\\', '/').replace('\\', '/')
            target_path = f'{dest_prefix}/{rel_file}'.replace('\\', '/')
            entries.append((guid, target_path, meta_bytes, asset_bytes))

    output_abs = os.path.abspath(output_pkg)
    with tempfile.TemporaryDirectory() as stage_dir:
        guid_dirs = []
        for guid, target_path, meta_bytes, asset_bytes in entries:
            entry_dir = os.path.join(stage_dir, guid)
            os.makedirs(entry_dir, exist_ok=True)
            guid_dirs.append(guid)

            with open(os.path.join(entry_dir, 'pathname'), 'wb') as f:
                f.write(target_path.encode('utf-8'))

            with open(os.path.join(entry_dir, 'asset.meta'), 'wb') as f:
                f.write(meta_bytes)

            if asset_bytes is not None:
                with open(os.path.join(entry_dir, 'asset'), 'wb') as f:
                    f.write(asset_bytes)

        subprocess.run(
            ['tar', '--format=ustar', '-czf', output_abs, '-C', stage_dir, *guid_dirs],
            check=True,
        )

if __name__ == '__main__':
    src = sys.argv[1] if len(sys.argv) > 1 else 'Packages/com.redliketeam.buildtool'
    out = sys.argv[2] if len(sys.argv) > 2 else 'BuildTool.unitypackage'
    pack(src, out)
