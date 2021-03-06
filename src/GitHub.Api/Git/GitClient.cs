﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GitHub.Unity
{
    public interface IGitClient
    {
        Task<NPath> FindGitInstallation();
        ITask<ValidateGitInstallResult> ValidateGitInstall(NPath path);

        ITask Init(IOutputProcessor<string> processor = null);

        ITask LfsInstall(IOutputProcessor<string> processor = null);

        ITask<GitStatus> Status(IOutputProcessor<GitStatus> processor = null);

        ITask<string> GetConfig(string key, GitConfigSource configSource,
            IOutputProcessor<string> processor = null);

        ITask<string> SetConfig(string key, string value, GitConfigSource configSource,
            IOutputProcessor<string> processor = null);

        ITask<string[]> GetConfigUserAndEmail();

        ITask<List<GitLock>> ListLocks(bool local,
            BaseOutputListProcessor<GitLock> processor = null);

        ITask<string> Pull(string remote, string branch,
            IOutputProcessor<string> processor = null);

        ITask<string> Push(string remote, string branch,
            IOutputProcessor<string> processor = null);

        ITask<string> Revert(string changeset,
            IOutputProcessor<string> processor = null);

        ITask<string> Fetch(string remote,
            IOutputProcessor<string> processor = null);

        ITask<string> SwitchBranch(string branch,
            IOutputProcessor<string> processor = null);

        ITask<string> DeleteBranch(string branch, bool deleteUnmerged = false,
            IOutputProcessor<string> processor = null);

        ITask<string> CreateBranch(string branch, string baseBranch,
            IOutputProcessor<string> processor = null);

        ITask<string> RemoteAdd(string remote, string url,
            IOutputProcessor<string> processor = null);

        ITask<string> RemoteRemove(string remote,
            IOutputProcessor<string> processor = null);

        ITask<string> RemoteChange(string remote, string url,
            IOutputProcessor<string> processor = null);

        ITask<string> Commit(string message, string body,
            IOutputProcessor<string> processor = null);

        ITask<string> Add(IList<string> files,
            IOutputProcessor<string> processor = null);

        ITask<string> AddAll(IOutputProcessor<string> processor = null);

        ITask<string> Remove(IList<string> files,
            IOutputProcessor<string> processor = null);

        ITask<string> AddAndCommit(IList<string> files, string message, string body,
            IOutputProcessor<string> processor = null);

        ITask<string> Lock(string file,
            IOutputProcessor<string> processor = null);

        ITask<string> Unlock(string file, bool force,
            IOutputProcessor<string> processor = null);

        ITask<List<GitLogEntry>> Log(BaseOutputListProcessor<GitLogEntry> processor = null);

        ITask<Version> Version(IOutputProcessor<Version> processor = null);

        ITask<Version> LfsVersion(IOutputProcessor<Version> processor = null);
    }

    class GitClient : IGitClient
    {
        private readonly IEnvironment environment;
        private readonly IProcessManager processManager;
        private readonly ITaskManager taskManager;
        private readonly CancellationToken cancellationToken;

        public GitClient(IEnvironment environment, IProcessManager processManager, ITaskManager taskManager)
        {
            this.environment = environment;
            this.processManager = processManager;
            this.taskManager = taskManager;
            this.cancellationToken = taskManager.Token;
        }

        public async Task<NPath> FindGitInstallation()
        {
            if (!String.IsNullOrEmpty(environment.GitExecutablePath))
                return environment.GitExecutablePath;

            NPath path = null;

            if (environment.IsWindows)
                path = await LookForPortableGit();

            if (path == null)
                path = await LookForSystemGit();

            if (path == null)
            {
                Logger.Trace("Git Installation not discovered");
            }
            else
            {
                Logger.Trace("Git Installation discovered: '{0}'", path);
            }

            return path;
        }

        private Task<NPath> LookForPortableGit()
        {
            Logger.Trace("LookForPortableGit");

            var gitHubLocalAppDataPath = environment.UserCachePath;
            if (!gitHubLocalAppDataPath.DirectoryExists())
                return null;

            var searchPath = "PortableGit_";

            var portableGitPath = gitHubLocalAppDataPath.Directories()
                .Where(s => s.FileName.StartsWith(searchPath, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (portableGitPath != null)
            {
                portableGitPath = portableGitPath.Combine("cmd", $"git{environment.ExecutableExtension}");
            }

            return TaskEx.FromResult(portableGitPath);
        }

        private async Task<NPath> LookForSystemGit()
        {
            Logger.Trace("LookForSystemGit");

            NPath path = null;
            if (!environment.IsWindows)
            {
                var p = new NPath("/usr/local/bin/git");

                if (p.FileExists())
                    path = p;
            }

            if (path == null)
            {
                path = await new FindExecTask("git", taskManager.Token)
                    .Configure(processManager).StartAwait();
            }

            return path;
        }

        public ITask<ValidateGitInstallResult> ValidateGitInstall(NPath path)
        {
            if (!path.FileExists())
            {
                return new FuncTask<ValidateGitInstallResult>(TaskEx.FromResult(new ValidateGitInstallResult(false, null, null)));
            }

            Version gitVersion = null;
            Version gitLfsVersion = null;

            var gitVersionTask = new GitVersionTask(cancellationToken).Configure(processManager, path);
            var gitLfsVersionTask = new GitLfsVersionTask(cancellationToken).Configure(processManager, path);

            return gitVersionTask
                .Then((result, version) => gitVersion = version)
                .Then(gitLfsVersionTask)
                .Then((result, version) => gitLfsVersion = version)
                .Then(success => new ValidateGitInstallResult(success &&
                    gitVersion?.CompareTo(Constants.MinimumGitVersion) >= 0 &&
                    gitLfsVersion?.CompareTo(Constants.MinimumGitLfsVersion) >= 0,
                    gitVersion, gitLfsVersion)
                );
        }

        public ITask Init(IOutputProcessor<string> processor = null)
        {
            return new GitInitTask(cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask LfsInstall(IOutputProcessor<string> processor = null)
        {
            Logger.Trace("LfsInstall");

            return new GitLfsInstallTask(cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<GitStatus> Status(IOutputProcessor<GitStatus> processor = null)
        {
            Logger.Trace("Status");

            return new GitStatusTask(new GitObjectFactory(environment), cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<List<GitLogEntry>> Log(BaseOutputListProcessor<GitLogEntry> processor = null)
        {
            Logger.Trace("Log");

            return new GitLogTask(new GitObjectFactory(environment), cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<Version> Version(IOutputProcessor<Version> processor = null)
        {
            Logger.Trace("Version");

            return new GitVersionTask(cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<Version> LfsVersion(IOutputProcessor<Version> processor = null)
        {
            Logger.Trace("LfsVersion");

            return new GitLfsVersionTask(cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> GetConfig(string key, GitConfigSource configSource, IOutputProcessor<string> processor = null)
        {
            Logger.Trace("GetConfig: {0}", key);

            return new GitConfigGetTask(key, configSource, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> SetConfig(string key, string value, GitConfigSource configSource, IOutputProcessor<string> processor = null)
        {
            Logger.Trace("SetConfig");

            return new GitConfigSetTask(key, value, configSource, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string[]> GetConfigUserAndEmail()
        {
            string username = null;
            string email = null;

            return GetConfig("user.name", GitConfigSource.User).Then((success, value) => {
                if (success)
                {
                    username = value;
                }

            }).Then(GetConfig("user.email", GitConfigSource.User).Then((success, value) => {
                if (success)
                {
                    email = value;
                }
            })).Then(success => {
                Logger.Trace("user.name:{1} user.email:{2}", success, username, email);
                return new[] { username, email };
            });
        }

        public ITask<List<GitLock>> ListLocks(bool local, BaseOutputListProcessor<GitLock> processor = null)
        {
            Logger.Trace("ListLocks");

            return new GitListLocksTask(new GitObjectFactory(environment), local, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> Pull(string remote, string branch, IOutputProcessor<string> processor = null)
        {
            Logger.Trace("Pull");

            return new GitPullTask(remote, branch, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> Push(string remote, string branch,
            IOutputProcessor<string> processor = null)
        {
            Logger.Trace("Push");

            return new GitPushTask(remote, branch, true, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> Revert(string changeset, IOutputProcessor<string> processor = null)
        {
            Logger.Trace("Revert");

            return new GitRevertTask(changeset, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> Fetch(string remote,
            IOutputProcessor<string> processor = null)
        {
            Logger.Trace("Fetch");

            return new GitFetchTask(remote, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> SwitchBranch(string branch, IOutputProcessor<string> processor = null)
        {
            Logger.Trace("SwitchBranch");

            return new GitSwitchBranchesTask(branch, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> DeleteBranch(string branch, bool deleteUnmerged = false,
            IOutputProcessor<string> processor = null)
        {
            Logger.Trace("DeleteBranch");

            return new GitBranchDeleteTask(branch, deleteUnmerged, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> CreateBranch(string branch, string baseBranch,
            IOutputProcessor<string> processor = null)
        {
            Logger.Trace("CreateBranch");

            return new GitBranchCreateTask(branch, baseBranch, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> RemoteAdd(string remote, string url,
            IOutputProcessor<string> processor = null)
        {
            Logger.Trace("RemoteAdd");

            return new GitRemoteAddTask(remote, url, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> RemoteRemove(string remote,
            IOutputProcessor<string> processor = null)
        {
            Logger.Trace("RemoteRemove");

            return new GitRemoteRemoveTask(remote, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> RemoteChange(string remote, string url,
            IOutputProcessor<string> processor = null)
        {
            Logger.Trace("RemoteChange");

            return new GitRemoteChangeTask(remote, url, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> Commit(string message, string body,
            IOutputProcessor<string> processor = null)
        {
            Logger.Trace("Commit");

            return new GitCommitTask(message, body, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> AddAll(IOutputProcessor<string> processor = null)
        {
            Logger.Trace("Add all files");

            return new GitAddTask(cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> Add(IList<string> files,
            IOutputProcessor<string> processor = null)
        {
            Logger.Trace("Add Files");

            GitAddTask last = null;
            foreach (var batch in files.Spool(5000))
            {
                var current = new GitAddTask(batch, cancellationToken, processor).Configure(processManager);
                if (last == null)
                {
                    last = current;
                }
                else
                {
                    last.Then(current);
                    last = current;
                }
            }

            return last;
        }

        public ITask<string> Remove(IList<string> files,
            IOutputProcessor<string> processor = null)
        {
            Logger.Trace("Remove");

            return new GitRemoveFromIndexTask(files, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> AddAndCommit(IList<string> files, string message, string body,
            IOutputProcessor<string> processor = null)
        {
            Logger.Trace("AddAndCommit");

            return Add(files)
                .Then(new GitCommitTask(message, body, cancellationToken)
                    .Configure(processManager));
        }

        public ITask<string> Lock(string file,
            IOutputProcessor<string> processor = null)
        {
            Logger.Trace("Lock");

            return new GitLockTask(file, cancellationToken, processor)
                .Configure(processManager);
        }

        public ITask<string> Unlock(string file, bool force,
            IOutputProcessor<string> processor = null)
        {
            Logger.Trace("Unlock");

            return new GitUnlockTask(file, force, cancellationToken, processor)
                .Configure(processManager);
        }

        protected static ILogging Logger { get; } = Logging.GetLogger<GitClient>();
    }
}
