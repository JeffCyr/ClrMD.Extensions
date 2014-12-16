ClrMD
================

ClrMD is a .Net library used to collect information from a live process or a memory dump file. It's like Windbg/sos.dll,
but instead of cryptic commands you get a fully object oriented API to collect and combine data.

Here is some resources to get you started:
 - http://blogs.msdn.com/b/dotnet/archive/2013/05/01/net-crash-dump-and-live-process-inspection.aspx
 - https://github.com/Microsoft/dotnetsamples/tree/master/Microsoft.Diagnostics.Runtime/CLRMD


ClrMD.Extensions
================

The goal of this library is to provide integration with LINPad and to make ClrMD even more easy to use.

ClrMDSession
----------------

`ClrMDSession` will take care of the initialization of ClrMD.

```c#
int pid = Process.GetProcessesByName("HelloWorld")[0].Id;
using (DataTarget dataTarget = DataTarget.AttachToProcess(pid, 5000))
{
  string dacLocation = dataTarget.ClrVersions[0].TryGetDacLocation();
  ClrRuntime runtime = dataTarget.CreateRuntime(dacLocation);

  // ...
}
```

will become

```c#
using (ClrMDSession session = ClrMDSession.AttachToProcess("HelloWorld"))
{
  // ...
}
```

or this for a memory dump file

```c#
using (ClrMDSession session = ClrMDSession.LoadCrashDump(@"C:\Dumps\crash.dmp"))
{
  // ...
}
```

Note that with `ClrMDSession`, the mscordac is downloaded automatically from Microsoft symbol servers
if it can't be found locally.

The ClrRuntime and ClrHeap are accessible from ClrMDSession's properties.


ClrObject
----------------

In ClrMD, you typically always access object's properties by its ClrType and object address.

```c#
ClrHeap heap = runtime.GetHeap();
foreach (ulong obj in heap.EnumerateObjects())
{
    ClrType type = heap.GetObjectType(obj);
    ulong size = type.GetSize(obj);
    Console.WriteLine("Address:{0} Size:{1} Type:{2}", obj, size, type.Name);
}
```

`ClrObject` is the object abstraction missing from ClrMD, it allows you to do this

```c#
foreach (ClrObject obj in session.AllObjects)
{
    Console.WriteLine("Address:{0} Size:{1} Type:{2}", obj.Address, obj.Size, obj.TypeName);
}
```

... more to come
