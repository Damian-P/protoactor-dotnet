// -----------------------------------------------------------------------
//   <copyright file="IChannelProvider.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using Grpc.Core;

namespace Proto.Remote
{
    public interface IChannelProvider
    {
        ChannelBase GetChannel(RemoteConfig remoteConfig, string address);
    }
}