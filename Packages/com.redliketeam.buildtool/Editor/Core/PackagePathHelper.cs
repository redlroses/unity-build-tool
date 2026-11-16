using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace BuildTool.Editor
{
    /// <summary>
    /// Utility for dynamically resolving paths to assets, styles, and files inside the package,
    /// regardless of whether the package is embedded in Packages/, referenced in Library/PackageCache/,
    /// or imported directly into Assets/.
    /// </summary>
    internal static class PackagePathHelper
    {
        public const string DefaultPackageId = "com.redliketeam.buildtool";
        private static string s_PackageRoot;

        /// <summary>
        /// Gets the resolved project-relative root path of this package (e.g. "Packages/com.redliketeam.buildtool").
        /// </summary>
        public static string PackageRoot
        {
            get
            {
                if (string.IsNullOrEmpty(s_PackageRoot))
                {
                    var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackagePathHelper).Assembly);
                    s_PackageRoot = info != null && !string.IsNullOrEmpty(info.assetPath)
                        ? info.assetPath
                        : $"Packages/{DefaultPackageId}";
                }

                return s_PackageRoot;
            }
        }

        /// <summary>
        /// Returns a full asset path for a relative sub-path within the package.
        /// Example: GetAssetPath("Editor/Core/BuildWindow.uss") -> "Packages/com.redliketeam.buildtool/Editor/Core/BuildWindow.uss"
        /// </summary>
        public static string GetAssetPath(string relativeSubPath)
        {
            if (string.IsNullOrEmpty(relativeSubPath))
            {
                return PackageRoot;
            }

            var trimmed = relativeSubPath.TrimStart('/', '\\');
            return $"{PackageRoot}/{trimmed}";
        }

        /// <summary>
        /// Loads a StyleSheet at the specified package-relative path with fallback to AssetDatabase.FindAssets.
        /// </summary>
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
