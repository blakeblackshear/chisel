using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;
using Loggly;
using Newtonsoft.Json;

namespace Chisel
{
    public class ChiselService
    {
        public IGatherLogs[] LogGatherers { get; set; }
        private List<Timer> timers;
        private Logger logger;

        public ChiselService(string inputKey)
        {
            logger = new Logger(inputKey);
        }

        public bool Start(IGatherLogs[] logGatherers)
        {
            timers = new List<Timer>();
            LogGatherers = logGatherers;
            foreach (var gatherer in LogGatherers)
            {
                var timer = new Timer(gatherer.IntervalMilliseconds);
                timer.Elapsed += (sender, args) =>
                    {
                        timer.Enabled = false;
                        try
                        {
                            var result = gatherer.GatherLogs();
                            Trace.WriteLine(string.Format("Gatherer: {0}, Count: {1}, LastLogEntryTime: {2}",
                                                          gatherer.GetType().Name,
                                                          result.Logs.Count(),
                                                          result.LastLogEntryTime.HasValue
                                                              ? result.LastLogEntryTime.Value.ToString()
                                                              : "N/A"));
                            foreach (var log in result.Logs)
                            {
                                logger.Log(JsonConvert.SerializeObject(log), true);
                            }
                            if (result.LastLogEntryTime.HasValue)
                                gatherer.LastLogEntrySent = result.LastLogEntryTime.Value;
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine(ex.ToString());
                        }
                        timer.Enabled = true;
                    };
                timer.Enabled = true;
                timers.Add(timer);
            }
            return true;
        }

        public bool Stop()
        {
            foreach (var timer in timers)
            {
                timer.Enabled = false;
                timer.Close();
                timer.Dispose();
            }
            return true;
        }
    }
}
