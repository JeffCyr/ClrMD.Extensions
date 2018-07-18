using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClrMD.Extensions.Obfuscation
{
    internal static class TypeNameRegex
    {
        private const string TypeDefRegex = @"(?<typeDeclaration>\w[\w\.\d\+`]*(?<genericArgs><[^<>]*(((?<Open><)[^<>]*)+((?<Close-Open>>)[^<>]*)+)*(?(Open)(?!))>)?(?<array>(?:\[,*\])+)*)";
        private static readonly Regex s_typeRegex = new Regex(TypeDefRegex, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex s_argumentListRegex = new Regex($"{TypeDefRegex},?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex s_callstackLineRegex = new Regex(@"\w[\w\.\d\+<>`]*\((?<args>.*)\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex s_nestedTypeShort = new Regex(@"^[A-Z]{1,3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex s_cleanTypeNames = new Regex(@"`\d", RegexOptions.Compiled);
        private static List<Tuple<Regex, string>> AliasReplacements = new List<Tuple<Regex, string>>()
        {
            Tuple.Create(new Regex(@"object", RegexOptions.Compiled), "System.Object"),
            Tuple.Create(new Regex(@"string", RegexOptions.Compiled), "System.String"),
            Tuple.Create(new Regex(@"unsigned int(\d{2})", RegexOptions.Compiled), "System.UInt$1"),
            Tuple.Create(new Regex(@"int(\d{2})", RegexOptions.Compiled), "System.Int$1"),
            Tuple.Create(new Regex(@"^Int(\d{2})", RegexOptions.Compiled), "System.Int$1"),
            Tuple.Create(new Regex(@"float64", RegexOptions.Compiled), "System.Double"),
            Tuple.Create(new Regex(@"float32", RegexOptions.Compiled), "System.Single"),
            Tuple.Create(new Regex(@"bool", RegexOptions.Compiled), "System.Boolean"),
            Tuple.Create(new Regex(@"^Boolean", RegexOptions.Compiled), "System.Boolean"),
            Tuple.Create(new Regex(@"unsigned int8", RegexOptions.Compiled), "System.Byte"),
            Tuple.Create(new Regex(@"int8", RegexOptions.Compiled), "System.SByte"),
            Tuple.Create(new Regex(@"/", RegexOptions.Compiled), "+"),
        };
       
        static IEnumerable<TypeName> ParseArgList(string input)
        {
            foreach (Match match in s_argumentListRegex.Matches(input))
            {
                var type = ParseType(match.Value);
                if (type != null)
                {
                    yield return type;
                }
            }
        }
        public static TypeName ParseType(string input)
        {
            var match = s_typeRegex.Match(input);
            if (match.Success)
            {
                string completeName = match.Value;
                string typeName = completeName;
                var genericArgs = match.Groups["genericArgs"];
                string array = null;
                var arrayGroup = match.Groups["array"];
                if (arrayGroup?.Success == true)
                {
                    array = arrayGroup.Value;
                }
                if (genericArgs != null && genericArgs.Success)
                {
                    typeName = completeName.Substring(0, genericArgs.Index - match.Index);
                    var args = match.Groups["genericArgs"].Value;
                    args = args.Substring(1, args.Length - 2);
                    return new TypeName(SanitizeType(typeName), array, ParseArgList(args).ToArray());
                }

                if (array != null)
                {
                    typeName = typeName.Substring(0, typeName.Length - array.Length);
                }

                return new TypeName(SanitizeType(typeName), array);
            }
            return new TypeName(input);
        }

     
        public static bool CouldBeNestedType(string input)
        {
            return s_nestedTypeShort.IsMatch(input);
        }

        public static bool TryExtractMethodInfo(string input, out string prefix, out TypeName[] argTypes)
        {
            var m = s_callstackLineRegex.Match(input);
            if (m.Success)
            {
                string args = m.Groups["args"].Value;
                prefix = SanitizeType(input.Substring(0, m.Groups["args"].Index - 1));
                argTypes = ParseArgList(args).ToArray();
                return true;
            }

            prefix = null;
            argTypes = new TypeName[0];
            return false;
        }

        internal static string SanitizeType(string input)
        {
            return CorrectTypeNames(RemoveArityIndicators(input));
        }

        private static string CorrectTypeNames(string val)
        {
            string result = val;
            foreach (var rep in AliasReplacements)
            {
                result = rep.Item1.Replace(result, rep.Item2);
            }
            return result;
        }

        private static string RemoveArityIndicators(string val)
        {
            return s_cleanTypeNames.Replace(val, "");
        }
    }
}