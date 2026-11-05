# Unity Build Tool

An automation and pipeline orchestration tool for Unity that configures, executes, and tracks sequential multi-platform builds with custom pre/post-processing extensions, notifications, and CI/CD CLI support.

![Build Tool Interface](Images/interface-preview.png)

---

## Features

- **Multi-Platform Sequential Builds**: Configure and run batch builds across Android, Standalone, iOS, XR, and other platforms in a single automated pass.
- **Pre-Build Addressables Automation**: Built-in `AddressablesProcessor` automatically builds Addressables player content and optionally cleans cache before compiling the player.
- **Extensible Pipeline**: Plug in custom pre-processors (`IBuildPreprocessor`), post-processors (`IBuildPostprocessor`), and Build Window tabs (`IBuildWindowExtension`).
- **Built-in Archiver**: Automatically compress build outputs into zip archives upon successful compilation, with custom folder exclusions.
- **Multi-Channel Notifications**: Deliver build results, duration, errors, and status cards to **Telegram**, **Discord**, **Slack**, **Generic Webhooks**, or **Desktop** toasts (with native CI/CD environment variable support).
- **Flexible Path Templating**: Customizable output folder patterns using built-in tokens or custom registered tokens.
- **Git Version Tracking**: Track global build index, commit metadata, and reports on an isolated Git tracking branch (`build-tracking`) with team fetch optimization.
- **CI/CD & CLI Ready**: Execute builds via classic Unity batchmode or native Unity CLI commands with industry-standard flag conventions.

---

## Installation

### Via Unity Package Manager (Git URL)

1. In Unity, open **Window → Package Manager**.
2. Click **+** → **Add package from git URL...**
3. Paste:
   ```text
   https://github.com/redlroses/unity-build-tool.git?path=Packages/com.buildtool#v1.0.0
   ```

Alternatively, add it directly to your `Packages/manifest.json`:
```json
{
  "dependencies": {
    "com.buildtool": "https://github.com/redlroses/unity-build-tool.git?path=Packages/com.buildtool#v1.0.0"
  }
}
```

---

## Usage Guide

### 1. Build Window

Open the window from the top menu: **Tools → Build → Show Build Window** (Shortcut: `Ctrl + Shift + Alt + B`).

1. **Select Profiles**: Check the `BuildProfile` assets you wish to build sequentially. Use **All** / **None** for quick selection, and the **Switch** button next to any profile to activate it in the editor immediately.
2. **Build Options**:
   - **Development build**: Enable profiler connection, script debugging, and debug symbols.
   - **Clean build**: Clear Unity build cache before compiling (`BuildOptions.CleanBuildCache`) to force full asset rebuild.
   - **Clear build folder first**: Clean output directory before compiling to prevent stale artifacts.
   - **Open folder after build**: Reveal build directory in system file explorer on completion.
   - **Register version** *(requires Git Tracking module)*: Increment build number and record commit metadata to the tracking branch.
3. **Extensions**: Configure pre/post-build pipeline modules:
   - **Force Build Addressables**: Build Addressables bundles before player compilation, with optional cache clean and profile filter.
   - **Archiver**: Automatically compress build output into `.zip`, with configurable profile targets and folder exclusion patterns.
4. **Utility Bar**:
   - **Clear AppData**: Clear `Application.persistentDataPath` to test clean-slate launches.
   - **Open AppData**: Reveal persistent data directory in file explorer.
   - **Open Build Folder**: Reveal the configured root build directory.
   - **Setup Wizard**: Shortcut to module configuration.
5. **Build**: Click **Build** to execute the pipeline. Progress is displayed in real time, and profile logs are saved to `<BuildFolder>/Logs/`.

### 2. Setup Wizard

Open **Tools → Build → Setup Wizard** to configure optional modules without editing code or assembly definitions:
- **Build Notifications**: Master toggle and notification channel status.
- **Experimental CLI**: Enable `com.unity.pipeline` native commands (`BUILDTOOL_CLI_EXPERIMENTAL`). Installs `com.unity.pipeline` via UPM if missing.
- **Git Build Tracking**: Enable version indexing and repository tracking (`BUILDTOOL_TRACKING`).
- **Build Profiles & Output Paths**: Overview of configured profiles and quick link to Project Settings.

