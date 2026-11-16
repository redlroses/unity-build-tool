using System;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace BuildTool.Editor.DirectoryBuilders
{
    public class PathContext
    {
        public BuildProfile Profile { get; set; }
        public BuildTarget Target { get; set; }
        public string ProfileName { get; set; }
        public string ProductName { get; set; }
        public string AppVersion { get; set; }
        public string BuildIndex { get; set; }
        public string PlatformSuffix { get; set; }
        public string Extension { get; set; }
        public string Subtarget { get; set; }
        public string RootBuildFolder { get; set; }
        public DateTime Now { get; set; }
        public string Parameter { get; set; }
    }
}
