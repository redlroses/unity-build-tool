using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BuildTool.Editor.Extensions;
using BuildTool.Editor.Pipeline;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor.Processors
{
    [BuildExtension(50, title: "Force Build Addressables")]
    public class AddressablesProcessor : BuildExtensionBase, IBuildPreprocessor, IConfigurableBuildExtension
    {
        private const string ExtensionName = "Force Build Addressables";
        private const string LabelAll = "All Profiles";

        private static MethodInfo s_BuildPlayerContentMethod;

        public override string Title => ExtensionName;
        public override string Description =>
            "Forces building of Addressables content before the player build, preventing missing bundles.";
        protected override string EnabledSaveKey => _enabledToggleSaveKey;

        public override bool IsEnabled
        {
            get => EditorPrefs.GetBool(_enabledToggleSaveKey, false);
            set => EditorPrefs.SetBool(_enabledToggleSaveKey, value);
        }

        private readonly string _allToggleSaveKey = "addressables_builder_all".AppendProjectName();
        private readonly string _cleanToggleSaveKey = "addressables_builder_clean".AppendProjectName();
        private readonly string _enabledToggleSaveKey = "addressables_builder_enabled".AppendProjectName();
        private readonly string _profilesSaveKey = "addressables_builder_profiles".AppendProjectName();

        private readonly Dictionary<string, bool> _profilesToBuild = new();

        private bool _isAll;
        private bool _isCleanBeforeBuild;
        private BuildContext _context;

        public AddressablesProcessor()
        {
            _isCleanBeforeBuild = EditorPrefs.GetBool(_cleanToggleSaveKey, false);
            _isAll = EditorPrefs.GetBool(_allToggleSaveKey, true);

            var savedProfiles = EditorPrefs.GetString(_profilesSaveKey, string.Empty);
            if (!string.IsNullOrEmpty(savedProfiles))
            {
                var profileNames = savedProfiles.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var name in profileNames)
                {
                    _profilesToBuild[name.Trim()] = true;
                }
            }
        }

        public void Initialize(BuildContext context)
        {
            _context = context;
            if (context?.Request?.ExtensionArgs == null)
            {
                return;
            }

            var cleanArg = context.Request.GetExtensionArg("addressables.clean") ??
                           context.Request.GetExtensionArg("clean-addressables");
            if (!string.IsNullOrEmpty(cleanArg))
            {
                _isCleanBeforeBuild = cleanArg.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                     cleanArg.Equals("1") ||
                                     cleanArg.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }

            var addrArg = context.Request.GetExtensionArg("addressables") ??
                          context.Request.GetExtensionArg("build-addressables");
            if (addrArg == null)
            {
                return;
            }

            var trimmed = addrArg.Trim();

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
                _profilesToBuild.Clear();
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
                            _profilesToBuild.Clear();
                            foreach (var name in names)
                            {
                                _profilesToBuild[name.Trim()] = true;
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
                _profilesToBuild.Clear();
                return;
            }

            _isAll = false;
            _profilesToBuild.Clear();
            var profileNames = trimmed.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var name in profileNames)
            {
                _profilesToBuild[name.Trim()] = true;
            }
        }

        public IEnumerable<ExtensionCliParam> GetParameters()
        {
            return new[]
            {
                new ExtensionCliParam(
                    "addressables",
                    "string/bool",
                    "Profiles to build: comma-separated list, 'all' for all profiles, or true/false",
                    "false"),
                new ExtensionCliParam(
                    "addressables.clean",
                    "bool",
                    "Clean Addressables build cache before building (true/false)",
                    "false")
            };
        }

        public void PreProcess(BuildProfile profile)
        {
            var contains = _profilesToBuild.GetValueOrDefault(profile.name, false);
            if (!IsEnabled || (!_isAll && !contains))
            {
                return;
            }

            var target = BuildTool.GetBuildTarget(profile);
            var targetGroup = BuildTool.GetBuildTargetGroup(profile);
            var subtarget = (int)BuildTool.GetStandaloneBuildSubtarget(profile);
            var cacheKey = $"{target}_{subtarget}";

            var builtTargets = _context?.GetState<HashSet<string>>("addressables_built_targets");
            if (builtTargets == null)
            {
                builtTargets = new HashSet<string>();
                _context?.SetState("addressables_built_targets", builtTargets);
            }

            if (builtTargets.Contains(cacheKey))
            {
                Debug.Log(
                    $"[Addressables] Content for target '{target}' (Subtarget: {subtarget}) " +
                    $"already built in this sequence. Skipping rebuild for profile '{profile.name}'.");
                return;
            }

            Debug.Log(
                $"[Addressables] Executing forced Addressables build for profile '{profile.name}' " +
                $"(Target: {target}, Clean: {_isCleanBeforeBuild})...");

            var result = BuildAddressablesContent(target, targetGroup, _isCleanBeforeBuild, subtarget);

            if (result != null && !string.IsNullOrEmpty(result.Error))
            {
                var errorMsg = $"[Addressables] Build failed for profile '{profile.name}': {result.Error}";
                Debug.LogError(errorMsg);
                throw new InvalidOperationException(errorMsg);
            }

            builtTargets.Add(cacheKey);
        }

        public override VisualElement CreateBodyGUI(IEnumerable<BuildProfile> profiles)
        {
            var body = new VisualElement();

            var cleanToggle = CreateToggle("Clean cache before build", _isCleanBeforeBuild, val =>
            {
                _isCleanBeforeBuild = val;
                SaveSettings();
            }, "Cleans Addressables build cache before building, avoiding stale bundle hashes.");
            body.Add(cleanToggle);

            var profilesContainer = new VisualElement();
            profilesContainer.style.marginLeft = 16;
            profilesContainer.style.marginTop = 2;
            profilesContainer.style.marginBottom = 4;
            profilesContainer.style.display = _isAll ? DisplayStyle.None : DisplayStyle.Flex;

            var allToggle = CreateToggle(LabelAll, _isAll, val =>
            {
                _isAll = val;
                profilesContainer.style.display = val ? DisplayStyle.None : DisplayStyle.Flex;
                SaveSettings();
            });
            body.Add(allToggle);

            if (profiles != null)
            {
                foreach (var profile in profiles)
                {
                    if (profile == null)
                    {
                        continue;
                    }

                    _profilesToBuild.TryAdd(profile.name, false);
                    var isSelected = _profilesToBuild[profile.name];
                    var profToggle = CreateToggle(profile.name, isSelected, val =>
                    {
                        _profilesToBuild[profile.name] = val;
                        SaveSettings();
                    });
                    profilesContainer.Add(profToggle);
                }
            }
            body.Add(profilesContainer);

            var buttonsRow = CreateRow(
                CreateButton("Clean Addressables", () =>
                {
                    CleanAddressablesContent();
                }, "Cleans Addressables build cache immediately"),
                CreateButton("Build Addressables Now", () =>
                {
                    var activeProfile = BuildProfile.GetActiveBuildProfile();
                    var target = activeProfile != null
                        ? BuildTool.GetBuildTarget(activeProfile)
                        : EditorUserBuildSettings.activeBuildTarget;
                    var targetGroup = activeProfile != null
                        ? BuildTool.GetBuildTargetGroup(activeProfile)
                        : BuildPipeline.GetBuildTargetGroup(target);
                    var subtarget = activeProfile != null
                        ? (int)BuildTool.GetStandaloneBuildSubtarget(activeProfile)
                        : 0;
                    BuildAddressablesContent(target, targetGroup, _isCleanBeforeBuild, subtarget);
                }, "Builds Addressables player content immediately for the active profile/target")
            );
            body.Add(buttonsRow);

            return body;
        }

        private void SaveSettings()
        {
            var activeProfiles = string.Join(",", _profilesToBuild
                .Where(entry => entry.Value)
                .Select(entry => entry.Key));

            EditorPrefs.SetString(_profilesSaveKey, activeProfiles);
            EditorPrefs.SetBool(_allToggleSaveKey, _isAll);
            EditorPrefs.SetBool(_cleanToggleSaveKey, _isCleanBeforeBuild);
            EditorPrefs.SetBool(_enabledToggleSaveKey, IsEnabled);
        }

        public static AddressablesPlayerBuildResult BuildAddressablesContent(
            BuildTarget target,
            BuildTargetGroup targetGroup,
            bool clean = false,
            int subtarget = 0)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                const string err =
                    "[Addressables] AddressableAssetSettingsDefaultObject.Settings is null. " +
                    "Please configure Addressables in the project.";
                Debug.LogError(err);
                throw new InvalidOperationException(err);
            }

            if (clean)
            {
                CleanAddressablesContent(settings);
            }

            Debug.Log($"[Addressables] Building Addressables player content for target '{target}' ({targetGroup})...");

            var bpo = new BuildPlayerOptions
            {
                target = target,
                targetGroup = targetGroup,
                subtarget = subtarget
            };

            var input = new AddressablesDataBuilderInput(settings, bpo);
            AddressablesPlayerBuildResult result = null;

            if (s_BuildPlayerContentMethod == null)
            {
                s_BuildPlayerContentMethod = typeof(AddressableAssetSettings).GetMethod(
                    "BuildPlayerContent",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(AddressablesPlayerBuildResult).MakeByRefType(),
                        typeof(AddressablesDataBuilderInput)
                    },
                    null);
            }

            if (s_BuildPlayerContentMethod != null)
            {
                var parameters = new object[] { null, input };
                s_BuildPlayerContentMethod.Invoke(null, parameters);
                result = parameters[0] as AddressablesPlayerBuildResult;
            }
            else
            {
                AddressableAssetSettings.BuildPlayerContent(out result);
            }

            if (result != null)
            {
                if (!string.IsNullOrEmpty(result.Error))
                {
                    Debug.LogError($"[Addressables] Build failed: {result.Error}");
                }
                else
                {
                    Debug.Log(
                        $"[Addressables] Content successfully built in {result.Duration:F1}s. " +
                        $"Locations: {result.LocationCount}, Output: '{result.OutputPath}'");
                }
            }

            return result;
        }

        public static void CleanAddressablesContent(AddressableAssetSettings settings = null)
        {
            settings ??= AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogWarning("[Addressables] Cannot clean Addressables: settings asset is null.");
                return;
            }

            Debug.Log("[Addressables] Cleaning Addressables build cache...");
            AddressableAssetSettings.CleanPlayerContent(null);
        }
    }
}