### 3. Welcome Screen

Open **Window → Build Tool → Welcome Screen** for a guided overview, quick access to settings and wizards, interactive readiness checklist, release notes, and reference samples.

---

## Command Line Interface (CLI)

Build Tool provides full parity between the GUI and headless execution, adhering to standard CLI flag conventions.

### Classic Batchmode Runner (`BuildCLI`)

Runs out-of-the-box in any Unity project without extra package dependencies.

#### Syntax Example:
```bash
# Standard execution with flags
Unity.exe -batchmode -quit -projectPath "C:/Projects/MyProject" \
  -executeMethod BuildTool.Editor.BuildCLI.Build \
  -profiles "StandaloneWindows64,Android" \
  -dev \
  -clean-build \
  -clear \
  -no-open

# Or dedicated clean build entry point:
Unity.exe -batchmode -quit -projectPath "C:/Projects/MyProject" \
  -executeMethod BuildTool.Editor.BuildCLI.CleanBuild \
  -profiles "StandaloneWindows64"
```

#### Supported Arguments:

| Argument | Type | Description |
|---|---|---|
| `-profiles "<names/GUIDs>"` | String | Comma- or semicolon-separated profile list (empty for active profile). |
| `-dev` | Flag | Enable development build. |
| `-clean-build` (or `-cleanBuild`, `-clean`) | Flag | Clean Unity build cache before compiling (`BuildOptions.CleanBuildCache`). |
| `-no-clean-build` (or `-noCleanBuild`) | Flag | Explicitly disable clean build cache. |
| `-clear` | Flag | Clear output directory before building. |
| `-register` | Flag | Register version and increment build index via Git tracking. |
| `-continue-on-error` (or `-continueOnError`) | Flag | Continue building subsequent profiles if a profile fails. |
| `-out-dir "<path>"` (or `-outDir`) | String | Custom output root directory override. |
| `-no-open` (or `-no-open-folder`) | Flag | Do not open build directory in explorer on completion. |
| `-open` (or `-open-folder`) | Flag | Explicitly open build directory in explorer on completion. |
| `-addressables` (or `-buildAddressables`) | Flag / Value | Force build Addressables. Pass profile name(s), `all`, or omit value for `true`. |
| `-clean-addressables` (or `-cleanAddressables`) | Flag | Clean Addressables build cache before compiling. |
| `-ext-args "<args>"` (or `-extArgs`) | String | Semicolon-separated key-value pairs passed to extensions (e.g. `zip=all;zip.exclude=*DontShip*`). |

> **CI/CD Robustness**: All boolean flags in `BuildCLI` support both bare flag syntax (`-dev`, `-clear`) and explicit boolean assignment (`-dev false`, `-dev true`, `-clear 0`) for seamless integration with CI matrix interpolation templates.

#### Exit Codes:
- `0`: All profiles built successfully.
- `1`: One or more profiles failed compilation or an unhandled exception occurred.

#### Inspect Installed Extensions:
```bash
Unity.exe -batchmode -quit -projectPath "C:/Projects/MyProject" \
  -executeMethod BuildTool.Editor.BuildCLI.ListExtensions
```
Prints all registered pipeline extensions, execution priority orders, and configurable CLI parameters to the console.

---

### Native Unity CLI (`[CliCommand]`)

Guarded behind `BUILDTOOL_CLI_EXPERIMENTAL` define constraint (requires `com.unity.pipeline`, installable via Setup Wizard).

#### 1. Execute Sequential Builds (`build:run`)
```bash
# Standard execution with flags
unity command build:run --profiles "StandaloneWindows64,Android" --dev --clear --no-open-folder

# Output plain text summary table instead of JSON
unity command build:run --profiles "StandaloneWindows64" --dev --no-json
```

