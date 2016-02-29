using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ClrMD.Extensions.Core;
using ClrMD.Extensions.LINQPad;
using ClrMD.Extensions.Obfuscation;
using LINQPad;
using Microsoft.Diagnostics.Runtime;

namespace ClrMD.Extensions
{
    public class ClrObject : DynamicObject, IEnumerable<ClrObject>, IComparable, ICustomMemberProvider
    {
        public const ulong NullAddress = 0;
        private const string ToStringFieldIndentation = "  ";
        private static readonly Regex s_fieldNameRegex = new Regex("^<([^>]+)>k__BackingField$", RegexOptions.Compiled);

        private readonly ITypeDeobfuscator m_deobfuscator;
        private readonly TypeVisualizer m_visualizer;

        public ulong Address { get; private set; }

        public ClrType Type { get; private set; }

        public string TypeName
        {
            get { return m_deobfuscator.OriginalName; }
        }

        public bool IsInterior { get; private set; }

        public ClrHeap Heap
        {
            get { return Type.Heap; }
        }

        public ClrObject this[string fieldName]
        {
            get
            {
                ClrInstanceField field = GetField(fieldName);

                if (field == null)
                    throw new ArgumentException(string.Format("Field '{0}' not found in Type '{1}'", fieldName, Type.Name));

                return this[field];
            }
        }

        public ClrObject this[ClrInstanceField field]
        {
            get
            {
                return GetInnerObject(field.GetAddress(Address, IsInterior), field.Type);
            }
        }

        public ClrObject this[int arrayIndex]
        {
            get
            {
                if (!Type.IsArray)
                    throw new InvalidOperationException(string.Format("Type '{0}' is not an array", Type.Name));

                int arrayLength = Type.GetArrayLength(Address);

                if (arrayIndex >= arrayLength)
                    throw new IndexOutOfRangeException(string.Format("Array index '{0}' is not between 0 and '{1}'", arrayIndex, arrayLength));

                return GetInnerObject(Type.GetArrayElementAddress(Address, arrayIndex), Type.ArrayComponentType);
            }
        }

        public IEnumerable<ClrInstanceField> Fields
        {
            get
            {
                return Type.Fields;
            }
        }

        public int ArrayLength
        {
            get
            {
                if (!Type.IsArray)
                    throw new InvalidOperationException(string.Format("Type '{0}' is not an array", Type.Name));

                return Type.GetArrayLength(Address);
            }
        }

        public bool HasSimpleValue
        {
            get { return SimpleValueHelper.IsSimpleValue(Type); }
        }

        public object SimpleValue
        {
            get { return SimpleValueHelper.GetSimpleValue(this); }
        }

        public ulong Size
        {
            get { return Type.GetSize(Address); }
        }

        public dynamic Dynamic
        {
            get { return this; }
        }

        public object Visualizer
        {
            get { return m_visualizer.GetValue(this); }
        }

        public ClrObject(ulong address, ClrType type, bool isInterior = false)
        {
            Address = address;
            Type = type;
            IsInterior = isInterior;

            if (ClrMDSession.Current == null)
                m_deobfuscator = DummyTypeDeobfuscator.GetDeobfuscator(type.Name);
            else
                m_deobfuscator = ClrMDSession.Current.GetTypeDeobfuscator(type);

            m_visualizer = TypeVisualizer.TryGetVisualizer(this);
        }

        public bool IsNull()
        {
            return Address == NullAddress || Type is UnknownType;
        }

        public ClrInstanceField GetField(string fieldName)
        {
            ClrInstanceField field = null;
            string obfuscatedName;

            if (m_deobfuscator.TryObfuscateField(fieldName, out obfuscatedName))
                field = Type.GetFieldByName(obfuscatedName);

            string backingFieldName = GetAutomaticPropertyField(fieldName);

            if (m_deobfuscator.TryObfuscateField(backingFieldName, out obfuscatedName))
                field = Type.GetFieldByName(obfuscatedName);

            if (field == null)
                field = Type.GetFieldByName(fieldName);

            if (field == null)
                field = Type.GetFieldByName(backingFieldName);

            return field;
        }

        public static string GetAutomaticPropertyField(string propertyName)
        {
            return "<" + propertyName + ">" + "k__BackingField";
        }

        public string GetFieldName(string fieldName)
        {
            string deobfuscatedName;

            if (m_deobfuscator.TryDeobfuscateField(fieldName, out deobfuscatedName))
                fieldName = deobfuscatedName;

            var match = s_fieldNameRegex.Match(fieldName);

            if (match.Success)
                return match.Groups[1].Value;

            return fieldName;
        }

