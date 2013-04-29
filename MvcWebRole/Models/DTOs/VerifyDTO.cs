using System;
using System.Runtime.Serialization;

namespace MvcWebRole.Models.DTOs
{
    [DataContract(Name="Verify")]
    public class VerifyDTO
    {
        [DataMember(Name = "ApplicationName")]
        public string ApplicationName { get; set; }

        [DataMember(Name = "LogName")]
        public string LogName { get; set; }

        [DataMember(Name = "APIKey")]
        public string APIKey { get; set; }

        // Confirm or other
        [DataMember(Name = "Action")]
        public string Action { get; set; }
    }
}