# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

<!-- Headers should be listed in this order: Added, Changed, Deprecated, Removed, Fixed, Security -->

## [1.0.1] - 2026-11-20
### Changed
- Improved `BuildPipelineRunner.IsSafeDirectoryToClear` to block parent directories of the project root and protected project folders (`.git`, `Library`, `UserSettings`, `Temp`, `Logs`).
- Switched extension and notification channel discovery to Unity `TypeCache` (`BuildWindow`, `BuildNotificationManager`).
- Preserved active `selectedBuildTargetGroup` across batch build execution and exposed `BuildExtensionAttribute.Title` in `GetExtensionInfo`.
- Updated `BuildNotificationSettings` to Base64-encode Generic Webhook URLs and support both `BUILD_WEBHOOK_*` and `GENERIC_WEBHOOK_*` environment variables.
- Added path segment sanitization and `{CompanyName}` / `{BundleId}` token support to `BuildTool.GetRootBuildPath`.
- Updated `NotificationSettings` UI switch styling, welcome window layout, and documentation menu paths.

### Fixed
- Fixed potential batchmode deadlock in `BuildTool.ProcessBuild` when invoked from `BuildCLI.WaitForTask`.
- Fixed `GitRepository.Commit` and `CommitSingleOrphan` failing with `fatal: pathspec '-A' did not match any files`.
- Fixed `BuildTracker.FinalizeRemoteBuildReport` to honor `KeepSingleCommit`, retain the latest `MaxHistoryEntries` reports, and retry failed pushes.
- Fixed macOS/VisionOS `.app` bundle post-processor directory resolution in `BuildTool.ExecuteBuildFromProfile`.
- Fixed `BuildTool.GetCurrentBuildIndex` returning `"local"` for CLI `-register` builds and eliminated redundant disk reads in `BuildTool.GetProfileVersion`.
- Fixed dead-code `ENV` badge detection in `NotificationChannelBase.CreateMaskedField`, `GenericWebhookNotificationChannel` ENV variable names, and `AddTelegramChatId` baking ENV IDs into `EditorPrefs`.
- Fixed `Archiver` and `AddressablesProcessor` mutating `EditorPrefs` during CLI runs and prevented `Archiver` self-archiving.
- Fixed `BuildManifestGenerator` sample CLI `TEXT` format handling, backup retention pruning, and `EditorPrefs` state synchronization.
- Fixed `BuildToolWelcomeWindow` version-update check (`ShowOnUpdatePrefKey` / `LastVersionPrefKey`) and legacy package prefix resolution.

## [1.0.0] - 2026-11-05
### Added
- Multi-platform sequential build execution with dedicated Build Window UI (`Tools → Build Tool → Show Build Window`).
- Modular Git Build Tracking: tracking module under `BuildTool.Tracking` (`com.buildtool.tracking.editor` and `com.buildtool.tracking.runtime`), with orphan branch tracking, remote synchronization, and automated build indexing.
- Multi-channel notification engine (Telegram, Discord, Slack, Generic Webhook, Desktop toasts) with batch summary notification cards and per-profile reporting.
- Command Line Interface support with both classic batchmode runner (`BuildCLI`) and native Unity CLI commands (`build:run`, `build:clean`).
- Clean build cache toggle (`BuildOptions.CleanBuildCache`) in Build Window and CLI flags (`-clean-build`).
- Interactive Welcome & Onboarding Screen with tabbed navigation, readiness diagnostics, release notes, and samples discovery (`Tools → Build Tool → Welcome Screen`).
- Build extension lifecycle badges (Pre-Process / Post-Process) in Build Window cards.
- Extensible pipeline supporting pre-processors (`IBuildPreprocessor`), post-processors (`IBuildPostprocessor`), and custom build window tabs (`IBuildWindowExtension`).
- Pre-build Addressables processor (`AddressablesProcessor`) with conditional compilation (`HAS_ADDRESSABLES`).
- Customizable output path templating via `PatternLocationPathBuilder` with tokens (`{Version}`, `{BuildIndex}`, `{Platform}`, `{Subtarget}`, `{Date}`, `{Time}`).
- Automatic Android App Bundle (`.aab`) vs Standalone APK (`.apk`) path resolution based on active profile and build settings.
- Export of detailed per-profile build report logs to `<buildFolder>/Logs/` directory.
- Modular Build Info Display sample (`Samples~/BuildInfo`) with runtime watermark prefab and demo scene.
- Build Manifest extension sample (`Samples~/BuildManifest`) with UI Toolkit settings and hooks.
- Static `BuildInfoProvider` runtime API for programmatic access to build metadata.
- Preconfigured development build profiles for Windows, Android, Linux, and WebGL.
- Automated release workflow in GitHub Actions with standalone `.unitypackage` export script (`.github/scripts/pack_unitypackage.py`).
- Unit and integration test suite in `Assets/Tests/Editor`.
