using System;
using System.IO;
using RailgunLogisticsServer;

int count=0;
void Check(bool actual,string message) {if(!actual)throw new Exception(message);count++;Console.WriteLine("PASS "+message);}
const string complete="PART\n{\n\tuid = 12\n}\nPART\n{\n\tuid = 13\n}\nPART\n{\n\tuid = 99\n}\n";
var original=new uint[]{12,13};
Check(ShellSnapshotEvidence.ExactAssembly(complete,original,99),"Exact original assembly plus replacement shell is durable evidence");
Check(ShellSnapshotEvidence.ExactAssembly(complete.Replace("\n","\r\n"),original,99),"Native Windows newlines preserve exact identity checks");
Check(!ShellSnapshotEvidence.ExactAssembly(complete.Replace("uid = 99","uid = 98"),original,99),"Old or different shell ID cannot acknowledge replacement");
Check(!ShellSnapshotEvidence.ExactAssembly(complete.Replace("uid = 99","uid = 12"),original,99),"Duplicate native IDs are rejected");
Check(!ShellSnapshotEvidence.ExactAssembly(complete.Replace("uid = 13","uid = 14"),original,99),"Replacement of an original structural part is rejected");
Check(!ShellSnapshotEvidence.ExactAssembly(complete+"PART\n{\nuid = 100\n}\n",original,99),"Unexpected additional hardware cannot be hidden in a durable refit");
Check(!ShellSnapshotEvidence.ExactAssembly(complete.Replace("uid = 99","uid = 4294967296"),original,99),"Overflowing native identity is refused without an exception");
Check(!ShellSnapshotEvidence.ExactAssembly("",original,99),"Truncated empty file is not a saved assembly");
Check(!ShellSnapshotEvidence.ExactAssembly(complete,new uint[]{12,12},99)&&!ShellSnapshotEvidence.ExactAssembly(complete,original,12),"Invalid expected identities cannot bypass conservation checks");
var native=Environment.GetEnvironmentVariable("RAILGUN_NATIVE_SHELL_SNAPSHOT");
if(!string.IsNullOrEmpty(native)) {
    var expected=Environment.GetEnvironmentVariable("RAILGUN_NATIVE_ORIGINAL_IDS").Split(',');
    var nativeIds=Array.ConvertAll(expected,uint.Parse);
    var shell=uint.Parse(Environment.GetEnvironmentVariable("RAILGUN_NATIVE_SHELL_ID"));
    var text=File.ReadAllText(native);
    Check(ShellSnapshotEvidence.ExactAssembly(text,nativeIds,shell),"Actual native KSP/Luna shell snapshot retains exact original and shell IDs");
    Check(!ShellSnapshotEvidence.ExactAssembly(text,nativeIds,shell+1),"Actual snapshot refuses an unobserved replacement identity");
}
Console.WriteLine($"PASS {count} durable shell snapshot checks. Native attachment remains a separate validation.");
