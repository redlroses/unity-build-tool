import sys
import os
import re
import json
import hashlib
import subprocess
import tempfile

EXCLUDED_ROOT_DIRS = {'images'}
EXCLUDED_ROOT_FILES = {'package.json', 'license.md', 'license'}
ROOT_GUID = '8f7e2d9a3b4c5d6e7f8a9b0c1d2e3f4a'


def get_guid(meta_path):
    with open(meta_path, 'r', encoding='utf-8', errors='ignore') as f:
        content = f.read()
    m = re.search(r'guid:\s*([0-9a-fA-F]{32})', content)
    return m.group(1) if m else None


def make_guid(key):
    return hashlib.md5(f'com.redliketeam.buildtool:{key}'.encode('utf-8')).hexdigest()


def make_folder_meta(guid):
    return (
        f'fileFormatVersion: 2\n'
        f'guid: {guid}\n'
        f'folderAsset: yes\n'
        f'DefaultImporter:\n'
        f'  externalObjects: {{}}\n'
        f'  userData: \n'
        f'  assetBundleName: \n'
        f'  assetBundleVariant: \n'
    ).encode('utf-8')


def make_default_meta(guid):
    return (
        f'fileFormatVersion: 2\n'
        f'guid: {guid}\n'
        f'DefaultImporter:\n'
        f'  externalObjects: {{}}\n'
        f'  userData: \n'
        f'  assetBundleName: \n'
        f'  assetBundleVariant: \n'
    ).encode('utf-8')


def make_text_meta(guid):
    return (
        f'fileFormatVersion: 2\n'
        f'guid: {guid}\n'
        f'TextScriptImporter:\n'
        f'  externalObjects: {{}}\n'
        f'  userData: \n'
        f'  assetBundleName: \n'
        f'  assetBundleVariant: \n'
    ).encode('utf-8')


def load_package_manifest(source_dir):
    manifest_path = os.path.join(source_dir, 'package.json')
    if not os.path.isfile(manifest_path):
        return {}
    with open(manifest_path, 'r', encoding='utf-8') as f:
        return json.load(f)


def collect_entries(
    source_dir,
    dest_prefix,
    root_guid=None,
    root_meta_bytes=None,
    exclude_root_dirs=None,
    exclude_root_files=None,
):
    entries = []
    excluded_dirs = set(exclude_root_dirs or ())
    excluded_files = set(exclude_root_files or ())

    if root_guid and root_meta_bytes is not None:
        entries.append((root_guid, dest_prefix, root_meta_bytes, None))

    for dirpath, dirnames, filenames in os.walk(source_dir):
        rel_dir = os.path.relpath(dirpath, source_dir).replace('\\\\', '/').replace('\\', '/')
        dirnames[:] = sorted(
            d for d in dirnames
            if not d.endswith('~')
            and not d.startswith('.')
            and not (rel_dir == '.' and d.lower() in excluded_dirs)
        )

        if rel_dir != '.':
            dir_meta = dirpath + '.meta'
            if os.path.exists(dir_meta):
                guid = get_guid(dir_meta)
                if guid:
                    with open(dir_meta, 'rb') as f:
                        meta_bytes = f.read()
                    target_path = f'{dest_prefix}/{rel_dir}'.replace('\\', '/')
                    entries.append((guid, target_path, meta_bytes, None))

        for f in sorted(filenames):
            if f.endswith('.meta') or f.startswith('.'):
                continue
            if rel_dir == '.' and f.lower() in excluded_files:
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

    return entries


def write_unitypackage(entries, output_pkg):
    seen_guids = set()
    for guid, target_path, _, _ in entries:
        if not guid:
            raise ValueError(f'Missing GUID for entry: {target_path}')
        if guid in seen_guids:
            raise ValueError(f'Duplicate GUID {guid} for entry: {target_path}')
        seen_guids.add(guid)

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


def collect_sample_entries(source_dir, dest_prefix):
    manifest = load_package_manifest(source_dir)
    package_display_name = manifest.get('displayName') or 'Build Tool'
    package_version = manifest.get('version') or '1.0.0'
    samples = manifest.get('samples') or []
    entries = []
    added_samples_folder = False

    with tempfile.TemporaryDirectory() as tmp_dir:
        for sample in samples:
            rel_path = (sample.get('path') or '').replace('\\', '/').strip('/')
            if not rel_path:
                continue
            sample_src = os.path.join(source_dir, *rel_path.split('/'))
            if not os.path.isdir(sample_src):
                continue

            if not added_samples_folder:
                samples_dir_target = f'{dest_prefix}/Samples'
                samples_dir_guid = make_guid(samples_dir_target)
                entries.append((
                    samples_dir_guid,
                    samples_dir_target,
                    make_folder_meta(samples_dir_guid),
                    None,
                ))
                added_samples_folder = True

            folder_name = os.path.basename(sample_src)
            sample_display_name = sample.get('displayName') or folder_name
            sample_dest_prefix = f'Assets/Samples/{package_display_name}/{package_version}/{sample_display_name}'

            sample_root_meta_path = sample_src + '.meta'
            sample_root_guid = get_guid(sample_root_meta_path) if os.path.isfile(sample_root_meta_path) else None
            if sample_root_guid:
                with open(sample_root_meta_path, 'rb') as f:
                    sample_root_meta_bytes = f.read()
            else:
                sample_root_guid = make_guid(sample_dest_prefix)
                sample_root_meta_bytes = make_folder_meta(sample_root_guid)

            sample_entries = collect_entries(
                sample_src,
                sample_dest_prefix,
                root_guid=sample_root_guid,
                root_meta_bytes=sample_root_meta_bytes,
            )

            inner_pkg_path = os.path.join(tmp_dir, f'{folder_name}.unitypackage')
            write_unitypackage(sample_entries, inner_pkg_path)
            with open(inner_pkg_path, 'rb') as pf:
                pkg_bytes = pf.read()

            pkg_target = f'{dest_prefix}/Samples/{folder_name}.unitypackage'
            pkg_guid = make_guid(pkg_target)
            entries.append((pkg_guid, pkg_target, make_default_meta(pkg_guid), pkg_bytes))

            readme_src = os.path.join(sample_src, 'README.md')
            if os.path.isfile(readme_src):
                with open(readme_src, 'rb') as rf:
                    readme_bytes = rf.read()
                readme_target = f'{dest_prefix}/Samples/{folder_name}_README.md'
                readme_guid = make_guid(readme_target)
                entries.append((readme_guid, readme_target, make_text_meta(readme_guid), readme_bytes))

    return entries


def pack(source_dir, output_pkg, dest_prefix='Assets/BuildTool'):
    entries = collect_entries(
        source_dir,
        dest_prefix,
        root_guid=ROOT_GUID,
        root_meta_bytes=make_folder_meta(ROOT_GUID),
        exclude_root_dirs=EXCLUDED_ROOT_DIRS,
        exclude_root_files=EXCLUDED_ROOT_FILES,
    )
    entries.extend(collect_sample_entries(source_dir, dest_prefix))
    write_unitypackage(entries, output_pkg)


if __name__ == '__main__':
    src = sys.argv[1] if len(sys.argv) > 1 else 'Packages/com.redliketeam.buildtool'
    out = sys.argv[2] if len(sys.argv) > 2 else 'BuildTool.unitypackage'
    pack(src, out)
