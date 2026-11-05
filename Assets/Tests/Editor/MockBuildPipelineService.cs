using System;
using System.Collections.Generic;
using BuildTool.Editor.Pipeline;
using UnityEditor;
using UnityEditor.Build.Profile;

namespace BuildTool.Editor.Tests
{
    /// <summary>
    /// Test mock implementation of <see cref="IBuildPipelineService"/>.
    /// Records all build invocations and allows configuring custom returns (Success, Failure, Exceptions)
    /// without executing any actual Unity player builds.
    /// </summary>
    public class MockBuildPipelineService : IBuildPipelineService
    {
        public List<(BuildPlayerWithProfileOptions Options, BuildProfile Profile)> Invocations { get; } = new();

        public Func<BuildPlayerWithProfileOptions, BuildProfile, BuildPlayerResult> CustomHandler { get; set; }

        public BuildPlayerResult BuildPlayer(BuildPlayerWithProfileOptions options, BuildProfile profile = null)
        {
            Invocations.Add((options, profile));

            if (CustomHandler != null)
            {
                return CustomHandler(options, profile);
            }

            return BuildPlayerResult.CreateSuccessful(options.locationPathName);
        }
    }
}

