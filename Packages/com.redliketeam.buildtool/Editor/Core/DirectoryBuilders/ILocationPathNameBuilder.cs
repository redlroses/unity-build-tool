using UnityEditor.Build.Profile;

namespace BuildTool.Editor.DirectoryBuilders
{
    public interface ILocationPathNameBuilder
    {
        string GetLocationPathName(BuildProfile profile, string buildPath = "");
    }
}
