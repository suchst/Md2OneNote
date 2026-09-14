using System;

namespace Md2OneNote.Diagrams
{
    /// <summary>
    /// The runaway-rendering policy of DESIGN.md §8.2, kept pure so it can be tested without a
    /// browser: a timeout poisons the instance, and too many consecutive faults degrade the
    /// subsystem for the rest of the import.
    /// </summary>
    /// <remarks>
    /// A diagram's own failure — bad Mermaid syntax, say — is an outcome, not a fault: the
    /// renderer did its job and reported. Faults are the renderer's failures: timeouts, a
    /// browser that will not initialise, exceptions from the host. Only those count toward
    /// degradation, because only those predict that the next diagram will fail the same way.
    /// </remarks>
    internal sealed class RendererHealth
    {
        public const int DefaultFaultsBeforeDegraded = 3;

        private readonly int _faultsBeforeDegraded;
        private int _consecutiveFaults;

        public RendererHealth(int faultsBeforeDegraded = DefaultFaultsBeforeDegraded)
        {
            if (faultsBeforeDegraded <= 0) throw new ArgumentOutOfRangeException(nameof(faultsBeforeDegraded));

            _faultsBeforeDegraded = faultsBeforeDegraded;
        }

        /// <summary>The current instance must be thrown away before the next render.</summary>
        public bool Poisoned { get; private set; }

        /// <summary>No further renders this import; every diagram takes the fallback path.</summary>
        public bool Degraded { get; private set; }

        public int ConsecutiveFaults
        {
            get { return _consecutiveFaults; }
        }

        public void RecordSuccess()
        {
            _consecutiveFaults = 0;
        }

        /// <summary>A diagram that reported its own failure. Resets the fault streak: the renderer worked.</summary>
        public void RecordDiagramFailure()
        {
            _consecutiveFaults = 0;
        }

        /// <summary>The renderer itself failed. Returns true when this fault tipped it into degradation.</summary>
        public bool RecordFault(bool poisonsInstance)
        {
            if (poisonsInstance)
            {
                Poisoned = true;
            }

            _consecutiveFaults++;
            if (_consecutiveFaults >= _faultsBeforeDegraded && !Degraded)
            {
                Degraded = true;
                return true;
            }

            return false;
        }

        /// <summary>The poisoned instance has been disposed and a fresh one may be created.</summary>
        public void InstanceReplaced()
        {
            Poisoned = false;
        }
    }
}
