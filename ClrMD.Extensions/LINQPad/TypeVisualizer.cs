using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using LINQPad;

namespace ClrMD.Extensions.LINQPad
{
    public abstract class TypeVisualizer
    {
        private static Dictionary<string, TypeVisualizer> m_visualizers;

        private static List<KeyValuePair<Regex, TypeVisualizer>> m_regexVisualizers;

        static TypeVisualizer()
        {
            m_visualizers = new Dictionary<string, TypeVisualizer>();
            m_regexVisualizers = new List<KeyValuePair<Regex, TypeVisualizer>>();

            RegisterVisualizer("System.Collections.Generic.Dictionary", new DictionaryVisualizer());
            RegisterVisualizer("System.Collections.Generic.List", new ListVisualizer());
            RegisterVisualizer("System.Collections.Generic.Queue", new QueueVisualizer());
            RegisterVisualizer("System.Data.DataRow", new DataRowVisualizer());
            RegisterVisualizer("System.Data.DataTable", new DataTableVisualizer());
            RegisterVisualizer("System.Data.DataSet", new DataSetVisualizer());
        }

        public static void RegisterVisualizer(string typeName, TypeVisualizer visualizer)
        {
            m_visualizers[typeName] = visualizer;
        }

        public static void RegisterRegexVisualizer(string typeName, TypeVisualizer visualizer)
        {
            m_regexVisualizers.Add(new KeyValuePair<Regex, TypeVisualizer>(new Regex(typeName, RegexOptions.Compiled), visualizer));
        }

        public static TypeVisualizer TryGetVisualizer(ClrDynamic o)
        {
            TypeVisualizer visualizer;

            //strip to non generic portion.
            string name = o.TypeName;
            int index = name.IndexOf("<");
            if (index != -1)
            {
                name = name.Substring(0, index);
            }

            if (m_visualizers.TryGetValue(name, out visualizer))
                return visualizer;

            foreach (var keyValuePair in m_regexVisualizers)
            {
                if (keyValuePair.Key.IsMatch(name))
                {
                    return keyValuePair.Value;
                }
            }

            return null;
        }

        public abstract object GetValue(ClrDynamic o);
    }

    public interface ISingleCellEnumerableVisual
    {
        int Count { get; }
        IEnumerable<ClrDynamic> Items { get; }
    }

    public class QueueVisualizer : TypeVisualizer
    {
        public class QueueVisual : ISingleCellEnumerableVisual
        {
            public int Count { get; set; }

            public IEnumerable<ClrDynamic> Items { get; set; }
        }

        public override object GetValue(ClrDynamic o)
        {
            int size = (int)o.Dynamic._size;
            int head = (int)o.Dynamic._head;
            int tail = (int)o.Dynamic._tail;
            var items = o.Dynamic._array as IEnumerable<ClrDynamic>;

            if (tail >= head)
            {
                return new QueueVisual()
                {
                    Count = size,
                    Items = items.Skip(head).Take(size),
                };
            }

            //wrap around!
            return new QueueVisual()
            {
                Count = size,
                Items = items.Skip(head).Concat(items.Take(tail))
            };

        }
    }

    public class ListVisualizer : TypeVisualizer
    {
        public class ListVisual : ISingleCellEnumerableVisual
        {
            public int Count { get; set; }

            public IEnumerable<ClrDynamic> Items { get; set; }
        }

        public override object GetValue(ClrDynamic o)
        {
            int size = (int) o.Dynamic._size;
            var col = o.Dynamic._items as IEnumerable<ClrDynamic>;
            return new ListVisual()
            {
                Count = size,
                Items = col?.Take(size),
            };
        }
    }

    public class DictionaryVisualizer : TypeVisualizer
    {
        public class DictionaryVisual
        {
            public int Count { get; set; }
            public IEnumerable<KeyValuePair<ClrDynamic, ClrDynamic>> Items { get; set; }
        }
        
        public override object GetValue(ClrDynamic o)
        {
            return new DictionaryVisual
            {
                Count = (int)o.Dynamic.count - (int)o.Dynamic.freeCount,
                Items = from entry in o["entries"]
                        where (int)entry["hashCode"] > 0
                        select new KeyValuePair<ClrDynamic, ClrDynamic>(entry["key"], entry["value"])
            };
        }
    }

