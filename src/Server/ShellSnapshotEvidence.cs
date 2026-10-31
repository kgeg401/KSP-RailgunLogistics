using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace RailgunLogisticsServer
{
    public static class ShellSnapshotEvidence
    {
        public static bool ExactAssembly(string saved, IEnumerable<uint> originals, uint shellId)
        {
            if (saved == null || originals == null || shellId == 0) return false;
            var original = originals.ToArray();
            if (original.Length == 0 || original.Any(id => id == 0) || original.Distinct().Count() != original.Length || original.Contains(shellId)) return false;
            var matches = Regex.Matches(saved, @"(?m)^\s*uid\s*=\s*(\d+)\s*$").Cast<Match>().ToArray();
            var actual = new List<uint>();
            foreach (var match in matches) {
                if (!uint.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out uint id)) return false;
                actual.Add(id);
            }
            return actual.Count == original.Length + 1 && actual.Distinct().Count() == actual.Count &&
                actual.OrderBy(id => id).SequenceEqual(original.Concat(new[] { shellId }).OrderBy(id => id));
        }
    }
}
