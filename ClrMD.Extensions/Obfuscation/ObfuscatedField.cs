namespace ClrMD.Extensions.Obfuscation
{
    public struct ObfuscatedField
    {
        public readonly string ObfuscatedName;
        public readonly string OriginalName;
        public readonly TypeName DeclaringType;


        public override string ToString()
        {
            return $"{ObfuscatedName} <==>  {OriginalName}   ({DeclaringType})";
        }

        public ObfuscatedField(string obfuscatedName, string originalName, TypeName originalFieldType)
        {
            ObfuscatedName = obfuscatedName;
            OriginalName = originalName;
            DeclaringType = originalFieldType;
        }
    }
}