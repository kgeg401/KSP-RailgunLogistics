using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RailgunSandbox;

string root=Environment.GetEnvironmentVariable("RAILGUN_STATUS_TEST_ROOT")??throw new Exception("Choose a new private RAILGUN_STATUS_TEST_ROOT");
if(!Path.IsPathRooted(root)||Directory.Exists(root))throw new Exception("Use a fresh absolute fixture path");
Directory.CreateDirectory(root);string path=Path.Combine(root,"status.txt");
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
string Report(int i)=>"network=Running scene=FLIGHT\nsequence="+i+"\n"+new string('x',16384)+"\nEND\n";
Check(AtomicStatusFile.TryWrite(path,Report(0)),"First status is published completely");
using(var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
    Check(!AtomicStatusFile.TryWrite(path,Report(1)),"A Windows reader lock defers publication without blocking the flight");
    Check(File.ReadAllText(path)==Report(0),"Deferred publication preserves the exact previous complete report");
}
Check(AtomicStatusFile.TryWrite(path,Report(2))&&File.ReadAllText(path)==Report(2),"Publication recovers after the reader releases its lock");
int done=0,reads=0;
var reader=Task.Run(()=>{while(Volatile.Read(ref done)==0){try {string text=File.ReadAllText(path);if(!text.StartsWith("network=Running scene=FLIGHT\n")||!text.EndsWith("\nEND\n"))throw new Exception("Partial native status became visible");Interlocked.Increment(ref reads);}catch(IOException){}}});
try {for(int i=3;i<503;i++){AtomicStatusFile.TryWrite(path,Report(i));if(i%10==0)Thread.Sleep(1);}}
finally {Volatile.Write(ref done,1);}
reader.GetAwaiter().GetResult();
Check(reads>0&&AtomicStatusFile.TryWrite(path,Report(503))&&File.ReadAllText(path)==Report(503),"Concurrent reader sees complete reports and final publication remains possible");
Console.WriteLine("PASS 5 atomic status scenarios; native-flight evidence remains separate. Reads="+reads);
