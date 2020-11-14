namespace Proto.Cluster
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;

    public class PidCache
    {
        private readonly ConcurrentDictionary<ClusterIdentity, PID> _cacheDict;
        private readonly ICollection<KeyValuePair<ClusterIdentity, PID>> _cacheCollection;
        private readonly ActorSystem _system;
        private PID? _pidCacheUpdater;

        public PidCache(ActorSystem system)
        {
            _cacheDict = new ConcurrentDictionary<ClusterIdentity, PID>();
            _cacheCollection = _cacheDict;
            _system = system;
        }

        public void StartWatch()
        {
            _pidCacheUpdater = _system.Root.SpawnNamed(Props.FromProducer(() => new PidCacheWatcher(this)), "PidCacheUpdater");
        }

        public bool TryGet(ClusterIdentity clusterIdentity, out PID pid)
        {
            if (clusterIdentity is null)
            {
                throw new ArgumentNullException(nameof(clusterIdentity));
            }

            
            return _cacheDict.TryGetValue(clusterIdentity, out pid);
        }

        public bool TryAdd(ClusterIdentity clusterIdentity, PID pid)
        {
            if (clusterIdentity is null)
            {
                throw new ArgumentNullException(nameof(clusterIdentity));
            }
            
            if (pid is null)
            {
                throw new ArgumentNullException(nameof(pid));
            }
            if(_pidCacheUpdater is not null)
                _system.Root.Send(_pidCacheUpdater, new Activation { ClusterIdentity = clusterIdentity, Pid = pid });
            return _cacheDict.TryAdd(clusterIdentity, pid);
        }

        public bool TryUpdate(ClusterIdentity clusterIdentity, PID newPid, PID existingPid)
        {
            if (clusterIdentity is null)
            {
                throw new ArgumentNullException(nameof(clusterIdentity));
            }
            
            if (newPid is null)
            {
                throw new ArgumentNullException(nameof(newPid));
            }
            
            if (existingPid is null)
            {
                throw new ArgumentNullException(nameof(existingPid));
            }

            if(_pidCacheUpdater is not null)
                _system.Root.Send(_pidCacheUpdater, new Activation { ClusterIdentity = clusterIdentity, Pid = existingPid });

            return _cacheDict.TryUpdate(clusterIdentity, newPid, existingPid);
        }

        public bool TryRemove(ClusterIdentity clusterIdentity)
        {
            if (clusterIdentity is null)
            {
                throw new ArgumentNullException(nameof(clusterIdentity));
            }
            
            return _cacheDict.TryRemove(clusterIdentity, out _);
        }

        public bool RemoveByVal(ClusterIdentity clusterIdentity, PID pid)
        {
            var key = clusterIdentity;
            if (_cacheDict.TryGetValue(key, out var existingPid))
            {
                return _cacheCollection.Remove(new KeyValuePair<ClusterIdentity, PID>(key, existingPid));
            }

            return false;
        }

        public void RemoveByMember(Member member)
        {
            RemoveByPredicate(pair => member.Address.Equals(pair.Value.Address));
        }

        private void RemoveByPredicate(Func<KeyValuePair<ClusterIdentity, PID>, bool> predicate)
        {
            var toBeRemoved = _cacheDict.Where(predicate).ToList();
            if (toBeRemoved.Count == 0) return;
            foreach (var item in toBeRemoved)
            {
                _cacheCollection.Remove(item);
            }
        }
    }
}