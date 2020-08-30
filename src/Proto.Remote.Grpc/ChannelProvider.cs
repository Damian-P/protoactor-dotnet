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
        private readonly Action<List<ChannelOption>>? _configureChannelOptions;

        public ChannelProvider(Action<List<ChannelOption>>? configureChannelOptions = null)
        {
            _configureChannelOptions = configureChannelOptions;
        }
        public ChannelBase GetChannel(ChannelCredentials channelCredentials, string address)
        {
            var channelOptions = new List<ChannelOption>();
            _configureChannelOptions?.Invoke(channelOptions);
            return new Channel(address, channelCredentials, channelOptions);
        }
    }
}