// -----------------------------------------------------------------------
//   <copyright file="Plugins.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;

namespace Proto
{
    public class Plugins: IServiceProvider
    {
        private ConcurrentDictionary<Type, IProtoPlugin> _plugins { get; } = new ConcurrentDictionary<Type, IProtoPlugin>();
        public bool AddPlugin<TPlugin, TInstance>(TInstance plugin)
        where TPlugin : IProtoPlugin
        where TInstance : class, TPlugin
        {
            return _plugins.TryAdd(typeof(TPlugin), plugin);
        }
        public bool AddPlugin<TPlugin>(TPlugin plugin)
        where TPlugin : IProtoPlugin
        {
            return _plugins.TryAdd(typeof(TPlugin), plugin);
        }
        public object GetService(Type serviceType)
        {
            return _plugins[serviceType];
        }
    }
}