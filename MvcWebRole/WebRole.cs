using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Diagnostics;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace MvcWebRole
{
    public class WebRole : RoleEntryPoint
    {
        private void ConfigureDiagnostics()
        {
            DiagnosticMonitorConfiguration config = DiagnosticMonitor.GetDefaultInitialConfiguration();
            config.Logs.BufferQuotaInMB = 500;
            config.Logs.ScheduledTransferLogLevelFilter = LogLevel.Verbose;
            config.Logs.ScheduledTransferPeriod = TimeSpan.FromMinutes(1d);

            DiagnosticMonitor.Start(
                "Microsoft.WindowsAzure.Plugins.Diagnostics.ConnectionString",
                config);
        }

        public override bool OnStart()
        {
            ConfigureDiagnostics();
            return base.OnStart();
        }

        /*
         * The OnStop method has up to 5 minutes to exit before the application is shut down. 
         * You could add a sleep call for 5 minutes to the OnStop method to give your 
         * application the maximum amount of time to process the current requests, but if your 
         * application is scaled correctly, it should be able to process the remaining requests 
         * in much less than 5 minutes. It is best to stop as quickly as possible, so that the 
         * application can restart as quickly as possible and continue processing requests.
         * 
         * Note: Trace data is not saved when called from the OnStop method without performing 
         * an On-Demand Transfer. You can view the OnStop trace information in real time with 
         * the dbgview utility from a remote desktop connection.
         */
        public override void OnStop()
        {
            Trace.TraceInformation("OnStop called from WebRole");
            var rcCounter = new PerformanceCounter("ASP.NET", "Requests Current", "");
            while (rcCounter.NextValue() > 0)
            {
                Trace.TraceInformation("ASP.NET Requests Current = " + rcCounter.NextValue().ToString());
                System.Threading.Thread.Sleep(1000);
            }
        }
    }
}
