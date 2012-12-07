using System;
using System.Dynamic;
using System.Linq;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Security.Principal;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Chisel.LogGatherers
{
    public class WindowsEventLogGatherer : IGatherLogs
    {
        private static object _threadlock;
        private readonly string lastSavedFile;

        /*<Select Path=""Security"">*[System[(Level=1  or Level=2 or Level=3 or Level=4 or Level=0) and TimeCreated[@SystemTime&gt;'{0}']]]</Select>*/
        private const string WindowsLogQuery = @"<QueryList>
                                  <Query Id=""0"" Path=""Application"">
                                    <Select Path=""Application"">*[System[(Level=1  or Level=2 or Level=3 or Level=4 or Level=0) and TimeCreated[@SystemTime&gt;'{0}']]]</Select>
                                    <Select Path=""Security"">*[System[(Level=1  or Level=2 or Level=3 or Level=4 or Level=0) and TimeCreated[@SystemTime&gt;'{0}']]]</Select>
                                    <Select Path=""Setup"">*[System[(Level=1  or Level=2 or Level=3 or Level=4 or Level=0) and TimeCreated[@SystemTime&gt;'{0}']]]</Select>
                                    <Select Path=""System"">*[System[(Level=1  or Level=2 or Level=3 or Level=4 or Level=0) and TimeCreated[@SystemTime&gt;'{0}']]]</Select>
                                  </Query>
                                </QueryList>";

        public double IntervalMilliseconds { get; private set; }
        public DateTime LastLogEntrySent
        {
            get
            {
                return !File.Exists(lastSavedFile) ? DateTime.UtcNow.AddMinutes(-10) : DateTime.Parse(File.ReadAllText(lastSavedFile)).ToUniversalTime();
            }
            set
            {
                lock (_threadlock)
                {
                    File.WriteAllText(lastSavedFile, value.ToString("o"));
                }
            }
        }
 
        public WindowsEventLogGatherer()
        {
            // init thread lock for writing to file
            _threadlock = new object();
            
            if (ConfigurationManager.AppSettings["WindowsLogLastSavedFile"] == null) throw new ConfigurationErrorsException("Missing WindowsLogLastSavedFile in AppSettings");
            lastSavedFile = ConfigurationManager.AppSettings["WindowsLogLastSavedFile"];
            if (ConfigurationManager.AppSettings["WindowsLogInterval"] == null) throw new ConfigurationErrorsException("Missing WindowsLogInterval in AppSettings");
            IntervalMilliseconds = double.Parse(ConfigurationManager.AppSettings["WindowsLogInterval"]);
        }

        public GatherResult GatherLogs()
        {
            var logEntries = new List<dynamic>();

            var logQuery = string.Format(WindowsLogQuery, LastLogEntrySent.ToString("o"));
            var eventQuery = new EventLogQuery("Application", PathType.LogName, logQuery);
            var eventReader = new EventLogReader(eventQuery);
            var record = eventReader.ReadEvent();
            DateTime? lastLogEntryTime = null;
            while (record != null)
            {
                if (record.TimeCreated.HasValue && record.TimeCreated.Value.ToUniversalTime() > LastLogEntrySent)
                {
                    lastLogEntryTime = record.TimeCreated.Value.ToUniversalTime();
                }
                logEntries.Add(CreateDynamic(record));
                record = eventReader.ReadEvent();
            }

            return new GatherResult
                {
                    Logs = logEntries,
                    LastLogEntryTime = lastLogEntryTime
                };
        }

        public dynamic CreateDynamic(EventRecord record)
        {
            var obj = new ExpandoObject();
            IDictionary<string, object> underObject = obj;
            underObject["Source"] = "WindowsEventLog";
            underObject["Devicename"] = record.MachineName.ToUpper();
            underObject["EventTime"] = record.TimeCreated.Value.ToUniversalTime().ToString("o");
            underObject["EventId"] = record.Id.ToString();
            underObject["Level"] = record.Level.HasValue ? ((int)record.Level.Value).ToString() : string.Empty;
            underObject["User"] = record.UserId != null ? record.UserId.Translate(typeof(NTAccount)).ToString() : "N/A";
            underObject["ProviderName"] = record.ProviderName;

            // if SQL Audit Event
            if (record.Id == 33205)
            {
                var entries = record.FormatDescription().Replace("Audit event: ", "").Split(new[] {'\n'});
                foreach (var entry in entries)
                {
                    var colon = entry.IndexOf(':');
                    if (colon != -1)
                    {
                        underObject.Add(entry.Substring(0, colon), entry.Substring(colon + 1, entry.Length - colon - 1));
                    }
                }
            }
            else
            {
                underObject["Description"] = record.FormatDescription();
                var root = XElement.Parse(record.ToXml());
                XNamespace x = "http://schemas.microsoft.com/win/2004/08/events/event";
                var dataNodes = root.Descendants(x + "Data")
                                    .Where(e => e.HasAttributes && e.Attributes().Any(a => a.Name == "Name"));
                foreach (var node in dataNodes)
                {
                    var key = node.Attributes().First(a => a.Name == "Name").Value;
                    if (!underObject.ContainsKey(key))
                    {
                        underObject.Add(key, node.Value);
                    }
                }
            }

            return underObject;
        }
    }
}
