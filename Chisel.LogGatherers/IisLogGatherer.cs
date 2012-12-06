using System;
using System.Collections.Generic;
using System.Configuration;
using System.Dynamic;
using System.IO;
using MSUtil;

namespace Chisel.LogGatherers
{
    public class IisLogGatherer : IGatherLogs
    {
        private static object _threadlock;
        private readonly string lastSavedFile;
        private readonly string logLocation;

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
                for (var i = 0; i < columnCount; ++i)
                {
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
