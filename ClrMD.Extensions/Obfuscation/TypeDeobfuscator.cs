using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace ClrMD.Extensions.Obfuscation
{
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
                let fieldType = TypeNameRegex.ParseType((string) fieldNode.Element("signature"))
                select new ObfuscatedField(obfuscatedName, originalName, fieldType)).ToArray();

            m_methods = typeNode.Elements("methodlist").Elements("method").Select(m =>
            {
                string originalName = (string) m.Element("name");
                String obfuscatedName = (string) m.Element("newname");
                TypeName returnType = default(TypeName);
                string prefix;
                TypeName[] args;
                if (TypeNameRegex.TryExtractMethodInfo((string) m.Element("signature"), out prefix, out args))
                {
                    if (!string.IsNullOrEmpty(prefix))
                    {
                        returnType = TypeNameRegex.ParseType(prefix);
                    }
                }
                //TypeName returnType = null;
                return new ObfuscatedMethod(obfuscatedName, originalName, returnType, args);
            }).ToArray();

            Array.Sort(m_fields, (left, right) => m_comparer.Compare(left.ObfuscatedName + left.DeclaringType, right.ObfuscatedName + right.DeclaringType));
            Array.Sort(m_methods, (left, right) => m_comparer.Compare(left.SortKey, right.SortKey));
        }

        public bool TryDeobfuscateMethod(string obfuscatedMethodName, string returnType, string[] parameterTypes, out string originalMethodName)
        {
            if (string.IsNullOrEmpty(returnType))
            {
                returnType = "void";
            }

            if (TryDeobfuscateMethod(obfuscatedMethodName, parameterTypes, out var matches))
            {
                var realMatch = matches.FirstOrDefault(m =>  ((string)m.ReturnType ?? "void") == returnType);
                originalMethodName = realMatch.OriginalName;
                return originalMethodName != null;
            }

            originalMethodName = null;
            return false;
        }

        public bool TryDeobfuscateMethod(string obfuscatedMethodName, string[] parameterTypes, out List<ObfuscatedMethod> originalMethodNames)
        {
            string lookup = $"{obfuscatedMethodName}({string.Join(",", parameterTypes)})";
            originalMethodNames = null;
            int lo = 0;
            int hi = m_methods.Length - 1;
            while (lo <= hi)
            {
                int i = lo + ((hi - lo) >> 1);
                ObfuscatedMethod method = m_methods[i];

                int order = m_comparer.Compare(method.SortKey, lookup);

                if (order == 0)
                {
                    originalMethodNames = new List<ObfuscatedMethod> {method};
                    //there may be more than 1 match.
                    int firstMatch = i;
                    while (--i >= lo && order == 0)
                    {
                        method = m_methods[i];
                        order = m_comparer.Compare(method.SortKey, lookup);
                        if (order == 0)
                        {
                            originalMethodNames.Add(method);
                        }
                    }

                    i = firstMatch;
                    while (++i <= hi && order == 0)
                    {
                        method = m_methods[i];
                        order = m_comparer.Compare(method.SortKey, lookup);
                        if (order == 0)
                        {
                            originalMethodNames.Add(method);
                        }
                    }

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

            return false;
        }

        public bool TryDeobfuscateField(string obfuscatedFieldName, out string originalFieldName)
        {
            int lo = 0;
            int hi = m_fields.Length - 1;
            while (lo <= hi)
            {
                int i = lo + ((hi - lo) >> 1);
                ObfuscatedField field = m_fields[i];

                int order = m_comparer.Compare(field.ObfuscatedName + field.DeclaringType, obfuscatedFieldName + ObfuscatedName);

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
}