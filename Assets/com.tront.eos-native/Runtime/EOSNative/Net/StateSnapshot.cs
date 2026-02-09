using UnityEngine;

namespace EOSNative.Net
{
    /// <summary>
    /// Immutable snapshot of an object's physics state at a specific tick.
    /// Used by <see cref="NetworkPrediction"/> and <see cref="LagCompensation"/> for
    /// history recording, prediction correction, and lag-compensated hit detection.
    /// </summary>
    public struct StateSnapshot
    {
        public uint Tick;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
    }

    /// <summary>
    /// Fixed-capacity ring buffer storing recent <see cref="StateSnapshot"/> entries.
    /// O(1) record and O(1) lookup by tick. Oldest entries are overwritten when full.
    /// Default capacity of 64 entries stores ~2.1 seconds at 30 ticks/sec.
    /// </summary>
    public class StateHistory
    {
        private readonly StateSnapshot[] _buffer;
        private readonly int _capacity;
        private int _head;   // next write index
        private int _count;

        /// <summary>Number of recorded snapshots (0..capacity).</summary>
        public int Count => _count;

        /// <summary>Tick of the most recently recorded snapshot, or 0 if empty.</summary>
        public uint NewestTick => _count > 0 ? _buffer[(_head - 1 + _capacity) % _capacity].Tick : 0;

        /// <summary>Tick of the oldest snapshot still in the buffer, or 0 if empty.</summary>
        public uint OldestTick => _count > 0 ? _buffer[(_head - _count + _capacity) % _capacity].Tick : 0;

        public StateHistory(int capacity = 64)
        {
            _capacity = Mathf.Max(1, capacity);
            _buffer = new StateSnapshot[_capacity];
        }

        /// <summary>Record a snapshot, overwriting the oldest entry if at capacity.</summary>
        public void Record(StateSnapshot snapshot)
        {
            _buffer[_head] = snapshot;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity) _count++;
        }

        /// <summary>Try to retrieve the snapshot recorded at exactly the given tick.</summary>
        public bool TryGetAtTick(uint tick, out StateSnapshot snapshot)
        {
            if (_count == 0 || tick < OldestTick || tick > NewestTick)
            {
                snapshot = default;
                return false;
            }

            // O(1) direct index: offset from oldest tick
            uint oldest = OldestTick;
            int offset = (int)(tick - oldest);
            if (offset < 0 || offset >= _count)
            {
                snapshot = default;
                return false;
            }

            int idx = (_head - _count + offset + _capacity) % _capacity;
            var found = _buffer[idx];

            // Verify tick matches (handles wrapping edge cases)
            if (found.Tick == tick)
            {
                snapshot = found;
                return true;
            }

            // Fallback: linear scan (should rarely happen)
            for (int i = 0; i < _count; i++)
            {
                int scanIdx = (_head - _count + i + _capacity) % _capacity;
                if (_buffer[scanIdx].Tick == tick)
                {
                    snapshot = _buffer[scanIdx];
                    return true;
                }
            }

            snapshot = default;
            return false;
        }

        /// <summary>
        /// Interpolate between two snapshots for sub-tick accuracy.
        /// The float tick value is split into floor/ceil ticks and lerped.
        /// Returns the nearest available snapshot if exact ticks aren't found.
        /// </summary>
        public StateSnapshot GetInterpolated(float tickFloat)
        {
            if (_count == 0) return default;

            uint tickA = (uint)Mathf.FloorToInt(tickFloat);
            uint tickB = tickA + 1;
            float t = tickFloat - tickA;

            bool hasA = TryGetAtTick(tickA, out var a);
            bool hasB = TryGetAtTick(tickB, out var b);

            if (hasA && hasB)
            {
                return new StateSnapshot
                {
                    Tick = tickA,
                    Position = Vector3.Lerp(a.Position, b.Position, t),
                    Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t),
                    Velocity = Vector3.Lerp(a.Velocity, b.Velocity, t),
                    AngularVelocity = Vector3.Lerp(a.AngularVelocity, b.AngularVelocity, t)
                };
            }

            if (hasA) return a;
            if (hasB) return b;

            // Clamp to nearest available
            if (tickFloat < OldestTick)
            {
                TryGetAtTick(OldestTick, out var oldest);
                return oldest;
            }

            TryGetAtTick(NewestTick, out var newest);
            return newest;
        }

        /// <summary>Clear all recorded snapshots.</summary>
        public void Clear()
        {
            _head = 0;
            _count = 0;
        }
    }
}
