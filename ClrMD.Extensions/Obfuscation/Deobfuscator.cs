using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Xml.Linq;
using Microsoft.Diagnostics.Runtime;

namespace ClrMD.Extensions.Obfuscation
{
    internal sealed class TypeKey : IEquatable<TypeKey>
    {
        public string ModuleName { get; }
        public string TypeName { get; }

        public TypeKey(string moduleName, string typeName)
        {
            ModuleName = moduleName;
            TypeName = typeName;
        }

        public bool Equals(TypeKey other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(ModuleName, other.ModuleName) && string.Equals(TypeName, other.TypeName);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is TypeKey && Equals((TypeKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((ModuleName != null ? ModuleName.GetHashCode() : 0) * 397) ^ (TypeName != null ? TypeName.GetHashCode() : 0);
            }
        }

        public static bool operator ==(TypeKey left, TypeKey right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(TypeKey left, TypeKey right)
        {
            return !Equals(left, right);
        }
    }

    public class Deobfuscator
    {
        // (ModuleName, TypeName) -> TypeDeobfuscator
        private readonly Dictionary<TypeKey, ITypeDeobfuscator> m_obfuscationMap;

        private readonly Dictionary<ClrType, ITypeDeobfuscator> m_typeLookup;

        public string RenamingMapPath { get; private set; }

        public Deobfuscator(string renamingMapFilePath)
        {
            RenamingMapPath = renamingMapFilePath;
            var doc = XDocument.Load(renamingMapFilePath);

            m_obfuscationMap = (from moduleNode in doc.Elements("dotfuscatorMap").Elements("mapping").Elements("module")
                         from typeNode in moduleNode.Elements("type")
                         let obfuscatedTypeName = (string)typeNode.Element("newname")
                         where obfuscatedTypeName != null
                         select new
                                {
                                    TypeKey = new TypeKey((string)moduleNode.Element("name"), obfuscatedTypeName.Replace("/", "+")),
                                    TypeNode = typeNode
                                }).ToDictionary(item => item.TypeKey, item => (ITypeDeobfuscator)new TypeDeobfuscator(item.TypeNode));

            m_typeLookup = new Dictionary<ClrType, ITypeDeobfuscator>();
        }

        public ITypeDeobfuscator GetTypeDeobfuscator(string typeName)
        {
            return (from item in m_obfuscationMap
                    where item.Key.TypeName == typeName
                    select item.Value).FirstOrDefault() ?? DummyTypeDeobfuscator.GetDeobfuscator(typeName);
        }

        public ITypeDeobfuscator GetTypeDeobfuscator(ClrType type)
        {
            ITypeDeobfuscator result;

            if (m_typeLookup.TryGetValue(type, out result))
                return result;

            if (type.Module != null && type.Module.IsFile)
            {
                string moduleName = Path.GetFileName(type.Module.FileName);
                var key = new TypeKey(moduleName, type.Name);

                m_obfuscationMap.TryGetValue(key, out result);
            }

            if (result == null)
                result = DummyTypeDeobfuscator.GetDeobfuscator(type.Name);

            m_typeLookup.Add(type, result);

            return result;
        }

        public string ObfuscateSimpleType(string deobfuscatedTypeName)
        {
            return (from deobfuscator in m_obfuscationMap.Values
                    where deobfuscator.OriginalName.Equals(deobfuscatedTypeName, StringComparison.OrdinalIgnoreCase)
                    select deobfuscator.ObfuscatedName).FirstOrDefault() ?? deobfuscatedTypeName;
        }

      
        public string ObfuscateType(string deobfuscatedTypeName)
        {
            TypeName typeInfo = TypeNameRegex.ParseType(deobfuscatedTypeName);
            if (typeInfo.Name != null)
            {
                var obf = ObfuscateType(typeInfo);
                return obf.ToString();
            }
            return deobfuscatedTypeName;
        }

        internal TypeName ObfuscateType(TypeName deobfuscatedTypeName)
        {
            if (deobfuscatedTypeName.Name != null)
            {
                string name = ObfuscateSimpleType(deobfuscatedTypeName.Name);
                if (deobfuscatedTypeName.GenericArgs == null)
                {
                    return new TypeName(name);
                }

                TypeName[] genericArgs = deobfuscatedTypeName.GenericArgs.Select(ObfuscateType).ToArray();
                return new TypeName(name, genericArgs);
            }
            return deobfuscatedTypeName;
        }

        public string DeobfuscateSimpleType(string obfuscatedTypeName)
        {
            return (from deobfuscator in m_obfuscationMap.Values
                       where deobfuscator.ObfuscatedName.Equals(obfuscatedTypeName, StringComparison.OrdinalIgnoreCase)
                       select deobfuscator.OriginalName).FirstOrDefault() ?? obfuscatedTypeName;
        }

        public string DeobfuscateCallstack(string callstack)
        {
            string[] lines = callstack.Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (!line.Contains("System.Obfuscation"))
                {
                    continue;
                }
                string prefix;
                TypeName[] args;
                if (TypeNameRegex.TryExtractMethodInfo(line, out prefix, out args))
                {
                    string declaringType = prefix.Substring(0, prefix.LastIndexOf("."));
                    string deObfType = DeobfuscateSimpleType(declaringType);

                    string methodName = prefix.Substring(declaringType.Length + 1);
                    for (int j = 0; j < args.Length; j++)
                    {
                        string before = args[j].Name;
                        args[j] = DeobfuscateType(args[j]);
                        if (before == args[j].Name)
                        {
                            if (TypeNameRegex.CouldBeNestedType(args[j].Name))
                            {
                                //try as nested type.....
                                var arg2 = declaringType + "+" + args[j].Name;
                                var arg3 = DeobfuscateSimpleType(arg2);
                                if (arg2 != arg3)
                                {
                                    args[j] = new TypeName(arg3);
                                }
                            }
                        }
                    }
                    var obf = GetTypeDeobfuscator(declaringType);
                    if (obf != null)
                    {
                        List<ObfuscatedMethod> matches;
                        if (obf.TryDeobfuscateMethod(methodName, args.Select(a => a.ToString()).ToArray(), out matches))
                        {
                            if (matches.Count == 1)
                            {
                                lines[i] = $"{deObfType}.{matches[0].OriginalName}({string.Join(",", args)})";
                            }
                            else
                            {
                                var options = matches.Select(m => $"{deObfType}.{m.OriginalName}({string.Join(",", args)})");
                                lines[i] = "--AMBIGUOUS--\r\n\t" + string.Join("\r\n\t", options);
                            }
                        }
                    }
                }
            }
            return string.Join(Environment.NewLine, lines);
        }

        private TypeName DeobfuscateType(TypeName obfuscatedTypeName)
        {
            if (obfuscatedTypeName.Name != null)
            {
                string name = DeobfuscateSimpleType(obfuscatedTypeName.Name);
                if (obfuscatedTypeName.GenericArgs == null)
                {
                    return new TypeName(name);
                }

                var genericArgs = obfuscatedTypeName.GenericArgs.Select(DeobfuscateType).ToArray();
                return new TypeName(name, genericArgs);
            }
            return obfuscatedTypeName;
        }

        public string DeobfuscateType(string obfuscatedTypeName)
        {
            var typeName = TypeNameRegex.ParseType(obfuscatedTypeName);
            
            if (typeName.Name != null)
                return DeobfuscateType(typeName).ToString();

            return obfuscatedTypeName;
        }
    }
}