        public IEnumerable<ClrObject> EnumerateReferenceBy()
        {
            IEnumerable<ClrObject> allObjects;

            if (ClrMDSession.Current != null)
            {
                if (ClrMDSession.Current.IsReferenceMappingCreated)
                    return ClrMDSession.Current.GetReferenceBy(this);
                
                allObjects = ClrMDSession.Current.AllObjects;
            }
            else
            {
                allObjects = Type.Heap.EnumerateClrObjects();
            }

            return EnumerateReferenceBy(allObjects);
        }

        public IEnumerable<ClrObject> EnumerateReferenceBy(params ClrType[] typeFilter)
        {
            return EnumerateReferenceBy(Type.Heap.EnumerateClrObjects(typeFilter));
        }

        public IEnumerable<ClrObject> EnumerateReferenceBy(IEnumerable<ClrObject> allObjects)
        {
            return from parent in allObjects
                   where parent.EnumerateReferencesAddress().Contains(Address)
                   select parent;
        }

        public IEnumerable<ClrObject> EnumerateReferences()
        {
            return EnumerateReferencesAddress().Select(address => Type.Heap.GetClrObject(address));
        }

        public IEnumerable<ulong> EnumerateReferencesAddress()
        {
            List<ulong> references = new List<ulong>();

            Type.EnumerateRefsOfObject(Address, (objRef, fieldOffset) => references.Add(objRef));
            return references;
        }

        private ClrObject GetInnerObject(ulong pointer, ClrType type)
        {
            ulong fieldAddress;
            ClrType actualType = type;

            if (type.IsObjectReference)
            {
                Type.Heap.ReadPointer(pointer, out fieldAddress);

                if (!type.IsSealed && fieldAddress != NullAddress)
                    actualType = type.Heap.GetSafeObjectType(fieldAddress);
            }
            else if (type.IsPrimitive)
            {
                // Unfortunately, ClrType.GetValue for primitives assumes that the value is boxed,
                // we decrement PointerSize because it will be added when calling ClrType.GetValue.
                // ClrMD should be updated in a future version to include ClrType.GetValue(int interior).
                fieldAddress = pointer - (ulong)type.Heap.PointerSize;
            }
            else if (type.IsValueClass)
            {
                fieldAddress = pointer;
            }
            else
            {
                throw new NotSupportedException(string.Format("Object type not supported '{0}'", type.Name));
            }

            return new ClrObject(fieldAddress, actualType, !type.IsObjectReference);
        }

        #region Operators

        public override bool Equals(object other)
        {
            if (HasSimpleValue)
            {
                if (other is string && Type.IsEnum)
                    return Equals(other, Type.GetEnumName(SimpleValue));

                return Equals(other, SimpleValue);
            }

            ClrObject clrOther = other as ClrObject;

            if (clrOther != null)
                return Address == clrOther.Address;

            return false;
        }

        public override int GetHashCode()
        {
            if (HasSimpleValue)
                return SimpleValue.GetHashCode();

            return Address.GetHashCode();
        }

        public int CompareTo(object other)
        {
            if (HasSimpleValue)
                return Comparer.DefaultInvariant.Compare(other, SimpleValue);

            return Comparer.DefaultInvariant.Compare(other, Address);
        }

        public static bool operator ==(ClrObject left, object right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(ClrObject left, object right)
        {
            return !Equals(left, right);
        }

        public static bool operator <(ClrObject left, object right)
        {
            return Comparer.DefaultInvariant.Compare(left, right) > 0;
        }

        public static bool operator >(ClrObject left, object right)
        {
            return Comparer.DefaultInvariant.Compare(left, right) < 0;
        }

        public static bool operator <=(ClrObject left, object right)
        {
            return Comparer.DefaultInvariant.Compare(left, right) >= 0;
        }

        public static bool operator >=(ClrObject left, object right)
        {
            return Comparer.DefaultInvariant.Compare(left, right) <= 0;
        }

        public static bool operator true(ClrObject obj)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");

            if (obj.HasSimpleValue)
                return (bool)obj.SimpleValue;

            throw new InvalidCastException(string.Format("Cannot cast type '{0}' to bool.", obj.Type));
        }

        public static bool operator false(ClrObject obj)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");

            if (obj.HasSimpleValue)
                return !(bool)obj.SimpleValue;

