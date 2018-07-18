using System.Collections.Generic;

namespace ClrMD.Extensions.Obfuscation
{
    public struct TypeName
    {
        private readonly TypeName[] m_genericArgs;

        public string Name { get; }

        public bool IsArray { get; }

        public IReadOnlyList<TypeName> GenericArgs => m_genericArgs;

        public TypeName(string name) : this()
        {
            Name = name;
            IsArray = false;
        }

        public TypeName(string name, params TypeName[] genericArgs)
        {
            Name = name;
            IsArray = false;
            m_genericArgs = genericArgs;
        }

        public TypeName(string name, bool array) : this()
        {
            Name = name;
            IsArray = array;
        }

        public TypeName(string name, bool array, params TypeName[] genericArgs)
        {
            Name = name;
            IsArray = array;
            m_genericArgs = genericArgs;
        }

        public override string ToString()
        {
            string s = "";
            if (GenericArgs == null || GenericArgs.Count == 0)
            {
                s = Name;
            }
            else
            {
                s = $"{Name}<{string.Join(",", GenericArgs)}>";
            }

            if (IsArray)
            {
                s += "[]";
            }
            return s;
        }

        public static implicit operator string(TypeName typeName)
        {
            return typeName.ToString();
        }
    }
}