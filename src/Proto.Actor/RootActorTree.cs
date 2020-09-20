using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Proto
{
    public class RootActorTree: IReadOnlyDictionary<PID, RootActorTree>
    {
        internal RootActorTree(){}
        private readonly ConcurrentDictionary<PID, RootActorTree> _children = new ConcurrentDictionary<PID, RootActorTree>();

        internal void Add(PID pid, ActorTree actorTree)
        {
            _children.TryAdd(pid, actorTree);
        }

        internal void Remove(PID pid)
        {
            _children.TryRemove(pid, out _);
        }

        public void Print()
        {
            Print(this, 0);
        }
        private void Print(RootActorTree actorTree, int generation)
        {
            foreach (var child in actorTree._children)
            {
                Console.WriteLine($"{GenerateTabulations(generation)}{child.Key}");
                Print(child.Value, generation + 1);
            }
            string GenerateTabulations(int number)
            {
                return string.Join("", GenerateTabulationsEnumerator(generation));
            }
            IEnumerable<Char> GenerateTabulationsEnumerator(int number)
            {
                for (int i = 0; i < number; i++)
                {
                    yield return '\t';
                }
            }
        }

        public int TotalCount
        {
            get
            {
                return _children.Values.Aggregate(0, (a, b) => a + b._children.Count + b.TotalCount);
            }
        }
        public IEnumerable<PID> Keys => _children.Keys;
        public IEnumerable<RootActorTree> Values => _children.Values;
        public int Count => _children.Count;
        public RootActorTree this[PID key] => _children[key];
        public bool ContainsKey(PID key)
        {
            return _children.ContainsKey(key);
        }
        public bool TryGetValue(PID key, out RootActorTree value)
        {
            return _children.TryGetValue(key, out value);
        }
        public IEnumerator<KeyValuePair<PID, RootActorTree>> GetEnumerator()
        {
            return _children.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return _children.GetEnumerator();
        }
    }

}