using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ClrMD.Extensions.Core;
using Microsoft.Diagnostics.Runtime;

namespace ClrMD.Extensions
{
    public static class ClrMDExtensions
    {
        public static IEnumerable<T> Cast<T>(this IEnumerable<ClrObject> source)
        {
            foreach (var item in source)
                yield return (T)(dynamic)item;
        }

        public static ClrType GetSafeObjectType(this ClrHeap heap, ulong address)
        {
            return heap.GetObjectType(address) ?? new UnknownType(heap);
        }

        public static ClrObject GetClrObject(this ClrHeap heap, ulong address)
        {
            return new ClrObject(address, heap.GetSafeObjectType(address));
        }

        public static IEnumerable<ClrObject> EnumerateClrObjects(this ClrHeap heap)
        {
            return from address in heap.EnumerateObjects()
                   select heap.GetClrObject(address);
        }

        public static IEnumerable<ClrObject> EnumerateClrObjects(this ClrHeap heap, ClrType type)
        {
            return from address in heap.EnumerateObjects()
                   let objectType = heap.GetSafeObjectType(address)
                   where objectType == type
                   select new ClrObject(address, type);
        }

        public static IEnumerable<ClrObject> EnumerateClrObjects(this ClrHeap heap, params ClrType[] types)
        {
            return heap.EnumerateClrObjects((IEnumerable<ClrType>)types);
        }

        public static IEnumerable<ClrObject> EnumerateClrObjects(this ClrHeap heap, IEnumerable<ClrType> types)
        {
            if (types == null)
                return heap.EnumerateClrObjects();

            IList<ClrType> castedTypes = types as IList<ClrType> ?? types.ToList();

            if (castedTypes.Count == 0)
                return heap.EnumerateClrObjects();

            if (castedTypes.Count == 1)
                return heap.EnumerateClrObjects(castedTypes[0]);

            HashSet<ClrType> set = new HashSet<ClrType>(castedTypes);

            return from address in heap.EnumerateObjects()
                   let type = heap.GetSafeObjectType(address)
                   where set.Contains(type)
                   select new ClrObject(address, type);
        }

        public static IEnumerable<ClrObject> EnumerateClrObjects(this ClrHeap heap, string typeName)
        {
            if (typeName.Contains("*"))
            {
                string typeNameRegex = "^" + Regex.Escape(typeName).Replace("\\*", ".*") + "$";
                Regex regex = new Regex(typeNameRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                
                return from address in heap.EnumerateObjects()
                       let type = heap.GetSafeObjectType(address)
                       where type != null && regex.IsMatch(type.Name)
                       select new ClrObject(address, type);
            }

            return from address in heap.EnumerateObjects()
                   let type = heap.GetSafeObjectType(address)
                   where type != null && type.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase)
                   select new ClrObject(address, type);
        }

        public static IEnumerable<ClrObject> EnumerateClrObjects(this ClrHeap heap, params string[] typeNames)
        {
            return heap.EnumerateClrObjects((IEnumerable<string>)typeNames);
        }

        public static IEnumerable<ClrObject> EnumerateClrObjects(this ClrHeap heap, IEnumerable<string> typeNames)
        {
            if (typeNames == null)
                return heap.EnumerateClrObjects();

            IList<string> castedTypes = typeNames as IList<string> ?? typeNames.ToList();

            if (castedTypes.Count == 0)
                return heap.EnumerateClrObjects();

            if (castedTypes.Count == 1)
                return heap.EnumerateClrObjects(castedTypes[0]);

            HashSet<string> set = new HashSet<string>(castedTypes, StringComparer.OrdinalIgnoreCase);

            return from address in heap.EnumerateObjects()
                   let type = heap.GetSafeObjectType(address)
                   where type != null && set.Contains(type.Name)
                   select new ClrObject(address, type);
        }

        public static ClrObject DowncastToBase(this ClrObject clrObject)
        {
            return clrObject?.Type?.BaseType != null ? new ClrObject(clrObject.Address, clrObject.Type.BaseType) : null;
        }

        public class StackFrameInfo
        {
            public string Function;
            public List<ClrObject> Objects;
        }

        public static List<StackFrameInfo> GetDetailedStackTrace(this ClrThread thread)
        {
            List<StackFrameInfo> stackframes = new List<StackFrameInfo>();

            List<ClrRoot> stackObjects = thread.EnumerateStackObjects().ToList();

            ulong lastAddress = 0;
            foreach (ClrStackFrame frame in thread.StackTrace)
            {
                ClrStackFrame f = frame;
                List<ClrObject> objectsInFrame = stackObjects
                    .Where(o => o.Address > lastAddress && o.Address <= f.StackPointer)
                    .OrderBy(o => o.Address)
                    .Select(o => ClrMDSession.Current.Heap.GetClrObject(o.Object))
                    .ToList();

                stackframes.Add(new StackFrameInfo
                {
                    Function = f.DisplayString,
                    Objects = objectsInFrame
                });

                lastAddress = f.StackPointer;
            }

            return stackframes;
        }

        public static string GetStackTrace(this ClrThread thread)
        {
            StringBuilder builder = new StringBuilder();

            foreach (ClrStackFrame frame in thread.StackTrace)
                builder.AppendLine(frame.DisplayString);

            return builder.ToString();
        }

        public static ClrObjectWrapper Wrap(this ClrObject item)
        {
            return new ClrObjectWrapper(item);
        }
    }

    public class ClrObjectWrapper
    {
        public string Type
        {
            get { return Object.Type.Name; }
        }

        public ClrObject Object { get; set; }

        public ClrObjectWrapper(ClrObject item)
        {
            Object = item;
        }
    }
}