using System;
using System.Collections.Generic;
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
        };

        private static readonly Regex s_genericTypeArgsRegex = new Regex(@"\w[\w\.\d]*<(?<args>.*)>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static bool TryExtractGenericArgs(string input, out string baseType, out string[] genericArgs)
        {
            var m = s_genericTypeArgsRegex.Match(input);
            if (m.Success)
            {
                string args = m.Groups["args"].Value;
                baseType = input.Substring(0, m.Groups["args"].Index - 1);
                genericArgs = args.Split(new[] {',', ' '}, StringSplitOptions.RemoveEmptyEntries);
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