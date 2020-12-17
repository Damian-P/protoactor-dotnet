// -----------------------------------------------------------------------
// <copyright file="RootActors.cs" company="Asynkron AB">
//      Copyright (C) 2015-2020 Asynkron AB All rights reserved
// </copyright>
// -----------------------------------------------------------------------
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Proto
{
    public class RootActors : IEnumerable<PID>
    {
        private readonly ConcurrentDictionary<string, PID> _lookup = new();
        internal void Add(PID pid) => _lookup.TryAdd(pid.Id, pid);

        internal void Remove(PID pid) => _lookup.TryRemove(pid.Id, out _);

        internal Task StopAllAsync(IRootContext context) => Task.WhenAll(_lookup.Values.ToArray().Select(a => context.StopAsync(a)));
        public IEnumerator<PID> GetEnumerator() => _lookup.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _lookup.Values.GetEnumerator();
    }
}