// -----------------------------------------------------------------------
//   <copyright file="SelfHostedRemote.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Grpc.Core;

namespace Proto.Remote
{
    public class ChannelProvider : IChannelProvider
    {
        private readonly GrpcRemoteConfig _grpcRemoteConfig;

        public ChannelProvider(GrpcRemoteConfig? grpcRemoteConfig = null)
        {
            _grpcRemoteConfig = grpcRemoteConfig?? new GrpcRemoteConfig();
        }
        public ChannelBase GetChannel(string address)
        {
            return new Channel(address, _grpcRemoteConfig.ChannelCredentials, _grpcRemoteConfig.ChannelOptions);
        }
    }
}