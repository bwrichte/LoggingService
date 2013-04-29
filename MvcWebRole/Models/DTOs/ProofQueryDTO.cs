using System;
using System.Runtime.Serialization;

namespace MvcWebRole.Models.DTOs
{
    [DataContract(Name="ProofQuery")]
    public class ProofQueryDTO
    {
        [DataMember(Name = "ApplicationName")]
        public string ApplicationName { get; set; }

        [DataMember(Name = "LogName")]
        public string LogName { get; set; }

        [DataMember(Name = "TimeBlock")]
        public string TimeBlockValue
        {
            get
            {
                return TimeBlock.Ticks > 0 ? TimeBlock.ToString() : null;
            }
            set
            {
                long ticks;
                DateTime dt;
                if (long.TryParse(value, out ticks))
                {
                    TimeBlock = new DateTime(ticks);
                }
                else if (DateTime.TryParse(value, out dt))
                {
                    TimeBlock = dt;
                }
            }
        }

        [IgnoreDataMember]
        public DateTime TimeBlock { get; set; }

        // Equal, LessThan, GreaterThan, LessThanOrEqual, GreaterThanOrEqual
        [DataMember(Name = "QueryComparison")]
        public string QueryComparison { get; set; }
    }
}