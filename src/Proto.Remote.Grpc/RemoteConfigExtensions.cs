// -----------------------------------------------------------------------
//   <copyright file="GrpcRemoteConfig.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using Grpc.Core;

namespace Proto.Remote
{
    public static class RemoteConfigExtensions
    {
        public static TRemoteConfig WithChannelOptions<TRemoteConfig>(this TRemoteConfig remoteConfig, IEnumerable<ChannelOption> options)
        where TRemoteConfig : GrpcRemoteConfig
        {
            remoteConfig.ChannelOptions = options;
            return remoteConfig;
        }

        public static TRemoteConfig WithChannelCredentials<TRemoteConfig>(this TRemoteConfig remoteConfig, ChannelCredentials channelCredentials)
        where TRemoteConfig : GrpcRemoteConfig
        {
            remoteConfig.ChannelCredentials = channelCredentials;
            return remoteConfig;
        }

        public static TRemoteConfig WithServerCredentials<TRemoteConfig>(this TRemoteConfig remoteConfig, ServerCredentials serverCredentials)
        where TRemoteConfig : GrpcRemoteConfig
        {
            remoteConfig.ServerCredentials = serverCredentials;
            return remoteConfig;
        }

    }
}