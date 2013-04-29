using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace MvcWebRole.SharedSrc
{
    public class Utils
    {
        public static TimeSpan LoggingOperationSpan
        {
            get
            {
                return TimeSpan.FromMinutes(8);
            }
        }

        public static DateTime NextOperationTB
        {
            get
            {
                return RoundUp(DateTime.Now, LoggingOperationSpan);
            }
        }

        public static DateTime PrevOperationTB
        {
            get
            {
                return RoundDown(DateTime.Now, LoggingOperationSpan);
            }
        }

        private static DateTime RoundUp(DateTime dt, TimeSpan d)
        {
            return new DateTime(((dt.Ticks + d.Ticks - 1) / d.Ticks) * d.Ticks);
        }

        private static DateTime RoundDown(DateTime dt, TimeSpan d)
        {
            return new DateTime((dt.Ticks / d.Ticks) * d.Ticks);
        }
    }
}