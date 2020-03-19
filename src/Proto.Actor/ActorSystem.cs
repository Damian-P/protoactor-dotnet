namespace Proto
{
    public interface IActorSystem
    {
        ProcessRegistry ProcessRegistry { get; }
        RootContext Root { get; }
        Guardians Guardians { get; }
        DeadLetterProcess DeadLetter { get; }
        EventStream EventStream { get; }
    }

    public class ActorSystem : IActorSystem
    {
        public ProcessRegistry ProcessRegistry { get; }
        public RootContext Root { get; }

        public Guardians Guardians { get; }

        public DeadLetterProcess DeadLetter { get; }

        public EventStream EventStream { get; }

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