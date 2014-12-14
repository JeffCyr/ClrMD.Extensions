using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Diagnostics.Runtime;

namespace ClrMD.Extensions.Obfuscation
{
    public abstract class Token
    {
        
    }

    public class ParameterTypeToken : Token
    {
        public string TypeName { get; set; }
        public ParameterTypeToken[] GenericArgumentTypes { get; set; }
    }

    public class MethodTypeToken : Token
    {
        public string TypeName { get; set; }
        public MethodTypeToken[] GenericArgumentTypes { get; set; }
    }

    public class MethodToken : Token
    {
        public string MethodName { get; set; }
        public MethodTypeToken MethodType { get; set; }
        public MethodTypeToken[] GenericArgumentTypes { get; set; }
    }

    public class MethodSignatureToken : Token
    {
        public MethodToken Method { get; private set; }
        public ParameterTypeToken[] Parameters { get; private set; }

        public MethodSignatureToken(MethodToken method, ParameterTypeToken[] parameters)
        {
            Method = method;
            Parameters = parameters;
        }
    }

    public class UnknownToken : Token
    {
        public string Text { get; private set; }

        public UnknownToken(string text)
        {
            Text = text;
        }
    }

    public class MethodSignatureParser
    {
        private Regex m_signatureRegex = new Regex(@"^\s*(?:at\s+)?(?<method>[^\(]+)\((?<parameters>[^\)]*)\)\s*$", RegexOptions.Compiled);

        public void GetMethodInfo(ClrMethod method)
        {

        }

        private Token Parse(string methodSignature)
        {
            var match = m_signatureRegex.Match(methodSignature);

            if (!match.Success)
                return new UnknownToken(methodSignature);

            string method = match.Groups["method"].Value;
            string parameters = match.Groups["parameters"].Value;

            MethodToken methodToken = ParseMethod(method);

            if (methodToken == null)
                return new UnknownToken(methodSignature);

            ParameterTypeToken[] parameterTokens = ParseParameters(parameters);

            if (parameterTokens == null)
                return new UnknownToken(methodSignature);

            return new MethodSignatureToken(methodToken, parameterTokens);
        }

        private MethodToken ParseMethod(string method)
        {
            ReverseStringReader reader = new ReverseStringReader(method);

            reader.SkipWhiteSpaces();

            int endPosition = reader.Position;
            char c = reader.Read();

            if (c == '.')
                return null;

            while (c != '.')
            {
                if (c == ']')
                {
                    
                }
                else if (char.IsWhiteSpace(c))
                {
                    return null;
                }

                c = reader.Read();
            }
            




            return null;
        }

        public ParameterTypeToken[] ParseParameters(string parameters)
        {
            return null;
        }
    }

    public class ReverseStringReader
    {
        public string Input { get; set; }

        public int Position { get; set; }

        public ReverseStringReader(string input)
        {
            Input = input;
            Position = input.Length - 1;
        }

        public char Read()
        {
            if (Position < 0 || Position >= Input.Length)
                return '\0';

            return Input[Position--];
        }

        public char Peek()
        {
            if (Position < 0 || Position >= Input.Length)
                return '\0';

            return Input[Position];
        }

        public void SkipWhiteSpaces()
        {
            if (Position >= Input.Length)
                return;

            while (Position >= 0 && char.IsWhiteSpace(Input[Position]))
                --Position;
        }
    }
}