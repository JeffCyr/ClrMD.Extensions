using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Diagnostics.Runtime;

namespace ClrMD.Extensions.Core
{
    internal static class SimpleValueHelper
    {
        private class SimpleValueHandler
        {
            public Func<ClrDynamic, ulong, object> GetSimpleValue { get; }
            public Func<object, string> GetSimpleValueString { get; }

            private SimpleValueHandler(Func<ClrDynamic, ulong, object> simpleValueExtractor, Func<object, string> stringFormatter = null)
            {
                GetSimpleValue = simpleValueExtractor;
                GetSimpleValueString = stringFormatter ?? (o => o?.ToString());
            }

            public static SimpleValueHandler Create<T>(Func<ClrDynamic, ulong, T> valueExtractor, Func<T, string> formatter = null)
            {
                if (formatter is null)
                    return new SimpleValueHandler((o, addr) => valueExtractor(o, addr));
                return new SimpleValueHandler((o, addr) => valueExtractor(o, addr), o => formatter((T) o));
            }
        }

        private static readonly Dictionary<string, SimpleValueHandler> s_simpleValueHandlers = new Dictionary<string, SimpleValueHandler>
        {
            ["System.String"] = SimpleValueHandler.Create(ExtractString),
            ["System.Guid"] = SimpleValueHandler.Create(ExtractGuid),
            ["System.TimeSpan"] = SimpleValueHandler.Create(ExtractTimeSpan),
            ["System.DateTime"] = SimpleValueHandler.Create(ExtractDateTime, GetDateTimeString),
            ["System.Net.IPAddress"] = SimpleValueHandler.Create(ExtractIPAddress),
            ["System.Net.IPEndPoint"] = SimpleValueHandler.Create(ExtractIPEndPoint),
            ["System.Net.DnsEndPoint"] = SimpleValueHandler.Create(ExtractDnsEndPoint),
            ["System.Security.Cryptography.X509Certificates.X509Certificate"] = SimpleValueHandler.Create(ExtractCertificate, GetX509CertificateString),
            ["System.Security.Cryptography.X509Certificates.X509Certificate2"] = SimpleValueHandler.Create(ExtractCertificate, GetX509CertificateString)
        };

        private static string ExtractString(ClrDynamic obj, ulong address)
        {
            return (string) obj.Type.GetValue(obj.Address);
        }

        private static DnsEndPoint ExtractDnsEndPoint(ClrDynamic obj, ulong address)
        {
            var dyn = obj.Dynamic;
            return new DnsEndPoint(dyn.m_Host, dyn.m_Port, dyn.m_Family);
        }

        private static IPEndPoint ExtractIPEndPoint(ClrDynamic obj, ulong address)
        {
            var dyn = obj.Dynamic;
            return new IPEndPoint((IPAddress)dyn.m_Address, (int)dyn.m_Port);
        }

        private static IPAddress ExtractIPAddress(ClrDynamic obj, ulong _)
        {
            const int IPv6AddressBytes = 16;
            const int NumberOfLabels = IPv6AddressBytes / 2;

            var dyn = obj.Dynamic;

            AddressFamily family = dyn.m_Family;
            switch (family)
            {
                case AddressFamily.InterNetworkV6:
                    var bytes = new byte[IPv6AddressBytes];
                    int j = 0;

                    var numbers = obj["m_Numbers"].ToArray<ushort>();

                    for (int i = 0; i < NumberOfLabels; i++)
                    {
                        ushort number = numbers[i];
                        bytes[j++] = (byte) ((number >> 8) & 0xFF);
                        bytes[j++] = (byte) (number & 0xFF);
                    }

                    return new IPAddress(bytes);
                case AddressFamily.InterNetwork:
                    return new IPAddress((long) dyn.m_Address);
                default:
                    throw new ArgumentException("This object is neither IPv4 nor IPv6!");
            }
        }

        private static DateTime ExtractDateTime(ClrDynamic obj, ulong address)
        {
            byte[] buffer = ReadBuffer(obj.Heap, address, 8);
            ulong dateData = BitConverter.ToUInt64(buffer, 0);
            return GetDateTime(dateData);
        }

        private static TimeSpan ExtractTimeSpan(ClrDynamic obj, ulong address)
        {
            byte[] buffer = ReadBuffer(obj.Heap, address, 8);
            long ticks = BitConverter.ToInt64(buffer, 0);
            return new TimeSpan(ticks);
        }

        private static Guid ExtractGuid(ClrDynamic obj, ulong address)
        {
            byte[] buffer = ReadBuffer(obj.Heap, address, 16);
            return new Guid(buffer);
        }

