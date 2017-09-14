﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace GitHub.Unity
{
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    class Repository : IRepository, IEquatable<Repository>
    {
        private IRepositoryManager repositoryManager;
        private ConfigBranch? currentBranch;
        private ConfigRemote? currentRemote;
        private GitStatus currentStatus;
        private string head;

        private Dictionary<string, ConfigBranch> localBranches = new Dictionary<string, ConfigBranch>();

        private IEnumerable<GitLock> locks;
        private Dictionary<string, Dictionary<string, ConfigBranch>> remoteBranches = new Dictionary<string, Dictionary<string, ConfigBranch>>();
        private Dictionary<string, ConfigRemote> remotes;

        public event Action<GitStatus> OnStatusUpdated;
        public event Action<string> OnCurrentBranchChanged;
        public event Action<string> OnCurrentRemoteChanged;
        public event Action OnLocalBranchListChanged;
        public event Action OnRemoteBranchListChanged;
        public event Action OnHeadChanged;
        public event Action<IEnumerable<GitLock>> OnLocksUpdated;
        public event Action OnRepositoryInfoChanged;

        public IEnumerable<GitBranch> LocalBranches
        {
            get
            {
                throw new NotImplementedException();
//                return repositoryManager.LocalBranches.Values.Select(x => new GitBranch(x.Name,
//                    (x.IsTracking ? (x.Remote.Value.Name + "/" + x.Name) : "[None]"), x.Name == CurrentBranch?.Name));
            }
        }

        public IEnumerable<GitBranch> RemoteBranches
        {
            get
            {
                throw new NotImplementedException();
//                return repositoryManager.RemoteBranches.Values.SelectMany(x => x.Values).Select(x =>
//                    new GitBranch(x.Remote.Value.Name + "/" + x.Name, "[None]", false));
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Repository"/> class.
        /// </summary>
        /// <param name="repositoryManager"></param>
        /// <param name="name">The repository name.</param>
        /// <param name="cloneUrl">The repository's clone URL.</param>
        /// <param name="localPath"></param>
        public Repository(string name, NPath localPath)
        {
            Guard.ArgumentNotNullOrWhiteSpace(name, nameof(name));
            Guard.ArgumentNotNull(localPath, nameof(localPath));

            Name = name;
            LocalPath = localPath;
            this.User = new User();
        }

        public void Initialize(IRepositoryManager repositoryManager)
        {
            Guard.ArgumentNotNull(repositoryManager, nameof(repositoryManager));

            this.repositoryManager = repositoryManager;

            repositoryManager.OnHeadUpdated += RepositoryManager_OnHeadUpdated;
            repositoryManager.OnStatusUpdated += RepositoryManager_OnStatusUpdated;
            repositoryManager.OnLocksUpdated += RepositoryManager_OnLocksUpdated;
            repositoryManager.OnLocalBranchListUpdated += RepositoryManager_OnLocalBranchListUpdated;
            repositoryManager.OnRemoteBranchListUpdated += RepositoryManager_OnRemoteBranchListUpdated;
            repositoryManager.OnUpdateLocalBranch += RepositoryManager_OnUpdateLocalBranch;
            repositoryManager.OnAddLocalBranch += RepositoryManager_OnAddLocalBranch;
            repositoryManager.OnRemoveLocalBranch += RepositoryManager_OnRemoveLocalBranch;
            repositoryManager.OnGitUserLoaded += user => User = user;
        }

        public void Refresh()
        {
            repositoryManager?.Refresh();
        }

        public ITask SetupRemote(string remote, string remoteUrl)
        {
            Guard.ArgumentNotNullOrWhiteSpace(remote, "remote");
            Guard.ArgumentNotNullOrWhiteSpace(remoteUrl, "remoteUrl");
            if (!CurrentRemote.HasValue || String.IsNullOrEmpty(CurrentRemote.Value.Name)) // there's no remote at all
            {
                return repositoryManager.RemoteAdd(remote, remoteUrl);
            }
            else
            {
                return repositoryManager.RemoteChange(remote, remoteUrl);
            }
        }

        public ITask<List<GitLogEntry>> Log()
        {
            if (repositoryManager == null)
                return new FuncListTask<GitLogEntry>(TaskHelpers.GetCompletedTask(new List<GitLogEntry>()));

            return repositoryManager.Log();
        }

        public ITask CommitAllFiles(string message, string body)
        {
            return repositoryManager.CommitAllFiles(message, body);
        }

        public ITask CommitFiles(List<string> files, string message, string body)
        {
            return repositoryManager.CommitFiles(files, message, body);
        }

        public ITask Pull()
        {
            return repositoryManager.Pull(CurrentRemote.Value.Name, CurrentBranch?.Name);
        }

        public ITask Push()
        {
            return repositoryManager.Push(CurrentRemote.Value.Name, CurrentBranch?.Name);
        }

        public ITask Fetch()
        {
            return repositoryManager.Fetch(CurrentRemote.Value.Name);
        }
        
        public ITask Revert(string changeset)
        {
            return repositoryManager.Revert(changeset);
        }

        public ITask ListLocks()
        {
            if (repositoryManager == null)
                return new ActionTask(TaskExtensions.CompletedTask);
            return repositoryManager.ListLocks(false);
        }

        public ITask RequestLock(string file)
        {
            return repositoryManager.LockFile(file);
        }

        public ITask ReleaseLock(string file, bool force)
        {
            return repositoryManager.UnlockFile(file, force);
        }

        private void SetCloneUrl()
        {
            if (CurrentRemote.HasValue)
                CloneUrl = new UriString(CurrentRemote.Value.Url);
            else
                CloneUrl = null;
            Name = CloneUrl != null ? CloneUrl.RepositoryName : LocalPath.FileName;
            OnRepositoryInfoChanged?.Invoke();
        }

        private void RepositoryManager_OnStatusUpdated(GitStatus status)
        {
            CurrentStatus = status;
        }

        private void RepositoryManager_OnLocksUpdated(IEnumerable<GitLock> locks)
        {
            CurrentLocks = locks;
            OnLocksUpdated?.Invoke(CurrentLocks);
        }

        private void RepositoryManager_OnHeadUpdated(string h)
        {
            if (head != h)
            {
                head = h;
                var branch = GetCurrentBranch();
                var remote = GetCurrentRemote();

                if (!Nullable.Equals(currentBranch, branch))
                {
                    currentBranch = branch;
                    OnCurrentBranchChanged?.Invoke(currentBranch.HasValue ? currentBranch.Value.Name : null);
                }

                if (!Nullable.Equals(currentRemote, remote))
                {
                    currentRemote = remote;
                    OnCurrentRemoteChanged?.Invoke(currentRemote.HasValue ? currentRemote.Value.Name : null);
                }
            }
        }

        private void RepositoryManager_OnRemoteBranchListUpdated(Dictionary<string, Dictionary<string, ConfigBranch>> branches)
        {
            remoteBranches = branches;
            OnRemoteBranchListChanged?.Invoke();
        }

        private void RepositoryManager_OnLocalBranchListUpdated(Dictionary<string, ConfigBranch> obj)
        {
            localBranches = obj;
        }

        private void RepositoryManager_OnRemoveLocalBranch(string name)
        {
            if (localBranches.ContainsKey(name))
            {
                localBranches.Remove(name);
                OnLocalBranchListChanged?.Invoke();
            }
        }

        private void RepositoryManager_OnAddLocalBranch(string name)
        {
            if (!localBranches.ContainsKey(name))
            {
                var branch = repositoryManager.Config.GetBranch(name);
                if (!branch.HasValue)
                {
                    branch = new ConfigBranch { Name = name };
                }
                localBranches.Add(name, branch.Value);
                OnLocalBranchListChanged?.Invoke();
            }
        }

        private void RepositoryManager_OnUpdateLocalBranch(string name)
        {
            if (name == currentBranch?.Name)
            {
                // commit of current branch changed, trigger OnHeadChanged
                OnHeadChanged?.Invoke();
                repositoryManager.Refresh();
            }
        }       
        
        
        private void AddRemoteBranch(string remote, string name)
        {
            Dictionary<string, ConfigBranch> branchList = null;
            if (remoteBranches.TryGetValue(remote, out branchList))
            {
                if (!branchList.ContainsKey(name))
                {
                    branchList.Add(name, new ConfigBranch { Name = name, Remote = remotes[remote] });
                    OnRemoteBranchListChanged?.Invoke();
                }
            }
        }
        
        private void RemoveRemoteBranch(string remote, string name)
        {
            Dictionary<string, ConfigBranch> branchList = null;
            if (remoteBranches.TryGetValue(remote, out branchList))
            {
                if (localBranches.ContainsKey(name))
                {
                    localBranches.Remove(name);
                    OnRemoteBranchListChanged?.Invoke();
                }
            }
        }

        private ConfigBranch? GetCurrentBranch()
        {
            if (head.StartsWith("ref:"))
            {
                var branch = head.Substring(head.IndexOf("refs/heads/") + "refs/heads/".Length);
                currentBranch = GetBranch(branch);
            }
            else
            {
                currentBranch = null;
            }
            return currentBranch;
        }
        
        private ConfigBranch? GetBranch(string name)
        {
            if (localBranches.ContainsKey(name))
            {
                return localBranches[name];
            }
        
            return null;
        }

        private ConfigRemote? GetCurrentRemote(string defaultRemote = "origin")
        {
            if (currentBranch.HasValue && currentBranch.Value.IsTracking)
            {
                return currentBranch.Value.Remote;
            }
        
            var remote = repositoryManager.Config.GetRemote(defaultRemote);
            if (remote.HasValue)
            {
                return remote;
            }
        
            using (var remoteEnumerator = repositoryManager.Config.GetRemotes().GetEnumerator())
            {
                if (remoteEnumerator.MoveNext())
                {
                    return remoteEnumerator.Current;
                }
            }
        
            return null;
        }

        /// <summary>
        /// Note: We don't consider CloneUrl a part of the hash code because it can change during the lifetime
        /// of a repository. Equals takes care of any hash collisions because of this
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return LocalPath.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
                return true;
            var other = obj as Repository;
            return Equals(other);
        }

        public bool Equals(Repository other)
        {
            return (Equals((IRepository)other));
        }

        public bool Equals(IRepository other)
        {
            if (ReferenceEquals(this, other))
                return true;
            return other != null &&
                object.Equals(LocalPath, other.LocalPath);
        }

        public ConfigBranch? CurrentBranch
        {
            get { return currentBranch; }
            set
            {
                if (currentBranch.HasValue != value.HasValue || (currentBranch.HasValue && !currentBranch.Value.Equals(value.Value)))
                {
                    currentBranch = value;
                    Logger.Trace("OnActiveBranchChanged: {0}", value?.ToString() ?? "NULL");
                    OnCurrentBranchChanged?.Invoke(CurrentBranch.HasValue ? CurrentBranch.Value.Name : null);
                }
            }
        }
        /// <summary>
        /// Gets the current branch of the repository.
        /// </summary>

        public string CurrentBranchName => currentBranch?.Name;

        /// <summary>
        /// Gets the current remote of the repository.
        /// </summary>
        public ConfigRemote? CurrentRemote
        {
            get { return currentRemote; }
            set
            {
                if (currentRemote.HasValue != value.HasValue || (currentRemote.HasValue && !currentRemote.Value.Equals(value.Value)))
                {
                    currentRemote = value;
                    SetCloneUrl();
                    Logger.Trace("OnActiveRemoteChanged: {0}", value?.ToString() ?? "NULL");
                    OnCurrentRemoteChanged?.Invoke(CurrentRemote.HasValue ? CurrentRemote.Value.Name : null);
                }
            }
        }

        public UriString CloneUrl { get; private set; }

        public string Name { get; private set; }

        public NPath LocalPath { get; private set; }

        public string Owner => CloneUrl?.Owner ?? null;

        public bool IsGitHub { get { return HostAddress.IsGitHubDotCom(CloneUrl); } }

        internal string DebuggerDisplay => String.Format(
            CultureInfo.InvariantCulture,
            "{0} Owner: {1} Name: {2} CloneUrl: {3} LocalPath: {4} Branch: {5} Remote: {6}",
            GetHashCode(),
            Owner,
            Name,
            CloneUrl,
            LocalPath,
            CurrentBranch,
            CurrentRemote);

        public GitStatus CurrentStatus
        {
            get { return currentStatus; }
            set
            {
                Logger.Trace("OnStatusUpdated: {0}", value.ToString());
                currentStatus = value;
                OnStatusUpdated?.Invoke(CurrentStatus);
            }
        }

        public IUser User { get; set; }
        public IEnumerable<GitLock> CurrentLocks { get; private set; }
        protected static ILogging Logger { get; } = Logging.GetLogger<Repository>();
    }

    interface IUser
    {
        string Name { get; set; }
        string Email { get; set; }
    }

    [Serializable]
    class User : IUser
    {
        public string Name { get; set; }
        public string Email { get; set; }

        public override string ToString()
        {
            return String.Format("Name: {0} Email: {1}", Name, Email);
        }
    }
}