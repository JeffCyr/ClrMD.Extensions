using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Diagnostics.Runtime;

namespace ClrMD.Extensions.Core
{
    public static class ClrTypeExtensions
    {
        private static readonly Dictionary<string, Type> s_dumpToClrTypes;

        static ClrTypeExtensions()
        {
            s_dumpToClrTypes = new Dictionary<string, Type>();

            void AddType(Type t)
            {
                s_dumpToClrTypes.Add(t.FullName, t);
            }

            AddType(typeof(object));
            AddType(typeof(string));
            AddType(typeof(void));
            AddType(typeof(byte));
            AddType(typeof(sbyte));
            AddType(typeof(short));
            AddType(typeof(ushort));
            AddType(typeof(int));
            AddType(typeof(uint));
            AddType(typeof(long));
            AddType(typeof(ulong));
            AddType(typeof(float));
            AddType(typeof(double));
            AddType(typeof(decimal));
            AddType(typeof(Guid));
            AddType(typeof(DateTime));
            AddType(typeof(TimeSpan));
            AddType(typeof(IPAddress));
            AddType(typeof(IPEndPoint));
            AddType(typeof(DnsEndPoint));
            AddType(typeof(X509Certificate));
            AddType(typeof(X509Certificate2));
        }

        public static Type GetRealType(this ClrType type)
        {
            if (s_dumpToClrTypes.TryGetValue(type.Name, out var t))
                return t;

            throw new ArgumentException("Only basic types can be matched to the concrete runtime types");
        }

        public static TypeCode GetTypeCode(this ClrType type)
        {
            return s_dumpToClrTypes.TryGetValue(type.Name, out var t)
                ? Type.GetTypeCode(t)
                : TypeCode.Object;
        }
    }
}