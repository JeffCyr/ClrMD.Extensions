﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ClrMD.Extensions.Core
{
    public class ReferenceMap
    {
        private Dictionary<ulong, ParentList> m_referenceMap;

        public ReferenceMap(IEnumerable<ClrDynamic> allObjects)
        {
            var references = from parent in allObjects
                             from child in parent.EnumerateReferences()
                             select new { Parent = parent, Child = child.Address };

            m_referenceMap = new Dictionary<ulong, ParentList>();

            foreach (var item in references)
            {
                ParentList parents;

                if (!m_referenceMap.TryGetValue(item.Child, out parents))
                {
                    parents = new ParentList();
                    m_referenceMap.Add(item.Child, parents);
                }

                parents.Start = new Node(item.Parent, parents.Start);
            }
        }

        public IEnumerable<ClrDynamic> GetReferenceBy(ClrDynamic o)
        {
            ParentList parents;
            if (!m_referenceMap.TryGetValue(o.Address, out parents))
                return new ClrDynamic[0];

            return parents.Start.Enumerate();
        }

        private class ParentList
        {
            public Node Start;
        }

        private class Node
        {
            public ClrDynamic Value;
            public Node Next;

            public Node(ClrDynamic o, Node next)
            {
                Value = o;
                Next = next;
            }

            public IEnumerable<ClrDynamic> Enumerate()
            {
                Node node = this;

                while (node != null)
                {
                    yield return node.Value;
                    node = node.Next;
                }
            }
        }
    }
}