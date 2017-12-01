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
    public struct ObfuscatedField
    {
        public readonly string ObfuscatedName;
        public readonly string OriginalName;
        public readonly string OriginalFieldType;


        public override string ToString()
        {
            return $"{ObfuscatedName} <==>  {OriginalName}   ({OriginalFieldType})";
        }

        public ObfuscatedField(string obfuscatedName, string originalName, string originalFieldType)
        {
            ObfuscatedName = obfuscatedName;
            OriginalName = originalName;
            OriginalFieldType = originalFieldType;
        }
    }

    public struct ObfuscatedMethod
    {
        public readonly string ObfuscatedName;
        public readonly string OriginalName;
        public readonly string ReturnType;
        public readonly string[] ArgumentTypes;

        internal string SortKey => $"{ObfuscatedName}({string.Join(",", ArgumentTypes)})";

        public override string ToString()
        {
            return $"{ObfuscatedName} <==> {OriginalName}     ({string.Join(",", ArgumentTypes)}) : {ReturnType ?? "void"}";
        }

        public ObfuscatedMethod(string obfuscatedName, string originalName, string returnType, string[] argumentTypes)
        {
            ObfuscatedName = obfuscatedName;
            OriginalName = originalName;
            ReturnType = returnType;
            ArgumentTypes = argumentTypes;
        }
    }

    public interface ITypeDeobfuscator
    {
        string ObfuscatedName { get; }
        string OriginalName { get; }

        bool TryDeobfuscateField(string obfuscatedFieldName, string typeName, out string originalFieldName);

        bool TryDeobfuscateMethod(string obfuscatedMethodName, string[] parameterTypes, out string originalMethodName);

        bool TryObfuscateField(string originalFieldName, out ObfuscatedField field);

    }

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

        public bool TryDeobfuscateField(string obfuscatedFieldName, string typeName, out string originalFieldName)
        {
            originalFieldName = null;
            return false;
        }

        public bool TryDeobfuscateMethod(string obfuscatedMethodName, string[] parameterTypes, out string originalMethodName)
        {
            originalMethodName = null;
            return false;
        }

        public bool TryObfuscateField(string originalFieldName, out ObfuscatedField obfuscatedFieldName)
        {
            obfuscatedFieldName = default(ObfuscatedField);
            return false;
        }
    }

    public class TypeDeobfuscator : ITypeDeobfuscator
    {
        private ObfuscatedField[] m_fields;
        private ObfuscatedMethod[] m_methods;
        private StringComparer m_comparer;

        public string ObfuscatedName { get; private set; }
        public string OriginalName { get; private set; }

        public IEnumerable<ObfuscatedField> Fields
        {
            get { return m_fields; }
        }

        internal TypeDeobfuscator(XElement typeNode)
        {
            m_comparer = StringComparer.Ordinal;
            ObfuscatedName = ((string) typeNode.Element("newname")).Replace('/', '+');
            OriginalName = ((string) typeNode.Element("name")).Replace('/', '+');

            m_fields = (from fieldNode in typeNode.Elements("fieldlist").Elements("field")
                let originalName = (string) fieldNode.Element("name")
                let obfuscatedName = (string) fieldNode.Element("newname")
                let fieldType = TypeNameRegex.CorrectTypeNames(TypeNameRegex.RemoveArityIndicators((string) fieldNode.Element("signature"))).Split(' ')[0]
                select new ObfuscatedField(obfuscatedName, originalName, fieldType)).ToArray();

            m_methods = typeNode.Elements("methodlist").Elements("method").Select(m =>
            {
                string originalName = (string) m.Element("name");
                String obfuscatedName = (string) m.Element("newname");
                string returnType;
                string[] args;
                if (TypeNameRegex.TryExtractMethodInfo((string) m.Element("signature"), out returnType, out args))
                {
                    returnType = TypeNameRegex.CorrectTypeNames(TypeNameRegex.RemoveArityIndicators(returnType));
                    for (int i = 0; i < args.Length; i++)
                    {
                        args[i] = TypeNameRegex.CorrectTypeNames(TypeNameRegex.RemoveArityIndicators(args[i]));
                    }
                }
                return new ObfuscatedMethod(obfuscatedName, originalName, returnType, args);
            }).ToArray();

            Array.Sort(m_fields, (left, right) => m_comparer.Compare(left.ObfuscatedName + left.OriginalFieldType, right.ObfuscatedName + right.OriginalFieldType));
            Array.Sort(m_methods, (left, right) => m_comparer.Compare(left.SortKey, right.SortKey));
        }

        public bool TryDeobfuscateMethod(string obfuscatedMethodName, string[] parameterTypes, out string originalMethodName)
        {
            string lookup = $"{obfuscatedMethodName}({string.Join(",", parameterTypes)})";

            int lo = 0;
            int hi = m_methods.Length - 1;
            while (lo <= hi)
            {
                int i = lo + ((hi - lo) >> 1);
                ObfuscatedMethod field = m_methods[i];

                int order = m_comparer.Compare(field.SortKey, lookup);

                if (order == 0)
                {
                    originalMethodName = field.OriginalName;
                    return true;
                }

                if (order < 0)
                {
                    lo = i + 1;
                }
                else
                {
                    hi = i - 1;
                }
            }

            originalMethodName = null;
            return false;


        }

        public bool TryDeobfuscateField(string obfuscatedFieldName, string originalFieldType, out string originalFieldName)
        {
            int lo = 0;
            int hi = m_fields.Length - 1;
            while (lo <= hi)
            {
                int i = lo + ((hi - lo) >> 1);
                ObfuscatedField field = m_fields[i];

                int order = m_comparer.Compare(field.ObfuscatedName + field.OriginalFieldType, obfuscatedFieldName + originalFieldType);

                if (order == 0)
                {
                    originalFieldName = field.OriginalName;
                    return true;
                }

                if (order < 0)
                {
                    lo = i + 1;
                }
                else
                {
                    hi = i - 1;
                }
            }

            originalFieldName = null;
            return false;
        }

        public bool TryObfuscateField(string originalFieldName, out ObfuscatedField obfuscatedFieldName)
        {
            for (int i = 0; i < m_fields.Length; i++)
            {
                ObfuscatedField field = m_fields[i];

                if (m_comparer.Compare(field.OriginalName, originalFieldName) == 0)
                {
                    obfuscatedFieldName = field;
                    return true;
                }
            }

            obfuscatedFieldName = default(ObfuscatedField);
            return false;
        }
    }

    public class Deobfuscator
    {
        // (ModuleName, TypeName) -> TypeDeobfuscator
        private readonly Dictionary<(string, string), ITypeDeobfuscator> m_obfuscationMap;

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
                                    TypeKey = ((string)moduleNode.Element("name"), obfuscatedTypeName.Replace("/", "+")),
                                    TypeNode = typeNode
                                }).ToDictionary(item => item.TypeKey, item => (ITypeDeobfuscator)new TypeDeobfuscator(item.TypeNode));

            m_typeLookup = new Dictionary<ClrType, ITypeDeobfuscator>();
        }

        public ITypeDeobfuscator GetTypeDeobfuscator(string typeName)
        {
            return (from item in m_obfuscationMap
                    where item.Key.Item2 == typeName
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
                var key = (moduleName, type.Name);

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
            string type;
            string[] split;
            if (TypeNameRegex.TryExtractGenericArgs(deobfuscatedTypeName, out type, out split))
            {
                for (int i = 0; i < split.Length; i++)
                {
                    split[i] = ObfuscateType(split[i]);
                }
                return $"{TypeNameRegex.RemoveArityIndicators(ObfuscateSimpleType(type))}<{string.Join(",", split)}>";
            }
            else
            {
                return ObfuscateSimpleType(deobfuscatedTypeName);
            }
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
                string[] args;
                if (TypeNameRegex.TryExtractMethodInfo(line, out prefix, out args))
                {
                    string declaringType = prefix.Substring(0, prefix.LastIndexOf("."));
                    string deObfType = TypeNameRegex.RemoveArityIndicators(DeobfuscateSimpleType(declaringType));

                    string methodName = prefix.Substring(declaringType.Length + 1);
                    for (int j = 0; j < args.Length; j++)
                    {
                        string arg = DeobfuscateType(args[j]);

                        if (arg == args[j])
                        {
                            if (TypeNameRegex.CouldBeNestedType(arg))
                            {
                                //try as nested type.....
                                var arg2 = declaringType + "+" + arg;
                                var arg3 = DeobfuscateType(arg2);
                                if (arg2 != arg3)
                                {
                                    arg = arg3;
                                }
                            }
                            else
                            {
                                arg = TypeNameRegex.FixMethodArgumentType(arg);
                            }
                        }

                        args[j] = arg;
                    }
                    var obf = GetTypeDeobfuscator(declaringType);
                    if (obf != null)
                    {
                        string deObfName;
                        if (obf.TryDeobfuscateMethod(methodName, args, out deObfName))
                        {
                            lines[i] = $"{deObfType}.{deObfName}({string.Join(",", args)})";
                         //   break;
                        }

                      /*  var clrType = ClrMDSession.Current.Heap.GetTypeByName(obf.ObfuscatedName);
                        if (clrType?.BaseType != null)
                        {
                            obf = GetTypeDeobfuscator(clrType.BaseType); 
                        }
                        else
                        {
                            obf = null;
                        }*/
                    }
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        public string DeobfuscateType(string obfuscatedTypeName)
        {
            string type;
            string[] split;
            if (TypeNameRegex.TryExtractGenericArgs(obfuscatedTypeName, out type, out split))
            {
                for (int i = 0; i < split.Length; i++)
                {
                    split[i] = DeobfuscateType(split[i]);
                }
                return $"{TypeNameRegex.RemoveArityIndicators(DeobfuscateSimpleType(type))}<{string.Join(",", split)}>";
            }
            else
            {
                return DeobfuscateSimpleType(obfuscatedTypeName);
            }
        }
    }
}