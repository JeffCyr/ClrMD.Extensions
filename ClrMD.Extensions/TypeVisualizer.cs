using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace ClrMD.Extensions
{
    public abstract class TypeVisualizer
    {
        private static Dictionary<string, TypeVisualizer> m_visualizers;

        static TypeVisualizer()
        {
            m_visualizers = new Dictionary<string, TypeVisualizer>();

            m_visualizers.Add("System.Collections.Generic.Dictionary", new DictionaryVisualizer());
        }

        public static TypeVisualizer TryGetVisualizer(ClrType type)
        {
            return null;
        }

        public abstract object GetValue(ClrObject o);
    }

    public class DictionaryVisualizer : TypeVisualizer
    {
        public class DictionaryVisual
        {
            public int Count { get; set; }
            public IEnumerable<KeyValuePair<ClrObject, ClrObject>> Items { get; set; }
        }
        
        public override object GetValue(ClrObject o)
        {
            return new DictionaryVisual
            {
                Count = (int)o.Dynamic.count - (int)o.Dynamic.freeCount,
                Items = from entry in o["entries"]
                        where (int)entry["hashCode"] > 0
                        select new KeyValuePair<ClrObject, ClrObject>(entry["key"], entry["value"])
            };
        }
    }
}