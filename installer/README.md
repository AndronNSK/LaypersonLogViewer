# Windows MSI installer

For Ubuntu 22.04 / WSL packaging, see [README-linux.md](README-linux.md).

The MSI bundles a **self-contained .NET 10 / Windows x64** publication. The destination
computer needs neither the .NET SDK nor a separately installed .NET runtime. Installation
works offline through Windows Installer, with a Start menu shortcut, optional desktop
shortcut, and removal through Windows Installed apps. The default per-user location is
`%LOCALAPPDATA%\Programs\Layperson Log Viewer`.

Windows 11 x64 is the primary target. The package accepts Windows 10 or newer; support
also depends on the OS edition and lifecycle. This is not a native ARM64 or 32-bit build.
The application UI remains Russian; MSI dialogs can be built in Russian or English.

## Prepare build tools

On the **build computer** only, install the .NET 10 SDK. From the repository folder:

```powershell
dotnet tool install wix --version 5.0.2 --tool-path .tools/wix5 --allow-roll-forward
.tools/wix5/wix.exe extension add WixToolset.UI.wixext/5.0.2
```

WiX runs as a local .NET tool. Inno Setup is not used. WiX and its extension are pinned
to the same version. Generated tools, extension caches, and packages are ignored by Git.

## Build

```powershell
pwsh -NoProfile -File installer/build-installer.ps1 -Version 0.2.4
```

Add `-Culture en-us` for English setup dialogs; the default is `ru-ru`.
The script publishes into a fresh directory, checks the bundled runtime, includes available
dependency notices, generates stable MSI components, builds the MSI, and writes a SHA-256 checksum.
Output for the default culture:

```text
artifacts/installer/LaypersonLogViewer-0.2.4-win-x64-ru-ru.msi
artifacts/installer/LaypersonLogViewer-0.2.4-win-x64-ru-ru.msi.sha256
```

Send the **MSI file** to the destination computer and double-click it. It contains the
complete application and runtime. The setup feature list allows enabling the desktop shortcut.
Sample logs are installed in `samples`, the Russian specification in `docs`, and notices in `licenses`.

The **Explorer integration** setup page has a checkbox to add **Открыть в LP LogViewer…**
to Explorer's context menu for files. It is checked by default on a new installation.
Uncheck it to install without the menu entry. Run setup again and select **Change** to
add or remove it later. Upgrades preserve the previous choice.
It opens the selected file and follows appended lines, using `--open "path"`.
The entry is registered for the current Windows user and removed on uninstall.
On Windows 11, it is available under **Показать дополнительные параметры**.
Default file associations are preserved. One selected file is supported per invocation.
See [Microsoft's context menu documentation](https://learn.microsoft.com/windows/apps/desktop/modernize/integrate-packaged-app-with-file-explorer).

`-SkipPublish` is a development option to package an already prepared publication in
`artifacts/publish/win-x64`; normally use the default fresh publication to avoid stale files.
Use `-OutputDirectory artifacts/installer-preview` to build a development MSI separately
from release installers. Increment the package version before distributing an update.
Commit the `.wxs`, `.wxl`, `.ps1`, and documentation, not generated binaries or tool caches.

## Updating

Keep the package's UpgradeCode stable. Increase `-Version` for every distributed release;
Windows Installer uses it to upgrade the existing installation and prevent downgrades.
Use the same setup language when upgrading. Do not distribute different contents with the
same version. Per-file component identifiers are derived from relative installation paths.

Trimming is disabled to retain dependencies used by the UI. Rebuild and redistribute after
.NET runtime updates: installing a shared runtime does not update this app's bundled runtime.

## Verification

- Run `dotnet test LaypersonLogViewer.slnx`.
- Run `pwsh -NoProfile -File installer/test-installer.ps1 -Path artifacts/installer/LaypersonLogViewer-0.2.4-win-x64-ru-ru.msi`
  to check the compiled checkbox, feature selection, navigation, registry command, and checksum.
- Confirm `coreclr.dll`, `hostfxr.dll`, `hostpolicy.dll`, and `System.Private.CoreLib.dll`
  are bundled, and runtime configuration uses `includedFrameworks` rather than `framework`.
- Test installation, shortcuts, startup, and removal. Inspect the started process to confirm
  it loads `coreclr.dll` from the installed application folder.
- Right-click a file with spaces or Russian letters in its name and open it using
  **Открыть в LP LogViewer…**. Confirm appended lines appear and uninstall removes the entry.
- Install with the Explorer checkbox cleared and confirm the entry is absent. In **Change**
  mode, enable it, then disable it again. Check that upgrading preserves the selected state.
- Before public distribution, test on a clean Windows x64 machine/VM without .NET: open a
  sample log, add a filter, select a timestamp, and check grouping. Local testing on a development
  PC, even with `DOTNET_ROOT` pointing to an empty folder, is not a substitute for that check.

No signing certificate is configured. The MSI is unsigned; Windows may show an unknown
publisher warning. Signing can be added for distributed releases.

References: [.NET publishing](https://learn.microsoft.com/en-us/dotnet/core/deploying/),
[WiX .NET tool](https://www.nuget.org/packages/wix/5.0.2).
