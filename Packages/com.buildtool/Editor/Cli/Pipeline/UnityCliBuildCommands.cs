using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildTool.Editor.Pipeline;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildTool.Editor.Cli
{
    public static class UnityCliBuildCommands
    {
        /// <summary>
        /// Unity CLI command to run sequential builds.
        /// Invoked via: unity command build:run [args] OR unity run --command build:run [args]
        /// </summary>
        [CliCommand("build:run", "Executes sequential builds with specified profiles and parameters")]
        public static async Task<string> BuildRun(
            [CliArg("profiles", "Comma-separated list of profile names or GUIDs (empty for active/all)")]
            string profiles = "",
            [CliArg("dev", "Create a development build")] bool dev = false,
            [CliArg("clean-build", "Clean Unity build cache before building (BuildOptions.CleanBuildCache)")]
            bool cleanBuild = false,
            [CliArg("no-clean-build", "Do not clean Unity build cache before building")]
            bool noCleanBuild = false,
            [CliArg("clear", "Clear output folder before build")] bool clear = false,
            [CliArg("register", "Register version in remote tracker")] bool register = false,
            [CliArg("continue-on-error", "Continue next profile build if previous fails")]
            bool continueOnError = false,
            [CliArg("out-dir", "Custom output root directory for the builds")] string outDir = "",
            [CliArg("open-folder", "Open build folder in explorer after completion (default: true)")]
            bool openFolder = true,
            [CliArg("no-open-folder", "Do not open build folder in explorer after completion")]
            bool noOpenFolder = false,
            [CliArg("addressables",
                "Force build Addressables ('true'/'all', comma-separated profile names, or 'false')")]
            string addressables = "",
            [CliArg("clean-addressables", "Clean Addressables cache before building")]
            bool cleanAddressables = false,
            [CliArg("ext-args", "Key-value args for extensions (e.g. zip=all;zip.exclude=*DontShip*)")]
            string extArgs = "",
            [CliArg("json", "Format output as JSON (default: true)")]
            bool json = true,
            [CliArg("no-json", "Format output as plain text table instead of JSON")]
            bool noJson = false)
        {
            var effectiveCleanBuild = cleanBuild && !noCleanBuild;
            var effectiveOpenFolder = openFolder && !noOpenFolder;
            var effectiveJson = json && !noJson;

            var request = new BuildExecutionRequest
            {
                IsDevelopment = dev,
                CleanBuild = effectiveCleanBuild,
                ClearFolderFirst = clear,
                RegisterVersion = register,
                ContinueOnError = continueOnError,
                OpenFolder = effectiveOpenFolder,
                RootBuildPathOverride = string.IsNullOrWhiteSpace(outDir) ? null : outDir
            };

            if (!string.IsNullOrWhiteSpace(addressables))
            {
                request.SetExtensionArg("addressables", addressables);
            }

            if (cleanAddressables)
            {
                request.SetExtensionArg("addressables.clean", "true");
            }

            if (!string.IsNullOrWhiteSpace(profiles))
            {
                var names = profiles.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();
                request.ProfileNames.AddRange(names);
            }

            if (!string.IsNullOrWhiteSpace(extArgs))
            {
                request.ParseExtensionArgs(extArgs);
            }

            Debug.Log(
                $"[UnityCliBuildCommands] Executing build:run with {request.ProfileNames.Count} profiles requested " +
                $"(Dev: {dev}, CleanBuild: {effectiveCleanBuild}, Clear: {clear}, Register: {register}, " +
                $"ContinueOnError: {continueOnError})");

            BuildExecutionResult result;
            try
            {
                result = await BuildPipelineRunner.RunAsync(request);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityCliBuildCommands] Fatal exception during build:run: {ex.Message}");
                result = BuildExecutionResult.CreateFailed(ex.Message);
            }

            if (effectiveJson)
            {
                return result.ToJson(pretty: true);
            }

            if (result.Reports != null && result.Reports.Count > 0)
            {
                return BuildReportFormatter.FormatSummaryTable(result.Reports, result.RootBuildPath);
            }

            return result.Success
                ? $"Build succeeded: {result.SucceededProfiles}/{result.TotalProfiles} profiles built in " +
                  $"{result.TotalSeconds:F1}s."
                : $"Build failed: {result.FailedProfiles}/{result.TotalProfiles} failed. Error: {result.ErrorMessage}";
        }

        [CliCommand("build:clean",
            "Executes sequential clean builds with specified profiles (BuildOptions.CleanBuildCache)")]
        public static Task<string> BuildClean(
            [CliArg("profiles", "Comma-separated list of profile names or GUIDs (empty for active/all)")]
            string profiles = "",
            [CliArg("dev", "Create a development build")] bool dev = false,
            [CliArg("clear", "Clear output folder before build")] bool clear = false,
            [CliArg("register", "Register version in remote tracker")] bool register = false,
            [CliArg("continue-on-error", "Continue next profile build if previous fails")]
            bool continueOnError = false,
            [CliArg("out-dir", "Custom output root directory for the builds")] string outDir = "",
            [CliArg("open-folder", "Open build folder in explorer after completion (default: true)")]
            bool openFolder = true,
            [CliArg("no-open-folder", "Do not open build folder in explorer after completion")]
            bool noOpenFolder = false,
            [CliArg("addressables",
                "Force build Addressables ('true'/'all', comma-separated profile names, or 'false')")]
            string addressables = "",
            [CliArg("clean-addressables", "Clean Addressables cache before building")]
            bool cleanAddressables = false,
            [CliArg("ext-args", "Key-value args for extensions (e.g. zip=all;zip.exclude=*DontShip*)")]
            string extArgs = "",
            [CliArg("json", "Format output as JSON (default: true)")]
            bool json = true,
            [CliArg("no-json", "Format output as plain text table instead of JSON")]
            bool noJson = false)
        {
            return BuildRun(
                profiles: profiles,
                dev: dev,
                cleanBuild: true,
                noCleanBuild: false,
                clear: clear,
                register: register,
                continueOnError: continueOnError,
                outDir: outDir,
                openFolder: openFolder,
                noOpenFolder: noOpenFolder,
                addressables: addressables,
                cleanAddressables: cleanAddressables,
                extArgs: extArgs,
                json: json,
                noJson: noJson);
        }

        [CliCommand("build:list-profiles", "Lists all available build profiles in the project")]
        public static string ListProfiles(
            [CliArg("json", "Format output as JSON (default: true)")] bool json = true,
            [CliArg("no-json", "Format output as plain text list")] bool noJson = false)
        {
            var effectiveJson = json && !noJson;
            var profiles = BuildTool.GetBuildProfiles();
            var active = BuildProfile.GetActiveBuildProfile();

            var list = new ProfileListResponse();

            foreach (var p in profiles)
            {
                if (p == null)
                {
                    continue;
                }

                var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p));
                var target = BuildTool.GetBuildTarget(p);

                list.Profiles.Add(new ProfileInfo
                {
                    Name = p.name,
                    Guid = guid,
                    BuildTarget = target.ToString(),
                    IsActive = (p == active)
                });
            }

            if (effectiveJson)
            {
                return JsonUtility.ToJson(list, true);
            }

            var lines = list.Profiles.Select(
                p => $"{(p.IsActive ? "* " : "  ")}{p.Name} [{p.BuildTarget}] (GUID: {p.Guid})");
            return string.Join(Environment.NewLine, lines);
        }

        [CliCommand("build:list-extensions",
            "Lists all available build extensions and their configurable CLI parameters")]
        public static string ListExtensions(
            [CliArg("json", "Format output as JSON (default: true)")] bool json = true,
            [CliArg("no-json", "Format output as plain text list")] bool noJson = false)
        {
            var effectiveJson = json && !noJson;
            var response = BuildPipelineRunner.GetExtensionInfo();

            if (effectiveJson)
            {
                return JsonUtility.ToJson(response, true);
            }

            return BuildPipelineRunner.FormatExtensionInfo(response);
        }

        [Serializable]
        public class ProfileListResponse
        {
            public List<ProfileInfo> Profiles = new();
        }

        [Serializable]
        public class ProfileInfo
        {
            public string Name;
            public string Guid;
            public string BuildTarget;
            public bool IsActive;
        }
    }
}
