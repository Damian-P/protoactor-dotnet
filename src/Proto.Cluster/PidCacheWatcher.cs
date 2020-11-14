using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Proto.Cluster
{
    public class PidCacheWatcher : IActor
    {
        private readonly PidCache pidCache;
        private readonly Dictionary<PID, ClusterIdentity> lookup = new Dictionary<PID, ClusterIdentity>();

        public PidCacheWatcher(PidCache pidCache)
        {
            this.pidCache = pidCache;
        }
        public Task ReceiveAsync(IContext context)
        {
            switch (context.Message)
            {
                case Started _:
                    context.SetReceiveTimeout(TimeSpan.FromSeconds(5));
                    break;
                case ReceiveTimeout _:
                    context.SetReceiveTimeout(TimeSpan.FromSeconds(5));
                    break;
                case Activation activation:
                    lookup[activation.Pid] = activation.ClusterIdentity;
                    context.Watch(activation.Pid);
                    break;
                case Terminated terminated:
                    if (lookup.ContainsKey(terminated.Who))
                    {
                        var clusterIdentity = lookup[terminated.Who];
                        pidCache.RemoveByVal(clusterIdentity, terminated.Who);
                        lookup.Remove(terminated.Who);
                    }
                    break;
                default:
                    break;
            }
            return Task.CompletedTask;
        }
    }
}