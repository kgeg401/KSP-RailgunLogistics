using System;
using System.IO;
using System.Text;

namespace RailgunSandbox
{
    // Single main-thread writer per status path. Readers see a complete old or
    // new report. A locked file keeps the last report until a later update;
    // never wait on file locks inside the flight update.
    public static class AtomicStatusFile
    {
        public static bool TryWrite(string path, string text)
        {
            string pending = path + ".pending";
            try {
                File.WriteAllText(pending, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(pending, path, null);
                else File.Move(pending, path);
                return true;
            } catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
