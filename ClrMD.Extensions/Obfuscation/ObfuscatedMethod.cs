using System.Collections.Generic;

namespace ClrMD.Extensions.Obfuscation
{
    public struct ObfuscatedMethod
    {
        public readonly string ObfuscatedName;
        public readonly string OriginalName;
        public readonly TypeName ReturnType;
        private readonly TypeName[] m_argumentTypes;
        public IReadOnlyList<TypeName> ArgumentTypes => m_argumentTypes;

        internal string SortKey => $"{ObfuscatedName}({string.Join(",", m_argumentTypes)})";

        public override string ToString()
        {
            return $"{ObfuscatedName} <==> {OriginalName}     ({string.Join(",", m_argumentTypes)}) : {(string)ReturnType ?? "void"}";
        }

        public ObfuscatedMethod(string obfuscatedName, string originalName, TypeName returnType, TypeName[] argumentTypes)
        {
            ObfuscatedName = obfuscatedName;
            OriginalName = originalName;
            ReturnType = returnType;
            m_argumentTypes = argumentTypes;
        }
    }
}