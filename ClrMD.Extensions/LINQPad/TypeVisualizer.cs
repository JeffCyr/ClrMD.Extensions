using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LINQPad;

namespace ClrMD.Extensions.LINQPad
{
    public abstract class TypeVisualizer
    {
        private static Dictionary<string, TypeVisualizer> m_visualizers;

        static TypeVisualizer()
        {
            m_visualizers = new Dictionary<string, TypeVisualizer>();

            RegisterVisualizer("System.Collections.Generic.Dictionary", new DictionaryVisualizer());
            RegisterVisualizer("System.Data.DataRow", new DataRowVisualizer());
            RegisterVisualizer("System.Data.DataTable", new DataTableVisualizer());
            RegisterVisualizer("System.Data.DataSet", new DataSetVisualizer());
        }

        public static void RegisterVisualizer(string typeName, TypeVisualizer visualizer)
        {
            m_visualizers[typeName] = visualizer;
        }

        public static TypeVisualizer TryGetVisualizer(ClrObject o)
        {
            TypeVisualizer visualizer;
            if (m_visualizers.TryGetValue(o.TypeName, out visualizer))
                return visualizer;

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

    public class DataRowVisualizer : TypeVisualizer
    {
        public class DataRowVisual : ICustomMemberProvider
        {
            private readonly ClrObject m_row;
            private List<ClrObject> m_columns; 

            public DataRowVisual(ClrObject row)
            {
                m_row = row;

                m_columns = (from column in (ClrObject)row.Dynamic._columns._list._items
                             where !column.IsNull()
                             select column).ToList();
            }

            public IEnumerable<string> GetNames()
            {
                return m_columns.Select(column => (string)column["_columnName"]);
            }

            public IEnumerable<Type> GetTypes()
            {
                return m_columns.Select(column => typeof(object));
            }

            public IEnumerable<object> GetValues()
            {
                int record = (int)m_row["tempRecord"];

                if (record == -1)
                    record = (int)m_row["newRecord"];

                if (record == -1)
                    record = (int)m_row["oldRecord"];

                foreach (ClrObject column in m_columns)
                {
                    var value = (ClrObject)column.Dynamic._storage.values[record];

                    yield return value.HasSimpleValue ? value.SimpleValue : value;
                }
            }
        }

        public override object GetValue(ClrObject o)
        {
            return new DataRowVisual(o);
        }
    }

    public class DataTableVisualizer : TypeVisualizer
    {
        public class DataTableVisual : IEnumerable<DataRowVisualizer.DataRowVisual>
        {
            private readonly ClrObject m_table;

            public DataTableVisual(ClrObject table)
            {
                m_table = table;
            }

            public IEnumerator<DataRowVisualizer.DataRowVisual> GetEnumerator()
            {
                var enumerator = new RBTreeEnumerator(m_table.Dynamic.rowCollection.list);

                while (enumerator.MoveNext())
                {
                    yield return new DataRowVisualizer.DataRowVisual(enumerator.Current);
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        internal class RBTreeEnumerator : IEnumerator<ClrObject>
        {
            private const int NIL = 0;
            private readonly ClrObject m_tree;
            private int m_index;
            private int m_mainTreeNodeId;
            private ClrObject m_current;

            private dynamic Tree
            {
                get { return m_tree; }
            }
 
            internal RBTreeEnumerator(ClrObject tree)
            {
                m_tree = tree;
                m_index = NIL;
                m_mainTreeNodeId = (int)tree["root"];
                m_current = null;
            }

            public void Dispose()
            {
                
            }

            public bool MoveNext()
            {
                bool hasCurrent = Successor(ref m_index, ref m_mainTreeNodeId);
                m_current = Key(m_index);
                return hasCurrent;
            }

            private int Successor(int x_id)
            {
                if (Right(x_id) != NIL)
                    return Minimum(Right(x_id)); //return left most node in right sub-tree.
                int y_id = Parent(x_id);

                while (y_id != NIL && x_id == Right(y_id))
                {
                    x_id = y_id;
                    y_id = Parent(y_id);
                }
                return y_id;
            }

            private bool Successor(ref int nodeId, ref int mainTreeNodeId)
            {
                if (NIL == nodeId)
                {   // find first node, using branchNodeId as the root
                    nodeId = Minimum(mainTreeNodeId);
                    mainTreeNodeId = NIL;
                }
                else
                {   // find next node
                    nodeId = Successor(nodeId);

                    if ((NIL == nodeId) && (NIL != mainTreeNodeId))
                    {   // done with satellite branch, move back to main tree
                        nodeId = Successor(mainTreeNodeId);
                        mainTreeNodeId = NIL;
                    }
                }
                if (NIL != nodeId)
                {   // test for satellite branch
                    if (NIL != Next(nodeId))
                    {   // find first node of satellite branch
                        if (NIL != mainTreeNodeId)
                        {   // satellite branch has satellite branch - very bad
                            throw new InvalidOperationException("satellite branch has satellite branch");
                        }
                        mainTreeNodeId = nodeId;
                        nodeId = Minimum(Next(nodeId));
                    }
                    // has value
                    return true;
                }
                // else no value, done with main tree
                return false;
            }

            private int Minimum(int x_id)
            {
                while (Left(x_id) != NIL)
                {
                    x_id = Left(x_id);
                }
                return x_id;
            }

            private int Right(int nodeId)
            {
                return (int)(Tree._pageTable[nodeId >> 16].Slots[nodeId & 0xFFFF].rightId);
            }

            private int Left(int nodeId)
            {
                return (int)(Tree._pageTable[nodeId >> 16].Slots[nodeId & 0xFFFF].leftId);
            }

            private int Parent(int nodeId)
            {
                return (int)(Tree._pageTable[nodeId >> 16].Slots[nodeId & 0xFFFF].parentId);
            }

            private int Next(int nodeId)
            {
                return (int)(Tree._pageTable[nodeId >> 16].Slots[nodeId & 0xFFFF].nextId);
            }

            private ClrObject Key(int nodeId)
            {
                return (Tree._pageTable[nodeId >> 16].Slots[nodeId & 0xFFFF].keyOfNode);
            }

            public void Reset()
            {
                m_index = NIL;
                m_mainTreeNodeId = (int)m_tree["root"];
                m_current = null;
            }

            public ClrObject Current
            {
                get { return m_current; }
            }

            object IEnumerator.Current
            {
                get { return Current; }
            }
        }

        public override object GetValue(ClrObject o)
        {
            return new DataTableVisual(o);
        }
    }

    public class DataSetVisualizer : TypeVisualizer
    {
        public class DataSetVisual : ICustomMemberProvider
        {
            private List<ClrObject> m_tables; 

            public DataSetVisual(ClrObject dataset)
            {
                m_tables = (from table in (ClrObject)dataset.Dynamic.tableCollection._list._items
                            where !table.IsNull()
                            orderby (string)table["tableName"]
                            select table).ToList();
            }

            public IEnumerable<string> GetNames()
            {
                return m_tables.Select(table => (string)table["tableName"]);
            }

            public IEnumerable<Type> GetTypes()
            {
                return m_tables.Select(table => typeof(DataTableVisualizer.DataTableVisual));
            }

            public IEnumerable<object> GetValues()
            {
                return m_tables.Select(table => new DataTableVisualizer.DataTableVisual(table));
            }
        }

        public override object GetValue(ClrObject o)
        {
            return new DataSetVisual(o);
        }
    }
}