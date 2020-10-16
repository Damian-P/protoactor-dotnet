﻿// -----------------------------------------------------------------------
//   <copyright file="Remote.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Proto.Remote
{
    public static class IRemoteExtensions
    {
        /// <summary>
        ///     Spawn a remote actor with auto-generated name
        /// </summary>
        /// <param name="address">Remote node address</param>
        /// <param name="kind">Actor kind, must be known on the remote node</param>
        /// <param name="timeout">Timeout for the confirmation to be received from the remote node</param>
        /// <returns></returns>
        public static Task<ActorPidResponse> SpawnAsync(this IRemote remote, string address, string kind, TimeSpan timeout) =>
            SpawnNamedAsync(remote, address, "", kind, timeout);

        /// <summary>
        ///     Spawn a remote actor with a name
        /// </summary>
        /// <param name="address">Remote node address</param>
        /// <param name="name">Remote actor name</param>
        /// <param name="kind">Actor kind, must be known on the remote node</param>
        /// <param name="timeout">Timeout for the confirmation to be received from the remote node</param>
        /// <returns></returns>
        public static async Task<ActorPidResponse> SpawnNamedAsync(this IRemote remote, string address, string name, string kind, TimeSpan timeout)
        {
            var activator = ActivatorForAddress(address);

            var res = await remote.System.Root.RequestAsync<ActorPidResponse>(
                activator, new ActorPidRequest
                {
                    Kind = kind,
                    Name = name
                }, timeout
            );

            return res;

            static PID ActivatorForAddress(string address) => PID.FromAddress(address, "activator");
        }
    }
}