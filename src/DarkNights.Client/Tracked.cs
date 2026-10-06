namespace DarkNights.Client
{
    /// <summary>
    /// A value this mod scales without owning. The rule that keeps it from fighting the game
    /// or another mod over the same setting: whatever is in the field is the base, unless it
    /// is exactly what this mod last wrote. So
    ///
    ///   - the game rewriting it every frame is picked up as a new base every frame;
    ///   - the game rewriting it only now and then (TOD_Sky's light, every UpdateInterval) is
    ///     picked up when it happens, and the scale is not applied on top of itself between;
    ///   - another mod setting it is adopted as the base, and the night is applied on top.
    ///
    /// Pure, and compiled into the tests.
    /// </summary>
    public sealed class Tracked
    {
        private bool _haveWritten;
        private float _written;

        /// <summary>The value the field holds when nothing of ours is in it.</summary>
        public float Base { get; private set; }

        /// <summary>
        /// How many times something else has written the field since we started. For a field
        /// the game rewrites on a timer this climbs at that rate; for one nothing else should
        /// touch (the exposure ceiling) any climb means another writer, and the log says so.
        /// </summary>
        public int BaseChanges { get; private set; }

        /// <summary>Returns what to write. Call with the field's current value, every time.</summary>
        public float Apply(float current, float multiplier)
        {
            Adopt(current);
            return Write(Base * multiplier);
        }

        /// <summary>
        /// Apply in two steps, for a change that is not a multiplier -- a ceiling, say: Adopt
        /// the field's current value and get the base back, then Write what goes in. (Two steps
        /// rather than a callback, which would allocate a closure every frame.)
        /// </summary>
        public float Adopt(float current)
        {
            if (!_haveWritten || current != _written)
            {
                if (_haveWritten)
                {
                    BaseChanges++;
                }

                Base = current;
            }

            return Base;
        }

        /// <summary>Records what is about to be written, and returns it.</summary>
        public float Write(float value)
        {
            _written = value;
            _haveWritten = true;
            return value;
        }

        /// <summary>Returns what to write to put the field back as it was, and forgets.</summary>
        public float Restore(float current)
        {
            float back = _haveWritten && current == _written ? Base : current;
            _haveWritten = false;
            return back;
        }
    }
}
