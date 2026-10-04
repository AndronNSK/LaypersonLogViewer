# Ubuntu 22.04 package

`build-deb.sh` builds a self-contained .NET 10 Linux x64 application and a Debian package
for **Ubuntu 22.04 LTS amd64**. The installed app does not require a .NET SDK or a separately
installed .NET runtime. The version comes from `Directory.Build.props`.

## Build prerequisites

Inside Ubuntu 22.04 (including WSL):

```bash
sudo apt-get update
sudo apt-get install -y --no-install-recommends \
  curl ca-certificates dpkg-dev file rsync python3 \
  libicu70 libssl3 libx11-6 libice6 libsm6 libfontconfig1 \
  zlib1g libstdc++6 libgcc-s1 fonts-dejavu-core

mkdir -p ~/.cache/layperson-build
curl -fsSL https://dot.net/v1/dotnet-install.sh \
  -o ~/.cache/layperson-build/dotnet-install.sh
bash ~/.cache/layperson-build/dotnet-install.sh \
  --channel 10.0 --install-dir ~/.dotnet --no-path
```

The script discovers `~/.dotnet/dotnet` when `dotnet` is not on PATH. SDK installation is
only needed on the build machine. Optional GUI smoke tests use `xvfb` and `xauth`.

## Build

From Windows PowerShell:

```powershell
wsl -d Ubuntu-22.04 -- bash /mnt/c/projects/LaypersonLogViewer/installer/build-deb.sh
```

Or from Ubuntu, run `bash installer/build-deb.sh` in the checkout.

The script copies source into a fresh `/tmp/layperson-deb.*` directory, runs the test suite
on Linux, and publishes `linux-x64` with the runtime included and trimming disabled.
Windows `bin` and `obj` directories are not reused. Temporary build directories remain
available for diagnostics until removed or cleared by the operating system.

Output is in `artifacts/installer/`:

```text
layperson-log-viewer_<version>_ubuntu22.04_amd64.deb
layperson-log-viewer_<version>_ubuntu22.04_amd64.deb.sha256
```

## Install and run

For version 0.1.4, inside Ubuntu:

```bash
sudo apt install /mnt/c/projects/LaypersonLogViewer/artifacts/installer/layperson-log-viewer_0.1.4_ubuntu22.04_amd64.deb
layperson-log-viewer
```

The package installs the app under `/usr/lib/layperson-log-viewer`, a launcher under
`/usr/bin`, and a desktop-menu entry. Samples and documentation are under
`/usr/share/doc/layperson-log-viewer`. Dependency notices accompany the published files.
Running the GUI requires an X11 desktop or WSLg. Uninstall with:

```bash
sudo apt remove layperson-log-viewer
```

## Dependencies and compatibility

`dpkg-shlibdeps` scans all published ELF binaries and determines their system libraries
and minimum ABI versions. ICU, OpenSSL, and Avalonia's X11 libraries are also declared
explicitly because dynamically loaded libraries may not appear in ELF imports. Build
tools, a compiler, the SDK, and the virtual-display tools are not package dependencies.
The optional `libcoreclrtraceptprovider.so` component is omitted, so the package does not
require the legacy LTTng tracing library. External LTTng runtime tracing is unavailable;
normal application behavior and the bundled .NET runtime remain available.

This package targets Ubuntu 22.04 and its ICU 70 package. Do not assume the same package
installs on every newer Ubuntu/Debian release; build and verify a variant for each target.
The application still holds a snapshot of the log in memory. Bundling the runtime reduces
external dependencies but makes the package larger, and runtime updates require a rebuild.

References: [Avalonia Linux support](https://docs.avaloniaui.net/docs/supported-platforms)
and [.NET on Ubuntu](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install).
