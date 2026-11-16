using UnityEditor.Build.Profile;

namespace BuildTool.Editor.Processors
{
    public interface IBuildPreprocessor
    {
        void PreProcess(BuildProfile profile);
    }
}
