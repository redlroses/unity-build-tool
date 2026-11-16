using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
    public class PatternLocationPathBuilder : SimpleLocationPathNameBuilder
    {
#if BUILDTOOL_TRACKING
        public const string DefaultPattern = "{ProfileName}_{Version}.{BuildIndex}/{ProductName}";
#else
        public const string DefaultPattern = "{ProfileName}_{Version}/{ProductName}";
#endif

        private static readonly ConcurrentDictionary<string, (Func<PathContext, string> resolver, string description)>
            s_RegisteredTokens = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex s_BuildFolderRegex =
            new(@"\{BuildFolder\}[\/]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex s_ExtRegex =
            new(@"\{Ext\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex s_TokenRegex =
            new(@"\{([a-zA-Z0-9_]+)(?::([^}]+))?\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [SerializeField]
        private bool _useCustomPattern;

        [SerializeField]
        private string _pattern = DefaultPattern;

        public bool UseCustomPattern
        {
            get => _useCustomPattern;
            set => _useCustomPattern = value;
        }

        public string Pattern
        {
            get => _useCustomPattern && !string.IsNullOrEmpty(_pattern) ? _pattern : DefaultPattern;
            set => _pattern = value;
        }

        public string RawPattern
        {
            get => _pattern;
            set => _pattern = value;
        }

        public static void RegisterToken(string tokenName, Func<PathContext, string> resolver, string description = "")
        {
            if (string.IsNullOrWhiteSpace(tokenName) || resolver == null)
            {
                return;
            }

            s_RegisteredTokens[tokenName.Trim()] = (resolver, description ?? "");
        }

        public static bool UnregisterToken(string tokenName)
        {
            if (string.IsNullOrWhiteSpace(tokenName))
            {
                return false;
            }

            return s_RegisteredTokens.TryRemove(tokenName.Trim(), out _);
        }

        public static IReadOnlyDictionary<string, string> GetAvailableTokens()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ProfileName"] = "Name of the build profile",
                ["ProductName"] = "PlayerSettings.productName or profile name",
                ["Version"] = "Application.version (e.g. 1.0.0)",
                ["AppVersion"] = "Alias for {Version}",
#if BUILDTOOL_TRACKING
                ["BuildIndex"] = "Build index from BuildTracker (e.g. 42)",
#endif
                ["Platform"] = "Platform code (e.g. win, android, macos)",
                ["Subtarget"] = "Subtarget if standalone (e.g. Server)",
                ["Date"] = "Current date in yyyy-MM-dd (or {Date:FORMAT})",
                ["Time"] = "Current time in HH-mm-ss",
                ["CompanyName"] = "PlayerSettings.companyName",
                ["BundleId"] = "Application.identifier"
            };

            foreach (var kvp in s_RegisteredTokens)
            {
                map[kvp.Key] = string.IsNullOrEmpty(kvp.Value.description) ? "Custom token" : kvp.Value.description;
            }

            return map;
        }

        public override string GetLocationPathName(BuildProfile profile, string buildPath = "")
        {
            var pattern = Pattern;
            var target = BuildTool.GetBuildTarget(profile);
            var (platformSuffix, extension) = GetPlatformSuffixAndExtension(target, profile);

            var rootBuildFolder = string.IsNullOrEmpty(buildPath)
                ? (string.IsNullOrEmpty(BuildToolSettings.instance.BuildFolderName)
                    ? "Build"
                    : BuildToolSettings.instance.BuildFolderName)
                : buildPath;

            var profileName = profile != null ? profile.name : "Profile";
            var productName = GetProductName(profile);
            var appVersion = BuildTool.GetProfileVersion(profile);
#if BUILDTOOL_TRACKING
            var buildIndex = BuildTool.GetCurrentBuildIndex();
#else
            var buildIndex = "";
#endif

            var subtarget = BuildTool.GetStandaloneBuildSubtarget(profile) switch
            {
                StandaloneBuildSubtarget.Server => "Server",
                _ => ""
            };

            var now = DateTime.Now;

            var context = new PathContext
            {
                Profile = profile,
                Target = target,
                ProfileName = profileName,
                ProductName = productName,
                AppVersion = appVersion,
                BuildIndex = buildIndex,
                PlatformSuffix = platformSuffix,
                Extension = extension,
                Subtarget = subtarget,
                RootBuildFolder = rootBuildFolder,
                Now = now
            };

            var result = pattern;

            result = s_BuildFolderRegex.Replace(result, "");
            result = s_ExtRegex.Replace(result, "");
#if !BUILDTOOL_TRACKING
            result = Regex.Replace(result, @"[._-]?\{BuildIndex\}", "", RegexOptions.IgnoreCase);
#endif

            result = s_TokenRegex.Replace(result, match =>
            {
                var token = match.Groups[1].Value;
                var param = match.Groups[2].Success ? match.Groups[2].Value : null;
                context.Parameter = param;

                if (s_RegisteredTokens.TryGetValue(token, out var custom))
                {
                    try
                    {
                        return custom.resolver(context) ?? "";
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PatternLocationPathBuilder] Error resolving token '{token}': {ex.Message}");
                        return match.Value;
                    }
                }

                switch (token.ToLowerInvariant())
                {
                    case "profilename":
                        return SanitizePathSegment(context.ProfileName ?? "");
                    case "productname":
                        return SanitizePathSegment(context.ProductName ?? "");
                    case "version":
                    case "appversion":
                        return SanitizePathSegment(context.AppVersion ?? "");
#if BUILDTOOL_TRACKING
                    case "buildindex":
                        return SanitizePathSegment(context.BuildIndex ?? "");
#endif
                    case "platform":
                        return SanitizePathSegment(context.PlatformSuffix ?? "");
                    case "subtarget":
                        return SanitizePathSegment(context.Subtarget ?? "");
                    case "date":
                        var dateFormat = !string.IsNullOrWhiteSpace(param) ? param : "yyyy-MM-dd";
                        return SanitizePathSegment(context.Now.ToString(dateFormat));
                    case "time":
                        return SanitizePathSegment(context.Now.ToString("HH-mm-ss"));
                    case "companyname":
                        return SanitizePathSegment(PlayerSettings.companyName ?? "");
                    case "bundleid":
                    case "applicationid":
                        return SanitizePathSegment(Application.identifier ?? "");
                    default:
                        return match.Value;
                }
            });

            result = result.Trim().TrimStart('/', '\\').TrimEnd('/', '\\');

            var combinedPath = string.IsNullOrEmpty(result)
                ? rootBuildFolder
                : Path.Combine(rootBuildFolder, result);

            var isUnc = combinedPath.StartsWith(@"\\") || combinedPath.StartsWith("//");
            combinedPath = combinedPath.Replace('\\', '/');
            combinedPath = Regex.Replace(combinedPath, @"(?<!^)/+", "/");
            if (isUnc)
            {
                combinedPath = "/" + Regex.Replace(combinedPath, @"^/+", "/");
            }

            if (combinedPath.Length > 3 || (combinedPath.Length == 3 && combinedPath[1] != ':'))
            {
                combinedPath = combinedPath.TrimEnd('/');
            }

            if (target == BuildTarget.Android)
            {
                if (extension == ".aab" && combinedPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                {
                    combinedPath = combinedPath.Substring(0, combinedPath.Length - 4) + ".aab";
                }
                else if (extension == ".apk" && combinedPath.EndsWith(".aab", StringComparison.OrdinalIgnoreCase))
                {
                    combinedPath = combinedPath.Substring(0, combinedPath.Length - 4) + ".apk";
                }
            }

            if (!string.IsNullOrEmpty(extension) &&
                !combinedPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                combinedPath = $"{combinedPath}{extension}";
            }

            return combinedPath;
        }

        protected static string SanitizePathSegment(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var chars = input.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalidChars, chars[i]) >= 0 && chars[i] != '/' && chars[i] != '\\')
                {
                    chars[i] = '_';
                }
            }

            return new string(chars).Replace("..", "__");
        }

        protected override string GetProductName(BuildProfile profile)
        {
            var projectProductName = PlayerSettings.productName;
            var raw = !string.IsNullOrWhiteSpace(projectProductName)
                ? projectProductName
                : (profile != null ? profile.name : "App");

            return SanitizePathSegment(raw);
        }
    }
}
