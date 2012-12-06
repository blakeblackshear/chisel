using System.Collections.Generic;
using System.Configuration;
using Chisel.LogGatherers;
using Topshelf;

namespace Chisel.Service
{
    class Program
    {
        static void Main(string[] args)
        {
            var logGatherers = new List<IGatherLogs>();
            if (bool.Parse(ConfigurationManager.AppSettings["WindowsEventLogEnabled"]))
            {
                logGatherers.Add(new WindowsEventLogGatherer());
            }
            if (bool.Parse(ConfigurationManager.AppSettings["SqlAuditLogEnabled"]))
            {
                logGatherers.Add(new SqlAuditLogGatherer());
            }
            if (bool.Parse(ConfigurationManager.AppSettings["IisLogEnabled"]))
            {
                logGatherers.Add(new IisLogGatherer());
            }
            HostFactory.Run(x =>
                {
                    x.Service<ChiselService>(s =>
                        {
                            s.ConstructUsing(name => new ChiselService(ConfigurationManager.AppSettings["LogglyKey"]));
                            s.WhenStarted(ts => ts.Start(logGatherers.ToArray()));
                            s.WhenStopped(ts => ts.Stop());
                        });
                    x.StartAutomatically();
                    x.RunAsLocalSystem();
                    x.SetDescription("Service for gathering and sending logs to loggly");
                    x.SetDisplayName("Chisel Log Service");
                    x.SetServiceName("ChiselLogService");
                });
        }
    }
}
