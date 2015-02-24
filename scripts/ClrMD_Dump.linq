<Query Kind="Program">
  <Reference>C:\Code\GitHub\ClrMD.Extensions\ClrMD.Extensions\bin\Release\ClrMD.Extensions.dll</Reference>
  <Reference>C:\Code\GitHub\ClrMD.Extensions\ClrMD.Extensions\bin\Release\Microsoft.Diagnostics.Runtime.dll</Reference>
  <Namespace>ClrMD.Extensions</Namespace>
  <Namespace>ClrMD.Extensions.LINQPad</Namespace>
  <Namespace>Microsoft.Diagnostics.Runtime</Namespace>
</Query>

void Main()
{
    // ClrMDSession is a singleton and is only created on the first query execution.
    // Calling 'LoadCrashDump' again will return the already initialized ClrMDSession (if it's the same dump path).
    ClrMDSession session = ClrMDSession.LoadCrashDump(@"C:\Dumps\YourDumpFile.dmp");
    
    session.DumpThreads();
    session.DumpHeapStatistics();
}

public static class LocalExtensions
{
    public static void DumpThreads(this ClrMDSession session, bool includeThreadName = true)
    {
        // Get the thread names from the 'Thread' instances of the heap.
        var threadsInfo = from o in session.EnumerateClrObjects("System.Threading.Thread")
                          select new
                          {
                              ManagedThreadId = (int)o.Dynamic.m_ManagedThreadId,
                              Name = (string)o.Dynamic.m_Name
                          };
        
        (   // Join the ClrThreads with their respective thread names
            from t in session.Runtime.Threads
            join info in threadsInfo on t.ManagedThreadId equals info.ManagedThreadId into infoGroup
            let name = infoGroup.Select(item => item.Name).FirstOrDefault() ?? ""
            select new
                {
                    ManagedThreadId = t.ManagedThreadId,
                    Name = name,
                    StackTrace = t.GetStackTrace(),
                    Exception = t.CurrentException,
                    t.BlockingObjects
                }
        ).Dump("Threads", depth:0);
    }
    
    public static void DumpHeapStatistics(this ClrMDSession session)
    {
        (   // Start with all objects
            from o in session.AllObjects 
            // Group by object type.
            group o by o.Type into typeGroup
            // Get the instance count of this type.
            let count = typeGroup.Count()
            // Get the memory usage of all instances of this type
            let totalSize = typeGroup.Sum(item => (double)item.Size)
            // Orderby to get objects with most instance count first
            orderby count descending
            select new
            {
                Type = typeGroup.Key.Name,
                Count = count,
                TotalSize = (totalSize / 1024 / 1024).ToString("0.## MB"),
                // Get the first 100 instances of the type.
                First100Objects = typeGroup.Take(100),
            }
        ).Take(100).Dump("Heap statistics", depth:0);
    }
}
