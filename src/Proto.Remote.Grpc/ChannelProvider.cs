// -----------------------------------------------------------------------
//   <copyright file="ChannelProvider.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------
using Grpc.Core;

namespace Proto.Remote.Grpc
{
    internal class ChannelProvider : IChannelProvider
    {
        public ChannelBase GetChannel(RemoteConfig remoteConfig, string address)
        {
            var channel = new Channel(address, remoteConfig.ChannelCredentials, remoteConfig.ChannelOptions);
            return channel;
        }
    }
}