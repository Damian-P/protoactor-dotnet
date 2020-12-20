// -----------------------------------------------------------------------
// <copyright file="RequestAsyncStrategy.cs" company="Asynkron AB">
//      Copyright (C) 2015-2020 Asynkron AB All rights reserved
// </copyright>
// -----------------------------------------------------------------------
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Proto.Cluster.IdentityLookup;
using Proto.Utils;

namespace Proto.Cluster
{
    public interface IClusterContext
    {
        Task<T> RequestAsync<T>(ClusterIdentity clusterIdentity, object message, CancellationToken ct);
        Task<T> RequestAsync<T>(ClusterIdentity clusterIdentity, object message, ISenderContext context, CancellationToken ct);
    }

    public class DefaultClusterContext : IClusterContext
    {
        private readonly ISenderContext _context;
        private readonly IIdentityLookup _identityLookup;
        private readonly ILogger _logger;
        private readonly PidCache _pidCache;
        private readonly ShouldThrottle _requestLogThrottle;

        public DefaultClusterContext(IIdentityLookup identityLookup, PidCache pidCache, ISenderContext context,
            ILogger logger)
        {
            _identityLookup = identityLookup;
            _pidCache = pidCache;
            _context = context;
            _logger = logger;
            _requestLogThrottle = Throttle.Create(
                3,
                TimeSpan.FromSeconds(2),
                i => _logger.LogInformation("Throttled {LogCount} TryRequestAsync logs.", i)
            );
        }

        public async Task<T> RequestAsync<T>(ClusterIdentity clusterIdentity, object message, ISenderContext context, CancellationToken ct)
        {
            _logger.LogDebug("Requesting {ClusterIdentity} Message {Message}", clusterIdentity.ToShortString(), message);
            var i = 0;
            PID? pid;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (context.System.Shutdown.IsCancellationRequested)
                    {
                        return default!;
                    }

                    if (_pidCache.TryGet(clusterIdentity, out var cachedPid))
                    {
                        _logger.LogDebug("Requesting {Identity}-{Kind} Message {Message} - Got PID {Pid} from PidCache",
                            clusterIdentity.Identity, clusterIdentity.Kind, message, cachedPid
                        );
                        var (statusFromPidCache, resultFromPidCache) = await TryRequestAsync<T>(clusterIdentity, message, cachedPid, "PidCache", context, ct);
                        if (statusFromPidCache == ResponseStatus.Ok) return resultFromPidCache;
                        _pidCache.TryRemove(clusterIdentity);
                    }

                    var delay = i * 20;
                    i++;

                    //try get a pid from id lookup
                    try
                    {

                        pid = await _identityLookup.GetAsync(clusterIdentity, ct);
                        if (pid is null)
                        {
                            _logger.LogDebug(
                                "Requesting {Identity}-{Kind} Message {Message} - Did not get PID from IdentityLookup",
                                clusterIdentity.Identity, clusterIdentity.Kind, message
                            );
                            await Task.Delay(delay, ct);
                            continue;
                        }

                        //got one, update cache
                        _pidCache.TryAdd(clusterIdentity, pid);
                    }
                    catch (Exception)
                    {
                        if (_requestLogThrottle().IsOpen())
                            _logger.LogWarning("Failed to get PID from IIdentityLookup");
                        await Task.Delay(delay, ct);
                        continue;
                    }
                    _logger.LogDebug(
                       "Requesting {Identity}-{Kind} Message {Message} - Got PID {PID} from IdentityLookup",
                       clusterIdentity.Identity, clusterIdentity.Kind, message, pid
                       );

                    if (context.System.Shutdown.IsCancellationRequested)
                    {
                        return default!;
                    }

                    var (status, res) = await TryRequestAsync<T>(clusterIdentity, message, pid!, "IIdentityLookup", context, ct);
                    switch (status)
                    {
                        case ResponseStatus.Ok:
                            return res;
                        case ResponseStatus.TimedOutOrException:
                            await Task.Delay(delay, ct);
                            break;
                        case ResponseStatus.DeadLetter:
                            await _identityLookup.RemovePidAsync(pid!, ct);
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (context.System.Shutdown.IsCancellationRequested)
                {
                    return default!;
                }
            }
            return default!;
        }

        public Task<T> RequestAsync<T>(ClusterIdentity clusterIdentity, object message, CancellationToken ct) => RequestAsync<T>(clusterIdentity, message, _context, ct);

        private async Task<(ResponseStatus ok, T res)> TryRequestAsync<T>(ClusterIdentity clusterIdentity,
            object message,
            PID cachedPid, string source, ISenderContext context, CancellationToken ct)
        {
            try
            {
                var res = await context.RequestAsync<T>(cachedPid, message, ct);

                if (res is not null) return (ResponseStatus.Ok, res);
            }
            catch (DeadLetterException)
            {
                // if (!context.System.Shutdown.IsCancellationRequested && _requestLogThrottle().IsOpen())
                //     _logger.LogInformation("TryRequestAsync failed, dead PID from {Source}", source);
                _pidCache.TryRemove(clusterIdentity);
                return (ResponseStatus.DeadLetter, default)!;
            }
            catch (TimeoutException)
            {
                if (!context.System.Shutdown.IsCancellationRequested && _requestLogThrottle().IsOpen())
                    _logger.LogWarning("TryRequestAsync timed out, PID from {Source}", source);
            }
            catch (Exception x)
            {
                if (!context.System.Shutdown.IsCancellationRequested && _requestLogThrottle().IsOpen())
                    _logger.LogWarning(x, "TryRequestAsync failed with exception, PID from {Source}", source);
            }
            _pidCache.TryRemove(clusterIdentity);
            return (ResponseStatus.TimedOutOrException, default)!;
        }

        private enum ResponseStatus
        {
            Ok,
            TimedOutOrException,
            DeadLetter
        }
    }
}