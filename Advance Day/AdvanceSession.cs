using System;

namespace AdvanceDay
{
    internal enum AdvanceResult
    {
        Running,
        Complete,
        Stalled,
        ClockMovedBackward
    }

    internal sealed class AdvanceSession
    {
        private readonly double start;
        private readonly double target;
        private double last;
        private float lastProgressAt;

        internal int TargetDay { get; }
        internal float TargetTimeOfDay { get; }

        internal int ProgressPercent
        {
            get
            {
                double length = Math.Max(0.000001d, target - start);
                return (int)Math.Max(0d, Math.Min(100d, 100d * (last - start) / length));
            }
        }

        internal AdvanceSession(int day, float timeOfDay, float targetTimeOfDay, float realtime)
        {
            TargetDay = checked(day + 1);
            TargetTimeOfDay = targetTimeOfDay;
            start = last = day + (double)timeOfDay;
            target = TargetDay + (double)targetTimeOfDay;
            lastProgressAt = realtime;
        }

        internal AdvanceResult Observe(int day, float timeOfDay, float realtime)
        {
            if (float.IsNaN(timeOfDay) || float.IsInfinity(timeOfDay) || timeOfDay < 0f || timeOfDay > 1f)
                return AdvanceResult.ClockMovedBackward;

            double current = day + (double)timeOfDay;
            if (current < last - 0.001d)
                return AdvanceResult.ClockMovedBackward;

            if (current > last + 0.0000001d)
            {
                last = current;
                lastProgressAt = realtime;
            }

            if (day > TargetDay || (day == TargetDay && timeOfDay >= TargetTimeOfDay))
                return AdvanceResult.Complete;

            if (realtime - lastProgressAt >= 15f)
                return AdvanceResult.Stalled;

            return AdvanceResult.Running;
        }
    }
}
