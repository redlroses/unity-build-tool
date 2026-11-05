using System;
using System.Collections.Generic;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;

namespace BuildTool.Editor.Pipeline
{
    public class BuildContext
    {
        public BuildExecutionRequest Request { get; }
        public string RootBuildPath { get; set; }
        public string BuildIndex { get; set; }
        public IReadOnlyList<BuildProfile> Profiles { get; internal set; }
        public BuildProfile CurrentProfile { get; internal set; }
        public List<(BuildProfile Profile, BuildReport Report)> CompletedReports { get; } = new();
        public Dictionary<string, object> State { get; } = new(StringComparer.OrdinalIgnoreCase);

        public BuildContext(BuildExecutionRequest request)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
        }

        public T GetState<T>(string key, T defaultValue = default)
        {
            if (State != null && State.TryGetValue(key, out var val) && val is T typedVal)
            {
                return typedVal;
            }

            return defaultValue;
        }

        public void SetState<T>(string key, T value)
        {
            State[key] = value;
        }
    }
}

