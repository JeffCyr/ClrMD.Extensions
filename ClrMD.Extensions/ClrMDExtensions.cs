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
            return from address in heap.EnumerateObjects().Select(f => f.Address)
                   select heap.GetDynamicObject(address);
        }

        public static IEnumerable<ClrDynamic> EnumerateDynamicObjects(this ClrHeap heap, ClrType type)
        {
            return from address in heap.EnumerateObjects().Select(f => f.Address)
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

            return from address in heap.EnumerateObjects().Select(f => f.Address)
                   let type = heap.GetSafeObjectType(address)
                   where set.Contains(type)
                   select new ClrDynamic(address, type);
        }

        public static IEnumerable<ClrDynamic> EnumerateDynamicObjects(this ClrHeap heap, string typeName)
        {
            var heapTypes = heap.EnumerateTypes();
                //(from objects in heap.EnumerateObjects()
            //group objects by objects.Type into tn 
            // select (tn.Key)
            //);

            if (!typeName.Contains("*"))
            {
                var type = 
                    (from t in heapTypes
                    let deobfuscator = ClrMDSession.Current.GetTypeDeobfuscator(t)
                    where deobfuscator.OriginalName == typeName
                    select t).First();

                return (ClrDynamic)heap.EnumerateObjects().First(item => item.Type == type);
            }

            var regex = new Regex($"^{Regex.Escape(typeName).Replace("\\*", ".*")}$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var types = 
                from type in heapTypes
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

            List<IClrStackRoot> stackObjects = thread.EnumerateStackRoots().ToList(); 

            ulong lastAddress = 0;
            foreach (ClrStackFrame frame in thread.EnumerateStackTrace())
            {
                ClrStackFrame f = frame;
                List<ClrDynamic> objectsInFrame = stackObjects
                    .Where(o => o.Address > lastAddress && o.Address <= f.StackPointer)
                    .OrderBy(o => o.Address)
                    .Select(o => ClrMDSession.Current.Heap.GetDynamicObject(o.Object))
                    .ToList();

                stackframes.Add(new StackFrameInfo
                {
                    Function = f.Method.Name,
                    Objects = objectsInFrame
                });

                lastAddress = f.StackPointer;
            }

            return stackframes;
        }

        public static string GetStackTrace(this ClrThread thread)
        {
            StringBuilder builder = new StringBuilder();
            int count = 0;
            foreach (ClrStackFrame frame in thread.EnumerateStackTrace())
            {

                if (frame != null)
                    builder.AppendLine($"{frame}");

                count++;
                if (count == 100) break;
            }

            
            string stack = builder.ToString();
            return ClrMDSession.Current.DeobfuscateStack(stack);
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

    public static class EnumerateTypesExtension
    {
        /// <summary>
        /// Enumerates types with constructed method tables in all modules.
        /// </summary>
        /// <param name="heap"></param>
        /// <returns></returns>

//this is in clrmd master branch but didn't seem to be published in the nuget yet, so I added it here.
        public static IEnumerable<ClrType> EnumerateTypes(this ClrHeap heap)
        {
            if (heap is null)
                throw new ArgumentNullException(nameof(heap));

            // The ClrHeap actually doesn't know anything about 'types' in the strictest sense, that's
            // all tracked by the runtime.  First, grab the runtime object:

            ClrRuntime runtime = heap.Runtime;

            // Now we loop through every module and grab every constructed MethodTable
            foreach (ClrModule module in runtime.EnumerateModules())
            {
                foreach ((ulong mt, int _) in module.EnumerateTypeDefToMethodTableMap())
                {
                    // Now try to construct a type for mt.  This may fail if the type was only partially
                    // loaded, dump inconsistency, and in some odd corner cases like transparent proxies:
                    ClrType type = runtime.GetTypeByMethodTable(mt);

                    if (type != null)
                        yield return type;
                }
            }
        }
    }
}