Supported Arguments:
- `--profiles <string>`: Comma- or semicolon-separated profile names or GUIDs.
- `--dev`: Create a development build (profiler, script debugging).
- `--clean-build`: Clean Unity build cache before building (`BuildOptions.CleanBuildCache`).
- `--no-clean-build`: Do not clean Unity build cache.
- `--clear`: Clear output folder before build.
- `--register`: Register version in remote Git tracker.
- `--continue-on-error`: Continue next profile build if previous fails.
- `--out-dir <path>`: Custom output root directory override.
- `--open-folder` / `--no-open-folder`: Explorer popup behavior (default: open).
- `--addressables <value>`: Force build Addressables (`true`, `all`, comma-separated profile names, or `false`).
- `--clean-addressables`: Clean Addressables cache before building.
- `--ext-args <args>`: Key-value arguments for extensions (e.g. `zip=all;zip.exclude=*DontShip*`).
- `--json` / `--no-json`: Structured JSON result (default: true) or formatted ASCII summary table.

#### 2. Execute Clean Builds (`build:clean`)
```bash
# Clean build for specified profiles
unity command build:clean --profiles "StandaloneWindows64,Android" --dev
```
Executes sequential builds with `BuildOptions.CleanBuildCache` enabled by default. Accepts the same arguments as `build:run` (except `--clean-build` / `--no-clean-build`, which are implicitly configured).

#### 3. List Profiles (`build:list-profiles`)
```bash
# Machine-readable JSON output
unity command build:list-profiles

# Human-readable plain text list
unity command build:list-profiles --no-json
```

#### 4. List Extensions (`build:list-extensions`)
```bash
# List all registered build extensions and configurable parameters
unity command build:list-extensions --no-json
```

---

## Custom Extensions & Examples

You can extend the build pipeline by creating custom pre/post-processors without modifying core tool code.

### Pipeline Interfaces

| Interface / Attribute | Namespace | Role |
|---|---|---|
| `[BuildExtension(int order = 0)]` | `BuildTool.Editor.Extensions` | **Required**. Registers extension and defines execution priority. |
| `IBuildPreprocessor` | `BuildTool.Editor.Processors` | Runs **before** each `BuildProfile` is compiled. |
| `IBuildPostprocessor` | `BuildTool.Editor.Processors` | Runs **after** each `BuildProfile` finishes successfully. |
| `IBuildWindowExtension` | `BuildTool.Editor` | Renders custom controls in the Build Window **Extensions** card. |
| `IConfigurableBuildExtension` | `BuildTool.Editor.Extensions` | Handles CLI arguments passed via `--ext-args` (Unity CLI) or `-ext-args` / `-ext` (BuildCLI). |

### Minimal Example

```csharp
using BuildTool.Editor.Extensions;
using BuildTool.Editor.Processors;
using UnityEditor.Build.Profile;
using UnityEngine;

[BuildExtension(100, title: "Custom Watermark")]
public class CustomWatermarkExtension : IBuildPreprocessor
{
    public void PreProcess(BuildProfile profile)
    {
        Debug.Log($"[Watermark] Preparing assets for {profile.name}...");
    }
}
```

### Reference Example: Build Manifest Generator

A comprehensive sample demonstrating UI Toolkit controls, `EditorPrefs` persistence, CLI arguments, and lifecycle hooks is included in the package:

- **Location**: `Samples~/BuildManifest/BuildManifestGenerator.cs`
- **Installation**: Import via Unity Package Manager (**Build Tool → Samples → Build Manifest Extension**), or copy the script into your project's `Editor/` folder.
- **Configurable CLI Parameters**: `manifest=true`, `manifest.format=JSON|YAML|TEXT`, `manifest.file=<name>`, `manifest.backup=<dir>`.

---

## Output Path Templating & Directory Builders

Configure output paths globally in **Project Settings → Build Tool** or per-profile via custom `ILocationPathNameBuilder` instances.

### Available Pattern Tokens

