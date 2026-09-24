using System;

namespace WalkThroughBushes
{
    internal static class BushRules
    {
        internal static bool IsBushGroup(string value)
        {
            return string.Equals(value, "bushes", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "collectable_bushes", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool LooksLikeBush(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            string name = value.ToLowerInvariant();
            return name == "bush" ||
                   name.StartsWith("bush_") ||
                   name.StartsWith("s_bush_") ||
                   name.Contains("_bush_") ||
                   name.EndsWith("_bush") ||
                   name.StartsWith("bush(");
        }
    }
}
