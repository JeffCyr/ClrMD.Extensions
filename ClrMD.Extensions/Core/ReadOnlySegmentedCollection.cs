using System;
using System.Collections;
using System.Collections.Generic;

namespace ClrMD.Extensions.Core
{
    public class ReadOnlySegmentedCollection<T> : IReadOnlyCollection<T>
    {
        private const int SegmentSize = 4096;
    
        private readonly LinkedList<T[]> m_segments;
    
        public int Count { get; }
    
        public ReadOnlySegmentedCollection(IEnumerable<T> items)
        {
            m_segments = new LinkedList<T[]>();

            T[] segment = new T[SegmentSize];
            int i = 0;

            using (var enumerator = items.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    ++Count;
                    segment[i++] = enumerator.Current;

                    if (i == SegmentSize)
                    {
                        m_segments.AddLast(segment);

                        segment = new T[SegmentSize];
                        i = 0;
                    }
                }
            }

            if (i > 0)
            {
                Array.Resize(ref segment, i);
                m_segments.AddLast(segment);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var segment in m_segments)
            {
                for (int i = 0; i < segment.Length; i++)
                    yield return segment[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}