    public class DataRowVisualizer : TypeVisualizer
    {
        public class DataRowVisual : ICustomMemberProvider
        {
            private readonly List<ClrDynamic> m_columns;
            private readonly int m_record;

            public ClrDynamic Row { get; private set; }

            public ClrDynamic this[string columnName]
            {
                get
                {
                    var column = m_columns.FirstOrDefault(item => (string)item["_columnName"] == columnName);

                    if (column == null)
                        return null;

                    return (ClrDynamic)column.Dynamic._storage.values[m_record];
                }
            }

            public DataRowState RowState { get; private set; }

            public DataRowVisual(ClrDynamic row)
            {
                Row = row;

                int tempRecord = (int)row["tempRecord"];
                int newRecord = (int)row["newRecord"];
                int oldRecord = (int)row["oldRecord"];

                RowState = GetRowState(newRecord, oldRecord);
                m_record = GetRecord(tempRecord, newRecord, oldRecord);

                m_columns = (from column in (ClrDynamic)row.Dynamic._columns._list._items
                             where !column.IsNull()
                             select column).ToList();
            }

            public IEnumerable<string> GetNames()
            {
                yield return "RowState";

                foreach (var name in m_columns.Select(column => (string)column["_columnName"]))
                    yield return name;
            }

            public IEnumerable<Type> GetTypes()
            {
                yield return typeof(DataRowState);

                for (int i = 0; i < m_columns.Count; i++)
                    yield return typeof(object);
            }

            public IEnumerable<object> GetValues()
            {
                yield return RowState;

                foreach (ClrDynamic column in m_columns)
                {
                    var value = (ClrDynamic)column.Dynamic._storage.values[m_record];

                    yield return value.HasSimpleValue ? value.SimpleValue : value;
                }
            }

            private DataRowState GetRowState(int newRecord, int oldRecord)
            {
                if (oldRecord == newRecord)
                {
                    if (oldRecord == -1)
                        return DataRowState.Detached;

                    return DataRowState.Unchanged;
                }

                if (oldRecord == -1)
                    return DataRowState.Added;

                if (newRecord == -1)
                    return DataRowState.Deleted;

                return DataRowState.Modified;
            }

            private int GetRecord(int tempRecord, int newRecord, int oldRecord)
            {
                int record = tempRecord;

                if (record == -1)
                    record = newRecord;

                if (record == -1)
                    record = oldRecord;

                return record;
            }
        }

        public override object GetValue(ClrDynamic o)
        {
            return new DataRowVisual(o);
        }
    }

    public class DataTableVisualizer : TypeVisualizer
    {
        public class DataTableVisual : IEnumerable<DataRowVisualizer.DataRowVisual>
        {
            private readonly ClrDynamic m_table;

            public string Name
            {
                get { return (string)m_table["tableName"]; }
            }

            public DataTableVisual(ClrDynamic table)
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

        internal class RBTreeEnumerator : IEnumerator<ClrDynamic>
        {
            private const int NIL = 0;
            private readonly ClrDynamic m_tree;
            private int m_index;
            private int m_mainTreeNodeId;
            private ClrDynamic m_current;

            private dynamic Tree
            {
                get { return m_tree; }
            }
 
            internal RBTreeEnumerator(ClrDynamic tree)
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

            private ClrDynamic Key(int nodeId)
            {
                return (Tree._pageTable[nodeId >> 16].Slots[nodeId & 0xFFFF].keyOfNode);
            }

            public void Reset()
            {
                m_index = NIL;
                m_mainTreeNodeId = (int)m_tree["root"];
                m_current = null;
            }

            public ClrDynamic Current
            {
                get { return m_current; }
            }

            object IEnumerator.Current
            {
                get { return Current; }
            }
        }

        public override object GetValue(ClrDynamic o)
        {
            return new DataTableVisual(o);
        }
    }

    public class DataSetVisualizer : TypeVisualizer
    {
        public class DataSetVisual : ICustomMemberProvider
        {
            private readonly List<ClrDynamic> m_tables;

            public IEnumerable<DataTableVisualizer.DataTableVisual> DataTables
            {
                get { return m_tables.Select(table => new DataTableVisualizer.DataTableVisual(table)); }
            }

            public DataSetVisual(ClrDynamic dataset)
            {
                m_tables = (from table in (ClrDynamic)dataset.Dynamic.tableCollection._list._items
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

        public override object GetValue(ClrDynamic o)
        {
            return new DataSetVisual(o);
        }
    }
}