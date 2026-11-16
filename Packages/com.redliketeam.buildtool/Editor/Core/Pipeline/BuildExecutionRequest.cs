using System;
using System.Collections.Generic;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildTool.Editor.Pipeline
{
    [Serializable]
    public class BuildExecutionRequest
    {
        public List<string> ProfileNames = new();
        public List<BuildProfile> Profiles = new();
        public bool IsDevelopment;
        public bool CleanBuild;
        public bool ClearFolderFirst;
        public bool RegisterVersion;
        public bool OpenFolder = true;
        public bool ContinueOnError;
        public string RootBuildPathOverride;
        [NonSerialized]
        public Dictionary<string, string> ExtensionArgs = new(StringComparer.OrdinalIgnoreCase);

        public string GetExtensionArg(string key, string defaultValue = null)
        {
            if (ExtensionArgs != null && ExtensionArgs.TryGetValue(key, out var val))
            {
                return val;
            }

            return defaultValue;
        }

        public bool GetExtensionBool(string key, bool defaultValue = false)
        {
            var val = GetExtensionArg(key);
            if (string.IsNullOrEmpty(val))
            {
                return defaultValue;
            }

            if (bool.TryParse(val, out var result))
            {
                return result;
            }

            if (val == "1" || val.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (val == "0" || val.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return defaultValue;
        }

        public int GetExtensionInt(string key, int defaultValue = 0)
        {
            var val = GetExtensionArg(key);
            if (string.IsNullOrEmpty(val))
            {
                return defaultValue;
            }

            return int.TryParse(val, out var result) ? result : defaultValue;
        }

        public void SetExtensionArg(string key, string value)
        {
            ExtensionArgs ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ExtensionArgs[key] = value;
        }

        public void ParseExtensionArgs(string rawArgs)
        {
            if (string.IsNullOrWhiteSpace(rawArgs))
            {
                return;
            }

            ExtensionArgs ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var pairs = rawArgs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var equalsIdx = pair.IndexOf('=');
                if (equalsIdx > 0)
                {
                    var k = pair.Substring(0, equalsIdx).Trim();
                    var v = pair.Substring(equalsIdx + 1).Trim();
                    if (!string.IsNullOrEmpty(k))
                    {
                        ExtensionArgs[k] = v;
                    }
                }
                else
                {
                    var k = pair.Trim();
                    if (!string.IsNullOrEmpty(k))
                    {
                        ExtensionArgs[k] = "true";
                    }
                }
            }
        }
    }
}
