# WinSetup - post-install automation for Windows

[![Download Latest Release](https://img.shields.io/github/v/release/Ghaziiw/WinSetup?label=Download%20Latest%20Release&color=2ea44f)](https://github.com/Ghaziiw/WinSetup/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-blue.svg)](https://github.com/Ghaziiw/WinSetup)

C# / WPF (.NET 10) app that, on a fresh Windows install, installs your software through **winget**
(always the newest version), removes pre-installed Windows apps and applies privacy tweaks.

## Quick Start (No SDK or .NET runtime needed)
1. Download **`WinSetup-win-x64.zip`** from the **[Latest Release](https://github.com/Ghaziiw/WinSetup/releases/latest)**.
2. Extract the `.zip` folder (ensure `WinSetup.exe` and `Assets/` stay together).
3. Run `WinSetup.exe` (accept the UAC administrator prompt).

## Why WPF (and not WinUI 3 / PowerShell)
* Needs admin rights -> WPF handles `requireAdministrator` cleanly; packaged WinUI 3 apps do not.
* Plain `.exe`, can be published self-contained, so it runs on a PC that has nothing installed yet.
* Real async/await: the window never freezes while winget runs.

## Project layout
```
WinSetup/
 WinSetup.csproj          project file (net10.0-windows, WPF, CommunityToolkit.Mvvm)
 app.manifest             requireAdministrator -> UAC prompt at startup
 App.xaml(.cs)            theme + admin safety-net + global error handler
 MainWindow.xaml(.cs)     dashboard: category cards, checkboxes, progress bar, live log
 Assets/catalog.json      EDIT THIS to add/remove apps, bloatware patterns and tweaks
 Models/                  CatalogModels, StepResult
 Services/
   ProcessRunner.cs       async child processes + PowerShellHost
   WingetService.cs       winget bootstrap + install (latest version)
   GitHubReleaseService   fallback: newest GitHub release asset, resolved at runtime
   DebloatService.cs      Remove-AppxPackage + Remove-AppxProvisionedPackage
   TweakService.cs        registry policies, services, scheduled tasks, restore point
   Logger.cs              file log in %LocalAppData%\WinSetup\logs + live UI log
 ViewModels/              MainViewModel (queue, progress, error handling), Category/Item VMs
 tools/Validate-Catalog.ps1  verifies every winget id in the catalog
```

## Build and run (step by step)
1. Install the .NET 10 SDK:  `winget install Microsoft.DotNet.SDK.10`
   (If you only have an older SDK, change `net10.0-windows` in `WinSetup.csproj` to match.)
2. Open a terminal in the `WinSetup` folder and check the catalog ids (optional but recommended):
   `.\tools\Validate-Catalog.ps1`
3. Run: `dotnet run`  -> Windows shows the UAC prompt, then the window opens.
   *In Visual Studio: open `WinSetup.csproj`, but start Visual Studio itself "as administrator", otherwise debugging fails with "requires elevation".*

## Make a single .exe for a fresh PC
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
Output: `bin\Release\net10.0-windows\win-x64\publish\` - copy **WinSetup.exe and the Assets folder** to a USB stick.
(Self-contained means the target PC does not need .NET installed.)

### Automated GitHub Release (CI/CD)
Pushing a version tag triggers the GitHub Actions workflow to build and publish the release automatically:
```powershell
git tag v1.0.0
git push origin v1.0.0
```

## How "latest version" works
`winget install --id <id> --exact` always installs the newest version in the winget community repo.
The app runs `winget source update` first. If winget fails for an app and the catalog entry has a
`github` block, the newest GitHub release asset is downloaded at runtime instead (see 7-Zip in the catalog).

## Editing the catalog
```json
{ "name": "Git", "id": "Git.Git" }                                   // app (kind: "app")
{ "name": "News", "id": "Microsoft.BingNews", "default": true }      // debloat (kind: "debloat"), ';' separates several patterns
{ "name": "...", "id": "telemetry-policy" }                          // tweak (kind: "tweak"), key must exist in TweakService
```
Find ids with `winget search <name>`.

## Safety notes
* A **restore point** is created first (Windows may skip it if one was made in the last 24 h).
* Xbox / Game Pass components are deliberately NOT in the debloat list.
* On Windows Home/Pro the telemetry policy cannot go below "Required" - the services/tasks tweaks do the rest.
* Windows Update is never touched.
* Installs run one at a time (winget does not support parallel installs).
