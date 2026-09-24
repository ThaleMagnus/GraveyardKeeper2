using System;

namespace FurnaceSpeed
{
    internal static class FurnaceRules
    {
        internal static bool ContainsFurnaceName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return value.IndexOf("furnace", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("melting_furnace", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsFurnace(CraftComponent craft)
        {
            if (craft == null || craft.CraftableObject == null)
                return false;

            ICraftable craftable = craft.CraftableObject;
            if (ContainsFurnaceName(craftable.CraftableObjectId))
                return true;

            WgoData wgo = craftable as WgoData;
            if (wgo == null || wgo.Definition == null)
                return false;

            return ContainsFurnaceName(wgo.Definition.wgoGroup) ||
                   ContainsFurnaceName(wgo.Definition.customVisualId) ||
                   ContainsFurnaceName(wgo.Definition.devLabelStr);
        }

        internal static float ScaleDelta(float delta, float multiplier)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta <= 0f)
                return delta;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
                multiplier = 1f;

            multiplier = Math.Max(1f, Math.Min(10f, multiplier));
            float scaled = delta * multiplier;
            return float.IsNaN(scaled) || float.IsInfinity(scaled) ? delta : scaled;
        }
    }
}
