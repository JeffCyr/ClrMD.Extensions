using System.Collections.Generic;

namespace ClrMD.Extensions.Obfuscation
{
    public struct TypeName
    {
        private readonly TypeName[] m_genericArgs;

        public string Name { get; }

        public string ArrayDefinition{ get; }

        public IReadOnlyList<TypeName> GenericArgs => m_genericArgs;

        public TypeName(string name) : this()
        {
            Name = name;
            ArrayDefinition = null;
        }

        public TypeName(string name, params TypeName[] genericArgs)
        {
            Name = name;
            ArrayDefinition = null;
            m_genericArgs = genericArgs;
        }

        public TypeName(string name, string array) : this()
        {
            Name = name;
            ArrayDefinition = array;
        }

        public TypeName(string name, string array, params TypeName[] genericArgs)
        {
            Name = name;
            ArrayDefinition = array;
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

            if (ArrayDefinition != null)
            {
                s += ArrayDefinition;
            }
            return s;
        }

        public static implicit operator string(TypeName typeName)
        {
            return typeName.ToString();
        }
    }
}