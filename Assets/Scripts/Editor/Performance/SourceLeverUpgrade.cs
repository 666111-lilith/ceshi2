using System;
using Palworks.Performance;

namespace Palworks.Editor
{
    // Preserve the old entry point without recreating the retired 3D lever.
    public static class SourceLeverUpgrade
    {
        public static void ApplyTo(PerformanceSequence sequence)
        {
            if (sequence == null || sequence != UnityEngine.Object.FindObjectOfType<PerformanceSequence>())
                throw new InvalidOperationException("Upgrade the active performance scene.");
            FlatLeverRepair.Apply();
        }
    }
}