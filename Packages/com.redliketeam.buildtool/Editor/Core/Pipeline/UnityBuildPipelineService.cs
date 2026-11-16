using UnityEditor;
using UnityEditor.Build.Profile;

namespace BuildTool.Editor.Pipeline
{
    /// <summary>
    /// Default production implementation of <see cref="IBuildPipelineService"/>
    /// that executes the build via Unity's native
    /// <see cref="BuildPipeline.BuildPlayer(BuildPlayerWithProfileOptions)"/>.
    /// </summary>
    public class UnityBuildPipelineService : IBuildPipelineService
    {
        public BuildPlayerResult BuildPlayer(BuildPlayerWithProfileOptions options, BuildProfile profile = null)
        {
            var buildReport = BuildPipeline.BuildPlayer(options);
            return BuildPlayerResult.FromReport(buildReport);
        }
    }
}
