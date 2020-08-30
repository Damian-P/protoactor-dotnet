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
        public ChannelBase GetChannel(ChannelCredentials channelCredentials, string address, IEnumerable<ChannelOption> channelOptions)
        {
            return new Channel(address, channelCredentials, channelOptions);
        }
    }
}