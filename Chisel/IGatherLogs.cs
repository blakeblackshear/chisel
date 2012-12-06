using System;

namespace Chisel
{
    public interface IGatherLogs
    {
        double IntervalMilliseconds { get; }
        DateTime LastLogEntrySent { get; set; }
        GatherResult GatherLogs();
    }
}
