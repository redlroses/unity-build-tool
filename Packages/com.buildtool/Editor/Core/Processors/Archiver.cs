using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using BuildTool.Editor.Extensions;
using BuildTool.Editor.Pipeline;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.UIElements;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace BuildTool.Editor.Processors
{
    [BuildExtension(150, title: "Archiver")]
    public class Archiver : BuildExtensionBase, IBuildPostprocessor, IConfigurableBuildExtension
    {
        public const string DefaultExclusions = "*DontShip*,*DoNotShip*,D3D12";
        private const string ArchiveNameFormat = "{0}";

        public override string Title => "Archiver";
        public override string Description => "Compress build outputs into zip archives upon completion.";
        protected override string EnabledSaveKey => _enabledToggleSaveKey;

        private readonly string _allToggleSaveKey = "archiver_data_all_toggle".AppendProjectName();
        private readonly string _enabledToggleSaveKey = "archiver_data_enabled_toggle".AppendProjectName();
        private readonly string _profilesSaveKey = "archiver_data_profiles".AppendProjectName();
        private readonly string _exclusionsSaveKey = "archiver_data_exclusions".AppendProjectName();

        private readonly Dictionary<string, bool> _profilesToArchive = new();

        private bool _isAll;
        private string _excludePatterns;

        internal string ExcludePatterns => _excludePatterns;
        internal bool IsAll => _isAll;

        internal bool IsProfileConfigured(string name)
        {
            return _profilesToArchive.GetValueOrDefault(name, false);
        }

        public Archiver()
        {
            _isAll = EditorPrefs.GetBool(_allToggleSaveKey, true);
            _excludePatterns = EditorPrefs.GetString(_exclusionsSaveKey, DefaultExclusions);

            var enableProfileNames = EditorPrefs.GetString(_profilesSaveKey, string.Empty).Split(',');

            foreach (var profileName in enableProfileNames)
            {
                if (!string.IsNullOrWhiteSpace(profileName))
                {
                    _profilesToArchive[profileName.Trim()] = true;
                }
            }
        }

        public void Initialize(BuildContext context)
        {
            if (context?.Request?.ExtensionArgs == null)
            {
                return;
            }

            var excludeArg = context.Request.GetExtensionArg("zip.exclude") ??
                             context.Request.GetExtensionArg("archiver.exclude");
            if (!string.IsNullOrWhiteSpace(excludeArg))
            {
                _excludePatterns = excludeArg.Trim();
            }

            var archiveArg = context.Request.GetExtensionArg("zip");

            if (archiveArg == null)
            {
                return;
            }

            var trimmed = archiveArg.Trim();

            if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("0") ||
                trimmed.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                IsEnabled = false;
                return;
            }

            IsEnabled = true;

            if (trimmed.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                _isAll = true;
                _profilesToArchive.Clear();
                return;
            }

            if (string.IsNullOrEmpty(trimmed) ||
                trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("1"))
            {
                if (EditorPrefs.HasKey(_allToggleSaveKey) || EditorPrefs.HasKey(_profilesSaveKey))
                {
                    _isAll = EditorPrefs.GetBool(_allToggleSaveKey, true);
                    if (!_isAll)
                    {
                        var savedProfiles = EditorPrefs.GetString(_profilesSaveKey, "");
                        var names = savedProfiles.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        if (names.Length > 0)
                        {
                            _profilesToArchive.Clear();
                            foreach (var name in names)
                            {
                                _profilesToArchive[name.Trim()] = true;
                            }
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                _isAll = true;
                _profilesToArchive.Clear();
                return;
            }

            _isAll = false;
            _profilesToArchive.Clear();
            var profileNames = trimmed.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var name in profileNames)
            {
                _profilesToArchive[name.Trim()] = true;
            }
        }

        public IEnumerable<ExtensionCliParam> GetParameters()
        {
            return new[]
            {
                new ExtensionCliParam(
                    "zip",
                    "string/bool",
                    "Profiles to zip: comma-separated list, 'all' for all profiles, or empty to use EditorPrefs",
                    "all"),
                new ExtensionCliParam(
                    "zip.exclude",
                    "string",
                    $"Comma-separated folder patterns to exclude from archiving (Default: {DefaultExclusions})",
                    DefaultExclusions)
            };
        }

        public void PostProcess(BuildProfile profile, string path)
        {
            var contains = _profilesToArchive.GetValueOrDefault(profile.name, false);

            if (!IsEnabled || (!_isAll && !contains))
            {
                return;
            }

            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError($"Archiver error. Path for build is null or empty. Profile {profile.name}");
                return;
            }

            ArchiveFolder(path, string.Format(ArchiveNameFormat, Path.GetFileName(path)), _excludePatterns);
        }

        public override VisualElement CreateBodyGUI(IEnumerable<BuildProfile> profiles)
        {
            var body = new VisualElement();

            var profilesContainer = new VisualElement();
            profilesContainer.style.marginLeft = 16;
            profilesContainer.style.marginTop = 2;
            profilesContainer.style.marginBottom = 4;
            profilesContainer.style.display = _isAll ? DisplayStyle.None : DisplayStyle.Flex;

            var allToggle = CreateToggle("All Profiles", _isAll, val =>
            {
                _isAll = val;
                profilesContainer.style.display = val ? DisplayStyle.None : DisplayStyle.Flex;
                SaveSettings();
            });
            body.Add(allToggle);

            foreach (var profile in profiles)
            {
                if (profile == null)
                {
                    continue;
                }

                _profilesToArchive.TryAdd(profile.name, false);
                var isArchive = _profilesToArchive[profile.name];

                var profToggle = CreateToggle(profile.name, isArchive, val =>
                {
                    _profilesToArchive[profile.name] = val;
                    SaveSettings();
                });
                profilesContainer.Add(profToggle);
            }
            body.Add(profilesContainer);

            var excludeField = CreateTextField(
                "Exclude Folders",
                _excludePatterns,
                val =>
                {
                    _excludePatterns = val;
                    SaveSettings();
                },
                "Folder names or wildcards (*DontShip*, D3D12) to exclude from zip archive"
            );
            body.Add(excludeField);

            return body;
        }

        private void SaveSettings()
        {
            var archivedProfiles = string.Join(",", _profilesToArchive
                .Where(entry => entry.Value)
                .Select(entry => entry.Key));

            EditorPrefs.SetString(_profilesSaveKey, archivedProfiles);
            EditorPrefs.SetString(_exclusionsSaveKey, _excludePatterns);
            EditorPrefs.SetBool(_allToggleSaveKey, _isAll);
        }

        private static void ArchiveFolder(
            string buildFolderPath,
            string archiveName = null,
            string excludePatterns = DefaultExclusions)
        {
            archiveName = string.IsNullOrEmpty(archiveName) ? Path.GetFileName(buildFolderPath) : archiveName;
            string zipFilePath = null;

            try
            {
                if (Directory.Exists(buildFolderPath))
                {
                    var parentDir = Path.GetDirectoryName(Path.GetFullPath(buildFolderPath));
                    if (string.IsNullOrEmpty(parentDir))
                    {
                        parentDir = buildFolderPath;
                    }

                    zipFilePath = Path.Combine(parentDir, $"{archiveName}.zip");

                    if (File.Exists(zipFilePath))
                    {
                        File.Delete(zipFilePath);
                    }

                    var patterns = string.IsNullOrWhiteSpace(excludePatterns)
                        ? Array.Empty<string>()
                        : excludePatterns.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

                    CompressDirectory(buildFolderPath, zipFilePath, patterns);

                    Debug.Log($"Folder successfully archived to: {zipFilePath}");
                }
                else
                {
                    Debug.LogWarning($"Directory does not exist: {buildFolderPath}");
                }
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(zipFilePath) && File.Exists(zipFilePath))
                {
                    try
                    {
                        File.Delete(zipFilePath);
                    }
                    catch
                    {
                    }
                }

                Debug.LogError($"Failed to archive folder: {ex.Message}");
            }
        }

        private static void CompressDirectory(string sourceDir, string zipFilePath, string[] patterns)
        {
            using var archive = ZipFile.Open(zipFilePath, ZipArchiveMode.Create);
            var rootDir = new DirectoryInfo(sourceDir);
            AddDirectoryToArchive(archive, rootDir, rootDir.FullName, patterns);
        }

        private static void AddDirectoryToArchive(
            ZipArchive archive,
            DirectoryInfo currentDir,
            string rootPath,
            string[] patterns)
        {
            var directories = currentDir.GetDirectories();
            var files = currentDir.GetFiles();

            var nonExcludedDirs = patterns.Length > 0
                ? directories.Where(dir => !ShouldExclude(dir.Name, patterns)).ToArray()
                : directories;

            if (nonExcludedDirs.Length == 0 && files.Length == 0 &&
                !string.Equals(currentDir.FullName, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                var relativeDir = Path.GetRelativePath(rootPath, currentDir.FullName).Replace('\\', '/') + "/";
                archive.CreateEntry(relativeDir);
                return;
            }

            foreach (var file in files)
            {
                var relativeFile = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
                archive.CreateEntryFromFile(file.FullName, relativeFile, CompressionLevel.Fastest);
            }

            foreach (var dir in nonExcludedDirs)
            {
                AddDirectoryToArchive(archive, dir, rootPath, patterns);
            }
        }

        internal static bool ShouldExclude(string dirName, string[] patterns)
        {
            for (var i = 0; i < patterns.Length; i++)
            {
                var pattern = patterns[i].Trim();
                if (pattern.Length == 0)
                {
                    continue;
                }

                if (pattern.StartsWith("*") && pattern.EndsWith("*"))
                {
                    var core = pattern.Substring(1, pattern.Length - 2);
                    if (dirName.IndexOf(core, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                    continue;
                }

                if (pattern.StartsWith("*"))
                {
                    if (dirName.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    continue;
                }

                if (pattern.EndsWith("*"))
                {
                    if (dirName.StartsWith(pattern.Substring(0, pattern.Length - 1),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    continue;
                }

                if (string.Equals(dirName, pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
