[中文](README.md) | **English**

# Snapline — screenshot clothesline for Windows

An unofficial Windows implementation of [Tendedero](https://github.com/alejandrobujan/tendedero) by [Alejandro Buján](https://github.com/alejandrobujan), using C# / WPF and Win32. It hangs screenshots along the top of the screen for copying, previewing, editing, and dragging into other apps. This project has its own name and icon and is not endorsed by the upstream author.

**v1.4.11 fixes collection of PowerPoint text boxes as images.** Preview images attached when copying whole text boxes or shapes from Office/WPS are skipped; image objects copied from those applications are also skipped. Use region capture when needed. Since v1.4.10, native clipboard formats are checked before accessing OLE data, skipping text, tables, and recognized Illustrator/AICB/PDF/SVG mixed content. Automatic-collection failures notify at most once per run. This remains a Windows prerelease; see [QA](windows/QA.md) for the actual verification scope.

![Snapline Windows preview](docs/preview.png)

The actual WPF screenshot bar is shown on a generated presentation background. Glass is limited to screenshot containers; empty areas remain transparent.

![Unified settings](docs/settings.png)

The v1.4.6 settings window has General, Capture, Shortcuts, Storage, and About pages. Its background is sampled into memory before opening and repositioned while moving; changes behind it are not refreshed live.

## Download

- [Windows installer](https://github.com/jiuyang5354/snapline-windows/releases/download/v1.4.11/Snapline-Windows-Setup-v1.4.11.exe)
- [Portable package](https://github.com/jiuyang5354/snapline-windows/releases/download/v1.4.11/Snapline-Windows-v1.4.11.zip)
- [Buildable source](https://github.com/jiuyang5354/snapline-windows/releases/download/v1.4.11/Snapline-Windows-Source-v1.4.11.zip)
- [Installer source and verification](https://github.com/jiuyang5354/snapline-windows/releases/download/v1.4.11/Snapline-Windows-Installer-Source-v1.4.11.zip)
- [Installer SHA-256](https://github.com/jiuyang5354/snapline-windows/releases/download/v1.4.11/SHA256SUMS-Setup.txt)
- [Release notes and checksums](https://github.com/jiuyang5354/snapline-windows/releases/tag/v1.4.11)

Requires Windows 10/11 and .NET Framework 4.8 or later. No administrator rights are needed.

The installer defaults to `%LOCALAPPDATA%\Programs\Snapline`, provides Start menu and uninstall entries, and lets you choose a desktop shortcut. Startup at login is off by default. Uninstalling preserves screenshots and settings; see the [installer guide](windows/INSTALLER.zh-CN.md).

For portable use, extract the entire ZIP and run `Snapline.exe`. The app remains in the tray, possibly under hidden icons. It is unsigned, so Windows may show an unknown-publisher warning.

A [GitHub Packages NuGet package](https://github.com/users/jiuyang5354/packages/nuget/package/jiuyang5354.Snapline.Windows) is also available as `jiuyang5354.Snapline.Windows`, version `1.4.11-preview`. Its app files are under `tools/Snapline/`; downloading requires package authentication. Most users should choose the installer or portable ZIP. See [package guidance](windows/PACKAGE-README.md).

## Use

1. Capture with Win+Shift+S or copy an image.
2. Hover at the screen top, click the tray icon, or press Ctrl+Alt+T to expand the clothesline.
3. The toolbar offers region capture, copying the latest image, pausing collection, and settings.
4. Click an image to copy, double-click to preview, hold for about 0.45 seconds to edit in Paint, or drag into another app.
5. Exit from the tray menu.

If Ctrl+Alt+T is occupied, Snapline tries Ctrl+Alt+Shift+T. If both are unavailable, use the tray or hover. See the [Chinese usage guide](windows/README.zh-CN.md).

### Hover actions and region capture

Hover an image and press C to copy, A to edit in Paint, or D/Delete to remove. Holding a key does not remove neighboring images. Keyboard input stays with the original app when the pointer leaves, the shelf closes, another operation is active, or Ctrl/Alt/Shift/Win is held.

Choose Region capture from the toolbar or tray. Normally, releasing the drag saves a PNG and copies it. With **Settings → Capture → Include mouse pointer → Save** enabled, draw the region, position the pointer, then press Enter. Escape cancels; drag again to redraw. Manual capture works while automatic collection is paused.

![Pointer capture setting](docs/capture.png)

## Updates and collection settings

- Automatic checks, including prereleases, are enabled by default: about 15 seconds after startup, then at most once every 24 hours. A version is announced once; manual checks remain available when automatic checks are off.
- Installed copies download and verify an EXE, then open the upgrade wizard when you choose Install update. Portable copies download a ZIP. Both validate size and SHA-256.
- Pausing collection covers clipboard and folder images, persists across restarts, and skips images created while paused. Existing images and manual additions still work.
- Copy latest is available without expanding the shelf, and is disabled when empty.
- Startup at login is opt-in for the current user. Re-enable it after moving a portable installation to refresh the path.

In-app updates in v1.3.2 and earlier download only ZIPs. Installed users must run a newer installer once; subsequent in-app updates select the EXE. The installer replaces files after the old process exits and preserves the data folder, custom shortcut arguments, and startup entry. Portable users should exit before extracting an update.

Update checks read this repository's public version file from `raw.githubusercontent.com`; downloads use GitHub and its asset CDN. The app sends its version but no screenshots, clipboard content, local paths, account data, or telemetry. Failed, canceled, or unverified downloads do not replace saved packages. Other apps and Windows clipboard sync retain their own data policies.

## Data and behavior

- By default, Snapline collects PNG/bitmap clipboard images, not just screenshots. It skips text, tables, rich text, and recognized Illustrator/AICB/PDF/SVG vector mixtures before reading them as image data. Ordinary images with accompanying HTML can still be collected. Disable clipboard collection to monitor a folder only.
- Default inbox: `%LOCALAPPDATA%\Snapline\Inbox`; settings: `%LOCALAPPDATA%\Snapline\settings.json`. Changing the inbox does not move older images. The latest 12 are restored; older files remain on disk for manual cleanup.
- Clicking X or pressing D/Delete sends images in the current/default inbox to the Recycle Bin. Images in earlier custom inboxes or external locations are removed only from the display. Remove all preserves files.
- The target app determines copy/move behavior when dragging out. Moving may remove the original file from its source directory.
- Snapline does not intercept system screenshot shortcuts or alter screenshot-tool autosave settings.
- Glass rims refract a background sampled before expansion and reposition it with container tilt, sway, and scale. Samples stay in memory and refresh on reopening, not every video frame. Screenshot content is rendered independently. Transparency-off and high-contrast modes use solid borders.

## Build and validation

From the repository root, use Windows PowerShell or PowerShell 7:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\windows\build.ps1
```

Output: `windows/bin/Snapline.exe`. The build uses the system .NET Framework C# compiler without third-party runtime dependencies. Install the .NET Framework 4.8 Developer Pack if the compiler is missing.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\windows\build.ps1 -Test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\windows\package.ps1
```

Tests temporarily use the system clipboard and restore readable original formats afterward. Pause copying during these tests. Generated images and state use independent directories under `windows/qa/output/`.

`build.ps1 -Storage` checks historical-image recovery, deduplication, and constrained memory with generated regular, transparent, and 8K images. Separate subprocesses have a 192 MiB limit; this check does not read user data or operate the mouse, keyboard, or clipboard.

Installer packaging also requires [NSIS 3.13](https://nsis.sourceforge.io/Download). Extract its official ZIP under `windows/.tools/`, place the released portable ZIP under `windows/`, and run `windows/package-installer.ps1 -Test`. It verifies the portable package and outputs the installer, installer-source ZIP, and checksums. Results are in `windows/qa/output/installer/installer-results.json`.

For releases, `package.ps1` generates the ZIP, checksums, and `windows/dist/update.json`; installer packaging adds EXE metadata. Upload and verify all assets before publishing root `update.json`. Keep ZIP metadata for older clients and match the prerelease flag to the actual release. The Publish Windows package workflow verifies downloaded assets, EXE version, checksums, package linkage, visibility, and portable-file contents before and after publishing. See [workflow runs](https://github.com/jiuyang5354/snapline-windows/actions/workflows/publish-package.yml).

## Upstream and license

Upstream: [alejandrobujan/tendedero](https://github.com/alejandrobujan/tendedero), by Alejandro Buján; reference commit [`3c866d94d25ee9d1e008aecd9a78a4e9b2bf06a4`](https://github.com/alejandrobujan/tendedero/commit/3c866d94d25ee9d1e008aecd9a78a4e9b2bf06a4).

The Windows implementation uses C# 5, WPF, .NET Framework 4.8, and Win32. The upstream MIT license does not cover the original project name, icon, or promotional images. Snapline uses its own name/icon, excludes those assets, and does not imply upstream endorsement.

See [UPSTREAM-LICENSE.txt](UPSTREAM-LICENSE.txt), [LICENSE](LICENSE), and [NOTICE.md](NOTICE.md).
