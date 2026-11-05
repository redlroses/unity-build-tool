# Build Info Display Sample

This sample demonstrates how to display build information and watermarks at runtime in your Unity project using **Build Tool**.

## Features Demonstrated

1. **`BuildInfoDisplay` Prefab & `BuildInfoView`**:
   - Ready-to-use Canvas overlay positioned at the top-left of the screen.
   - Powered by `BuildInfoView`, which fetches raw build info from `BuildInfoProvider` and formats it.
   - Configurable format template, rich text support, and `OnBuildInfoLoaded` UnityEvent.
   - Configured with `DontDestroyOnLoad` so the watermark persists across scene transitions.
   - Equipped with `CopyToClipboard`: clicking the watermark text copies the full build information to the system clipboard for QA and bug reporting.

2. **`BuildInfoProvider` API**:
   - Programmatic access to build metadata via static `BuildInfoProvider.BuildInfo`.
   - Registration status check via `BuildInfoProvider.IsRegistered`.

3. **In-Game Custom UI**:
   - A demonstration panel (`BuildInfoDemoUI`) showing how to bind individual metadata fields (product name, version, build index, git commit, build date, platform, registration status) to custom UI elements.

## How it Works in Editor vs. Player Builds

- **In the Unity Editor / Local Development**:
  When you hit Play in the Editor or run a build created without Git tracking, no `BuildInfo.asset` exists in `Resources/`. `BuildInfoProvider` detects this and safely falls back to displaying:
  ```text
  <ProductName> v<Version> (build:N/A:N/A:N/A) <Platform>
  THIS VERSION IS NOT REGISTERED, NOT FOR SHARING
  ```
- **In an Automated Build (with Git Tracking)**:
  When a build is run via Build Tool with **Register version** (or `-register` / `--register` in CLI) and `BUILDTOOL_TRACKING` enabled, Build Tool automatically generates `Assets/Resources/BuildInfo.asset` before compilation. In the compiled build, `BuildInfoProvider` loads this asset and displays:
  ```text
  <ProductName> v<Version> (build:<Index>:<Commit>:<Date>) <Platform>
  ```
  After the build completes, Build Tool cleans up `Assets/Resources/BuildInfo.asset` from your workspace.

## How to Use

1. Open `Scenes/BuildInfoDemo.unity`.
2. Press **Play** in the Unity Editor to inspect the UI and watermark behavior.
3. To use `BuildInfoDisplay` in your own game:
   - Drag `Prefabs/BuildInfoDisplay.prefab` into your game's first/bootstrap scene.
   - Ensure an `EventSystem` exists in the scene so clicks can be detected by `CopyToClipboard`.
