using System;
using System.Collections.Generic;
using System.Configuration;
using System.Dynamic;
using System.IO;
using System.Linq;
using MSUtil;

namespace Chisel.LogGatherers
{
    public class IisLogGatherer : IGatherLogs
    {
        private static object _threadlock;
        private readonly string lastSavedFile;
        private readonly string logLocation;
        private readonly string[] filters;

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

        public IisLogGatherer()
        {
            // init thread lock for writing to file
            _threadlock = new object();

            if (ConfigurationManager.AppSettings["IisLogLastSavedFile"] == null) throw new ConfigurationErrorsException("Missing IisLogLastSavedFile in AppSettings");
            lastSavedFile = ConfigurationManager.AppSettings["IisLogLastSavedFile"];
            if (ConfigurationManager.AppSettings["IisLogLocation"] == null) throw new ConfigurationErrorsException("Missing IisLogLocation in AppSettings");
            logLocation = ConfigurationManager.AppSettings["IisLogLocation"];
            if (ConfigurationManager.AppSettings["IisLogInterval"] == null) throw new ConfigurationErrorsException("Missing IisLogInterval in AppSettings");
            IntervalMilliseconds = double.Parse(ConfigurationManager.AppSettings["IisLogInterval"]);
            if (ConfigurationManager.AppSettings["IisLogFilters"] == null) throw new ConfigurationErrorsException("Missing IisLogFilters in AppSettings");
            filters = ConfigurationManager.AppSettings["IisLogFilters"].Split(new[] {';'});
        }

        public GatherResult GatherLogs()
        {
            var dynamicResults = new List<dynamic>();
            DateTime? lastLogEntryTime = null;
            var logQuery = new LogQueryClassClass();
            var inputFormat = new COMW3CInputContextClassClass();
            const string query = "SELECT TO_TIMESTAMP(date, time) AS [EventTime], * FROM '{0}' WHERE EventTime > TIMESTAMP('{1}','yyyy-MM-dd HH:mm:ss')";

            var results =
                logQuery.Execute(string.Format(query, logLocation, LastLogEntrySent.ToString("yyyy-MM-dd HH:mm:ss")), inputFormat);
            var columnNames = new List<string>();
            var columnCount = results.getColumnCount();
            for (var i = 0; i < columnCount; ++i)
            {
                columnNames.Add(results.getColumnName(i));
            }

            while (!results.atEnd())
            {
                var obj = new ExpandoObject();
                IDictionary<string, object> underObject = obj;
                underObject.Add("Source", "IisLog");
                underObject.Add("Devicename", Environment.MachineName);
                var record = results.getRecord();
                var filtered = false;
                for (var i = 0; i < columnCount; ++i)
                {
                    if (columnNames[i] == "cs(User-Agent)")
                    {
                        var userAgent = (string)record.getValue(i);
                        if (filters.Any(f => userAgent.IndexOf(f) != -1))
                        {
                            filtered = true;
                            break;
                        };
                    }

                    if (columnNames[i] == "EventTime")
                    {
                        var eventDate = DateTime.Parse(((DateTime)record.getValue(i)).ToString("o") + "Z").ToUniversalTime();
                        if (eventDate > LastLogEntrySent) lastLogEntryTime = eventDate;
                        underObject.Add(columnNames[i], eventDate.ToString("o"));
                    }
                    else
                    {
                        underObject.Add(columnNames[i], record.getValue(i));
                    }
                }
                if(!filtered)
                    dynamicResults.Add(underObject);
                results.moveNext();
            }
            return new GatherResult
                {
                    Logs = dynamicResults,
                    LastLogEntryTime = lastLogEntryTime
                };
        }
    }
}