        private static X509Certificate2 ExtractCertificate(ClrDynamic obj, ulong address)
        {
            ulong handle = obj.Dynamic.m_safeCertContext.handle;
            if (handle == 0)
                return new X509Certificate2();
            var certCtxPointer = ReadIndirectAddress(obj.Heap, handle);
            if (certCtxPointer == 0)
                return new X509Certificate2();

            byte[] certContext = ReadBuffer(obj.Heap, certCtxPointer, 32);

            /*            
            typedef struct _CERT_CONTEXT {
                DWORD      dwCertEncodingType;
                BYTE       *pbCertEncoded;   <------- padded in x64!
                DWORD      cbCertEncoded;
                PCERT_INFO pCertInfo;
                HCERTSTORE hCertStore;
            } CERT_CONTEXT, *PCERT_CONTEXT;
             */

            ulong bytesPointer;
            int bytesCount;
            if (IntPtr.Size == 4)
            {
                bytesPointer = BitConverter.ToUInt32(certContext, 4);
                bytesCount = BitConverter.ToInt32(certContext, 8);
            }
            else
            {
                bytesPointer = BitConverter.ToUInt64(certContext, 8);
                bytesCount = BitConverter.ToInt32(certContext, 16);
            }

            byte[] certBytes = ReadBuffer(obj.Heap, bytesPointer, bytesCount);
            return new X509Certificate2(certBytes);
        }

        private static string GetX509CertificateString(X509Certificate2 cert)
        {
            return $"{cert.Subject} ({cert.Thumbprint})";
        }

        public static bool IsSimpleValue(ClrType type)
        {
            return type.IsPrimitive || s_simpleValueHandlers.ContainsKey(type.Name);
        }

        public static object GetSimpleValue(ClrDynamic obj)
        {
            if (obj.IsNull())
            {
                return null;
            }

            ClrType type = obj.Type;
            ClrHeap heap = type.Heap;

            if (type.IsPrimitive)
            {
                return type.GetValue(obj.Address);
            }

            if (!s_simpleValueHandlers.TryGetValue(obj.TypeName, out var handler))
                return false;

            ulong address = obj.IsInterior ? obj.Address : obj.Address + (ulong) heap.PointerSize;
            return handler.GetSimpleValue(obj, address);
        }

        public static string GetSimpleValueString(ClrDynamic obj)
        {
            object value = obj.SimpleValue;

            if (value == null)
                return "{null}";

            ClrType type = obj.Type;
            if (type != null && type.IsEnum)
                return type.GetEnumName(value) ?? value.ToString();

            if (s_simpleValueHandlers.TryGetValue(obj.TypeName, out var handler))
                return handler.GetSimpleValueString(value);

            return value.ToString();
        }

        private static byte[] ReadBuffer(ClrHeap heap, ulong address, int length)
        {
            byte[] buffer = new byte[length];
            int byteRead = heap.ReadMemory(address, buffer, 0, buffer.Length);

            if (byteRead != length)
                throw new InvalidOperationException(string.Format("Expected to read {0} bytes and actually read {1}", length, byteRead));

            return buffer;
        }

        private static ulong ReadIndirectAddress(ClrHeap heap, ulong address)
        {
            byte[] addr = ReadBuffer(heap, address, IntPtr.Size);
            if (IntPtr.Size == 4)
                return BitConverter.ToUInt32(addr, 0);
            return BitConverter.ToUInt64(addr, 0);
        }

        private static DateTime GetDateTime(ulong dateData)
        {
            const ulong DateTimeTicksMask = 0x3FFFFFFFFFFFFFFF;
            const ulong DateTimeKindMask = 0xC000000000000000;
            const ulong KindUnspecified = 0x0000000000000000;
            const ulong KindUtc = 0x4000000000000000;

            long ticks = (long) (dateData & DateTimeTicksMask);
            ulong internalKind = dateData & DateTimeKindMask;

            switch (internalKind)
            {
                case KindUnspecified:
                    return new DateTime(ticks, DateTimeKind.Unspecified);

                case KindUtc:
                    return new DateTime(ticks, DateTimeKind.Utc);

                default:
                    return new DateTime(ticks, DateTimeKind.Local);
            }
        }

        private static string GetDateTimeString(DateTime dateTime)
        {
            return dateTime.ToString(@"yyyy-MM-dd HH\:mm\:ss.FFFFFFF") + GetDateTimeKindString(dateTime.Kind);
        }

        private static string GetDateTimeKindString(DateTimeKind kind)
        {
            switch (kind)
            {
                case DateTimeKind.Unspecified:
                    return " (Unspecified)";
                case DateTimeKind.Utc:
                    return " (Utc)";
                default:
                    return " (Local)";
            }
        }
    }
}