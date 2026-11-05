using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using BuildTool.Editor.Extensions;
using BuildTool.Editor.Pipeline;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace BuildTool.Editor.Processors
{
    /// <summary>
    /// Comprehensive example of a custom build extension demonstrating ALL capabilities of the system:
    /// - Pure UI Toolkit layout with dark borders and cards matching the tool's design system
    /// - Master toggle with automatic body collapsing and state persistence
    /// - Auto-persisting form controls (TextField, DropdownField, IntegerField, FolderField, Toggle)
    /// - Contextual HelpBoxes and inline action buttons
    /// - Dynamic UI reactions (changing format, real-time validation)
    /// - Build lifecycle hooks: IBuildPreprocessor (pre-build) & IBuildPostprocessor (post-build)
    /// - CLI arguments integration via IConfigurableBuildExtension
    /// </summary>
    [BuildExtension(160, title: "Build Manifest & Pipeline Demo")]
    public class BuildManifestGenerator : BuildExtensionBase, IBuildPreprocessor, IBuildPostprocessor,
        IConfigurableBuildExtension
    {
        public override string Title => "Build Manifest";
        public override string Description =>
            "Generate build metadata manifests, manage artifacts, and demonstrate all extension features.";
        public override bool DefaultEnabled => false;

        private const string KeyFormat = "manifest_format";
        private const string KeyFilename = "manifest_filename";
        private const string KeyBackupDir = "manifest_backup_dir";
        private const string KeyRetentionCount = "manifest_retention";
        private const string KeyIncludeGit = "manifest_include_git";
        private const string KeyIncludeProfile = "manifest_include_profile";
        private const string KeyIncludeEnv = "manifest_include_env";
        private const string KeyLogOnBuild = "manifest_log_on_build";

        private string _format = "JSON";
        private string _filename = "build-manifest.json";
        private string _backupDir = "";
        private int _retentionCount = 5;
        private bool _includeGit = true;
        private bool _includeProfile = true;
        private bool _includeEnv = true;
        private bool _logOnBuild = true;

        private Stopwatch _profileStopwatch;

        [Serializable]
        public class ManifestModel
        {
            public string BuildTimeUtc;
            public double BuildDurationSeconds;
            public string ProfileName;
            public string TargetPlatform;
            public string UnityVersion;
            public string GitBranch;
            public string GitCommit;
            public string MachineName;
            public string OperatingSystem;
        }

        public BuildManifestGenerator()
        {
            _format = GetString(KeyFormat, "JSON");
            _filename = GetString(KeyFilename, "build-manifest.json");
            _backupDir = GetString(KeyBackupDir, "");
            _retentionCount = GetInt(KeyRetentionCount, 5);
            _includeGit = GetBool(KeyIncludeGit, true);
            _includeProfile = GetBool(KeyIncludeProfile, true);
            _includeEnv = GetBool(KeyIncludeEnv, true);
            _logOnBuild = GetBool(KeyLogOnBuild, true);
        }

        public void Initialize(BuildContext context)
        {
            if (context?.Request?.ExtensionArgs == null)
            {
                return;
            }

            var enabledArg = context.Request.GetExtensionArg("manifest");
            if (!string.IsNullOrWhiteSpace(enabledArg))
            {
                IsEnabled = !enabledArg.Equals("false", StringComparison.OrdinalIgnoreCase) &&
                            !enabledArg.Equals("0");
            }

            var formatArg = context.Request.GetExtensionArg("manifest.format");
            if (!string.IsNullOrWhiteSpace(formatArg))
            {
                _format = formatArg.Trim().ToUpperInvariant();
            }

            var fileArg = context.Request.GetExtensionArg("manifest.file");
            if (!string.IsNullOrWhiteSpace(fileArg))
            {
                _filename = fileArg.Trim();
            }

            var backupArg = context.Request.GetExtensionArg("manifest.backup");
            if (!string.IsNullOrWhiteSpace(backupArg))
            {
                _backupDir = backupArg.Trim();
            }
        }

        public IEnumerable<ExtensionCliParam> GetParameters()
        {
            return new[]
            {
                new ExtensionCliParam("manifest", "bool", "Enable or disable manifest generation", "false"),
                new ExtensionCliParam("manifest.format", "string", "Output format: JSON, YAML, or TEXT", "JSON"),
                new ExtensionCliParam("manifest.file", "string", "Manifest output filename", "build-manifest.json"),
                new ExtensionCliParam(
                    "manifest.backup", "string", "Optional secondary folder to copy manifests into", "")
            };
        }

        public override VisualElement CreateBodyGUI(IEnumerable<BuildProfile> profiles)
        {
            var body = new VisualElement();

            var helpBox = CreateHelpBox(
                "Demonstrates pure UI Toolkit extension with master toggle, collapsible cards, and "
                + "auto-persisted settings.",
                HelpBoxMessageType.Info);
            body.Add(helpBox);

            var formats = new List<string> { "JSON", "YAML", "Plain Text" };
            TextField filenameField = null;

            var formatDropdown = CreateDropdown("Format", formats, _format, val =>
            {
                _format = val;
                SaveString(KeyFormat, val);

                _filename = val switch
                {
                    "YAML" => "build-manifest.yaml",
                    "Plain Text" => "build-manifest.txt",
                    _ => "build-manifest.json"
                };
                SaveString(KeyFilename, _filename);

                if (filenameField != null)
                {
                    filenameField.value = _filename;
                }
            }, "Serialization format of the output manifest file");
            body.Add(formatDropdown);

            filenameField = CreateTextField("Manifest Filename", _filename, val =>
            {
                _filename = val;
                SaveString(KeyFilename, val);
            }, "Name of the manifest file placed into the profile's build directory");
            body.Add(filenameField);

            var backupField = CreateFolderField(
                "Backup Directory",
                KeyBackupDir,
                _backupDir,
                "Select Manifest Backup Directory",
                "Optional directory where a historical copy of every manifest will be archived");
            body.Add(backupField);

            var retentionField = CreateIntegerField(
                "Retention Count",
                KeyRetentionCount,
                _retentionCount,
                "Maximum number of historical manifests to keep in the backup folder");
            body.Add(retentionField);

            var togglesHeader = new Label("Include Metadata:")
            {
                style =
                {
                    marginTop = 6,
                    marginBottom = 2,
                    fontSize = 11,
                    unityFontStyleAndWeight = FontStyle.Bold
                }
            };
            body.Add(togglesHeader);

            var gitToggle = CreateToggle("Include Git Information", KeyIncludeGit, _includeGit,
                "Adds branch name and commit hash via git CLI");
            var profileToggle = CreateToggle("Include Profile Details", KeyIncludeProfile, _includeProfile,
                "Adds profile name and target platform");
            var envToggle = CreateToggle("Include Environment Info", KeyIncludeEnv, _includeEnv,
                "Adds machine name and OS version");
            var logToggle = CreateToggle("Print to Console on Build", KeyLogOnBuild, _logOnBuild,
                "Logs the full manifest into Unity Console after generation");

            body.Add(CreateRow(gitToggle, profileToggle));
            body.Add(CreateRow(envToggle, logToggle));

            var previewBox = CreateHelpBox(
                "Click 'Generate Preview' below to view mock output.", HelpBoxMessageType.None);
            previewBox.style.display = DisplayStyle.None;
            body.Add(previewBox);

            var actionRow = CreateRow(
                CreateButton("Generate Preview", () =>
                {
                    var mock = BuildModel(null, 42.5);
                    var output = FormatManifest(mock, _format);
                    previewBox.text = output;
                    previewBox.style.display = DisplayStyle.Flex;
                }, "Preview manifest output right here"),

                CreateButton("Open Backup Folder", () =>
                {
                    var dir = GetString(KeyBackupDir, "");
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        EditorUtility.RevealInFinder(dir);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(
                            "Folder Not Found",
                            "No valid backup directory selected or directory does not exist.", "OK");
                    }
                }, "Opens the configured backup folder in your file manager")
            );
            body.Add(actionRow);

            return body;
        }

        public void PreProcess(BuildProfile profile)
        {
            if (!IsEnabled)
            {
                return;
            }

            _profileStopwatch = Stopwatch.StartNew();
            var profileName = profile != null ? profile.name : "Active";
            Debug.Log($"<color=#76D275><b>[BuildManifest]</b></color> Starting tracking for profile: '{profileName}'");
        }

        public void PostProcess(BuildProfile profile, string path)
        {
            if (!IsEnabled)
            {
                return;
            }

            var duration = _profileStopwatch?.Elapsed.TotalSeconds ?? 0;
            _profileStopwatch?.Stop();

            if (string.IsNullOrWhiteSpace(path))
            {
                Debug.LogWarning("[BuildManifest] Cannot generate manifest: output path is empty.");
                return;
            }

            try
            {
                var targetDir = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path));
                if (string.IsNullOrWhiteSpace(targetDir) || !Directory.Exists(targetDir))
                {
                    if (!string.IsNullOrEmpty(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                }

                var model = BuildModel(profile, duration);
                var content = FormatManifest(model, _format);

                var filename = string.IsNullOrWhiteSpace(_filename) ? "build-manifest.json" : _filename;
                var outputPath = Path.Combine(targetDir, filename);

                File.WriteAllText(outputPath, content);
                Debug.Log($"<color=#76D275><b>[BuildManifest]</b></color> Manifest saved to: {outputPath}");

                if (_logOnBuild)
                {
                    Debug.Log($"<b>[BuildManifest Contents]</b>\n{content}");
                }

                if (!string.IsNullOrWhiteSpace(_backupDir))
                {
                    try
                    {
                        if (!Directory.Exists(_backupDir))
                        {
                            Directory.CreateDirectory(_backupDir);
                        }

                        var backupFilename =
                            $"{Path.GetFileNameWithoutExtension(filename)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}" +
                            $"{Path.GetExtension(filename)}";
                        var backupPath = Path.Combine(_backupDir, backupFilename);
                        File.Copy(outputPath, backupPath, true);
                        Debug.Log($"<color=#76D275><b>[BuildManifest]</b></color> Backup copied to: {backupPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[BuildManifest] Backup copy warning: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildManifest] Failed to write manifest: {ex.Message}");
            }
        }

        private ManifestModel BuildModel(BuildProfile profile, double durationSeconds)
        {
            var model = new ManifestModel
            {
                BuildTimeUtc = DateTime.UtcNow.ToString("o"),
                BuildDurationSeconds = Math.Round(durationSeconds, 2)
            };

            if (_includeProfile)
            {
                model.ProfileName = profile != null ? profile.name : "SampleProfile";
                model.TargetPlatform = BuildTool.GetBuildTarget(profile).ToString();
            }

            if (_includeEnv)
            {
                model.UnityVersion = Application.unityVersion;
                model.MachineName = Environment.MachineName;
                model.OperatingSystem = SystemInfo.operatingSystem;
            }

            if (_includeGit)
            {
                model.GitBranch = RunGit("rev-parse --abbrev-ref HEAD");
                model.GitCommit = RunGit("rev-parse --short HEAD");
            }

            return model;
        }

        private static string FormatManifest(ManifestModel model, string format)
        {
            return format switch
            {
                "YAML" => $"# Build Manifest\n" +
                          $"build_time_utc: \"{model.BuildTimeUtc}\"\n" +
                          $"duration_seconds: {model.BuildDurationSeconds.ToString(CultureInfo.InvariantCulture)}\n" +
                          $"profile: \"{model.ProfileName}\"\n" +
                          $"platform: \"{model.TargetPlatform}\"\n" +
                          $"unity_version: \"{model.UnityVersion}\"\n" +
                          $"git_branch: \"{model.GitBranch}\"\n" +
                          $"git_commit: \"{model.GitCommit}\"\n" +
                          $"machine: \"{model.MachineName}\"\n" +
                          $"os: \"{model.OperatingSystem}\"\n",

                "Plain Text" => $"BUILD MANIFEST\n" +
                                $"==============\n" +
                                $"Time (UTC):  {model.BuildTimeUtc}\n" +
                                $"Duration:    {model.BuildDurationSeconds.ToString(CultureInfo.InvariantCulture)}s\n" +
                                $"Profile:     {model.ProfileName}\n" +
                                $"Platform:    {model.TargetPlatform}\n" +
                                $"Unity:       {model.UnityVersion}\n" +
                                $"Git Branch:  {model.GitBranch}\n" +
                                $"Git Commit:  {model.GitCommit}\n" +
                                $"Machine:     {model.MachineName}\n" +
                                $"OS:          {model.OperatingSystem}\n",

                _ => JsonUtility.ToJson(model, true)
            };
        }

        private static string RunGit(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                };
                using var p = Process.Start(psi);
                if (p == null)
                {
                    return "Unknown";
                }
                var output = p.StandardOutput.ReadToEnd().Trim();
                if (!p.WaitForExit(3000))
                {
                    try
                    {
                        p.Kill();
                    }
                    catch
                    {
                    }
                }
                return string.IsNullOrEmpty(output) ? "Unknown" : output;
            }
            catch
            {
                return "Unknown";
            }
        }
    }
}
