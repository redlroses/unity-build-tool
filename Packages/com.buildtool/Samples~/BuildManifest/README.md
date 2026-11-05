# Build Manifest Extension (Example)

This sample demonstrates how to implement a custom, production-ready build pipeline extension for **Build Tool**.

## Features Demonstrated

1. **Pipeline Lifecycle Hooks**:
   - `IBuildPreprocessor`: Executed immediately before each build profile begins.
   - `IBuildPostprocessor`: Executed immediately after each build profile succeeds.
2. **Interactive UI Toolkit Configuration**:
   - Integrated into the Build Window's **Extensions** list via `BuildExtensionBase` and `IBuildWindowExtension`.
   - Toggle switch, text fields, dropdown format selectors, folder pickers, and inline action buttons.
3. **EditorPrefs Persistence**:
   - Uses `.AppendProjectName()` to prevent configuration collisions across projects.
4. **CLI Parameter Integration**:
   - Implements `IConfigurableBuildExtension` to expose `--ext manifest=all` and custom arguments to headless/CLI builds.

## How to Use

Copy `BuildManifestGenerator.cs` into your project's `Editor/` folder (or an Editor assembly that references `com.buildtool.editor`). Build Tool will automatically detect it and render it in the **Extensions** section of the Build Window.
