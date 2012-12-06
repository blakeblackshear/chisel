using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Dynamic;
using System.IO;
using System.Text;

namespace Chisel.LogGatherers
{
    public class SqlAuditLogGatherer : IGatherLogs
    {
        private static object _threadlock;
        private readonly string lastSavedFile;
        private readonly string connectionString;
        private readonly string logLocation;

        private const string Query = @"select event_time as EventTime, 
		        sequence_number as SequenceNumber, 
		        action_id as ActionId, 
		        succeeded as Succeeded,
		        permission_bitmask as PermissionBitmask,
		        is_column_permission as IsColumnPermission,
		        session_id as SessionId,
		        server_principal_id as ServerPrincipalId,
		        database_principal_id as DatabasePrincipalId,
		        target_server_principal_id as TargetServerPrincipalId,
		        target_database_principal_id as TargetDatabasePrincipalId,
		        object_id as ObjectId,
		        class_type as ClassType,
		        session_server_principal_name as SessionServerPrincipalName,
		        server_principal_name as ServerPrincipalName,
		        server_principal_sid as ServerPrincipalSid,
		        database_principal_name as DatabasePrincipalName,
		        target_server_principal_name as TargetServerPrincipalName,
		        target_server_principal_sid as TargetServerPrincipalSid,
		        target_database_principal_name as TargetDatabasePrincipalName,
		        server_instance_name as Devicename,
		        database_name as DatabaseName,
		        schema_name as SchemaName,
		        object_name as ObjectName,
		        statement as Statement,
		        additional_information as AdditionalInformation,
		        file_name as FileName,
		        audit_file_offset as AuditFileOffset,
		        user_defined_event_id as UserDefinedEventId,
		        user_defined_information as UserDefinedInformation
        from sys.fn_get_audit_file('{0}*',null,null) where event_time > '{1}'";

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

        public SqlAuditLogGatherer()
        {
            // init thread lock for writing to file
            _threadlock = new object();

            if (ConfigurationManager.AppSettings["SqlAuditLogLastSavedFile"] == null) throw new ConfigurationErrorsException("Missing SqlAuditLogLastSavedFile in AppSettings");
            lastSavedFile = ConfigurationManager.AppSettings["SqlAuditLogLastSavedFile"];
            if (ConfigurationManager.AppSettings["SqlAuditConnectionString"] == null) throw new ConfigurationErrorsException("Missing SqlAuditConnectionString in AppSettings");
            connectionString = ConfigurationManager.AppSettings["SqlAuditConnectionString"];
            if (ConfigurationManager.AppSettings["SqlAuditLogLocation"] == null) throw new ConfigurationErrorsException("Missing SqlAuditLogLocation in AppSettings");
            logLocation = ConfigurationManager.AppSettings["SqlAuditLogLocation"];
            if (ConfigurationManager.AppSettings["SqlAuditLogInterval"] == null) throw new ConfigurationErrorsException("Missing SqlAuditLogInterval in AppSettings");
            IntervalMilliseconds = double.Parse(ConfigurationManager.AppSettings["SqlAuditLogInterval"]);
        }

        public GatherResult GatherLogs()
        {
            var logEntries = new List<dynamic>();
            DateTime? lastLogEntryTime = null;
            var logQuery = string.Format(Query, logLocation, LastLogEntrySent.ToString("o").Replace("Z", ""));
            using (var conn = new SqlConnection(connectionString))
            using (var command = new SqlCommand(string.Format(logQuery), conn) { CommandType = System.Data.CommandType.Text })
            {
                conn.Open();
                var reader = command.ExecuteReader();
                if (reader.HasRows)
                {
                    while (reader.Read())
                    {
                        var entry = CreateEntry(GetRow(reader));
                        var eventDate =
                            DateTime.Parse(reader.GetDateTime(reader.GetOrdinal("EventTime")).ToString("o") + "Z").ToUniversalTime();
                        if (eventDate > LastLogEntrySent) lastLogEntryTime = eventDate;
                        logEntries.Add(entry);
                    }
                }

                reader.Close();
                conn.Close();
            }

            return new GatherResult
                {
                    Logs = logEntries,
                    LastLogEntryTime = lastLogEntryTime
                };
        }

        private dynamic CreateEntry(Dictionary<string, string> row)
        {
            var obj = new ExpandoObject();
            IDictionary<string, object> underObject = obj;
            underObject.Add("Source", "SqlAuditLog");
            foreach (var col in row)
            {
                underObject.Add(col.Key, col.Value);
            }

            return underObject;
        }

        private Dictionary<string, string> GetRow(SqlDataReader reader)
        {
            var headers = new Dictionary<string, string>();
            for (var i = 0; i < reader.FieldCount; ++i)
            {
                if (reader.GetFieldType(i) == typeof (DateTime))
                {
                    headers.Add(reader.GetName(i), ((DateTime) reader.GetValue(i)).ToString("o")+"Z");
                }
                else if (reader.GetFieldType(i) == typeof(Byte[]))
                {
                }
                else
                {
                    headers.Add(reader.GetName(i), reader.GetValue(i).ToString());
                }
            }
            return headers;
        }
    }
}