| Token | Description | Example |
|---|---|---|
| `{ProfileName}` | Name of the active `BuildProfile` asset | `Windows_Release` |
| `{ProductName}` | Application product name (`PlayerSettings.productName`) | `MyGame` |
| `{CompanyName}` | Company name (`PlayerSettings.companyName`) | `StudioName` |
| `{BundleId}` | Application identifier / package name | `com.studio.mygame` |
| `{Version}` / `{AppVersion}` | Application version (`Application.version`) | `1.2.0` |
| `{BuildIndex}` | Sequential index from `BuildTracker` (requires Git Tracking) | `42` |
| `{Platform}` | Short target platform identifier | `win`, `android`, `macos` |
| `{Subtarget}` | Standalone subtarget | `Server`, `Player` |
| `{Date}` | Current local build date in `yyyy-MM-dd` format | `2026-11-05` |
| `{Date:FORMAT}` | Custom date format string | `{Date:yyyy-MM-dd_HH-mm}` |
| `{Time}` | Current local build time in `HH-mm-ss` format | `14-30-00` |

### Registering Custom Tokens in C#
You can dynamically register custom tokens for output naming anywhere in editor code:

```csharp
using BuildTool.Editor.DirectoryBuilders;

PatternLocationPathBuilder.RegisterToken("Branch", ctx => "main", "Current git branch");
```

---

## Runtime Build Info & Watermark (`BuildInfoDisplay`)

Build Tool includes a built-in runtime tracking system that embeds build metadata directly into your standalone builds and displays it as a watermark overlay or via custom UI.

### How It Works

1. **Automated Asset Embedding**:
   - When building with Git Tracking enabled (`BUILDTOOL_TRACKING`) and **Register version** checked (or CLI `-register`), Build Tool generates a temporary `Assets/Resources/BuildInfo.asset` containing:
     - `Index`: Git-tracked build sequence number (or `local` for untracked runs).
     - `Commit`: Short Git commit hash.
     - `Date`: Build date in `yyyy-MM-dd` format.
     - `IsRegistered`: Status flag indicating whether the version was registered in remote tracking.
   - During compilation, Unity bakes this asset into the player's `Resources`.
   - After the build finishes, `BuildTool.Dispose()` removes the temporary asset from your workspace to keep Git clean.

2. **Runtime Initialization**:
   - Before the initial scene loads (`[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`), `BuildInfoProvider` initializes in standalone player builds (`!Application.isEditor`) and logs `[BUILD-INFO] <formatted info>` to the console/log.
   - If the player build was created without remote Git tracking (`!IsRegistered`), `BuildInfoProvider` logs the unregistered watermark:
     ```text
     <ProductName> v<Version> (build:N/A:N/A:N/A) <Platform>
     THIS VERSION IS NOT REGISTERED, NOT FOR SHARING
     ```
   - In the Unity Editor, `BuildInfoProvider.BuildInfo` safely provides fallback metadata for UI components without polluting the Editor console.

### Programmatic Access (`BuildInfoProvider` API)

The static class `BuildInfoProvider` provides direct programmatic access to build metadata anywhere in code:

```csharp
using BuildTool.Tracking;
using UnityEngine;

var info = BuildInfoProvider.BuildInfo;
if (BuildInfoProvider.IsRegistered)
{
    Debug.Log($"Build #{info.Index}, commit {info.Commit}, date {info.Date}");
}
```

### UI Display & Sample (`BuildInfoDisplay` Prefab)

UI presentation is separated into the **Build Info Display** sample (`Samples~/BuildInfo`), providing a ready-to-use overlay and custom view:

- **Location**: `Samples~/BuildInfo`
- **Installation**: Import via Unity Package Manager (**Window → Package Manager → Build Tool → Samples → Build Info Display → Import**).
- **Prefab**: `BuildInfoDisplay.prefab` featuring `BuildInfoView` and `CopyToClipboard`.
- **Customizable**: `BuildInfoView` provides configurable format templates, rich text options, `TMP_Text` reference, and `OnBuildInfoLoaded` events.
- **Click to Copy**: `CopyToClipboard` allows testers to click the overlay to copy build details to clipboard.

