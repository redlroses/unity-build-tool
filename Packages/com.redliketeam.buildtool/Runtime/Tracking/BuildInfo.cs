using UnityEngine;
using UnityEngine.Scripting;

namespace BuildTool.Tracking
{
    [Preserve]
    public class BuildInfo : ScriptableObject
    {
        [field: SerializeField] public bool IsRegistered { get; private set; }
        [field: SerializeField] public string Index { get; private set; } = string.Empty;
        [field: SerializeField] public string Commit { get; private set; } = string.Empty;
        [field: SerializeField] public string Date { get; private set; } = string.Empty;

        public static BuildInfo Create()
        {
            return CreateInstance<BuildInfo>();
        }

        public static BuildInfo Create(string index, string commit, string date, bool isRegistered = true)
        {
            var instance = CreateInstance<BuildInfo>();
            instance.Initialize(index, commit, date, isRegistered);
            return instance;
        }

        private void Initialize(string index, string commit, string date, bool isRegistered)
        {
            Index = index ?? string.Empty;
            Commit = commit ?? string.Empty;
            Date = date ?? string.Empty;
            IsRegistered = isRegistered;
        }
    }
}
