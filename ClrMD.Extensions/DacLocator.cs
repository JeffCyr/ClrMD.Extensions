using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Win32.SafeHandles;

namespace ClrMD.Extensions
{
    public class DacLocator : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern LibrarySafeHandle LoadLibrary(string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("x86\\dbghelp.dll", EntryPoint = "SymInitialize", SetLastError = true)]
        private static extern bool SymInitialize32(IntPtr hProcess, string symPath, bool fInvadeProcess);

        [DllImport("x86\\dbghelp.dll", EntryPoint = "SymCleanup", SetLastError = true)]
        private static extern bool SymCleanup32(IntPtr hProcess);

        [DllImport("x86\\dbghelp.dll", EntryPoint = "SymFindFileInPath", SetLastError = true)]
        private static extern bool SymFindFileInPath32(IntPtr hProcess, string searchPath, string filename, uint id, uint two, uint three, uint flags, StringBuilder filePath, IntPtr callback, IntPtr context);

        [DllImport("x64\\dbghelp.dll", EntryPoint = "SymInitialize", SetLastError = true)]
        private static extern bool SymInitialize64(IntPtr hProcess, string symPath, bool fInvadeProcess);

        [DllImport("x64\\dbghelp.dll", EntryPoint = "SymCleanup", SetLastError = true)]
        private static extern bool SymCleanup64(IntPtr hProcess);

        [DllImport("x64\\dbghelp.dll", EntryPoint = "SymFindFileInPath", SetLastError = true)]
        private static extern bool SymFindFileInPath64(IntPtr hProcess, string searchPath, string filename, uint id, uint two, uint three, uint flags, StringBuilder filePath, IntPtr callback, IntPtr context);

        private static bool SymInitialize(IntPtr hProcess, string symPath, bool fInvadeProcess)
        {
            if (Environment.Is64BitProcess)
                return SymInitialize64(hProcess, symPath, fInvadeProcess);

            return SymInitialize32(hProcess, symPath, fInvadeProcess);
        }

        private static bool SymCleanup(IntPtr hProcess)
        {
            if (Environment.Is64BitProcess)
                return SymCleanup64(hProcess);

            return SymCleanup32(hProcess);
        }

        private static bool SymFindFileInPath(IntPtr hProcess, string searchPath, string filename, uint id, uint two, uint three, uint flags, StringBuilder filePath, IntPtr callback, IntPtr context)
        {
            if (Environment.Is64BitProcess)
                return SymFindFileInPath64(hProcess, searchPath, filename, id, two, three, flags, filePath, callback, context);

            return SymFindFileInPath32(hProcess, searchPath, filename, id, two, three, flags, filePath, callback, context);
        }

        private String searchPath;
        private LibrarySafeHandle dbghelpModule;
        private Process ourProcess;

        private DacLocator(string searchPath)
        {
            this.searchPath = searchPath;
            ourProcess = Process.GetCurrentProcess();

            CreateFiles();

            dbghelpModule = Environment.Is64BitProcess ? LoadLibrary("x64\\dbghelp.dll")
                                                       : LoadLibrary("x86\\dbghelp.dll");

            if (dbghelpModule.IsInvalid)
                throw new InvalidOperationException("Could not load dbghelp.dll", new Win32Exception(Marshal.GetLastWin32Error()));

            if (!SymInitialize(ourProcess.Handle, searchPath, false))
                throw new InvalidOperationException("SymInitialize: Unexpected error occured.", new Win32Exception(Marshal.GetLastWin32Error()));
        }

        private static void CreateFiles()
        {
            if (Environment.Is64BitProcess)
            {
                if (!Directory.Exists("x64"))
                    Directory.CreateDirectory("x64");

                if (!File.Exists("x64\\dbghelp.dll"))
                    CreateFileFromResource("ClrMD.Extensions.Dlls.x64.dbghelp.dll",
                                           "x64\\dbghelp.dll");

                if (!File.Exists("x64\\symsrv.dll"))
                    CreateFileFromResource("ClrMD.Extensions.Dlls.x64.symsrv.dll",
                                           "x64\\symsrv.dll");

                if (!File.Exists("x64\\symsrv.yes"))
                    File.WriteAllText("x64\\symsrv.yes", "");
            }
            else
            {
                if (!Directory.Exists("x86"))
                    Directory.CreateDirectory("x86");

                if (!File.Exists("x86\\dbghelp.dll"))
                    CreateFileFromResource("ClrMD.Extensions.Dlls.x86.dbghelp.dll",
                                           "x86\\dbghelp.dll");

                if (!File.Exists("x86\\symsrv.dll"))
                    CreateFileFromResource("ClrMD.Extensions.Dlls.x86.symsrv.dll",
                                           "x86\\symsrv.dll");

                if (!File.Exists("x86\\symsrv.yes"))
                    File.WriteAllText("x86\\symsrv.yes", "");
            }
        }

        private static void CreateFileFromResource(string resourceName, string filePath)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);

            if (stream == null)
                throw new InvalidOperationException(string.Format("'{0}' not found in resources.", resourceName));

            using (stream)
            using (FileStream fileStream = File.Create(filePath))
            {
                stream.CopyTo(fileStream);
                fileStream.Flush();
            }
        }

        public static DacLocator FromPublicSymbolServer(string localCache)
        {
            return new DacLocator(String.Format("SRV*{0}*http://msdl.microsoft.com/download/symbols", localCache));
        }
        public static DacLocator FromEnvironment()
        {
            String ntSymbolPath = Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH");
            return new DacLocator(ntSymbolPath);
        }
        public static DacLocator FromSearchPath(string searchPath)
        {
            return new DacLocator(searchPath);
        }

        public string FindDac(ClrInfo clrInfo)
        {
            string dac = clrInfo.TryGetDacLocation();

            if (string.IsNullOrEmpty(dac))
                dac = FindDac(clrInfo.DacInfo.FileName, clrInfo.DacInfo.TimeStamp, clrInfo.DacInfo.FileSize);

            return dac;
        }
        public string FindDac(string dacname, uint timestamp, uint fileSize)
        {
            // attemp using the symbol server
            StringBuilder symbolFile = new StringBuilder(2048);
            if (SymFindFileInPath(ourProcess.Handle, searchPath, dacname,
                timestamp, fileSize, 0, 0x02, symbolFile, IntPtr.Zero, IntPtr.Zero))
            {
                return symbolFile.ToString();
            }

            throw new InvalidOperationException(string.Format("Unable to find dac file '{0}' in symbol server.", dacname), new Win32Exception(Marshal.GetLastWin32Error()));
        }

        public void Dispose()
        {
            if (ourProcess != null)
            {
                SymCleanup(ourProcess.Handle);
                ourProcess = null;
            }
            if (dbghelpModule != null && !dbghelpModule.IsClosed)
            {
                dbghelpModule.Dispose();
                dbghelpModule = null;
            }
        }

        public class LibrarySafeHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public LibrarySafeHandle()
                : base(true)
            {
            }
            public LibrarySafeHandle(IntPtr handle)
                : base(true)
            {
                this.SetHandle(handle);
            }
            protected override bool ReleaseHandle()
            {
                return FreeLibrary(this.handle);
            }
        }
    }
}