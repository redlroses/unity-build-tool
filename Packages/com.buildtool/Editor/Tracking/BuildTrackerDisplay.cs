using UnityEditor;
using UnityEngine;

namespace BuildTool.Tracking.Editor
{
    [InitializeOnLoad]
    public static class BuildTrackerDisplay
    {
        private static readonly ProgressBarHandler s_ProgressBar;

        static BuildTrackerDisplay()
        {
            s_ProgressBar = BuildTracker.ProgressBarHandler;
            s_ProgressBar.Updated += OnProgressUpdated;
        }

        private static void OnProgressUpdated()
        {
            if (s_ProgressBar.IsComplete)
            {
                EditorUtility.ClearProgressBar();
                return;
            }

            if (Application.isBatchMode)
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (s_ProgressBar.IsComplete)
                {
                    EditorUtility.ClearProgressBar();
                    return;
                }

                EditorUtility.DisplayProgressBar("Build tracking", s_ProgressBar.Description, s_ProgressBar.Progress);
            };
        }
    }
}
