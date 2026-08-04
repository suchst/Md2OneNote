using System;

namespace Md2OneNote.Core
{
    /// <summary>Wall clock, injected so that page metadata timestamps are deterministic in tests.</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public static SystemClock Instance { get; } = new SystemClock();

        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }
    }
}
