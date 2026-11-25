using System;
using UnityEngine;

namespace BuildTool.Tracking.Editor
{
    [Serializable]
    public class BuildHistoryEntry
    {
        [SerializeField]
        public int BuildIndex;

        [SerializeField]
        public string DateUtc;

        [SerializeField]
        public string Commit;

        [SerializeField]
        public string Branch;

        [SerializeField]
        public string AppVersion;

        public BuildHistoryEntry()
        {
        }

        public BuildHistoryEntry(int buildIndex, string dateUtc, string commit, string branch, string appVersion)
        {
            BuildIndex = buildIndex;
            DateUtc = dateUtc;
            Commit = commit;
            Branch = branch;
            AppVersion = appVersion;
        }
    }
}
