using System.Collections.Generic;

namespace ClrMD.Extensions.Obfuscation
{
    public interface ITypeDeobfuscator
    {
        string ObfuscatedName { get; }
        string OriginalName { get; }

        bool TryDeobfuscateField(string obfuscatedFieldName, out string originalFieldName);

        bool TryObfuscateField(string originalFieldName, out ObfuscatedField field);

        bool TryDeobfuscateMethod(string obfuscatedMethodName, string returnType, string[] parameterTypes, out string originalMethodName);
        bool TryDeobfuscateMethod(string obfuscatedMethodName, string[] parameterTypes, out List<ObfuscatedMethod> originalMethodNames);
    }
}