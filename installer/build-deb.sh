#!/usr/bin/env bash
set -euo pipefail

# Run inside Ubuntu 22.04. Keep Linux bin/obj files off the Windows checkout.
repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
source /etc/os-release
if [[ "$ID" != ubuntu || "$VERSION_ID" != 22.04 || "$(dpkg --print-architecture)" != amd64 ]]; then
    echo 'This package targets Ubuntu 22.04 amd64. Build in that environment.' >&2
    exit 1
fi
if ! command -v dotnet >/dev/null && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export PATH="$HOME/.dotnet:$PATH"
fi
for tool in dotnet rsync python3 dpkg-deb dpkg-shlibdeps file; do
    command -v "$tool" >/dev/null || { echo "Missing build tool: $tool" >&2; exit 1; }
done
version=$(python3 - "$repo_root/Directory.Build.props" <<'PY'
import sys, xml.etree.ElementTree as ET
print(ET.parse(sys.argv[1]).findtext('./PropertyGroup/Version'))
PY
)
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Invalid version' >&2; exit 1; }
build_dir=$(mktemp -d /tmp/layperson-deb.XXXXXX)
source_dir="$build_dir/source"
publish_dir="$build_dir/publish"
stage="$build_dir/package"
output="$repo_root/artifacts/installer"
mkdir -p "$source_dir" "$output"
rsync -a --exclude=.git --exclude=.tools --exclude=.wix --exclude=artifacts \
    --exclude=bin --exclude=obj --exclude=TestResults "$repo_root/" "$source_dir/"
printf 'Linux build directory: %s\n' "$build_dir"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
cd "$source_dir"
dotnet test -c Release
dotnet publish src/LaypersonLogViewer.App/LaypersonLogViewer.App.csproj \
    -c Release -r linux-x64 --self-contained true -p:PublishTrimmed=false \
    -p:DebugType=None -p:DebugSymbols=false -o "$publish_dir"

python3 - "$publish_dir" "$source_dir" <<'PY'
import json, pathlib, re, shutil, sys
publish, source = map(pathlib.Path, sys.argv[1:])
config = json.loads((publish / 'LaypersonLogViewer.App.runtimeconfig.json').read_text())['runtimeOptions']
assert config.get('includedFrameworks') and not config.get('frameworks') and not config.get('framework'), 'Not self-contained'
for name in ('LaypersonLogViewer.App', 'libcoreclr.so', 'libhostfxr.so', 'libhostpolicy.so', 'System.Private.CoreLib.dll'):
    assert (publish / name).is_file(), f'Missing runtime: {name}'
assets = json.loads((source / 'src/LaypersonLogViewer.App/obj/project.assets.json').read_text())
paths = {v['path'] for v in assets['libraries'].values() if v.get('type') == 'package'}
paths.update('microsoft.netcore.app.runtime.linux-x64/' + f['version'] for f in config['includedFrameworks'])
for package in sorted(paths):
    for root in assets['packageFolders']:
        folder = pathlib.Path(root) / package
        if not folder.is_dir():
            continue
        for notice in folder.iterdir():
            if notice.is_file() and re.match(r'^(licen[sc]e|notice|third.party)', notice.name, re.I):
                target = publish / 'licenses' / package.replace('/', '-')
                target.mkdir(parents=True, exist_ok=True)
                shutil.copy2(notice, target / notice.name)
        break
PY

install -d "$stage/DEBIAN" "$stage/usr/lib/layperson-log-viewer" "$stage/usr/bin" \
    "$stage/usr/share/applications" "$stage/usr/share/doc/layperson-log-viewer/samples"
# Optional LTTng tracing is not used by this app and requires a legacy system ABI.
rsync -a --exclude=libcoreclrtraceptprovider.so "$publish_dir/" "$stage/usr/lib/layperson-log-viewer/"
cp "$source_dir/README.md" "$source_dir/docs/Спецификация.md" "$stage/usr/share/doc/layperson-log-viewer/"
cp "$source_dir/samples/"* "$stage/usr/share/doc/layperson-log-viewer/samples/"
cat > "$stage/usr/bin/layperson-log-viewer" <<'SH'
#!/bin/sh
exec /usr/lib/layperson-log-viewer/LaypersonLogViewer.App "$@"
SH
cat > "$stage/usr/share/applications/layperson-log-viewer.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Layperson Log Viewer
Name[ru]=Просмотр журналов Layperson
Comment=View and filter log files and calculate statistics
Exec=layperson-log-viewer
TryExec=layperson-log-viewer
Terminal=false
Categories=Development;Utility;
DESKTOP
find "$stage" -type d -exec chmod 755 {} +
find "$stage" -type f -exec chmod 644 {} +
chmod 755 "$stage/usr/bin/layperson-log-viewer" "$stage/usr/lib/layperson-log-viewer/LaypersonLogViewer.App"
if [[ -f "$stage/usr/lib/layperson-log-viewer/createdump" ]]; then
    chmod 755 "$stage/usr/lib/layperson-log-viewer/createdump"
fi

# Let dpkg derive dependencies from ELF imports, including their minimum ABI versions.
# ICU, OpenSSL and X11 are also loaded dynamically, so list those explicitly below.
mkdir -p "$build_dir/debian"
cat > "$build_dir/debian/control" <<'CONTROL'
Source: layperson-log-viewer
Section: utils
Priority: optional
Maintainer: Layperson Log Viewer contributors <noreply@localhost>
Standards-Version: 4.6.0

Package: layperson-log-viewer
Architecture: amd64
Description: Log viewer with filters and numeric statistics
CONTROL
native_files=()
while IFS= read -r -d '' candidate; do
    if file -b "$candidate" | grep -q '^ELF '; then native_files+=("-e$candidate"); fi
done < <(find "$stage/usr/lib/layperson-log-viewer" -maxdepth 1 -type f -print0)
cd "$build_dir"
dependencies=$(dpkg-shlibdeps -O "-l$stage/usr/lib/layperson-log-viewer" "${native_files[@]}" | sed -n 's/^shlibs:Depends=//p')
[[ -n "$dependencies" ]] || { echo 'No native dependencies detected' >&2; exit 1; }
dependencies=$(python3 - "$dependencies" <<'PY'
import sys
items = sys.argv[1].split(', ')
names = {item.split()[0] for item in items}
items.extend(name for name in ('libicu70', 'libssl3', 'libx11-6', 'libice6', 'libsm6', 'libfontconfig1') if name not in names)
print(', '.join(items))
PY
)
installed_size=$(du -sk "$stage/usr" | cut -f1)
cat > "$stage/DEBIAN/control" <<CONTROL
Package: layperson-log-viewer
Version: $version
Section: utils
Priority: optional
Architecture: amd64
Maintainer: Layperson Log Viewer contributors <noreply@localhost>
Installed-Size: $installed_size
Depends: $dependencies
Description: Log viewer with filters and numeric statistics
 Desktop log viewer with include/exclude AND groups, timestamp grouping,
 and regex-based numeric statistics. Russian interface.
 Includes the .NET runtime; no separate .NET installation is required.
 Built for Ubuntu 22.04 LTS (amd64).
CONTROL
package="$output/layperson-log-viewer_${version}_ubuntu22.04_amd64.deb"
dpkg-deb --root-owner-group -Zxz --build "$stage" "$package"
cd "$output"
sha256sum "$(basename "$package")" > "$package.sha256"
printf '%s\n' "$build_dir" > "$output/deb-build-directory.txt"
printf 'Package: %s\nDependencies: %s\n' "$package" "$dependencies"
