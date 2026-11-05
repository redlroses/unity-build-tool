using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEngine;

namespace BuildTool.Editor
{
    public static class ScriptingDefineUtility
    {
        public const string CliExperimentalDefine = "BUILDTOOL_CLI_EXPERIMENTAL";
        public const string TrackingDefine = "BUILDTOOL_TRACKING";

        public static bool IsDefined(string define)
        {
            if (string.IsNullOrWhiteSpace(define))
            {
                return false;
            }

            if (EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server)
            {
                var serverDefines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Server);
                if (ParseDefines(serverDefines).Contains(define, StringComparer.Ordinal))
                {
                    return true;
                }
            }

            var activeTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            if (activeTargetGroup != BuildTargetGroup.Unknown)
            {
                var activeNamed = NamedBuildTarget.FromBuildTargetGroup(activeTargetGroup);
                var defines = PlayerSettings.GetScriptingDefineSymbols(activeNamed);
                if (ParseDefines(defines).Contains(define, StringComparer.Ordinal))
                {
                    return true;
                }
            }

            var standaloneDefines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            return ParseDefines(standaloneDefines).Contains(define, StringComparer.Ordinal);
        }

        public static bool SetDefined(string define, bool enable)
        {
            if (string.IsNullOrWhiteSpace(define))
            {
                return false;
            }

            var targets = GetTargetGroupsToUpdate();
            var anyChanged = false;

            foreach (var target in targets)
            {
                var defines = PlayerSettings.GetScriptingDefineSymbols(target);
                var list = ParseDefines(defines);

                if (enable)
                {
                    if (!list.Contains(define, StringComparer.Ordinal))
                    {
                        list.Add(define);
                        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", list));
                        anyChanged = true;
                    }
                }
                else
                {
                    if (list.RemoveAll(d => string.Equals(d, define, StringComparison.Ordinal)) > 0)
                    {
                        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", list));
                        anyChanged = true;
                    }
                }
            }

            if (anyChanged)
            {
                AssetDatabase.SaveAssets();
                RequestRecompile();
            }

            return anyChanged;
        }

        public static IEnumerable<NamedBuildTarget> GetTargetGroupsToUpdate()
        {
            var targets = new HashSet<NamedBuildTarget>
            {
                NamedBuildTarget.Server,
                NamedBuildTarget.Standalone
            };

            var activeTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            if (activeTargetGroup != BuildTargetGroup.Unknown)
            {
                targets.Add(NamedBuildTarget.FromBuildTargetGroup(activeTargetGroup));
            }

            var selectedGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (selectedGroup != BuildTargetGroup.Unknown)
            {
                targets.Add(NamedBuildTarget.FromBuildTargetGroup(selectedGroup));
            }

            try
            {
                var profiles = BuildTool.GetBuildProfiles();
                foreach (var profile in profiles)
                {
                    if (profile != null)
                    {
                        var target = BuildTool.GetBuildTarget(profile);
                        var group = BuildPipeline.GetBuildTargetGroup(target);
                        if (group != BuildTargetGroup.Unknown)
                        {
                            targets.Add(NamedBuildTarget.FromBuildTargetGroup(group));
                        }
                    }
                }
            }
            catch
            {
            }

            return targets;
        }

        public static void RequestRecompile()
        {
            CompilationPipeline.RequestScriptCompilation();
        }

        public static void AddDefine(NamedBuildTarget namedBuildTarget, string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            var currentDefines = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
            var definesList = ParseDefines(currentDefines);

            if (!definesList.Contains(symbol, StringComparer.Ordinal))
            {
                definesList.Add(symbol);
                var newDefines = string.Join(";", definesList);
                PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, newDefines);
            }
        }

        public static void RemoveDefine(NamedBuildTarget namedBuildTarget, string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            var currentDefines = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
            var definesList = ParseDefines(currentDefines);

            if (definesList.RemoveAll(d => string.Equals(d, symbol, StringComparison.Ordinal)) > 0)
            {
                var newDefines = string.Join(";", definesList);
                PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, newDefines);
            }
        }

        public static void AddDefineToAllTargets(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            var anyChanged = false;
            foreach (var target in GetAllActiveNamedTargets())
            {
                var currentDefines = PlayerSettings.GetScriptingDefineSymbols(target);
                var definesList = ParseDefines(currentDefines);

                if (!definesList.Contains(symbol, StringComparer.Ordinal))
                {
                    definesList.Add(symbol);
                    var newDefines = string.Join(";", definesList);
                    PlayerSettings.SetScriptingDefineSymbols(target, newDefines);
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                AssetDatabase.SaveAssets();
            }
        }

        public static void RemoveDefineFromAllTargets(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            var anyChanged = false;
            foreach (var target in GetAllActiveNamedTargets())
            {
                var currentDefines = PlayerSettings.GetScriptingDefineSymbols(target);
                var definesList = ParseDefines(currentDefines);

                if (definesList.RemoveAll(d => string.Equals(d, symbol, StringComparison.Ordinal)) > 0)
                {
                    var newDefines = string.Join(";", definesList);
                    PlayerSettings.SetScriptingDefineSymbols(target, newDefines);
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                AssetDatabase.SaveAssets();
            }
        }

        public static bool ContainsSymbol(NamedBuildTarget namedBuildTarget, string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return false;
            }

            var currentDefines = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
            return ParseDefines(currentDefines).Contains(symbol, StringComparer.Ordinal);
        }

        public static bool ContainsSymbol(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return false;
            }

            var currentTarget = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            return ContainsSymbol(currentTarget, symbol);
        }

        public static IEnumerable<NamedBuildTarget> GetAllActiveNamedTargets()
        {
            var targetGroups = new[]
            {
                BuildTargetGroup.Standalone,
                BuildTargetGroup.Android,
                BuildTargetGroup.iOS,
                BuildTargetGroup.WebGL,
                BuildTargetGroup.PS4,
                BuildTargetGroup.PS5,
                BuildTargetGroup.XboxOne,
                BuildTargetGroup.Switch
            };

            foreach (var group in targetGroups)
            {
                yield return NamedBuildTarget.FromBuildTargetGroup(group);
            }
        }

        private static List<string> ParseDefines(string definesString)
        {
            if (string.IsNullOrWhiteSpace(definesString))
            {
                return new List<string>();
            }

            return definesString
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrEmpty(d))
                .ToList();
        }
    }
}
