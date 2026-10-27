using System;
using System.Linq;
using System.Collections.Generic;

namespace RailgunSandbox
{
    public static class DockingPortPolicy
    {
        public static void ValidateHardware(string partName)
        {
            if (partName != "dockingPort2" && partName != "dockingPort3" && partName != "dockingPortLarge")
                throw new InvalidOperationException("Automatic hardware fitting currently supports stock Junior, standard and Sr. ports, including their TweakScale sizes.");
        }
        public static void ValidateToken(string type)
        {
            if (string.IsNullOrEmpty(type) || type.Length > 128 || type.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.'))
                throw new InvalidOperationException("Invalid native docking-interface identifier.");
        }
        public static string SelectType(string nodeTypes, bool gendered, bool snapRotation, IEnumerable<string> installedTypes = null)
        {
            if (gendered) throw new InvalidOperationException("Gendered docking connectors are not supported by this pod coupler.");
            if (snapRotation) throw new InvalidOperationException("Indexed docking connectors require a matching indexed coupler.");
            var types = (nodeTypes ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var available = new HashSet<string>(installedTypes ?? new[] { "size0", "size1", "size2" });
            foreach (string supported in new[] { "size1", "size0", "size2" }.Concat(available.OrderBy(x => x, StringComparer.Ordinal)))
                if (available.Contains(supported) && types.Contains(supported)) { ValidateToken(supported); return supported; }
            throw new InvalidOperationException("This connector has no compatible interface in the installed docking-port definitions.");
        }
        public static string Name(string type) => type == "size0" ? "Junior (0.625 m)" : type == "size1" ? "standard (1.25 m)" :
            type == "size2" ? "Sr. (2.5 m)" : "installed interface " + type;
    }
}
