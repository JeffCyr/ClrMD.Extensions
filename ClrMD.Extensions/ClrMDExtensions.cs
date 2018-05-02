using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Runtime;

namespace ClrMD.Extensions
{
    public static class ClrMDExtensions
    {
        public static IEnumerable<T> Cast<T>(this IEnumerable<ClrDynamic> source)
        {
            foreach (var item in source)
                yield return (T)(dynamic)item;
        }

        public static ClrType GetSafeObjectType(this ClrHeap heap, ulong address)
        {
            return heap.GetObjectType(address) ?? ClrMDSession.Current.ErrorType;
        }

        public static ClrDynamic GetDynamicObject(this ClrHeap heap, ulong address)
        {
            return new ClrDynamic(address, heap.GetSafeObjectType(address));
        }

        public static IEnumerable<ClrDynamic> EnumerateDynamicObjects(this ClrHeap heap)
        {
            return from address in heap.EnumerateObjectAddresses()
                   select heap.GetDynamicObject(address);
        }

        public static IEnumerable<ClrDynamic> EnumerateDynamicObjects(this ClrHeap heap, ClrType type)
        {
            return from address in heap.EnumerateObjectAddresses()
                   let objectType = heap.GetSafeObjectType(address)
                   where objectType == type
                   select new ClrDynamic(address, type);
        }

        public static IEnumerable<ClrDynamic> EnumerateDynamicObjects(this ClrHeap heap, params ClrType[] types)
        {
            return heap.EnumerateDynamicObjects((IEnumerable<ClrType>)types);
        }

        public static IEnumerable<ClrDynamic> EnumerateDynamicObjects(this ClrHeap heap, IEnumerable<ClrType> types)
        {
            if (types == null)
                return heap.EnumerateDynamicObjects();

            IList<ClrType> castedTypes = types as IList<ClrType> ?? types.ToList();

            if (castedTypes.Count == 0)
                return heap.EnumerateDynamicObjects();

            if (castedTypes.Count == 1)
                return heap.EnumerateDynamicObjects(castedTypes[0]);

            HashSet<ClrType> set = new HashSet<ClrType>(castedTypes);

            return from address in heap.EnumerateObjectAddresses()
                   let type = heap.GetSafeObjectType(address)
                   where set.Contains(type)
                   select new ClrDynamic(address, type);
        }

        public static IEnumerable<ClrDynamic> EnumerateDynamicObjects(this ClrHeap heap, string typeName)
        {
            if (!typeName.Contains("*"))
            {
                var type = 
                    (from t in heap.EnumerateTypes()
                    let deobfuscator = ClrMDSession.Current.GetTypeDeobfuscator(t)
                    where deobfuscator.OriginalName == typeName
                    select t).First();

                return (ClrDynamic)heap.EnumerateObjects().First(item => item.Type == type);
            }

            var regex = new Regex($"^{Regex.Escape(typeName).Replace("\\*", ".*")}$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var types = 
                from type in heap.EnumerateTypes()
                let deobfuscator = ClrMDSession.Current.GetTypeDeobfuscator(type)
                where regex.IsMatch(deobfuscator.OriginalName)
                select type;

            var typeSet = new HashSet<ClrType>(types);

            return heap.EnumerateObjects().Where(o => typeSet.Contains(o.Type)).Select(o => (ClrDynamic)o);
        }

        public static ClrDynamic DowncastToBase(this ClrDynamic clrObject)
        {
            return clrObject?.Type?.BaseType != null ? new ClrDynamic(clrObject.Address, clrObject.Type.BaseType) : null;
        }

        public class StackFrameInfo
        {
            public string Function;
            public List<ClrDynamic> Objects;
        }

        public static List<StackFrameInfo> GetDetailedStackTrace(this ClrThread thread)
        {
            List<StackFrameInfo> stackframes = new List<StackFrameInfo>();

            List<ClrRoot> stackObjects = thread.EnumerateStackObjects().ToList();

            ulong lastAddress = 0;
            foreach (ClrStackFrame frame in thread.StackTrace)
            {
                ClrStackFrame f = frame;
                List<ClrDynamic> objectsInFrame = stackObjects
                    .Where(o => o.Address > lastAddress && o.Address <= f.StackPointer)
                    .OrderBy(o => o.Address)
                    .Select(o => ClrMDSession.Current.Heap.GetDynamicObject(o.Object))
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

        public static ClrObjectWrapper Wrap(this ClrDynamic item)
        {
            return new ClrObjectWrapper(item);
        }

        public static ClrType GetDeclaringType(this ClrField field, ClrType containingType)
        {
            if (field.Offset == -1)
                return containingType;

            List<ClrType> types = new List<ClrType>();
            ClrType t = containingType;

            while (t != null)
            {
                types.Add(t);
                t = t.BaseType;
            }

            types.Reverse();

            foreach (var type in types)
            {
                if (type.Fields.Any(item => item.Offset == field.Offset))
                    return type;
            }

            return containingType;
        }
    }

    public class ClrObjectWrapper
    {
        public string Type
        {
            get { return Object.Type.Name; }
        }

        public ClrDynamic Object { get; set; }

        public ClrObjectWrapper(ClrDynamic item)
        {
            Object = item;
        }
    }
}