using System.Collections.Generic;

namespace ClrMD.Extensions.Obfuscation
{
    public struct TypeName
    {
        private readonly TypeName[] m_genericArgs;

        public string Name { get; }

        public IReadOnlyList<TypeName> GenericArgs => m_genericArgs;

        public TypeName(string name) : this()
        {
            Name = name;
        }

        public TypeName(string name, params TypeName[] genericArgs)
        {
            Name = name;
            m_genericArgs = genericArgs;
        }

        public override string ToString()
        {
            if (GenericArgs == null || GenericArgs.Count == 0)
            {
                return Name;
            }

            return $"{Name}<{string.Join(",", GenericArgs)}>";
        }

        public static implicit operator string(TypeName typeName)
        {
            return typeName.ToString();
        }
    }
}