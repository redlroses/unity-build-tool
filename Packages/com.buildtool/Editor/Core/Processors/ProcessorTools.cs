using UnityEngine;

namespace BuildTool.Editor.Processors
{
    public static class ProcessorTools
    {
        public static string AppendProjectName(this string origin)
        {
            return $"{origin}_{ComputeDeterministicHash(Application.dataPath):X8}";
        }

        private static uint ComputeDeterministicHash(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return 0;
            }

            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in input)
                {
                    hash = (hash ^ c) * 16777619u;
                }
                return hash;
            }
        }
    }
}
