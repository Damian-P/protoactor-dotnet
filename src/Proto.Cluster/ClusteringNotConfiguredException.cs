// -----------------------------------------------------------------------
//   <copyright file="ClusteringNotConfiguredException.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;

namespace Proto.Cluster
{
    public class ClusteringNotConfiguredException : Exception
    {
        public ClusteringNotConfiguredException() : base("Clustering is not configured")
        {
        }
    }
}