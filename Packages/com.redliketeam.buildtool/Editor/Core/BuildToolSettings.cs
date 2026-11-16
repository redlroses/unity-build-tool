using System;
using System.Collections.Generic;
using BuildTool.Editor.DirectoryBuilders;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildTool.Editor
{
    [FilePath("ProjectSettings/BuildToolSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class BuildToolSettings : ScriptableSingleton<BuildToolSettings>
    {
        public List<ProfileConfig> ProfileConfigs = new();

        [Tooltip("Pattern for the common root directory of build sequences.\n" +
                 "Supported tokens:\n" +
                 "{Version} / {AppVersion} - Application version (e.g. 1.0.0)\n" +
#if BUILDTOOL_TRACKING
                 "{BuildIndex} - Build index from BuildTracker\n" +
#endif
                 "{Date} - Current Date (yyyy-MM-dd)\n" +
                 "{Date:FORMAT} - Custom date format (e.g. {Date:yyyy-MM-dd_HH-mm})\n" +
                 "{Time} - Current Time (HH-mm-ss)\n" +
                 "{ProductName} - PlayerSettings.productName")]
        public string RootFolderPattern = BuildTool.DefaultRootFolderPattern;

        /// <summary>
        /// Gets the base folder name (first path segment) or "Build".
        /// </summary>
        public string BuildFolderName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(RootFolderPattern))
                {
                    return "Build";
                }

                var trimmed = RootFolderPattern.Trim();
                var tokenIndex = trimmed.IndexOf('{');
                var prefix = tokenIndex >= 0 ? trimmed.Substring(0, tokenIndex).TrimEnd('/', '\\') : trimmed;

                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    return prefix;
                }

                return "Build";
            }
            set
            {
                if (!string.IsNullOrWhiteSpace(value) &&
                    (string.IsNullOrWhiteSpace(RootFolderPattern) || RootFolderPattern == "Build"))
                {
                    RootFolderPattern = value;
                }
            }
        }

        public void Save()
        {
            Save(true);
        }

        public ILocationPathNameBuilder GetPathBuilderOrDefault(BuildProfile profile)
        {
            if (profile == null)
            {
                return new PatternLocationPathBuilder();
            }

            var profilePath = AssetDatabase.GetAssetPath(profile);
            var profileGuid = !string.IsNullOrEmpty(profilePath) ? AssetDatabase.AssetPathToGUID(profilePath) : null;

            var profileConfig = ProfileConfigs.Find(pc =>
            {
                if (pc.Profile == profile) return true;
                if (!string.IsNullOrEmpty(profileGuid) && pc.Profile != null)
                {
                    var pcPath = AssetDatabase.GetAssetPath(pc.Profile);
                    return !string.IsNullOrEmpty(pcPath) && AssetDatabase.AssetPathToGUID(pcPath) == profileGuid;
                }
                return false;
            });

            var builder = profileConfig?.PathBuilder;

            if (builder != null)
            {
                return builder;
            }

            return new PatternLocationPathBuilder();
        }

        [Serializable]
        public class ProfileConfig
        {
            public BuildProfile Profile;

            [SerializeReference] public ILocationPathNameBuilder PathBuilder;
        }
    }
}