---

## Notifications

Configure notification channels in **Project Settings → Build Tool → Notifications**:

- **Telegram**: HTML status summaries and log attachments via Telegram Bot API.
- **Discord**: Rich embeds with duration, build sizes, and error breakdowns.
- **Slack**: Block Kit messages via Incoming Webhooks.
- **Generic Webhook**: Custom JSON payload with configurable headers.
- **Desktop**: OS-level notifications and completion sound.

### CI/CD Environment Variables

For headless builds and CI/CD pipelines where GUI configuration is not feasible, channels automatically read fallback secrets from system environment variables:

| Channel | Environment Variable | Description |
|---|---|---|
| **Discord** | `DISCORD_WEBHOOK_URL` | Webhook incoming endpoint URL. |
| **Slack** | `SLACK_WEBHOOK_URL` | Webhook incoming endpoint URL. |
| **Generic Webhook** | `BUILD_WEBHOOK_URL` | Target HTTP POST URL. |
| **Generic Webhook** | `BUILD_WEBHOOK_AUTH` | Optional HTTP Authorization header value (e.g. `Bearer <token>`). |
| **Telegram** | `TELEGRAM_BOT_TOKEN` | Bot API token obtained from @BotFather. |
| **Telegram** | `TELEGRAM_CHAT_ID` / `TELEGRAM_CHAT_IDS` | Target chat ID or comma-separated list of chat IDs. |

---

## Git Build Tracking & Architecture

The Git Tracking module (`BuildTool.Tracking`) solves a fundamental DevOps challenge in distributed game development: maintaining a single, sequential, monotonically increasing build index (e.g., `#1`, `#2`, `#3`...) across multiple developer machines and CI/CD agents without requiring a centralized database or external web service. It leverages your existing Git remote repository to store build numbers, commit metadata, history records, and build logs on a dedicated tracking branch.

### Architecture & How It Works

1. **Zero Impact on Active Working Directory**:
   - The tracking system operates entirely in an isolated directory (`Temp/build-tracking`), using shallow single-branch clones (`--depth 1 --single-branch`).
   - Your local project branch, working directory, and staged changes are never switched, modified, or disturbed during the build process.

2. **Sequential Version Indexing & Atomic Concurrency**:
   - Reads `buildInfo.json` from the tracking branch.
   - Uses a system-wide OS mutex (`Global\BuildTool_Tracking_FileLock`) and atomic temporary-file swap (`File.Replace`) to protect against concurrent build increments.
   - Bumps `lastBuildIndex` and appends an entry to the `history` array:
     - `BuildIndex`: Sequential integer index.
     - `DateUtc`: UTC timestamp in `yyyy-MM-dd` format.
     - `Commit`: Short Git commit hash of the project state being compiled.
     - `Branch`: Active Git branch being built (e.g. `main`, `release/1.0`).
     - `AppVersion`: Target application version (`Application.version`).

3. **Runtime Asset Embedding**:
   - Before compiling the player, creates a temporary `Assets/Resources/BuildInfo.asset`.
   - Unity serializes this metadata into the player binary resources.
   - At runtime, `BuildInfoProvider` loads this asset before scene load, making it accessible to watermarks (`BuildInfoDisplay`), crash reporters, and diagnostics.
   - Once building finishes (success, failure, or cancellation), `BuildTool.Dispose()` removes `BuildInfo.asset` from disk so the repository remains clean.

4. **Repository Size & History Control**:
   - **Single Commit Mode (`KeepSingleCommit = true`, Default)**:
     Uses low-level plumbing (`write-tree` / `commit-tree` without ancestor references) to record a single orphan commit and pushes with `--force-with-lease`. Even after thousands of builds, the tracking branch contains only 1 commit, preventing Git repository size bloat.
   - **History Pruning (`MaxHistoryEntries`)**:
     Limits the number of historical records stored inside `buildInfo.json`. Older records are automatically trimmed to keep metadata files small.
   - **Automatic Retries**:
     If a push collision occurs (e.g., another developer or CI runner pushed at the exact same moment), the system automatically fetches the latest index, reconciles, and retries up to `PushRetryAttempts` times.

