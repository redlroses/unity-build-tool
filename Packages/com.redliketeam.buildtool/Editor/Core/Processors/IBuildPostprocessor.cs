using UnityEditor.Build.Profile;

namespace BuildTool.Editor.Processors
{
    public interface IBuildPostprocessor
    {
        void PostProcess(BuildProfile profile, string path);
    }
}

