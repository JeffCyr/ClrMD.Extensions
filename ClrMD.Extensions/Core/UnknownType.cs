using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;

namespace ClrMD.Extensions.Core
{
    // Null object pattern to prevent NullReferenceException when ClrHeap returns a null type.
    public sealed class UnknownType : ClrType
    {
        private ClrHeap m_heap;

        public override int Index
        {
            get { return -1; }
        }

        public override uint MetadataToken
        {
            get { throw new NotSupportedException(); }
        }

        public override string Name
        {
            get { return "Unknown Type"; }
        }

        public override ClrHeap Heap
        {
            get { return m_heap; }
        }

        public override IList<ClrInterface> Interfaces
        {
            get { return new ClrInterface[0]; }
        }

        public override bool IsFinalizable
        {
            get { return false; }
        }

        public override bool IsPublic
        {
            get { return true; }
        }

        public override bool IsPrivate
        {
            get { return false; }
        }

        public override bool IsInternal
        {
            get { return false; }
        }

        public override bool IsProtected
        {
            get { return false; }
        }

        public override bool IsAbstract
        {
            get { return false; }
        }

        public override bool IsSealed
        {
            get { return true; }
        }

        public override bool IsInterface
        {
            get { return false; }
        }

        public override ClrType BaseType
        {
            get { return null; }
        }

        public override int ElementSize
        {
            get { throw new NotSupportedException(); }
        }

        public override int BaseSize
        {
            get { return 0; }
        }

        public override bool ContainsPointers
        {
            get { return false; }
        }

        public override IList<ClrInstanceField> Fields
        {
            get { return new ClrInstanceField[0]; }
        }

        public override IList<ClrStaticField> StaticFields
        {
            get { return new ClrStaticField[0]; }
        }

        public override IList<ClrThreadStaticField> ThreadStaticFields
        {
            get { return new ClrThreadStaticField[0]; }
        }

        internal UnknownType(ClrHeap heap)
        {
            m_heap = heap;
        }

        public override ulong GetSize(ulong objRef)
        {
            return 0;
        }

        public override void EnumerateRefsOfObject(ulong objRef, Action<ulong, int> action)
        {

        }

        public override void EnumerateRefsOfObjectCarefully(ulong objRef, Action<ulong, int> action)
        {

        }

        public override bool GetFieldForOffset(int fieldOffset, bool inner, out ClrInstanceField childField, out int childFieldOffset)
        {
            childField = null;
            childFieldOffset = 0;
            return false;
        }

        public override ClrInstanceField GetFieldByName(string name)
        {
            return null;
        }

        public override ClrStaticField GetStaticFieldByName(string name)
        {
            return null;
        }

        public override int GetArrayLength(ulong objRef)
        {
            throw new NotSupportedException();
        }

        public override ulong GetArrayElementAddress(ulong objRef, int index)
        {
            throw new NotSupportedException();
        }

        public override object GetArrayElementValue(ulong objRef, int index)
        {
            throw new NotSupportedException();
        }

        public override bool Equals(object obj)
        {
            return obj is UnknownType;
        }

        public override int GetHashCode()
        {
            return 0;
        }
    }
}