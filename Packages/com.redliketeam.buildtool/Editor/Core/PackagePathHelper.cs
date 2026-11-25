using System;
using System.IO;
using UnityEditor;
using UnityEngine.UIElements;

namespace BuildTool.Editor
{
    internal static class PackagePathHelper
    {
        public const string DefaultPackageId = "com.redliketeam.buildtool";
        private const string HelperRelativeScriptPath = "/Editor/Core/PackagePathHelper.cs";
        private static readonly Lazy<string> s_PackageRoot = new(ResolvePackageRoot);
        private static readonly Lazy<string> s_PackagePhysicalRoot = new(ResolvePackagePhysicalRoot);

        public static string PackageRoot => s_PackageRoot.Value;

        public static string PackagePhysicalRoot => s_PackagePhysicalRoot.Value;

        private static string ResolvePackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackagePathHelper).Assembly);
            if (info != null && !string.IsNullOrEmpty(info.assetPath))
            {
                return info.assetPath;
            }

            return FindAssetInstallRoot() ?? $"Packages/{DefaultPackageId}";
        }

        private static string ResolvePackagePhysicalRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackagePathHelper).Assembly);
            return info != null && !string.IsNullOrEmpty(info.resolvedPath)
                ? info.resolvedPath
                : Path.GetFullPath(PackageRoot);
        }

        private static string FindAssetInstallRoot()
        {
            var guids = AssetDatabase.FindAssets("PackagePathHelper t:MonoScript");
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (assetPath.EndsWith(HelperRelativeScriptPath, StringComparison.OrdinalIgnoreCase))
                {
                    return assetPath.Substring(0, assetPath.Length - HelperRelativeScriptPath.Length);
                }
            }

            return null;
        }

        public static string GetAssetPath(string relativeSubPath)
        {
            if (string.IsNullOrEmpty(relativeSubPath))
            {
                return PackageRoot;
            }

            var trimmed = relativeSubPath.TrimStart('/', '\\');
            return $"{PackageRoot}/{trimmed}";
        }

        public static string GetFilePath(string relativeSubPath)
        {
            if (string.IsNullOrEmpty(relativeSubPath))
            {
                return PackagePhysicalRoot;
            }

            var trimmed = relativeSubPath.TrimStart('/', '\\');
            return Path.Combine(PackagePhysicalRoot, trimmed);
        }

        public static StyleSheet LoadStyleSheet(string relativeSubPath, string searchPattern = null)
        {
            var path = GetAssetPath(relativeSubPath);
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (styleSheet == null && !string.IsNullOrEmpty(searchPattern))
            {
                var guids = AssetDatabase.FindAssets($"{searchPattern} t:StyleSheet");
                if (guids.Length > 0)
                {
                    var foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(foundPath);
                }
            }

            return styleSheet;
        }
    }
}
