using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using ClrMD.Extensions.Core;
using ClrMD.Extensions.Obfuscation;
using Microsoft.Diagnostics.Runtime;

namespace ClrMD.Extensions
{
    public class ClrMDSession : IDisposable
    {
        private static ClrMDSession s_currentSession;
        private static string s_lastDumpPath;

        public static ClrMDSession Current
        {
            get { return s_currentSession; }
        }

        private Lazy<List<ClrObject>> m_allObjects;
        private ReferenceMap m_referenceMap;
        private Deobfuscator m_deobfuscator;

        public DataTarget Target { get; private set; }
        public ClrRuntime Runtime { get; private set; }
        public ClrHeap Heap { get; private set; }

        public IEnumerable<ClrObject> AllObjects
        {
            get { return m_allObjects.Value; }
        }

        public bool IsReferenceMappingCreated { get; private set; }

        private ClrMDSession(DataTarget target, string dacFile)
            : this(target, target.CreateRuntime(dacFile))
        { }

        public ClrMDSession(DataTarget target, ClrRuntime runtime)
        {
            Target = target;
            Runtime = runtime;
            Heap = Runtime.GetHeap();

            m_allObjects = new Lazy<List<ClrObject>>(() => Heap.EnumerateClrObjects().ToList());

            s_currentSession = this;
        }

        public static ClrMDSession LoadCrashDump(string dumpPath)
        {
            if (s_currentSession != null)
            {
                if (s_lastDumpPath == dumpPath)
                {
                    return s_currentSession;
                }

                s_currentSession.Dispose();
                s_currentSession = null;
            }

            string dacFile;
            DataTarget target = DataTarget.LoadCrashDump(dumpPath);

            try
            {
                if (target.Architecture == Architecture.X86 && Environment.Is64BitProcess ||
                    target.Architecture == Architecture.Amd64 && !Environment.Is64BitProcess)
                {
                    throw new InvalidOperationException("Mismatched architecture between this process and the target process.");
                }

                dacFile = target.ClrVersions[0].TryGetDacLocation();
                if (string.IsNullOrEmpty(dacFile))
                {
                    using (var locator = DacLocator.FromPublicSymbolServer("Symbols"))
                    {
                        dacFile = locator.FindDac(target.ClrVersions[0]);
                    }
                }
            }
            catch
            {
                target.Dispose();
                throw;
            }

            s_lastDumpPath = dumpPath;
            s_currentSession = new ClrMDSession(target, dacFile);
            return s_currentSession;
        }

        public static ClrMDSession AttachToProcess(string processName, uint millisecondsTimeout = 5000, AttachFlag attachFlag = AttachFlag.Invasive)
        {
            Process p = Process.GetProcessesByName(processName).FirstOrDefault();

            if (p == null)
                throw new ArgumentException("Process not found", "processName");

            return AttachToProcess(p.Id, millisecondsTimeout, attachFlag);
        }

        public static ClrMDSession AttachToProcess(int pid, uint millisecondsTimeout = 5000, AttachFlag attachFlag = AttachFlag.Invasive)
        {
            if (s_currentSession != null)
                {
                    s_currentSession.Dispose();
                    s_currentSession = null;
                    s_lastDumpPath = null;
                }

            DataTarget target = DataTarget.AttachToProcess(pid, millisecondsTimeout, attachFlag);
            string dacFile = target.ClrVersions[0].TryGetDacLocation();

            if (dacFile == null)
                throw new InvalidOperationException("Unable to find dac file.");

            s_currentSession = new ClrMDSession(target, dacFile);
            return s_currentSession;
        }

        public IEnumerable<ClrObject> EnumerateClrObjects(string typeName)
        {
            if (typeName.Contains("*"))
            {
                string typeNameRegex = "^" + Regex.Escape(typeName).Replace("\\*", ".*") + "$";
                Regex regex = new Regex(typeNameRegex, RegexOptions.Compiled | RegexOptions.IgnoreCase);

                return AllObjects.Where(item => regex.IsMatch(item.Type.Name));
            }

            typeName = ObfuscateType(typeName);

            return AllObjects.Where(item => item.Type.Name == typeName);
        }

        public void CreateReferenceMapping()
        {
            if (IsReferenceMappingCreated)
                return;

            m_referenceMap = new ReferenceMap(AllObjects);
            IsReferenceMappingCreated = true;
        }

        public void ClearReferenceMapping()
        {
            IsReferenceMappingCreated = false;
            m_referenceMap = null;
        }

        public void CreateDeobfuscator(string renamingMapFilePath)
        {
            if (m_deobfuscator != null && m_deobfuscator.RenamingMapPath.Equals(renamingMapFilePath, StringComparison.OrdinalIgnoreCase))
                return;

            m_allObjects = new Lazy<List<ClrObject>>(() => Heap.EnumerateClrObjects().ToList());
            m_deobfuscator = new Deobfuscator(renamingMapFilePath);
        }

        public ITypeDeobfuscator GetTypeDeobfuscator(string typeName)
        {
            if (m_deobfuscator == null)
                return new DummyTypeDeobfuscator(typeName);

            return m_deobfuscator.GetTypeDeobfuscator(typeName);
        }

        public ITypeDeobfuscator GetTypeDeobfuscator(ClrType type)
        {
            if (m_deobfuscator == null)
                return new DummyTypeDeobfuscator(type.Name);

            return m_deobfuscator.GetTypeDeobfuscator(type);
        }

        public string DeobfuscateType(string obfuscatedTypeName)
        {
            if (m_deobfuscator == null)
                return obfuscatedTypeName;

            return m_deobfuscator.DeobfuscateType(obfuscatedTypeName);
        }

        public string ObfuscateType(string deobfuscatedTypeName)
        {
            if (m_deobfuscator == null)
                return deobfuscatedTypeName;

            return m_deobfuscator.ObfuscateType(deobfuscatedTypeName);
        }

        public IEnumerable<ClrObject> GetReferenceBy(ClrObject o)
        {
            return m_referenceMap.GetReferenceBy(o);
        }

        ~ClrMDSession()
        {
            Target.Dispose();
        }

        public void Dispose()
        {
            Target.Dispose();

            s_currentSession = null;
            s_lastDumpPath = null;
            GC.SuppressFinalize(this);
        }

        public static void RunInMTA(Action action)
        {
            Exception exception = null;
            Thread t = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();

            t.Join();

            if (exception != null)
                throw exception;
        }
    }
}