using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
#if BUILDTOOL_TRACKING
using BuildTool.Tracking.Editor;
#endif
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildTool.Editor.DirectoryBuilders
{
    [Serializable]
    public class SimpleLocationPathNameBuilder : ILocationPathNameBuilder
    {
        private static readonly Dictionary<BuildTarget, string> s_Extensions = new()
        {
            { BuildTarget.NoTarget, "" },
            { BuildTarget.StandaloneOSX, ".app" },
            { BuildTarget.StandaloneWindows, ".exe" },
            { BuildTarget.iOS, "" },
            { BuildTarget.Android, ".apk" },
            { BuildTarget.StandaloneWindows64, ".exe" },
            { BuildTarget.WebGL, "" },
            { BuildTarget.WSAPlayer, ".appx" },
            { BuildTarget.StandaloneLinux64, ".x86_64" },
            { BuildTarget.tvOS, "" },
            { BuildTarget.Switch, ".nsp" },
            { BuildTarget.LinuxHeadlessSimulation, ".x86_64" },
            { BuildTarget.GameCoreXboxSeries, ".xbox" },
            { BuildTarget.GameCoreXboxOne, ".xbox" },
            { BuildTarget.PS5, ".pkg" },
            { BuildTarget.EmbeddedLinux, ".img" },
            { BuildTarget.QNX, ".qnx" },
            { BuildTarget.VisionOS, ".app" },
        };

        private static readonly Dictionary<BuildTarget, string> s_PlatformDescriptions = new()
        {
            { BuildTarget.NoTarget, "none" },
            { BuildTarget.StandaloneOSX, "macos" },
            { BuildTarget.StandaloneWindows, "win" },
            { BuildTarget.iOS, "ios" },
            { BuildTarget.Android, "android" },
            { BuildTarget.StandaloneWindows64, "win" },
            { BuildTarget.WebGL, "webgl" },
            { BuildTarget.WSAPlayer, "wsa" },
            { BuildTarget.StandaloneLinux64, "linux" },
            { BuildTarget.tvOS, "tvos" },
            { BuildTarget.Switch, "switch" },
            { BuildTarget.LinuxHeadlessSimulation, "linuxhs" },
            { BuildTarget.GameCoreXboxSeries, "xboxs" },
            { BuildTarget.GameCoreXboxOne, "xbox1" },
            { BuildTarget.PS5, "ps5" },
            { BuildTarget.EmbeddedLinux, "embedlin" },
            { BuildTarget.QNX, "qnx" },
            { BuildTarget.VisionOS, "visionos" },
        };

        public virtual string GetLocationPathName(BuildProfile profile, string buildPath = "")
        {
            var target = BuildTool.GetBuildTarget(profile);
            var (platformSuffix, extension) = GetPlatformSuffixAndExtension(target, profile);
            var productName = GetProductName(profile);
            var targetDirectory = CreateTargetDirectory(profile);

            var root = string.IsNullOrEmpty(buildPath) ? "" : buildPath;
            var dir = Path.Combine(root, targetDirectory);
            var fullPath = string.IsNullOrEmpty(extension) ? dir : Path.Combine(dir, $"{productName}{extension}");

            return fullPath.Replace('\\', '/');
        }

        protected virtual string CreateTargetDirectory(BuildProfile profile)
        {
            var parts = GetDirectoryParts(profile);
            return string.Join("_", parts.Where(p => !string.IsNullOrEmpty(p)));
        }

        protected virtual IEnumerable<string> GetDirectoryParts(BuildProfile profile)
        {
            yield return GetProductName(profile);
            yield return GetVersion();
        }

        protected virtual string GetVersion()
        {
#if BUILDTOOL_TRACKING
            var buildIndex = BuildTracker.BuildInfo != null && !string.IsNullOrEmpty(BuildTracker.BuildInfo.Index)
                ? BuildTracker.BuildInfo.Index
                : "1";
#else
            var buildIndex = "1";
#endif

            return $"v{Application.version}.{buildIndex}";
        }

        protected virtual string GetProductName(BuildProfile profile)
        {
            return profile != null ? profile.name : (PlayerSettings.productName ?? "Product");
        }

        protected virtual (string suffix, string extension) GetPlatformSuffixAndExtension(BuildTarget buildTarget)
        {
            return GetPlatformSuffixAndExtension(buildTarget, null);
        }

        protected virtual (string suffix, string extension) GetPlatformSuffixAndExtension(
            BuildTarget buildTarget,
            BuildProfile profile)
        {
            var platform = s_PlatformDescriptions.GetValueOrDefault(buildTarget, "unknown");
            var extension = s_Extensions.GetValueOrDefault(buildTarget, "unknown");

            if (buildTarget == BuildTarget.Android)
            {
                extension = DetermineAndroidExtension(profile);
            }

            return new(platform, extension);
        }

        private static readonly Regex s_BundlePattern =
            new(@"(?:^|\n)\s*(?:buildAppBundle|m_BuildAppBundle):\s*(.+?)\s*(?:\r?\n|$)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string DetermineAndroidExtension(BuildProfile profile)
        {
            if (profile != null)
            {
                try
                {
                    var so = new SerializedObject(profile);
                    var platformProp = so.FindProperty("m_PlatformBuildProfile");
                    var bundleProp = platformProp?.FindPropertyRelative("m_BuildAppBundle")
                        ?? so.FindProperty("m_BuildAppBundle");

                    if (bundleProp != null)
                    {
                        return bundleProp.boolValue ? ".aab" : ".apk";
                    }

                    var settingsProp = so.FindProperty("m_PlayerSettingsYaml.m_Settings");
                    if (settingsProp != null && settingsProp.isArray)
                    {
                        for (var i = 0; i < settingsProp.arraySize; i++)
                        {
                            var item = settingsProp.GetArrayElementAtIndex(i);
                            var lineProp = item.FindPropertyRelative("line");
                            if (lineProp != null && !string.IsNullOrEmpty(lineProp.stringValue))
                            {
                                var match = s_BundlePattern.Match(lineProp.stringValue);
                                if (match.Success)
                                {
                                    var val = match.Groups[1].Value.Trim().Trim('\'', '\"');
                                    if (bool.TryParse(val, out var b))
                                    {
                                        return b ? ".aab" : ".apk";
                                    }
                                    if (val == "1") return ".aab";
                                    if (val == "0") return ".apk";
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return EditorUserBuildSettings.buildAppBundle ? ".aab" : ".apk";
        }
    }
}
