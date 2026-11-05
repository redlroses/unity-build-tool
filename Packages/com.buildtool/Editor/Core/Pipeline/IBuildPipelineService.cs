using UnityEditor;
using UnityEditor.Build.Profile;

namespace BuildTool.Editor.Pipeline
{
    /// <summary>
    /// Service interface abstracting Unity's static BuildPipeline operations.
    /// Allows mocking the build step in automated test suites without triggering an actual Unity player build.
    /// </summary>
    public interface IBuildPipelineService
    {
        BuildPlayerResult BuildPlayer(BuildPlayerWithProfileOptions options, BuildProfile profile = null);
    }
}

