using System.Collections.Generic;

namespace ClrMD.Extensions.Obfuscation
{
    internal class DummyTypeDeobfuscator : ITypeDeobfuscator
    {
        private static readonly Dictionary<string, ITypeDeobfuscator> s_cache = new Dictionary<string, ITypeDeobfuscator>();

        public static ITypeDeobfuscator GetDeobfuscator(string typeName)
        {
            ITypeDeobfuscator value;
            if (!s_cache.TryGetValue(typeName, out value))
            {
                value = new DummyTypeDeobfuscator(typeName);
                s_cache.Add(typeName, value);
            }

            return value;
        }

        public string ObfuscatedName { get; set; }
        public string OriginalName { get; set; }

        private DummyTypeDeobfuscator(string typeName)
        {
            ObfuscatedName = typeName;
            OriginalName = typeName;
        }

        public bool TryDeobfuscateField(string obfuscatedFieldName, out string originalFieldName)
        {
            originalFieldName = null;
            return false;
        }

        public bool TryObfuscateField(string originalFieldName, out ObfuscatedField obfuscatedFieldName)
        {
            obfuscatedFieldName = default(ObfuscatedField);
            return false;
        }

        public bool TryDeobfuscateMethod(string obfuscatedMethodName, string returnType, string[] parameterTypes, out string originalMethodName)
        {
            originalMethodName = null;
            return false;
        }

        public bool TryDeobfuscateMethod(string obfuscatedMethodName, string[] parameterTypes, out List<ObfuscatedMethod> originalMethodNames)
        {
            originalMethodNames = null;
            return false;
        }
    }
}