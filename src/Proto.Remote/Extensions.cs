// -----------------------------------------------------------------------
//   <copyright file="Extensions.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Proto.Remote
        
{
    public static class Extensions
    {
        public static IRemote GetRemote(this ActorSystem actorSystem)
        {
            var remote = actorSystem.ServiceProvider.GetRequiredService<IRemote>();
            return remote;
        }
    }
}