5. **Build Report Archiving**:
   - Upon build sequence completion, detailed logs (timings, file sizes, errors, warnings) are committed to `Reports/BuildReport_<index>.log` on the tracking branch for auditability.

---

### Configuration & Settings Guide

Settings are stored in `ProjectSettings/BuildTrackingSettings.asset` and managed via **Edit → Project Settings... → Project → Build Tool → Tracking** (or from the shortcut in the Build Window utility bar).

| Setting | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| **Project Branch Override** | `string` | `build-tracking` | Name of the remote Git tracking branch. Invalid ref characters (spaces, tildes, colons) are automatically sanitized. |
| **Relative Git Path Override** | `string` | `""` *(empty)* | Relative path to repository root if the Unity project is located in a subfolder of a monorepo (e.g. `src/UnityClient`). |
| **Keep Single Commit (Force Push)** | `bool` | `true` | When enabled, replaces the tracking branch with a single orphan commit per build via `push --force-with-lease`. Eliminates commit accumulation. |
| **Push Retry Attempts** | `int` | `3` | Maximum number of push retries when encountering network failures or push collisions. |
| **Push Retry Delay (ms)** | `int` | `1000` | Delay between consecutive push retries in milliseconds. |
| **Max History Entries** | `int` | `0` | Maximum number of records retained in `buildInfo.json` history (`0` = unlimited history). |
| **Enable Logging** | `bool` | `false` | Enables verbose console logging for all underlying Git processes, arguments, and exit codes. |

---

### Team Optimization: Local Fetch Filtering (Negative Refspec)

By default, running `git fetch origin` or `git pull` in Git downloads all remote branches (`+refs/heads/*:refs/remotes/origin/*`), including the tracking branch. For large teams with frequent builds, downloading tracking commits and reports wastes bandwidth.

Build Tool includes built-in support for **Negative Refspecs** (Git 2.29+):
- Configures local Git repository with `^refs/heads/<tracking-branch>` in `remote.origin.fetch`.

**How to enable**:
1. Open **Project Settings → Project → Build Tool → Tracking**.
2. In the **Git Optimization** section, click **Ignore Branch in Local Fetch (Recommended)**.
3. Git will now completely skip the tracking branch during everyday fetches and pulls on your machine.

**How to revert**:
- Click **Stop Ignoring Branch (Download on Fetch)**.

---

### How to Use Tracking in Your Workflow

#### 1. In Unity Editor (Build Window)
- Open **Tools → Build → Show Build Window** (Shortcut: `Ctrl + Shift + Alt + B`).
- In the options panel, check **Register version** (only visible when `BUILDTOOL_TRACKING` is active).
- Click **Build**. The progress bar will reflect tracking initialization and index synchronization.

#### 2. In CI/CD & Command Line
- **Unity Batchmode**:
  ```bash
  Unity.exe -batchmode -quit -projectPath "<path>" -executeMethod BuildTool.Editor.BuildCLI.Build -profiles "Windows Standalone" -register
  ```
- **Native Unity CLI**:
  ```bash
  unity command build:run --profiles "Windows Standalone" --register
  ```

#### 3. In Path Templating
Use `{BuildIndex}` in your profile output directory template (configured in **Project Settings → Build Tool**):
```text
Builds/{Platform}_{Version}_b{BuildIndex}/
# Resolves to: Builds/win_1.0.0_b42/
```

#### 4. At Runtime in Code
```csharp
using BuildTool.Tracking;
using UnityEngine;

public class BuildInfoWatermark : MonoBehaviour
{
    private void Start()
    {
        if (BuildInfoProvider.IsRegistered)
        {
            var info = BuildInfoProvider.BuildInfo;
            Debug.Log($"Running Build #{info.Index} (commit: {info.Commit}, date: {info.Date})");
        }
        else
        {
            Debug.Log("Development / untracked build");
        }
    }
}
```

---

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.
