using System;
using System.Collections.Generic;

namespace Chisel
{
    public class GatherResult
    {
        public IEnumerable<dynamic> Logs { get; set; }
        public DateTime? LastLogEntryTime { get; set; }
    }
}
