using System;
using System.Collections.Generic;
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

        public ObfuscatedField(string obfuscatedName, string originalName)
        {
            ObfuscatedName = obfuscatedName;
            OriginalName = originalName;
        }
    }

    public interface ITypeDeobfuscator
    {
        string ObfuscatedName { get; }
        string OriginalName { get; }

        bool TryDeobfuscateField(string obfuscatedFieldName, out string originalFieldName);
        bool TryObfuscateField(string originalFieldName, out string obfuscatedFieldName);
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

        public bool TryDeobfuscateField(string obfuscatedFieldName, out string originalFieldName)
        {
            originalFieldName = null;
            return false;
        }

        public bool TryObfuscateField(string originalFieldName, out string obfuscatedFieldName)
        {
            obfuscatedFieldName = null;
            return false;
        }
    }

    public class TypeDeobfuscator : ITypeDeobfuscator
    {
        private ObfuscatedField[] m_fields;
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
            ObfuscatedName = ((string)typeNode.Element("newname")).Replace('/', '+');
            OriginalName = ((string)typeNode.Element("name")).Replace('/', '+');

            m_fields = (from fieldNode in typeNode.Elements("fieldlist").Elements("field")
                        let originalName = (string)fieldNode.Element("name")
                        let obfuscatedName = (string)fieldNode.Element("newname")
                        select new ObfuscatedField(obfuscatedName, originalName)).ToArray();

            Array.Sort(m_fields, (left, right) => m_comparer.Compare(left.ObfuscatedName, right.ObfuscatedName));
        }

        public bool TryDeobfuscateField(string obfuscatedFieldName, out string originalFieldName)
        {
            int lo = 0;
            int hi = m_fields.Length - 1;
            while (lo <= hi)
            {
                int i = lo + ((hi - lo) >> 1);
                ObfuscatedField field = m_fields[i];

                int order = m_comparer.Compare(field.ObfuscatedName, obfuscatedFieldName);

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

        public bool TryObfuscateField(string originalFieldName, out string obfuscatedFieldName)
        {
            for (int i = 0; i < m_fields.Length; i++)
            {
                var field = m_fields[i];

                if (m_comparer.Compare(field.OriginalName, originalFieldName) == 0)
                {
                    obfuscatedFieldName = field.ObfuscatedName;
                    return true;
                }
            }

            obfuscatedFieldName = null;
            return false;
        }
    }

    public class Deobfuscator
    {
        // (ModuleName, TypeName) -> TypeDeobfuscator
        private Dictionary<Tuple<string, string>, TypeDeobfuscator> m_typeMap;

        public string RenamingMapPath { get; private set; }

        public Deobfuscator(string renamingMapFilePath)
        {
            RenamingMapPath = renamingMapFilePath;

            var doc = XDocument.Load(renamingMapFilePath);

            m_typeMap = (from moduleNode in doc.Elements("dotfuscatorMap").Elements("mapping").Elements("module")
                         from typeNode in moduleNode.Elements("type")
                         let obfuscatedTypeName = (string)typeNode.Element("newname")
                         where obfuscatedTypeName != null
                         select new
                                {
                                    TypeKey = Tuple.Create((string)moduleNode.Element("name"), obfuscatedTypeName),
                                    TypeNode = typeNode
                                }).ToDictionary(item => item.TypeKey, item => new TypeDeobfuscator(item.TypeNode));
        }

        public ITypeDeobfuscator GetTypeDeobfuscator(string typeName)
        {
            return (from item in m_typeMap
                    where item.Key.Item2 == typeName
                    select item.Value).FirstOrDefault() ?? DummyTypeDeobfuscator.GetDeobfuscator(typeName);
        }

        public ITypeDeobfuscator GetTypeDeobfuscator(ClrType type)
        {
            if (type.Module != null && type.Module.IsFile)
            {
                TypeDeobfuscator result;
                string moduleName = Path.GetFileName(type.Module.FileName);
                var key = Tuple.Create(moduleName, GetDotFuscatorTypeName(type));

                if (m_typeMap.TryGetValue(key, out result))
                    return result;
            }

            return DummyTypeDeobfuscator.GetDeobfuscator(type.Name);
        }

        private string GetDotFuscatorTypeName(ClrType type)
        {
            return type.Name.Replace('+', '/');
        }

        public string ObfuscateType(string deobfuscatedTypeName)
        {
            return (from deobfuscator in m_typeMap.Values
                    where deobfuscator.OriginalName.Equals(deobfuscatedTypeName, StringComparison.OrdinalIgnoreCase)
                    select deobfuscator.ObfuscatedName).FirstOrDefault() ?? deobfuscatedTypeName;
        }

        public string DeobfuscateType(string obfuscatedTypeName)
        {
            return (from deobfuscator in m_typeMap.Values
                    where deobfuscator.ObfuscatedName.Equals(obfuscatedTypeName, StringComparison.OrdinalIgnoreCase)
                    select deobfuscator.OriginalName).FirstOrDefault() ?? obfuscatedTypeName;
        }
    }
}