using System.Collections.ObjectModel;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using FaultWitness.Core;

namespace FaultWitness.Platform.Windows;
public sealed record WindowsInventoryRow(IReadOnlyDictionary<string, object?> Values);
public interface IWindowsInventoryDataSource { Task<IReadOnlyList<WindowsInventoryRow>> QueryAsync(string wmiNamespace,string query,CancellationToken token); Task<IReadOnlyDictionary<string,object?>> ReadRegistryAsync(string subKey,IReadOnlyList<string> names,CancellationToken token); }

public sealed class WindowsSystemInventory
{
 public const string InventoryGroupOperatingSystem="InventoryGroupOperatingSystem", InventoryGroupProcessorMemory="InventoryGroupProcessorMemory", InventoryGroupGraphics="InventoryGroupGraphics", InventoryGroupFirmware="InventoryGroupFirmware", InventoryGroupStorage="InventoryGroupStorage", InventoryGroupDrivers="InventoryGroupDrivers";
 private readonly IWindowsInventoryDataSource source;
 public WindowsSystemInventory(IWindowsInventoryDataSource? source=null)=>this.source=source??new ManagementInventoryDataSource();
 public async Task<SystemInventorySnapshot> GetAsync(CancellationToken token)
 {
  token.ThrowIfCancellationRequested();
  var os=Read(InventoryGroupOperatingSystem,"os",["InventoryOperatingSystem","InventoryDisplayVersion","InventoryBuildNumber","InventoryArchitecture","InventoryProcessArchitecture"],"SELECT Caption,BuildNumber,OSArchitecture,Version FROM Win32_OperatingSystem",r=>[V("InventoryOperatingSystem",r,"Caption"),V("InventoryBuildNumber",r,"BuildNumber"),V("InventoryArchitecture",r,"OSArchitecture"),new InventoryField("InventoryProcessArchitecture",RuntimeInformation.ProcessArchitecture.ToString())],token);
  var cpu=Read(InventoryGroupProcessorMemory,"processor",["InventoryProcessor","InventoryProcessorCores","InventoryProcessorLogical","InventoryMemoryGiB","InventoryComputerManufacturer","InventoryComputerModel"],"SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor",r=>[V("InventoryProcessor",r,"Name"),V("InventoryProcessorCores",r,"NumberOfCores"),V("InventoryProcessorLogical",r,"NumberOfLogicalProcessors")],token);
  var computer=Query("SELECT Manufacturer,Model,TotalPhysicalMemory FROM Win32_ComputerSystem",token);
  var gpu=Read(InventoryGroupGraphics,"gpu",["InventoryGraphicsName","InventoryGraphicsVendor","InventoryGraphicsDriverVersion","InventoryGraphicsDriverDate"],"SELECT Name,AdapterCompatibility,DriverVersion,DriverDate FROM Win32_VideoController",r=>[V("InventoryGraphicsName",r,"Name"),V("InventoryGraphicsVendor",r,"AdapterCompatibility"),V("InventoryGraphicsDriverVersion",r,"DriverVersion"),Date("InventoryGraphicsDriverDate",r,"DriverDate")],token);
  var board=Read(InventoryGroupFirmware,"board",["InventoryBoardManufacturer","InventoryBoardModel","InventoryBiosVendor","InventoryBiosVersion","InventoryBiosDate"],"SELECT Manufacturer,Product FROM Win32_BaseBoard",r=>[V("InventoryBoardManufacturer",r,"Manufacturer"),V("InventoryBoardModel",r,"Product")],token);
  var bios=Query("SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate FROM Win32_BIOS",token);
  var disk=Read(InventoryGroupStorage,"disk",["InventoryStorageModel","InventoryStorageCapacityGiB","InventoryStorageMedia","InventoryStorageBus","InventoryStorageFirmware"],"SELECT Model,Size,MediaType,InterfaceType,FirmwareRevision FROM Win32_DiskDrive",r=>[V("InventoryStorageModel",r,"Model"),Size("InventoryStorageCapacityGiB",r,"Size"),V("InventoryStorageMedia",r,"MediaType"),V("InventoryStorageBus",r,"InterfaceType"),V("InventoryStorageFirmware",r,"FirmwareRevision")],token);
  var drivers=Read(InventoryGroupDrivers,"driver",["InventoryDriverName","InventoryDriverProvider","InventoryDriverVersion","InventoryDriverDate","InventoryDriverCategory"],"SELECT DeviceName,DriverProviderName,DriverVersion,DriverDate,DeviceClass FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL AND (DeviceClass='DISPLAY' OR DeviceClass='MEDIA' OR DeviceClass='SCSIAdapter' OR DeviceClass='HDC')",r=>[V("InventoryDriverName",r,"DeviceName"),V("InventoryDriverProvider",r,"DriverProviderName"),V("InventoryDriverVersion",r,"DriverVersion"),Date("InventoryDriverDate",r,"DriverDate"),V("InventoryDriverCategory",r,"DeviceClass")],token);
  await Task.WhenAll(os,cpu,computer,gpu,board,bios,disk,drivers);
  return new SystemInventorySnapshot([Put(Put(await Registry(await os,token),"InventoryArchitecture",RuntimeInformation.OSArchitecture.ToString()),"InventoryProcessArchitecture",RuntimeInformation.ProcessArchitecture.ToString()),Computer(await cpu,await computer),await gpu,Bios(await board,await bios),await disk,await drivers]);
 }
 private async Task<InventoryGroup> Read(string key, string id, string[] expected, string query, Func<WindowsInventoryRow,IEnumerable<InventoryField>> map, CancellationToken token)
 {
  var result = await Query(query, token).ConfigureAwait(false);
  return result.Rows.Count == 0 ? Fail(key,id,expected,result.State == InventoryAvailability.Available ? InventoryAvailability.Unavailable : result.State)
   : new(key,result.Rows.Select((r,i)=>new InventoryDevice(id+i,Complete(map(r),expected))).ToList());
 }
 private sealed record QueryResult(IReadOnlyList<WindowsInventoryRow> Rows, InventoryAvailability State);
 private async Task<QueryResult> Query(string query,CancellationToken token)
 {
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
  timeout.CancelAfter(TimeSpan.FromSeconds(5));
  try { var rows=await source.QueryAsync("root\\CIMV2",query,timeout.Token).WaitAsync(TimeSpan.FromSeconds(5),token).ConfigureAwait(false); return new(rows.Take(64).ToArray(),InventoryAvailability.Available); }
  catch(OperationCanceledException) when(token.IsCancellationRequested) { throw; }
  catch(Exception e) when(Expected(e)) { return new([],FailureState(e)); }
 }
 private static bool Expected(Exception e) => e is OperationCanceledException or TimeoutException or UnauthorizedAccessException or SecurityException or PlatformNotSupportedException or ManagementException or COMException or IOException or InvalidOperationException;
 private static InventoryAvailability FailureState(Exception e) => e switch {
  UnauthorizedAccessException or SecurityException => InventoryAvailability.AccessDenied,
  ManagementException { ErrorCode: ManagementStatus.AccessDenied } => InventoryAvailability.AccessDenied,
  COMException c when (c.HResult & 0xffff) is 5 or 0x1003 => InventoryAvailability.AccessDenied,
  PlatformNotSupportedException => InventoryAvailability.NotSupported,
  _ => InventoryAvailability.Unavailable
 };
 private async Task<InventoryGroup> Registry(InventoryGroup g,CancellationToken token)
 {
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
  try { var values=await source.ReadRegistryAsync("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion",["DisplayVersion"],timeout.Token).WaitAsync(TimeSpan.FromSeconds(5),token).ConfigureAwait(false); return Put(g,"InventoryDisplayVersion",values.TryGetValue("DisplayVersion",out var v)?v?.ToString():null); }
  catch(OperationCanceledException) when(token.IsCancellationRequested) { throw; }
  catch(Exception e) when(Expected(e)) { return Put(g,"InventoryDisplayVersion",null,FailureState(e)); }
 }
 private static InventoryGroup Computer(InventoryGroup g,QueryResult result)
 {
  var r=result.Rows.Count == 0 ? null : result.Rows[0];
  return Put(Put(Put(g,"InventoryComputerManufacturer",r is null?null:S(r,"Manufacturer"),result.State),"InventoryComputerModel",r is null?null:S(r,"Model"),result.State),"InventoryMemoryGiB",r is null?null:Size("x",r,"TotalPhysicalMemory").Value,result.State);
 }
 private static InventoryGroup Bios(InventoryGroup g,QueryResult result)
 {
  var r=result.Rows.Count == 0 ? null : result.Rows[0];
  return Put(Put(Put(g,"InventoryBiosVendor",r is null?null:S(r,"Manufacturer"),result.State),"InventoryBiosVersion",r is null?null:S(r,"SMBIOSBIOSVersion"),result.State),"InventoryBiosDate",r is null?null:Date("x",r,"ReleaseDate").Value,result.State);
 }
 private static InventoryGroup Put(InventoryGroup g,string k,string? v,InventoryAvailability a=InventoryAvailability.Available)
 {
  var f=new InventoryField(k,string.IsNullOrWhiteSpace(v)?null:v,a==InventoryAvailability.Available&&string.IsNullOrWhiteSpace(v)?InventoryAvailability.Unavailable:a);
  return new(g.Key,g.Devices.Select(d=>new InventoryDevice(d.Id,d.Fields.Any(x=>x.Key==k)?d.Fields.Select(x=>x.Key==k?f:x).ToArray():[..d.Fields,f])).ToArray());
 }
 private static List<InventoryField> Complete(IEnumerable<InventoryField> fs,string[] es){var d=fs.ToDictionary(x=>x.Key,StringComparer.OrdinalIgnoreCase);return es.Select(k=>d.TryGetValue(k,out var f)?f:new InventoryField(k,null,InventoryAvailability.Unavailable)).ToList();}
 private static InventoryGroup Fail(string k,string id,string[] fs,InventoryAvailability a)=>new(k,[new InventoryDevice(id,fs.Select(f=>new InventoryField(f,null,a)).ToList())]);
 private static string? S(WindowsInventoryRow r,string k)=>r.Values.TryGetValue(k,out var x)?x?.ToString():null;
 private static InventoryField V(string k,WindowsInventoryRow r,string s){var v=S(r,s);return new(k,string.IsNullOrWhiteSpace(v)?null:v,string.IsNullOrWhiteSpace(v)?InventoryAvailability.Unavailable:InventoryAvailability.Available);}
 private static InventoryField Size(string k,WindowsInventoryRow r,string s){var v=S(r,s);return long.TryParse(v,NumberStyles.Integer,CultureInfo.InvariantCulture,out var n)&&n>0?new(k,(n/1073741824d).ToString("0.##",CultureInfo.InvariantCulture)):new(k,null,InventoryAvailability.Unavailable);}
 private static InventoryField Date(string k,WindowsInventoryRow r,string s)
 {
  var v=S(r,s);
  return v is {Length:>=8} && DateTime.TryParseExact(v[..8],"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)
   ?new(k,d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)):new(k,null,InventoryAvailability.Unavailable);
 }
}
internal sealed class ManagementInventoryDataSource:IWindowsInventoryDataSource
{
 public async Task<IReadOnlyList<WindowsInventoryRow>> QueryAsync(string ns,string q,CancellationToken t)=>await Task.Run(()=>{if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();t.ThrowIfCancellationRequested();using var s=new ManagementObjectSearcher(new ManagementScope(ns),new ObjectQuery(q),new System.Management.EnumerationOptions{Timeout=TimeSpan.FromSeconds(3)});using var rs=s.Get();var names=q[(q.IndexOf("SELECT",StringComparison.OrdinalIgnoreCase)+6)..q.IndexOf("FROM",StringComparison.OrdinalIgnoreCase)].Split(',').Select(x=>x.Trim()).ToArray();var rows=new List<WindowsInventoryRow>();foreach(ManagementObject i in rs){using(i)rows.Add(new(new ReadOnlyDictionary<string,object?>(names.ToDictionary(n=>n,n=>(object?)i[n]))));if(rows.Count>=64)break;t.ThrowIfCancellationRequested();}return(IReadOnlyList<WindowsInventoryRow>)rows;},t);
 public async Task<IReadOnlyDictionary<string,object?>> ReadRegistryAsync(string sub,IReadOnlyList<string> ns,CancellationToken t)=>await Task.Run(()=>{if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();t.ThrowIfCancellationRequested();using var r=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);using var k=r.OpenSubKey(sub,false);return k is null?(IReadOnlyDictionary<string,object?>)new Dictionary<string,object?>():ns.ToDictionary(n=>n,n=>(object?)k.GetValue(n));},t);
}
