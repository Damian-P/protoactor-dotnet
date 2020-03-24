using System;
using System.Collections.Generic;

namespace Proto
{
    public class ActorSystem
    {
        public ProcessRegistry ProcessRegistry { get; }
        public RootContext Root { get; }

        public Guardians Guardians { get; }

        public DeadLetterProcess DeadLetter { get; }

        public EventStream EventStream { get; }

        public Dictionary<Type, object> Plugins { get; } = new Dictionary<Type, object>();

        public ActorSystem()
        {
            ProcessRegistry = new ProcessRegistry(this);
            Root = new RootContext(this);
            DeadLetter = new DeadLetterProcess(this);
            Guardians = new Guardians(this);
            EventStream = new EventStream();
        }
    }
}