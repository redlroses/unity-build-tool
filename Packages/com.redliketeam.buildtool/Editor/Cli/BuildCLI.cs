using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BuildTool.Editor.Pipeline;
using UnityEditor;
using UnityEngine;

namespace BuildTool.Editor
{
    /// <summary>
    /// Classic Unity batchmode entry point for sequential builds.
    /// Can be invoked via:
    ///   Unity.exe -batchmode -projectPath "..." -executeMethod BuildTool.Editor.BuildCLI.Build
    ///   -profiles "Profile1,Profile2" ...
    /// </summary>
    public static class BuildCLI
    {
        public static void Build()
        {
            try
            {
                Build(Environment.GetCommandLineArgs(), exitOnError: Application.isBatchMode);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildCLI] Unhandled exception in Build: {ex}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        public static void CleanBuild()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                var request = ParseCommandLineArgs(args);
                request.CleanBuild = true;
                Build(request, exitOnError: Application.isBatchMode);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildCLI] Unhandled exception in CleanBuild: {ex}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        public static BuildExecutionRequest ParseCommandLineArgs(string[] args)
        {
            var request = new BuildExecutionRequest
            {
                OpenFolder = !Application.isBatchMode
            };
            if (args == null)
            {
                return request;
            }

            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "-profiles", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    var profiles = args[i + 1].Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    request.ProfileNames.AddRange(
                        profiles.Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)));
                    i++;
                }
                else if (string.Equals(args[i], "-dev", StringComparison.OrdinalIgnoreCase))
                {
                    request.IsDevelopment = ParseOptionalBoolValue(args, ref i, true);
                }
                else if (string.Equals(args[i], "-cleanBuild", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(args[i], "-clean-build", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(args[i], "-clean", StringComparison.OrdinalIgnoreCase))
                {
                    request.CleanBuild = ParseOptionalBoolValue(args, ref i, true);
                }
                else if (string.Equals(args[i], "-noCleanBuild", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(args[i], "-no-clean-build", StringComparison.OrdinalIgnoreCase))
                {
                    request.CleanBuild = false;
                }
                else if (string.Equals(args[i], "-clear", StringComparison.OrdinalIgnoreCase))
                {
                    request.ClearFolderFirst = ParseOptionalBoolValue(args, ref i, true);
                }
                else if (string.Equals(args[i], "-register", StringComparison.OrdinalIgnoreCase))
                {
                    request.RegisterVersion = ParseOptionalBoolValue(args, ref i, true);
                }
                else if (string.Equals(args[i], "-continueOnError", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(args[i], "-continue-on-error", StringComparison.OrdinalIgnoreCase))
                {
                    request.ContinueOnError = ParseOptionalBoolValue(args, ref i, true);
                }
                else if ((string.Equals(args[i], "-outDir", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(args[i], "-out-dir", StringComparison.OrdinalIgnoreCase)) &&
                         i + 1 < args.Length)
                {
                    request.RootBuildPathOverride = args[i + 1];
                    i++;
                }
                else if (string.Equals(args[i], "-noOpenFolder", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(args[i], "-no-open-folder", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(args[i], "-no-open", StringComparison.OrdinalIgnoreCase))
                {
                    request.OpenFolder = false;
                }
                else if (string.Equals(args[i], "-openFolder", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(args[i], "-open-folder", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(args[i], "-open", StringComparison.OrdinalIgnoreCase))
                {
                    request.OpenFolder = ParseOptionalBoolValue(args, ref i, true);
                }
                else if ((string.Equals(args[i], "-extArgs", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(args[i], "-ext-args", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(args[i], "-ext", StringComparison.OrdinalIgnoreCase)) &&
                         i + 1 < args.Length)
                {
                    request.ParseExtensionArgs(args[i + 1]);
                    i++;
                }
                else if (string.Equals(args[i], "-addressables", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(args[i], "-buildAddressables", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(args[i], "-build-addressables", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        request.SetExtensionArg("addressables", args[i + 1]);
                        i++;
                    }
                    else
                    {
                        request.SetExtensionArg("addressables", "true");
                    }
                }
                else if (string.Equals(args[i], "-cleanAddressables", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(args[i], "-clean-addressables", StringComparison.OrdinalIgnoreCase))
                {
                    var cleanVal = ParseOptionalBoolValue(args, ref i, true);
                    request.SetExtensionArg("addressables.clean", cleanVal ? "true" : "false");
                }
            }

            return request;
        }

        private static bool ParseOptionalBoolValue(string[] args, ref int index, bool defaultValue = true)
        {
            if (index + 1 < args.Length)
            {
                var next = args[index + 1];
                if (bool.TryParse(next, out var parsed))
                {
                    index++;
                    return parsed;
                }

                if (string.Equals(next, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(next, "yes", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    return true;
                }

                if (string.Equals(next, "0", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(next, "no", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    return false;
                }
            }

            return defaultValue;
        }

        private static Action s_CachedPump;
        private static SynchronizationContext s_CachedContext;

        private static void PumpSynchronizationContext()
        {
            var context = SynchronizationContext.Current;
            if (context == null)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }

            if (context != s_CachedContext)
            {
                s_CachedContext = context;
                var method = context.GetType().GetMethod(
                    "Exec", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null)
                {
                    try
                    {
                        s_CachedPump = (Action)Delegate.CreateDelegate(typeof(Action), context, method);
                    }
                    catch
                    {
                        s_CachedPump = () => method.Invoke(context, null);
                    }
                }
                else
                {
                    s_CachedPump = null;
                }
            }

            if (s_CachedPump != null)
            {
                s_CachedPump();
            }
            else
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        /// <summary>
        /// Synchronously waits for an asynchronous task on the Unity main thread without deadlocking.
        /// Pumps Unity's native SynchronizationContext via reflection without replacing the context.
        /// </summary>
        public static T WaitForTask<T>(Func<Task<T>> taskFactory)
        {
            var task = taskFactory();
            if (task.IsCompleted)
            {
                return task.GetAwaiter().GetResult();
            }

            var start = DateTime.UtcNow;
            while (!task.IsCompleted)
            {
                PumpSynchronizationContext();
                Thread.Sleep(5);
                if ((DateTime.UtcNow - start).TotalHours > 12)
                {
                    throw new TimeoutException("Task execution timed out in WaitForTask.");
                }
            }

            PumpSynchronizationContext();
            return task.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Synchronously waits for an existing Task on the Unity main thread.
        /// </summary>
        public static T WaitForTask<T>(Task<T> task)
        {
            return WaitForTask(() => task);
        }

        public static BuildExecutionResult Build(BuildExecutionRequest request, bool exitOnError = true)
        {
            return WaitForTask(() => BuildAsync(request, exitOnError));
        }

        public static BuildExecutionResult Build(string[] args, bool exitOnError = true)
        {
            var request = ParseCommandLineArgs(args);
            return Build(request, exitOnError);
        }

        public static async Task<BuildExecutionResult> BuildAsync(string[] args, bool exitOnError = false)
        {
            var request = ParseCommandLineArgs(args);
            return await BuildAsync(request, exitOnError);
        }

        public static async Task<BuildExecutionResult> BuildAsync(
            BuildExecutionRequest request, bool exitOnError = false)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            Debug.Log(
                $"[BuildCLI] Starting sequential build with {request.ProfileNames.Count} profiles requested...");

            BuildExecutionResult result;
            try
            {
                result = await BuildPipelineRunner.RunAsync(request);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildCLI] Fatal exception during build: {ex.Message}");
                result = BuildExecutionResult.CreateFailed(ex.Message);
            }

            if (!result.Success)
            {
                Debug.LogError($"[BuildCLI] Build failed: {result.ErrorMessage}");
                if (exitOnError && Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
            else
            {
                if (result.Reports != null && result.Reports.Count > 0)
                {
                    Debug.Log(BuildReportFormatter.FormatSummaryTable(result.Reports, result.RootBuildPath));
                }

                Debug.Log(
                    $"[BuildCLI] Build completed successfully: {result.SucceededProfiles}/{result.TotalProfiles} " +
                    $"profiles in {result.TotalSeconds:F1}s.");
                if (exitOnError && Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }

            return result;
        }

        public static void ListExtensions()
        {
            try
            {
                var response = BuildPipelineRunner.GetExtensionInfo();
                var text = BuildPipelineRunner.FormatExtensionInfo(response);
                Debug.Log(text);

                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildCLI] Unhandled exception in ListExtensions: {ex}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }
    }
}
