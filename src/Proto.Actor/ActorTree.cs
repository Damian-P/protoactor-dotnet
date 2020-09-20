using System;

namespace Proto
{
    public class ActorTree : RootActorTree
    {
        public RootActorTree Parent { get; private set; }
        public PID PID { get; private set; }
        public ActorTree(PID pid, RootActorTree parent)
        {
            PID = pid;
            Parent = parent;
            Parent.Add(pid, this);
        }
    }
}