            throw new InvalidCastException(string.Format("Cannot cast type '{0}' to bool.", obj.Type));
        }

        public static bool operator !(ClrObject obj)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");

            if (obj.HasSimpleValue)
                return !(bool)obj.SimpleValue;

            throw new InvalidCastException(string.Format("Cannot cast type '{0}' to bool.", obj.Type));
        }

        public static explicit operator bool(ClrObject obj)
        {
            return (bool)obj.SimpleValue;
        }

        public static explicit operator char(ClrObject obj)
        {
            return (char)obj.SimpleValue;
        }

        public static explicit operator sbyte(ClrObject obj)
        {
            return (sbyte)obj.SimpleValue;
        }

        public static explicit operator byte(ClrObject obj)
        {
            return (byte)obj.SimpleValue;
        }

        public static explicit operator short(ClrObject obj)
        {
            return (short)obj.SimpleValue;
        }

        public static explicit operator ushort(ClrObject obj)
        {
            return (ushort)obj.SimpleValue;
        }

        public static explicit operator int(ClrObject obj)
        {
            return (int)obj.SimpleValue;
        }

        public static explicit operator uint(ClrObject obj)
        {
            return (uint)obj.SimpleValue;
        }

        public static explicit operator long(ClrObject obj)
        {
            return (long)obj.SimpleValue;
        }

        public static explicit operator ulong(ClrObject obj)
        {
            return (ulong)obj.SimpleValue;
        }

        public static explicit operator float(ClrObject obj)
        {
            return (float)obj.SimpleValue;
        }

        public static explicit operator double(ClrObject obj)
        {
            return (double)obj.SimpleValue;
        }

        public static explicit operator string(ClrObject obj)
        {
            if (obj.Type.IsEnum)
                return obj.Type.GetEnumName(obj.SimpleValue);

            return (string)obj.SimpleValue;
        }

        public static explicit operator Guid(ClrObject obj)
        {
            return (Guid)obj.SimpleValue;
        }

        public static explicit operator TimeSpan(ClrObject obj)
        {
            return (TimeSpan)obj.SimpleValue;
        }

        public static explicit operator DateTime(ClrObject obj)
        {
            return (DateTime)obj.SimpleValue;
        }

        public static explicit operator IPAddress(ClrObject obj)
        {
            return (IPAddress)obj.SimpleValue;
        }

        #endregion

        #region IEnumerable

        public IEnumerator<ClrObject> GetEnumerator()
        {
            if (Type.IsArray)
            {
                for (int i = 0; i < ArrayLength; i++)
                    yield return this[i];
            }
            else
            {
                foreach (ClrInstanceField field in Type.Fields)
                    yield return this[field];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Dynamic

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            result = this[binder.Name];
            return true;
        }

        #endregion

        #region ToString

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            ToString(builder);

            return builder.ToString();
        }

        public string ToDetailedString(bool includeInteriorFields = true)
        {
            StringBuilder builder = new StringBuilder();
            ToDetailedString(builder, ToStringFieldIndentation, includeInteriorFields);
            return builder.ToString();
        }

        private void ToString(StringBuilder builder)
        {
            if (HasSimpleValue)
            {
                builder.Append(SimpleValueHelper.GetSimpleValueString(this));
                return;
            }

            builder.Append('{');
            builder.Append(m_deobfuscator.OriginalName);

            if (!IsInterior)
            {
                builder.Append(" (");
                builder.Append(GetAddressString());
                builder.Append(")");
            }

            builder.Append("}");
        }

        private string GetAddressString()
        {
            if (Address == NullAddress)
                return "null";
            
            return "0x" + Address.ToString("X");
        }

        private void ToDetailedString(StringBuilder builder, string indentation, bool includeInteriorFields)
        {
            ToString(builder);

            if (HasSimpleValue)
                return;

            if (Type.IsArray)
            {
                for (int i = 0; i < ArrayLength; i++)
                {
                    builder.AppendLine();
                    builder.Append(indentation);
                    builder.Append("[");
                    builder.Append(i);
                    builder.Append("]: ");

                    if (includeInteriorFields)
                        this[i].ToDetailedString(builder, indentation + ToStringFieldIndentation + ToStringFieldIndentation, true);
                    else
                        this[i].ToString(builder);
                }
            }
            else
            {
                foreach (ClrInstanceField field in Fields)
                {
                    ClrObject fieldValue = this[field];

                    builder.AppendLine();
                    builder.Append(indentation);
                    builder.Append(GetFieldName(field.Name));
                    builder.Append(": ");

                    if (fieldValue.HasSimpleValue || field.Type.IsObjectReference || !includeInteriorFields)
                    {
                        fieldValue.ToString(builder);
                    }
                    else
                    {
                        fieldValue.ToDetailedString(builder, indentation + ToStringFieldIndentation, true);
                    }
                }
            }
        }

        #endregion

        #region SimpleValueHelper

        private static class SimpleValueHelper
        {
            private const string GuidTypeName = "System.Guid";
            private const string TimeSpanTypeName = "System.TimeSpan";
            private const string DateTimeTypeName = "System.DateTime";
            private const string IPAddressTypeName = "System.Net.IPAddress";

            public static bool IsSimpleValue(ClrType type)
            {
                if (type.IsPrimitive ||
                    type.IsString)
                    return true;

                switch (type.Name)
                {
                    case GuidTypeName:
                    case TimeSpanTypeName:
                    case DateTimeTypeName:
                    case IPAddressTypeName:
                        return true;
                }

                return false;
            }

            public static object GetSimpleValue(ClrObject obj)
            {
                if (obj.IsNull())
                    return null;

                ClrType type = obj.Type;
                ClrHeap heap = type.Heap;

                if (type.IsPrimitive || type.IsString)
                    return type.GetValue(obj.Address);

                ulong address = obj.IsInterior ? obj.Address : obj.Address + (ulong)heap.PointerSize;

                switch (type.Name)
                {
                    case GuidTypeName:
                        {
                            byte[] buffer = ReadBuffer(heap, address, 16);
                            return new Guid(buffer);
                        }

                    case TimeSpanTypeName:
                        {
                            byte[] buffer = ReadBuffer(heap, address, 8);
                            long ticks = BitConverter.ToInt64(buffer, 0);
                            return new TimeSpan(ticks);
                        }

                    case DateTimeTypeName:
                        {
                            byte[] buffer = ReadBuffer(heap, address, 8);
                            ulong dateData = BitConverter.ToUInt64(buffer, 0);
                            return GetDateTime(dateData);
                        }

                    case IPAddressTypeName:
                        {
                            return GetIPAddress(obj);
                        }
                }

                throw new InvalidOperationException(string.Format("SimpleValue not available for type '{0}'", type.Name));
            }

            public static string GetSimpleValueString(ClrObject obj)
            {
                object value = obj.SimpleValue;

                if (value == null)
                    return "null";

                ClrType type = obj.Type;
                if (type != null && type.IsEnum)
                    return type.GetEnumName(value) ?? value.ToString();

                DateTime? dateTime = value as DateTime?;
                if (dateTime != null)
                    return GetDateTimeString(dateTime.Value);

                return value.ToString();
            }

            private static byte[] ReadBuffer(ClrHeap heap, ulong address, int length)
            {
                byte[] buffer = new byte[length];
                int byteRead = heap.ReadMemory(address, buffer, 0, buffer.Length);

                if (byteRead != length)
                    throw new InvalidOperationException(string.Format("Expected to read {0} bytes and actually read {1}", length, byteRead));

                return buffer;
            }

            private static DateTime GetDateTime(ulong dateData)
            {
                const ulong DateTimeTicksMask = 0x3FFFFFFFFFFFFFFF;
                const ulong DateTimeKindMask = 0xC000000000000000;
                const ulong KindUnspecified = 0x0000000000000000;
                const ulong KindUtc = 0x4000000000000000;

                long ticks = (long)(dateData & DateTimeTicksMask);
                ulong internalKind = dateData & DateTimeKindMask;

                switch (internalKind)
                {
                    case KindUnspecified:
                        return new DateTime(ticks, DateTimeKind.Unspecified);

                    case KindUtc:
                        return new DateTime(ticks, DateTimeKind.Utc);

                    default:
                        return new DateTime(ticks, DateTimeKind.Local);
                }
            }

            private static IPAddress GetIPAddress(ClrObject ipAddress)
            {
                const int AddressFamilyInterNetworkV6 = 23;
                const int IPv4AddressBytes = 4;
                const int IPv6AddressBytes = 16;
                const int NumberOfLabels = IPv6AddressBytes / 2;

                byte[] bytes;
                int family = (int)ipAddress["m_Family"].SimpleValue;

                if (family == AddressFamilyInterNetworkV6)
                {
                    bytes = new byte[IPv6AddressBytes];
                    int j = 0;

                    var numbers = ipAddress["m_Numbers"];

                    for (int i = 0; i < NumberOfLabels; i++)
                    {
                        ushort number = (ushort)numbers[i].SimpleValue;
                        bytes[j++] = (byte)((number >> 8) & 0xFF);
                        bytes[j++] = (byte)(number & 0xFF);
                    }
                }
                else
                {
                    long address = (long)ipAddress["m_Address"].SimpleValue;
                    bytes = new byte[IPv4AddressBytes];
                    bytes[0] = (byte)(address);
                    bytes[1] = (byte)(address >> 8);
                    bytes[2] = (byte)(address >> 16);
                    bytes[3] = (byte)(address >> 24);
                }

                return new IPAddress(bytes);
            }

            private static string GetDateTimeString(DateTime dateTime)
            {
                return dateTime.ToString(@"yyyy-MM-dd HH\:mm\:ss.FFFFFFF") + GetDateTimeKindString(dateTime.Kind);
            }

            private static string GetDateTimeKindString(DateTimeKind kind)
            {
                switch (kind)
                {
                    case DateTimeKind.Unspecified:
                        return " (Unspecified)";
                    case DateTimeKind.Utc:
                        return " (Utc)";
                    default:
                        return " (Local)";
                }
            }
        }

        #endregion

        #region LINQPad

        public IEnumerable<string> GetNames()
        {
            if (HasSimpleValue)
            {
                yield return "";
            }
            else if (Type.IsArray)
            {
                yield return "Type";
                yield return "Address";
                yield return "Length";
                yield return "Items";
            }
            else
            {
                yield return "Type";
                yield return "Address";

                if (!IsNull())
                {
                    if (m_visualizer != null)
                        yield return "Visualizer";

                    foreach (var field in Fields)
                        yield return GetFieldName(field.Name);
                }
            }

            if (LinqPadExtensions.DisplayReferencedByField && Type.IsObjectReference)
            {
                yield return "ReferencedBy";
            }
        }

        public IEnumerable<Type> GetTypes()
        {
            if (HasSimpleValue)
            {
                yield return GetSimpleValueType();
            }
            else if (Type.IsArray)
            {
                // Type Name
                yield return typeof(string);

                // Address
                yield return typeof(string);

                // Length
                yield return typeof(int);

                // Items
                yield return typeof(IEnumerable<ClrObject>);
            }
            else
            {
                // Type Name
                yield return typeof(string);

                // Address
                yield return typeof(string);

                if (!IsNull())
                {
                    if (m_visualizer != null)
                        yield return typeof(object);

                    for (int i = 0; i < Type.Fields.Count; ++i)
                    {
                        if (LinqPadExtensions.SmartNavigation)
                            yield return HasSimpleValue ? GetSimpleValueType() : typeof (ClrObject);
                        else
                            yield return typeof (string);
                    }
                }
            }

            if (LinqPadExtensions.DisplayReferencedByField && Type.IsObjectReference)
            {
                yield return typeof(IEnumerable<ClrObjectWrapper>);
            }
        }

        public IEnumerable<object> GetValues()
        {
            if (!IsNull() && HasSimpleValue)
            {
                yield return SimpleValue;
            }
            else if (!IsNull() && Type.IsArray)
            {
                yield return m_deobfuscator.OriginalName;
                yield return GetAddressString();
                yield return ArrayLength;

                yield return EnumerateArray();
            }
            else
            {
                yield return m_deobfuscator.OriginalName;
                yield return GetAddressString();

                if (!IsNull())
                {
                    if (m_visualizer != null)
                        yield return m_visualizer.GetValue(this);

                    foreach (ClrInstanceField field in Type.Fields)
                    {
                        if (LinqPadExtensions.SmartNavigation)
                            yield return (!this[field].IsNull() && this[field].HasSimpleValue) ? this[field].SimpleValue : this[field];
                        else
                            yield return this[field].ToString();
                    }
                }
            }

            if (LinqPadExtensions.DisplayReferencedByField && Type.IsObjectReference)
            {
                yield return EnumerateReferenceBy().Select(item => new { Type = item.m_deobfuscator.OriginalName, Object = item });
            }
        }

        private IEnumerable<ClrObject> EnumerateArray()
        {
            foreach (ClrObject clrObject in this)
                yield return clrObject;
        }

        private Type GetSimpleValueType()
        {
            return SimpleValue == null ? typeof(object) : SimpleValue.GetType();
        }

        #endregion
    }
}