using UnityEngine;

namespace BuildTool.Tracking
{
    public static class BuildInfoProvider
    {
        private const string BuildInfoPath = "BuildInfo";
        public const string NotForSharingDescription =
            "\nTHIS VERSION IS NOT REGISTERED, NOT FOR SHARING";

        private static readonly object s_Lock = new();
        private static BuildInfo s_BuildInfo;
        private static bool s_IsLoaded;

        public static BuildInfo BuildInfo
        {
            get
            {
                if (!s_IsLoaded)
                {
                    lock (s_Lock)
                    {
                        if (!s_IsLoaded)
                        {
                            s_BuildInfo = LoadBuildInfo();
                            s_IsLoaded = true;
                        }
                    }
                }

                return s_BuildInfo;
            }
            internal set
            {
                lock (s_Lock)
                {
                    s_BuildInfo = value;
                    s_IsLoaded = true;
                }
            }
        }

        public static bool IsRegistered => BuildInfo != null && BuildInfo.IsRegistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (!Application.isEditor)
            {
                var info = BuildInfo;

                if (IsRegistered)
                {
                    Debug.Log(
                        $"[BUILD-INFO] {Application.productName} v{Application.version} " +
                        $"(build:{info.Index}:{info.Commit}:{info.Date}) {Application.platform}");
                }
                else
                {
                    Debug.Log(
                        $"[BUILD-INFO] {Application.productName} v{Application.version} " +
                        $"(build:N/A:N/A:N/A) {Application.platform}{NotForSharingDescription}");
                }
            }
        }

        internal static void Reset()
        {
            lock (s_Lock)
            {
                s_BuildInfo = null;
                s_IsLoaded = false;
            }
        }

        private static BuildInfo LoadBuildInfo()
        {
            var buildInfo = Resources.Load<BuildInfo>(BuildInfoPath);
            return !buildInfo ? BuildInfo.Create() : buildInfo;
        }
    }
}
