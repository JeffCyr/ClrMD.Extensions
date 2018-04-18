using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClrMD.Extensions.Obfuscation
{
    internal static class TypeNameRegex
    {
        private static readonly Regex s_cleanTypeNames = new Regex(@"`\d", RegexOptions.Compiled);


        private static List<Tuple<Regex, string>> AliasReplacements = new List<Tuple<Regex, string>>()
        {
            Tuple.Create(new Regex(@"object", RegexOptions.Compiled), "System.Object"),
            Tuple.Create(new Regex(@"string", RegexOptions.Compiled), "System.String"),
            Tuple.Create(new Regex(@"unsigned int(\d{2})", RegexOptions.Compiled), "System.UInt$1"),
            Tuple.Create(new Regex(@"int(\d{2})", RegexOptions.Compiled), "System.Int$1"),
            Tuple.Create(new Regex(@"float64", RegexOptions.Compiled), "System.Double"),
            Tuple.Create(new Regex(@"float32", RegexOptions.Compiled), "System.Single"),
            Tuple.Create(new Regex(@"bool", RegexOptions.Compiled), "System.Boolean"),
            Tuple.Create(new Regex(@"unsigned int8", RegexOptions.Compiled), "System.Byte"),
            Tuple.Create(new Regex(@"int8", RegexOptions.Compiled), "System.SByte"),
            Tuple.Create(new Regex(@"/", RegexOptions.Compiled), "+"),
        };

        internal class TypeDef
        {
            public string Name { get; set; }
            public List<TypeDef> GenericArgs { get; set; }
            public override string ToString()
            {
                if (GenericArgs == null || GenericArgs.Count == 0)
                {
                    return Name;
                }

                return $"{Name}<{string.Join(",", GenericArgs)}>";
            }
        }

        static IEnumerable<TypeDef> ParseArgList(string input)
        {
            foreach (Match match in s_argumentListRegex.Matches(input))
            {
                string completeName = match.Groups["typeDeclaration"].Value;
                string typeName = completeName;
                var genericArgs = match.Groups["genericArgs"];
                if (genericArgs != null && genericArgs.Success)
                {
                    typeName = completeName.Substring(0, genericArgs.Index - match.Index);
                    var args = match.Groups["genericArgs"].Value;
                    args = args.Substring(1, args.Length - 2);
                    yield return new TypeDef()
                    {
                        Name = typeName,
                        GenericArgs = ParseArgList(args).ToList(),
                    };
                }
                else
                {
                    yield return new TypeDef()
                    {
                        Name = typeName,
                    };
                }
            }

        }

        private static readonly Regex s_missingSystemPrefix = new Regex(@"Int(\d{2})", RegexOptions.Compiled);


        private const string TypeDefRegex = @"(?<typeDeclaration>\w[\w\.\d\+`]*(?<genericArgs><[^<>]*(((?<Open><)[^<>]*)+((?<Close-Open>>)[^<>]*)+)*(?(Open)(?!))>)?)";
        private static readonly Regex s_typeRegex = new Regex(TypeDefRegex, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex s_argumentListRegex = new Regex($"{TypeDefRegex},?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex s_genericTypeArgsRegex = new Regex(@"\w[\w\.\d\+]*<(?<args>.*)>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex s_callstackLineRegex = new Regex(@"\w[\w\.\d\+<>`]*\((?<args>.*)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex s_nestedTypeShort = new Regex(@"^[A-Z]{1,3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string FixMethodArgumentType(string type)
        {
            if (type == "Boolean")
            {
                return "System.Boolean";
            }

            return s_missingSystemPrefix.Replace(type, "System.Int$1");
        }

        public static bool CouldBeNestedType(string input)
        {
            return s_nestedTypeShort.IsMatch(input);
        }

        public static bool TryExtractMethodInfo(string input, out string prefix, out string[] argTypes)
        {
            var m = s_callstackLineRegex.Match(input);
            if (m.Success)
            {
                string args = m.Groups["args"].Value;
                prefix = input.Substring(0, m.Groups["args"].Index - 1);
                argTypes = ParseArgList(args).Select(a => a.ToString()).ToArray();
                return true;
            }

            prefix = null;
            argTypes = new string[0];
            return false;
        }


        public static bool TryExtractGenericArgs(string input, out string baseType, out string[] genericArgs)
        {
            var m = s_genericTypeArgsRegex.Match(input);
            if (m.Success)
            {
                string args = m.Groups["args"].Value;
                baseType = input.Substring(0, m.Groups["args"].Index - 1);
                genericArgs = ParseArgList(args).Select(a => a.ToString()).ToArray();
                return true;
            }

            baseType = null;
            genericArgs = null;
            return false;
        }

        public static string CorrectTypeNames(string val)
        {
            string result = val;
            foreach (var rep in AliasReplacements)
            {
                result = rep.Item1.Replace(result, rep.Item2);
            }
            return result;
        }

        public static string RemoveArityIndicators(string val)
        {
            return s_cleanTypeNames.Replace(val, "");
        }

    }
}