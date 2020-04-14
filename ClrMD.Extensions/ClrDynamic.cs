﻿using System;
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
    public class ClrDynamic : DynamicObject, IEnumerable<ClrDynamic>, IComparable, ICustomMemberProvider
    {
        public const ulong NullAddress = 0;
        private const string ToStringFieldIndentation = "  ";
        private static readonly Regex s_fieldNameRegex = new Regex("^<([^>]+)>k__BackingField$", RegexOptions.Compiled);

        private readonly ITypeDeobfuscator m_deobfuscator;

        public ulong Address { get; }

        public ClrType Type { get; }
        
        public bool IsInterior { get; }

        public string TypeName => m_deobfuscator.OriginalName;

        public ClrHeap Heap => Type.Heap;

        public ClrDynamic this[string fieldName]
        {
            get
            {
                ClrInstanceField field = GetField(fieldName);

                if (field == null)
                    return new ClrDynamic(0, ClrMDSession.Current.ErrorType);

                return this[field];
            }
        }

        public ClrDynamic this[ClrInstanceField field]
        {
            get
            {
                return GetInnerObject(field.GetAddress(Address, IsInterior), field.Type);
            }
        }

        public ClrDynamic this[int arrayIndex]
        {
            get
            {
                if (!Type.IsArray)
                    throw new InvalidOperationException(string.Format("Type '{0}' is not an array", Type.Name));

                int arrayLength = Type.GetArrayLength(Address);

                if (arrayIndex >= arrayLength)
                    throw new IndexOutOfRangeException(string.Format("Array index '{0}' is not between 0 and '{1}'", arrayIndex, arrayLength));

                return GetInnerObject(Type.GetArrayElementAddress(Address, arrayIndex), Type.ComponentType);
            }
        }

        public IEnumerable<ClrInstanceField> Fields => Type.Fields;

        public int ArrayLength
        {
            get
            {
                if (!Type.IsArray)
                    throw new InvalidOperationException(string.Format("Type '{0}' is not an array", Type.Name));

                return Type.GetArrayLength(Address);
            }
        }

        public bool HasSimpleValue => IsNull() || SimpleValueHelper.IsSimpleValue(Type);

        public object SimpleValue => SimpleValueHelper.GetSimpleValue(this);

        public object SimpleDisplayValue
        {
            get
            {
                if (Type.IsEnum)
                    return Type.GetEnumName(SimpleValue) ?? SimpleValue.ToString();
                return SimpleValue ?? "{null}";
            }
        }

        public ulong Size => Type.GetSize(Address);

        public dynamic Dynamic => this;

        public object Visualizer => TypeVisualizer.TryGetVisualizer(this)?.GetValue(this);

        public ClrDynamic(ClrObject obj)
            : this(obj.Address, obj.Type, false)
        { }

        public ClrDynamic(ulong address, ClrType type, bool isInterior = false)
        {
            Address = address;
            Type = type;
            IsInterior = isInterior;

            if (ClrMDSession.Current == null)
                m_deobfuscator = DummyTypeDeobfuscator.GetDeobfuscator(type.Name);
            else
                m_deobfuscator = ClrMDSession.Current.GetTypeDeobfuscator(type);
        }

        public bool IsNull()
        {
            return Address == NullAddress || Type == ClrMDSession.Current.ErrorType;
        }

        public bool IsUndefined()
        {
            return Type == ClrMDSession.Current.ErrorType;
        }

        private static ClrInstanceField FindField(ObfuscatedField oField)
        {
            TypeName delcaringType = ClrMDSession.Current.ObfuscateType(oField.DeclaringType);
            var target = ClrMDSession.Current.Heap.GetTypeByName(delcaringType);
            return target.GetFieldByName(oField.ObfuscatedName);
        }

        public ClrInstanceField GetField(string fieldName)
        {
            ClrInstanceField field = null;
            ObfuscatedField obfuscatedField;

            if (m_deobfuscator.TryObfuscateField(fieldName, out obfuscatedField))
                field = FindField(obfuscatedField);

            string backingFieldName = GetAutomaticPropertyField(fieldName);

            if (m_deobfuscator.TryObfuscateField(backingFieldName, out obfuscatedField))
                field = FindField(obfuscatedField);

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

            var obf = m_deobfuscator;
            var target = Type;
            while (obf != null)
            {
                if (obf.TryDeobfuscateField(fieldName, out deobfuscatedName))
                {
                    fieldName = deobfuscatedName;
                    break;
                }

                if (target.BaseType != null)
                {
                    target = target.BaseType;
                    obf = ClrMDSession.Current.GetTypeDeobfuscator(target);
                }
                else
                {
                    break;
                }
            }

            var match = s_fieldNameRegex.Match(fieldName);

            if (match.Success)
                return match.Groups[1].Value;

            return fieldName;
        }

        private IEnumerable<ClrDynamic> LazyEnumerateReferenceBy()
        {
            foreach (var o in EnumerateReferenceBy())
                yield return o;
        }

        public IEnumerable<ClrDynamic> EnumerateReferenceBy()
        {
            IEnumerable<ClrDynamic> allObjects;

            if (ClrMDSession.Current != null)
            {
                if (ClrMDSession.Current.IsReferenceMappingCreated)
                    return ClrMDSession.Current.GetReferenceBy(this);

                allObjects = ClrMDSession.Current.AllObjects;
            }
            else
            {
                allObjects = Type.Heap.EnumerateDynamicObjects();
            }

            return EnumerateReferenceBy(allObjects);
        }

        public IEnumerable<ClrDynamic> EnumerateReferenceBy(params ClrType[] typeFilter)
        {
            return EnumerateReferenceBy(Type.Heap.EnumerateDynamicObjects(typeFilter));
        }

        public IEnumerable<ClrDynamic> EnumerateReferenceBy(IEnumerable<ClrDynamic> allObjects)
        {
            return from parent in allObjects
                   where parent.EnumerateReferencesAddress().Contains(Address)
                   select parent;
        }

        public IEnumerable<ClrDynamic> EnumerateReferences()
        {
            return EnumerateReferencesAddress().Select(address => Type.Heap.GetDynamicObject(address));
        }

        public IEnumerable<ulong> EnumerateReferencesAddress()
        {
            List<ulong> references = new List<ulong>();

            Type.EnumerateRefsOfObject(Address, (objRef, fieldOffset) => references.Add(objRef));
            return references;
        }

        public IEnumerable<ClrDynamic> EnumerateDictionaryValues()
        {
            if (!TypeName.StartsWith("System.Collections.Generic.Dictionary<"))
                yield break;

            // https://referencesource.microsoft.com/#mscorlib/system/collections/generic/dictionary.cs,d864a2277aad4ece
            var entries = this["entries"];
            var count = (int)this["count"];
            int index = 0;

            // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
            // dictionary.count+1 could be negative if dictionary.count is Int32.MaxValue
            while ((uint)index < (uint)count)
            {
                if ((int)entries[index]["hashCode"] >= 0)
                {
                    yield return entries[index]["value"];
                    index++;
                }
            }
        }

        public T[] ToArray<T>()
        {
            return (T[]) ToArray(typeof(T));
        }

        public Array ToArray(Type elemType)
        {
            int itemCount;
            IEnumerable<ClrDynamic> items;

            if (Type.IsArray)
            {
                itemCount = ArrayLength;
                items = this;
            }
            else
            {
                var visual = Visualizer;
                if (!(visual is ISingleCellEnumerableVisual simpleVisual))
                    throw new InvalidOperationException("This is only valid on simple enumerable types");

                itemCount = simpleVisual.Count;
                items = simpleVisual.Items;
            }


            var array = Array.CreateInstance(elemType, itemCount);
            int i = 0;
            foreach (var val in items)
            {
                array.SetValue(val.ConvertContent(elemType), i++);
            }

            return array;
        }

        private ClrDynamic GetInnerObject(ulong pointer, ClrType type)
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

            return new ClrDynamic(fieldAddress, actualType, !type.IsObjectReference);
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

            ClrDynamic clrOther = other as ClrDynamic;

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

        public static bool operator ==(ClrDynamic left, object right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(ClrDynamic left, object right)
        {
            return !Equals(left, right);
        }

        public static bool operator <(ClrDynamic left, object right)
        {
            return Comparer.DefaultInvariant.Compare(left, right) > 0;
        }

        public static bool operator >(ClrDynamic left, object right)
        {
            return Comparer.DefaultInvariant.Compare(left, right) < 0;
        }

        public static bool operator <=(ClrDynamic left, object right)
        {
            return Comparer.DefaultInvariant.Compare(left, right) >= 0;
        }

        public static bool operator >=(ClrDynamic left, object right)
        {
            return Comparer.DefaultInvariant.Compare(left, right) <= 0;
        }

        public static bool operator true(ClrDynamic obj)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");

            if (obj.HasSimpleValue)
                return (bool)obj.SimpleValue;

            throw new InvalidCastException(string.Format("Cannot cast type '{0}' to bool.", obj.Type));
        }

        public static bool operator false(ClrDynamic obj)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");

            if (obj.HasSimpleValue)
                return !(bool)obj.SimpleValue;

            throw new InvalidCastException(string.Format("Cannot cast type '{0}' to bool.", obj.Type));
        }

        public static bool operator !(ClrDynamic obj)
        {
            if (obj == null)
                throw new ArgumentNullException("obj");

            if (obj.HasSimpleValue)
                return !(bool)obj.SimpleValue;

            throw new InvalidCastException(string.Format("Cannot cast type '{0}' to bool.", obj.Type));
        }

        public static explicit operator bool(ClrDynamic obj)
        {
            return (bool)obj.SimpleValue;
        }

        public static explicit operator char(ClrDynamic obj)
        {
            return (char)obj.SimpleValue;
        }

        public static explicit operator sbyte(ClrDynamic obj)
        {
            return (sbyte)obj.SimpleValue;
        }

        public static explicit operator byte(ClrDynamic obj)
        {
            return (byte)obj.SimpleValue;
        }

        public static explicit operator short(ClrDynamic obj)
        {
            return (short)obj.SimpleValue;
        }

        public static explicit operator ushort(ClrDynamic obj)
        {
            return (ushort)obj.SimpleValue;
        }

        public static explicit operator int(ClrDynamic obj)
        {
            return (int)obj.SimpleValue;
        }

        public static explicit operator uint(ClrDynamic obj)
        {
            return (uint)obj.SimpleValue;
        }

        public static explicit operator long(ClrDynamic obj)
        {
            return (long)obj.SimpleValue;
        }

        public static explicit operator ulong(ClrDynamic obj)
        {
            return (ulong)obj.SimpleValue;
        }

        public static explicit operator float(ClrDynamic obj)
        {
            return (float)obj.SimpleValue;
        }

        public static explicit operator double(ClrDynamic obj)
        {
            return (double)obj.SimpleValue;
        }

        public static explicit operator string(ClrDynamic obj)
        {
            if (obj.Type.IsEnum)
                return obj.Type.GetEnumName(obj.SimpleValue);

            return (string)obj.SimpleValue;
        }

        public static explicit operator Guid(ClrDynamic obj)
        {
            return (Guid)obj.SimpleValue;
        }

        public static explicit operator TimeSpan(ClrDynamic obj)
        {
            return (TimeSpan)obj.SimpleValue;
        }

        public static explicit operator DateTime(ClrDynamic obj)
        {
            return (DateTime)obj.SimpleValue;
        }

        public static explicit operator IPAddress(ClrDynamic obj)
        {
            return (IPAddress)obj.SimpleValue;
        }

        public static explicit operator ClrDynamic(ClrObject obj)
        {
            return new ClrDynamic(obj);
        }

        #endregion

        #region IEnumerable

        public IEnumerator<ClrDynamic> GetEnumerator()
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

        public override bool TryConvert(ConvertBinder binder, out object result)
        {
            result = ConvertContent(binder.ReturnType);
            return true;
        }

        public object ConvertContent(Type targetType)
        {
            if (!HasSimpleValue)
            {
                if (targetType.IsArray)
                    return ToArray(targetType.GetElementType());

                throw new InvalidOperationException($"{targetType.FullName} cannot be converted from {Type.Name}");
            }

            return ConvertValue(SimpleValue, targetType);
        }

        private static object ConvertValue(object simpleValue, Type targetType)
        {
            if (!targetType.IsEnum)
                return Convert.ChangeType(simpleValue, targetType);

            simpleValue = Convert.ChangeType(simpleValue, Enum.GetUnderlyingType(targetType));
            return Enum.ToObject(targetType, simpleValue);
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
            if (IsUndefined())
            {
                builder.Append("#undefined");
                return;
            }

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
                return "{null}";
            
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
                    ClrDynamic fieldValue = this[field];

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

        #region LINQPad

        public IEnumerable<string> GetNames()
        {
            if (IsNull() || HasSimpleValue || IsUndefined())
            {
                yield return "";
                yield break;
            }
            
            if (Type.IsArray)
            {
                yield return "[Type]";
                yield return "[Address]";
                yield return "[Length]";
                yield return "[Items]";
            }
            else
            {
                yield return "[Type]";
                yield return "[Address]";

                if (Visualizer != null)
                    yield return "[Visualizer]";

                foreach (var field in Fields)
                    yield return GetFieldName(field.Name);
            }

            if (LinqPadExtensions.DisplayReferencedByField && Type.IsObjectReference)
            {
                yield return "ReferencedBy";
            }
        }

        public IEnumerable<Type> GetTypes()
        {
            if (IsUndefined())
            {
                yield return typeof(string);
                yield break;
            }

            if (IsNull())
            {
                yield return typeof(object);
                yield break;
            }

            if (HasSimpleValue)
            {
                 yield return GetSimpleValueType();
                yield break;
            }
            
            if (Type.IsArray)
            {
                // Type Name
                yield return typeof(string);

                // Address
                yield return typeof(string);

                // Length
                yield return typeof(int);

                // Items
                yield return typeof(IEnumerable<ClrDynamic>);
            }
            else
            {
                // Type Name
                yield return typeof(string);

                // Address
                yield return typeof(string);

                if (Visualizer != null)
                    yield return typeof(object);

                foreach (ClrInstanceField field in Type.Fields)
                {
                    if (LinqPadExtensions.SmartNavigation)
                    {
                        ClrDynamic fieldValue = this[field];
                        yield return fieldValue.HasSimpleValue ? fieldValue.GetSimpleValueType() : typeof(ClrDynamic);
                    }
                    else
                    {
                        yield return typeof(string);
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
            if (IsUndefined())
            {
                yield return ToString();
                yield break;
            }

            if (IsNull())
            {
                yield return null;
                yield break;
            }

            if (HasSimpleValue)
            {
                yield return SimpleDisplayValue;
                yield break;
            }
            
            if (Type.IsArray)
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

                var visual = Visualizer;
                if (visual != null)
                    yield return visual;

                foreach (ClrInstanceField field in Type.Fields)
                {
                    var fieldValue = this[field];

                    if (fieldValue.IsNull())
                        yield return null;
                    else if (LinqPadExtensions.SmartNavigation)
                        yield return this[field].HasSimpleValue ? this[field].SimpleDisplayValue : this[field];
                    else
                        yield return this[field].ToString();
                }
            }

            if (LinqPadExtensions.DisplayReferencedByField && Type.IsObjectReference)
            {
                yield return LazyEnumerateReferenceBy().Select(item => new { Type = item.m_deobfuscator.OriginalName, Object = item });
            }
        }

        private IEnumerable<ClrDynamic> EnumerateArray()
        {
            foreach (ClrDynamic clrObject in this)
                yield return clrObject;
        }

        private Type GetSimpleValueType()
        {
            if (SimpleValue == null)
                return typeof (object);
            if (Type.IsEnum)
                return typeof (string);
            return SimpleValue.GetType();
        }

        #endregion
    }
}