using System;
using Newtonsoft.Json;
using UnityEngine;

namespace BuildTool.Tracking.Editor
{
    [Serializable]
    public class BuildHistoryEntry
    {
        [JsonProperty("buildIndex")]
        [SerializeField]
        public int BuildIndex;

        [JsonProperty("dateUtc")]
        [SerializeField]
        public string DateUtc;

        [JsonProperty("commit")]
        [SerializeField]
        public string Commit;

        [JsonProperty("branch")]
        [SerializeField]
        public string Branch;

        [JsonProperty("appVersion")]
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
