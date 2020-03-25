// -----------------------------------------------------------------------
//   <copyright file="ChannelProvider.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using Grpc.Core;

namespace Proto.Remote.Grpc
{
    internal class ChannelProvider : IChannelProvider
    {
        public ChannelBase GetChannel(string address, ChannelCredentials channelCredentials,
            IEnumerable<ChannelOption> channelOptions)
        {
            var channel = new Channel(address, channelCredentials, channelOptions);
            return channel;
        }
    }
}