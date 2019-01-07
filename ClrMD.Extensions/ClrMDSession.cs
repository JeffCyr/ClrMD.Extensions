using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
        private static int? s_lastProcessId;

        private Lazy<ReadOnlySegmentedCollection<ClrDynamic>> m_allObjects;
        private ReferenceMap m_referenceMap;
        private Deobfuscator m_deobfuscator;

        public static ClrMDSession Current => s_currentSession;

        public DataTarget Target { get; }
        public ClrRuntime Runtime { get; }
        public ClrHeap Heap { get; }

        public IEnumerable<ClrDynamic> AllObjects => m_allObjects.Value;

        public bool IsReferenceMappingCreated { get; private set; }

        internal ClrType ErrorType { get; private set; }

        private ClrMDSession(DataTarget target, string dacFile)
        {
            ClrMDSession.Detach();

            if (target.ClrVersions.Count == 0)
                throw new ArgumentException("DataTarget has no clr loaded.", nameof(target));

            Target = target;
            Runtime = dacFile == null ? target.ClrVersions[0].CreateRuntime() : target.ClrVersions[0].CreateRuntime(dacFile);
            Heap = Runtime.Heap;

            //Temp hack until ErrorType is made public
            var property = Heap.GetType().GetProperty("ErrorType", BindingFlags.Instance | BindingFlags.NonPublic);

            if (property == null)
                throw new InvalidOperationException("Unable to find 'ErrorType' property on ClrHeap.");

            ErrorType = (ClrType)property.GetValue(Heap);

            m_allObjects = CreateLazyAllObjects();

            s_currentSession = this;
        }

        private Lazy<ReadOnlySegmentedCollection<ClrDynamic>> CreateLazyAllObjects()
        {
            return new Lazy<ReadOnlySegmentedCollection<ClrDynamic>>(() => new ReadOnlySegmentedCollection<ClrDynamic>(Heap.EnumerateDynamicObjects()));
        }

        public static ClrMDSession LoadCrashDump(string dumpPath, string dacFile = null)
        {
            if (s_currentSession != null && s_lastDumpPath == dumpPath)
            {
                TestInvalidComObjectException();
                return s_currentSession;
            }

            Detach();

            DataTarget target = DataTarget.LoadCrashDump(dumpPath);

            try
            {
                if (target.Architecture == Architecture.X86 && Environment.Is64BitProcess ||
                    target.Architecture == Architecture.Amd64 && !Environment.Is64BitProcess)
                {
                    throw new InvalidOperationException("Mismatched architecture between this process and the target dump.");
                }
            }
            catch
            {
                target.Dispose();
                throw;
            }

            s_lastDumpPath = dumpPath;
            return new ClrMDSession(target, dacFile);
        }

        public static ClrMDSession AttachToProcess(string processName, uint millisecondsTimeout = 5000, AttachFlag attachFlag = AttachFlag.Invasive)
        {
            Process p = Process.GetProcessesByName(processName).FirstOrDefault();

            if (p == null)
                throw new ArgumentException("Process not found", "processName");

            return AttachToProcess(p, millisecondsTimeout, attachFlag);
        }

        public static ClrMDSession AttachToProcess(int pid, uint millisecondsTimeout = 5000, AttachFlag attachFlag = AttachFlag.Invasive)
        {
            Process p = Process.GetProcessById(pid);

            if (p == null)
                throw new ArgumentException("Process not found", "pid");

            return AttachToProcess(p, millisecondsTimeout, attachFlag);
        }

        public static ClrMDSession AttachToProcess(Process p, uint millisecondsTimeout = 5000, AttachFlag attachFlag = AttachFlag.Invasive)
        {
            if (s_currentSession != null && s_lastProcessId == p.Id)
            {
                TestInvalidComObjectException();
                return s_currentSession;
            }

            Detach();

            DataTarget target = DataTarget.AttachToProcess(p.Id, millisecondsTimeout, attachFlag);
            s_lastProcessId = p.Id;
            return new ClrMDSession(target, null);
        }

        public static ClrMDSession AttachWithSnapshot(string processName)
        {
            Process p = Process.GetProcessesByName(processName).FirstOrDefault();

            if (p == null)
                throw new ArgumentException("Process not found", "processName");

            return AttachWithSnapshot(p);
        }

        public static ClrMDSession AttachWithSnapshot(int pid)
        {
            Process p = Process.GetProcessById(pid);

            if (p == null)
                throw new ArgumentException("Process not found", "pid");

            return AttachWithSnapshot(p);
        }

        public static ClrMDSession AttachWithSnapshot(Process p)
        {
            if (s_currentSession != null && s_lastProcessId == p.Id)
            {
                TestInvalidComObjectException();
                return s_currentSession;
            }

            Detach();

            DataTarget target = DataTarget.CreateSnapshotAndAttach(p.Id);
            s_lastProcessId = p.Id;
            return new ClrMDSession(target, null);
        }

        public IEnumerable<ClrDynamic> EnumerateDynamicObjects(ClrType type)
        {
            return AllObjects.Where(item => item.Type == type);
        }

        public IEnumerable<ClrDynamic> EnumerateDynamicObjects(string typeName)
        {
            if (!typeName.Contains("*"))
                return AllObjects.Where(item => item.TypeName == typeName);

            var regex = new Regex($"^{Regex.Escape(typeName).Replace("\\*", ".*")}$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            var types = 
                from type in Heap.EnumerateTypes()
                let deobfuscator = GetTypeDeobfuscator(type)
                where regex.IsMatch(deobfuscator.OriginalName)
                select type;

            var typeSet = new HashSet<ClrType>(types);

            return AllObjects.Where(o => typeSet.Contains(o.Type));
        }

        public IEnumerable<ClrDynamic> EnumerateDynamicObjects(params ClrType[] types)
        {
            return EnumerateDynamicObjects((IEnumerable<ClrType>)types);
        }

        public IEnumerable<ClrDynamic> EnumerateDynamicObjects(IEnumerable<ClrType> types)
        {
            if (types == null)
                return EnumerateDynamicObjects();

            IList<ClrType> castedTypes = types as IList<ClrType> ?? types.ToList();

            if (castedTypes.Count == 0)
                return EnumerateDynamicObjects();

            if (castedTypes.Count == 1)
                return EnumerateDynamicObjects(castedTypes[0]);

            HashSet<ClrType> set = new HashSet<ClrType>(castedTypes);

            return AllObjects.Where(o => set.Contains(o.Type));
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

            if (m_allObjects.IsValueCreated)
                m_allObjects = CreateLazyAllObjects();

            m_deobfuscator = new Deobfuscator(renamingMapFilePath);
        }

        public ITypeDeobfuscator GetTypeDeobfuscator(string typeName)
        {
            if (m_deobfuscator == null)
                return DummyTypeDeobfuscator.GetDeobfuscator(typeName);

            return m_deobfuscator.GetTypeDeobfuscator(typeName);
        }

        public ITypeDeobfuscator GetTypeDeobfuscator(ClrType type)
        {
            if (m_deobfuscator == null)
                return DummyTypeDeobfuscator.GetDeobfuscator(type.Name);

            return m_deobfuscator.GetTypeDeobfuscator(type);
        }


        public string DeobfuscateStack(string obfuscatedStackTrace)
        {
            if (m_deobfuscator == null)
                return obfuscatedStackTrace;

            return m_deobfuscator.DeobfuscateCallstack(obfuscatedStackTrace);
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


        internal TypeName ObfuscateType(TypeName deobfuscatedTypeName)
        {
            if (m_deobfuscator == null)
                return deobfuscatedTypeName;

            return m_deobfuscator.ObfuscateType(deobfuscatedTypeName);
        }

        public IEnumerable<ClrDynamic> GetReferenceBy(ClrDynamic o)
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
            s_lastProcessId = null;

            GC.SuppressFinalize(this);
        }

        public static void Detach()
        {
            if (s_currentSession != null)
                s_currentSession.Dispose();
        }

        private static void TestInvalidComObjectException()
        {
            if (s_currentSession == null)
                return;

            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                try
                {
                    byte[] dummy = new byte[8];
                    int bytesRead;
                    s_currentSession.Runtime.ReadMemory(0, dummy, 8, out bytesRead);
                }
                catch (System.Runtime.InteropServices.InvalidComObjectException ex)
                {
                    const string msg = @"It looks like ClrMDSession was created from another STA Thread. 
If you are using LINQPad, change this setting: 
LINQPad Menu -> Edit -> Preferences -> Advanced -> Run Queries in MTA Threads = True";

                    throw new InvalidOperationException(msg, ex);
                }
            }
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