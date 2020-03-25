using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Proto
{
    public interface IProtoPlugin
    {

    }
    public class PluginNotFoundException<TPlugin> : Exception
    where TPlugin : IProtoPlugin
    {
        public PluginNotFoundException() : base($"Plugin {typeof(TPlugin).Name} not found") { }
    }
    public class Plugins
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
        public TPlugin GetPlugin<TPlugin>()
        where TPlugin : IProtoPlugin
        {
            var instanceIsPresent = _plugins.TryGetValue(typeof(TPlugin), out var foundInstance);
            return foundInstance is TPlugin plugin ? plugin : throw new PluginNotFoundException<TPlugin>();
        }
    }
}