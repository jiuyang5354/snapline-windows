using System;
using System.Windows;

namespace Snapline
{
    // A damped string, displaced from the existing resting curve. Endpoints stay pinned.
    internal sealed class RopePhysics
    {
        internal const int Count = 33;
        private readonly Vector[] offsets = new Vector[Count];
        private readonly Vector[] velocities = new Vector[Count];
        private readonly Vector[] forces = new Vector[Count];
        private double grip;
        private Vector pull;
        private bool dragging;

        internal void Pull(double fraction, Vector displacement)
        {
            grip = Math.Max(0, Math.Min(1, fraction));
            // Resistance rises with distance; crossing a whole monitor cannot stretch the line away.
            pull = new Vector(8 * Math.Tanh(displacement.X / 120), 12 * Math.Tanh(displacement.Y / 100));
            dragging = true;
        }

        internal void Release() { dragging = false; }

        internal void Kick()
        {
            for (int i = 1; i < Count - 1; i++) velocities[i].Y += 18 * Math.Sin(Math.PI * i / (Count - 1));
        }

        internal Vector Sample(double fraction)
        {
            double index = Math.Max(0, Math.Min(1, fraction)) * (Count - 1);
            int left = Math.Min(Count - 2, (int)index);
            return offsets[left] + (offsets[left + 1] - offsets[left]) * (index - left);
        }

        internal bool Step(double elapsed)
        {
            double remaining = Math.Max(0, Math.Min(.05, elapsed));
            while (remaining > 0)
            {
                double dt = Math.Min(1.0 / 120, remaining);
                for (int i = 1; i < Count - 1; i++)
                {
                    double distance = (double)i / (Count - 1) - grip;
                    Vector target = dragging ? pull * Math.Exp(-distance * distance / .018) : new Vector();
                    // Unit mass, stiffness 100, damping 10; neighbours transmit a small wave.
                    forces[i] = (target - offsets[i]) * 100 - velocities[i] * 10 +
                        (offsets[i - 1] + offsets[i + 1] - offsets[i] * 2) * 100;
                }
                for (int i = 1; i < Count - 1; i++)
                {
                    velocities[i] += forces[i] * dt;
                    offsets[i] += velocities[i] * dt;
                }
                remaining -= dt;
            }
            if (dragging) return true;
            for (int i = 1; i < Count - 1; i++)
                if (offsets[i].Length > .02 || velocities[i].Length > .1) return true;
            Reset();
            return false;
        }

        internal void Reset()
        {
            Array.Clear(offsets, 0, Count);
            Array.Clear(velocities, 0, Count);
            dragging = false;
            pull = new Vector();
        }
    }
}
