﻿// -----------------------------------------------------------------------
//   <copyright file="Cluster.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using JetBrains.Annotations;
using Proto.Cluster.IdentityLookup;
using Proto.Remote;

namespace Proto.Cluster
{
    [PublicAPI]
    public class ClusterConfig
    {
        public string Name { get; }
        public IClusterProvider ClusterProvider { get; }

        public RemoteConfig RemoteConfig { get; private set; }
        public TimeSpan TimeoutTimespan { get; private set; }
        public IMemberStatusValue? InitialMemberStatusValue { get; private set; }
        public IMemberStatusValueSerializer MemberStatusValueSerializer { get; private set; }
        public Func<string, IMemberStrategy> MemberStrategyBuilder { get; private set; }

        public bool UsePidCache { get; private set; } = true;

        public IIdentityLookup IdentityLookup { get; private set; } 

        public ClusterConfig(string name, IClusterProvider cp)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ClusterProvider = cp ?? throw new ArgumentNullException(nameof(cp));
            IdentityLookup = new PartitionIdentityLookup();
            RemoteConfig = new RemoteConfig();
            TimeoutTimespan = TimeSpan.FromSeconds(5);
            MemberStatusValueSerializer = new NullMemberStatusValueSerializer();
            MemberStrategyBuilder = kind => new SimpleMemberStrategy();
        }

        public ClusterConfig WithRemoteConfig(RemoteConfig remoteConfig)
        {
            RemoteConfig = remoteConfig;
            return this;
        }

        public ClusterConfig WithTimeoutSeconds(int timeoutSeconds)
        {
            TimeoutTimespan = TimeSpan.FromSeconds(timeoutSeconds);
            return this;
        }

        public ClusterConfig WithInitialMemberStatusValue(IMemberStatusValue statusValue)
        {
            InitialMemberStatusValue = statusValue;
            return this;
        }

        public ClusterConfig WithMemberStatusValueSerializer(IMemberStatusValueSerializer serializer)
        {
            MemberStatusValueSerializer = serializer;
            return this;
        }

        public ClusterConfig WithMemberStrategyBuilder(Func<string, IMemberStrategy> builder)
        {
            MemberStrategyBuilder = builder;
            return this;
        }

        public ClusterConfig WithPidCache(bool usePidCache)
        {
            UsePidCache = usePidCache;
            return this;
        }
        
        public ClusterConfig WithIdentityLookup(IIdentityLookup identityLookup)
        {
            IdentityLookup = identityLookup;
            return this;
        }
    }
}