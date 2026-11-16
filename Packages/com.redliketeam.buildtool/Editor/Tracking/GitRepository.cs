using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Debug = UnityEngine.Debug;

namespace BuildTool.Tracking.Editor
{
    public class GitRepository
    {
        private static string s_ResolvedGitPath;
        private readonly bool _enableLog;
        private readonly string _repoPath;

        private static string ResolveGitExecutable()
        {
            if (!string.IsNullOrEmpty(s_ResolvedGitPath))
            {
                return s_ResolvedGitPath;
            }

            var candidates = new[]
            {
                "git",
                @"C:\Program Files\Git\cmd\git.exe",
                @"C:\Program Files\Git\bin\git.exe",
                @"C:\Program Files (x86)\Git\cmd\git.exe",
                "/usr/bin/git",
                "/usr/local/bin/git",
                "/opt/homebrew/bin/git"
            };

            foreach (var candidate in candidates)
            {
                if (candidate == "git")
                {
                    try
                    {
                        var psi = new ProcessStartInfo("git", "--version")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        using var p = Process.Start(psi);
                        if (p != null)
                        {
                            p.WaitForExit(3000);
                            if (p.ExitCode == 0)
                            {
                                s_ResolvedGitPath = "git";
                                return s_ResolvedGitPath;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                else if (File.Exists(candidate))
                {
                    s_ResolvedGitPath = candidate;
                    return s_ResolvedGitPath;
                }
            }

            s_ResolvedGitPath = "git";
            return s_ResolvedGitPath;
        }

        public GitRepository(string repoPath, bool enableLog = false)
        {
            if (string.IsNullOrEmpty(repoPath))
            {
                throw new ArgumentNullException(nameof(repoPath));
            }

            _repoPath = Path.GetFullPath(repoPath);
            _enableLog = enableLog;

            if (!Directory.Exists(_repoPath))
            {
                Directory.CreateDirectory(_repoPath);
            }
        }

        public async Task<string> SendCommand(
            string command,
            string successMessage = null,
            string errorMessage = null,
            bool ignoreRepoExist = false)
        {
            if (ignoreRepoExist)
            {
                return await StartProcess(CreateProcessInfo(command), successMessage, errorMessage)
                    .ConfigureAwait(false);
            }

            if (!HasRepository())
            {
                throw new FileNotFoundException(
                    $"Repository not found on path {_repoPath}. Create or clone repository first.");
            }

            return await StartProcess(CreateProcessInfo(command), successMessage, errorMessage).ConfigureAwait(false);
        }

        public async Task Init()
        {
            if (!Directory.Exists(_repoPath))
            {
                Directory.CreateDirectory(_repoPath);
            }

            await SendCommand("init",
                "Repository initialized successfully.",
                "Failed to initialize repository.", true);
        }

        public async Task Fetch()
        {
            await SendCommand("fetch origin",
                "Fetched successfully.",
                "Failed to fetch.");
        }

        public async Task<bool> HasRemoteRepository(string repoUrl)
        {
            if (string.IsNullOrEmpty(repoUrl))
            {
                throw new ArgumentNullException(nameof(repoUrl));
            }

            if (!HasRepository())
            {
                return false;
            }

            var remotesResult =
                await SendCommand("remote",
                    "Remotes list received",
                    "Failed to receive remotes list");

            var remotes = remotesResult.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            if (!remotes.Contains("origin"))
            {
                return false;
            }

            var originUrl = (await SendCommand("config --get remote.origin.url",
                "Remote origin from config received",
                "Failed to receive remote origin from config")).Trim();

            return string.Equals(originUrl, repoUrl.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public bool HasRepository()
        {
            if (!Directory.Exists(_repoPath))
            {
                return false;
            }

            var gitPath = Path.Combine(_repoPath, ".git");
            return Directory.Exists(gitPath) || File.Exists(gitPath);
        }

        public async Task<string> GetCurrentBranch()
        {
            var branchName = await SendCommand("rev-parse --abbrev-ref HEAD",
                "Current branch obtained",
                "Failed to obtain current branch");

            var trimmed = branchName.Trim();
            if (string.Equals(trimmed, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                var ciBranch = Environment.GetEnvironmentVariable("GITHUB_REF_NAME")
                    ?? Environment.GetEnvironmentVariable("CI_COMMIT_BRANCH")
                    ?? Environment.GetEnvironmentVariable("CI_COMMIT_REF_NAME");

                if (!string.IsNullOrWhiteSpace(ciBranch))
                {
                    return ciBranch.Trim();
                }

                try
                {
                    var shortSha = await GetCurrentShortCommitCode();
                    return !string.IsNullOrEmpty(shortSha) ? $"detached-{shortSha}" : "HEAD";
                }
                catch
                {
                    return "HEAD";
                }
            }

            return trimmed;
        }

        public async Task<string> GetCurrentShortCommitCode()
        {
            var commitCode = await SendCommand("rev-parse --short HEAD",
                "Short commit code obtained",
                "Failed to obtain short commit code");

            Log($"Current commit code: {commitCode}");

            return commitCode.Trim();
        }

        public async Task<string> GetRepositoryUrl()
        {
            var url = await SendCommand("config --get remote.origin.url",
                "Repository URL obtained",
                "Failed to obtain repository url");

            return url.Trim();
        }

        public async Task<string> GetUncommittedChanges()
        {
            Log("Pending: Checking for uncommitted changes...");

            var output = await SendCommand("status --porcelain",
                "Checked for uncommitted changes",
                "Failed to check for uncommitted changes");

            return output;
        }

        public async Task<bool> HasOriginBranch(string branchName)
        {
            var output = await SendCommand($"ls-remote --heads origin {branchName}",
                "Checked for origin branch",
                "Failed to check for origin branch");

            return !string.IsNullOrEmpty(output);
        }

        public async Task<bool> HasLocalBranch(string branchName)
        {
            var output = await SendCommand($"branch --list {branchName}",
                "Branch list obtained",
                "Failed to obtain branch list");

            return !string.IsNullOrEmpty(output);
        }

        public async Task<bool> HasOrigin()
        {
            Log("Pending: Checking for remote origin...");

            var output = await SendCommand(
                "remote",
                "Checked for remote origin",
                "Failed to check for remote origin");

            return output.Contains("origin");
        }

        public async Task Clone(string repoUrl, string parameters = "")
        {
            CleanupDirectory(_repoPath);

            if (string.IsNullOrEmpty(repoUrl))
            {
                throw new InvalidOperationException(
                    "Cannot clone repository. No repository url found. Setup on constructor");
            }

            Log($"Pending: Cloning repository: {repoUrl} to {_repoPath}...");

            var parentDir = Directory.GetParent(_repoPath)?.FullName ?? _repoPath;
            if (!Directory.Exists(_repoPath))
            {
                Directory.CreateDirectory(_repoPath);
            }

            var args = string.IsNullOrWhiteSpace(parameters)
                ? $"clone {repoUrl} \"{_repoPath}\""
                : $"clone {parameters.Trim()} {repoUrl} \"{_repoPath}\"";

            await StartProcess(CreateProcessInfo(args, parentDir),
                "Repository cloned successfully.",
                "Failed to clone repository",
                timeoutMs: 120000).ConfigureAwait(false);
        }

        public async Task CloneSingleBranch(string repoUrl, string singleBranch, int depth = 1,
            string additionalParameters = "")
        {
            var branch = string.IsNullOrEmpty(singleBranch) ? string.Empty : $"-b {singleBranch} --depth {depth} ";

            Log("Pending: Cloning single branch");

            await Clone(repoUrl, $"{additionalParameters} {branch}".Trim());
        }

        public async Task CheckoutBranch(string branchName)
        {
            Log($"Pending: Checking out branch - {branchName}...");

            if (string.IsNullOrWhiteSpace(branchName) || branchName.StartsWith("-"))
            {
                throw new ArgumentException($"Invalid branch name: {branchName}", nameof(branchName));
            }

            await ResetBranch();

            await SendCommand($"checkout {branchName}",
                $"Switched to branch - {branchName}.",
                $"Failed switching to branch - {branchName}.");
        }

        public async Task CreateBranch(string branchName)
        {
            await SendCommand($"switch --orphan {branchName}",
                $"Created new orphan branch - {branchName}.",
                $"Failed to create orphan branch - {branchName}.");
        }

        public async Task ResetToRemote(string branchName)
        {
            if (string.IsNullOrEmpty(branchName))
            {
                throw new ArgumentException("Branch name is null or empty.", nameof(branchName));
            }

            Log($"Pending: Resetting hard to origin/{branchName}...");
            await SendCommand($"reset --hard origin/{branchName}",
                $"Reset to origin/{branchName} successfully.",
                $"Failed to reset to origin/{branchName}.");
        }

        public async Task Commit(string commitMessage, string filesPath = null)
        {
            if (string.IsNullOrEmpty(commitMessage))
            {
                commitMessage = "nameless commit";
            }

            Log($"Pending: Commit with message {commitMessage}...");

            if (!string.IsNullOrEmpty(filesPath))
            {
                await SendCommand($"add -- \"{filesPath}\"",
                    "Files added successfully.",
                    "Failed to add files.");
            }

            var tempMsgFile = Path.Combine(Path.GetTempPath(), $"git_msg_{Guid.NewGuid():N}.txt");
            try
            {
                File.WriteAllText(tempMsgFile, commitMessage, System.Text.Encoding.UTF8);
                await SendCommand($"commit --allow-empty -F \"{tempMsgFile.Replace('\\', '/')}\"",
                    "Committed successfully.",
                    "Failed commit to repository.");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempMsgFile))
                    {
                        File.Delete(tempMsgFile);
                    }
                }
                catch
                {
                }
            }
        }

        public async Task CommitSingleOrphan(string branchName, string commitMessage, string filesPath = null)
        {
            if (string.IsNullOrEmpty(commitMessage))
            {
                commitMessage = "nameless commit";
            }

            Log($"Pending: Single orphan commit on branch {branchName} with message {commitMessage}...");

            if (!string.IsNullOrEmpty(filesPath))
            {
                await SendCommand($"add -- \"{filesPath}\"",
                    "Files added successfully.",
                    "Failed to add files.");
            }
            else
            {
                await SendCommand("add -A",
                    "All files added successfully.",
                    "Failed to add all files.");
            }

            var treeSha = (await SendCommand("write-tree",
                "Wrote tree.",
                "Failed to write tree.")).Trim();

            var tempMsgFile = Path.Combine(Path.GetTempPath(), $"git_msg_{Guid.NewGuid():N}.txt");
            try
            {
                File.WriteAllText(tempMsgFile, commitMessage, System.Text.Encoding.UTF8);
                var commitSha = (await SendCommand($"commit-tree {treeSha} -F \"{tempMsgFile.Replace('\\', '/')}\"",
                    "Created orphan commit.",
                    "Failed to commit tree.")).Trim();

                await SendCommand($"reset --hard {commitSha}",
                    "Updated branch to orphan commit.",
                    "Failed to reset to orphan commit.");

                if (!string.IsNullOrEmpty(branchName))
                {
                    await SendCommand($"branch -M {branchName}",
                        $"Renamed orphan branch to {branchName}.",
                        $"Failed renaming orphan branch to {branchName}.");
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempMsgFile))
                    {
                        File.Delete(tempMsgFile);
                    }
                }
                catch
                {
                }
            }
        }

        public async Task Push(string branch, string parameters = "", bool isLocal = false)
        {
            Log($"Pending: Pushing branch - {branch}, parameters - \"{parameters}\"...");

            if (isLocal)
            {
                Log("Skipping push: local repository without remote origin.");
                return;
            }

            if (string.IsNullOrEmpty(branch))
            {
                throw new ArgumentException("Branch name is null or empty.");
            }

            await SendCommand($"push origin {branch} {parameters} ",
                "Branch pushed successfully.",
                "Failed to push branch.");
        }

        public async Task PushForceWithLease(string branch, bool isLocal = false)
        {
            Log($"Pending: Force pushing (with-lease) branch - {branch}...");

            if (isLocal)
            {
                Log("Skipping push: local repository without remote origin.");
                return;
            }

            if (string.IsNullOrEmpty(branch))
            {
                throw new ArgumentException("Branch name is null or empty.");
            }

            await SendCommand($"push origin {branch} --force-with-lease",
                "Branch force-pushed successfully.",
                "Failed to force-push branch.");
        }

        public async Task<bool> IsBranchIgnoredInFetch(string branchName)
        {
            if (string.IsNullOrEmpty(branchName))
            {
                return false;
            }

            try
            {
                var lines = await GetFetchRefspecs();
                var targetNegativeRefspec = $"^refs/heads/{branchName}";
                return lines.Any(
                    l => string.Equals(l.Trim(), targetNegativeRefspec, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public async Task IgnoreBranchInFetch(string branchName)
        {
            if (string.IsNullOrEmpty(branchName))
            {
                throw new ArgumentException("Branch name is null or empty.", nameof(branchName));
            }

            var isIgnored = await IsBranchIgnoredInFetch(branchName);
            if (isIgnored)
            {
                return;
            }

            var currentRefspecs = await GetFetchRefspecs();
            if (currentRefspecs.Length == 0)
            {
                await SendCommand("config --add remote.origin.fetch \"+refs/heads/*:refs/remotes/origin/*\"",
                    "Added default fetch refspec.",
                    "Failed to add default fetch refspec.");
            }

            await SendCommand($"config --add remote.origin.fetch \"^refs/heads/{branchName}\"",
                $"Ignored branch {branchName} in remote.origin.fetch.",
                $"Failed to ignore branch {branchName} in fetch.");

            try
            {
                await SendCommand($"branch -r -d origin/{branchName}",
                    $"Removed local remote-tracking branch origin/{branchName}.",
                    $"Failed to delete local remote-tracking branch origin/{branchName}.");
            }
            catch
            {
            }
        }

        public async Task UnignoreBranchInFetch(string branchName)
        {
            if (string.IsNullOrEmpty(branchName))
            {
                throw new ArgumentException("Branch name is null or empty.", nameof(branchName));
            }

            var currentRefspecs = await GetFetchRefspecs();
            var targetNegativeRefspec = $"^refs/heads/{branchName}";

            var remainingRefspecs = currentRefspecs
                .Where(r => !string.Equals(r.Trim(), targetNegativeRefspec, StringComparison.OrdinalIgnoreCase))
                .ToList();

            try
            {
                await SendCommand("config --unset-all remote.origin.fetch",
                    "Cleared remote.origin.fetch refspecs.",
                    "Failed to clear remote.origin.fetch.");
            }
            catch
            {
            }

            if (remainingRefspecs.Count == 0)
            {
                remainingRefspecs.Add("+refs/heads/*:refs/remotes/origin/*");
            }

            foreach (var refspec in remainingRefspecs)
            {
                await SendCommand($"config --add remote.origin.fetch \"{refspec}\"",
                    $"Restored fetch refspec {refspec}.",
                    $"Failed restoring fetch refspec {refspec}.");
            }
        }

        private async Task<string[]> GetFetchRefspecs()
        {
            try
            {
                var output = await SendCommand("config --get-all remote.origin.fetch",
                    "Fetched remote.origin.fetch refspecs",
                    "Failed to get remote.origin.fetch refspecs");

                return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public async Task Pull(string branch)
        {
            Log($"Pending: Pulling branch - {branch}...");

            if (string.IsNullOrEmpty(branch))
            {
                throw new ArgumentException("Branch name is null or empty.");
            }

            await SendCommand($"pull origin {branch}",
                "Branch pulled successfully.",
                "Failed to pull branch");
        }

        public async Task ResetBranch()
        {
            Log("Pending: Reset current branch...");

            await SendCommand("reset --hard",
                "Branch has been reset.",
                "Failed to reset current branch.");
        }

        private Task<string> StartProcess(
            ProcessStartInfo processInfo,
            string successMessage = null,
            string errorMessage = null,
            int timeoutMs = 60000)
        {
            return Task.Run(() =>
            {
                using var process = Process.Start(processInfo);

                if (process == null)
                {
                    throw new InvalidProgramException("Failed to start process.");
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }

                    throw new TimeoutException(
                        $"Git process timed out after {timeoutMs / 1000}s. Arguments: {processInfo.Arguments}");
                }

                process.WaitForExit();
                var tasksFinished = Task.WaitAll(new Task[] { outputTask, errorTask }, 5000);
                var error = tasksFinished && errorTask.IsCompleted ? errorTask.Result : string.Empty;
                var output = tasksFinished && outputTask.IsCompleted ? outputTask.Result : string.Empty;

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"{errorMessage} {error}. Arguments {processInfo.Arguments}");
                }

                if (!string.IsNullOrEmpty(successMessage))
                {
                    Log(successMessage);
                }

                return output;
            });
        }

        private ProcessStartInfo CreateProcessInfo(string arguments, string workingDirectory = null)
        {
            if (string.IsNullOrEmpty(arguments))
            {
                throw new ArgumentNullException(nameof(arguments));
            }

            var workDir = workingDirectory ?? _repoPath;
            if (!Directory.Exists(workDir))
            {
                Directory.CreateDirectory(workDir);
            }

            ProcessStartInfo processInfo = new(ResolveGitExecutable())
            {
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workDir
            };

            processInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            processInfo.EnvironmentVariables["GIT_NON_INTERACTIVE_CHECK"] = "1";
            processInfo.EnvironmentVariables["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes";
            processInfo.EnvironmentVariables["GCM_INTERACTIVE"] = "never";

            return processInfo;
        }

        internal static void CleanupDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                SetAttributesNormal(path);
                Directory.Delete(path, true);
            }
        }

        internal static void SetAttributesNormal(string path)
        {
            foreach (var file in Directory.GetFiles(path))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            foreach (var directory in Directory.GetDirectories(path))
            {
                SetAttributesNormal(directory);
            }
        }

        private void Log(string message)
        {
            if (_enableLog)
            {
                Debug.Log(message);
            }
        }
    }
}
