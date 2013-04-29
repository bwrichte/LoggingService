using System;
using System.Runtime.Serialization;
using MvcWebRole.Models;

namespace MvcWebRole.Models.DTOs
{
    [DataContract(Name="Snapshot")]
    public class SnapshotDTO
    {
        public SnapshotDTO()
        {
        }

        public SnapshotDTO(Snapshot snapshot)
        {
            TimeBlock = snapshot.TimeBlock;
            ApplicationName = snapshot.ApplicationName;
            LogName = snapshot.LogName;
            SnapshotValue = snapshot.SnapshotValue;
            Status = snapshot.Status;
        }

        [DataMember(Name = "TimeBlock")]
        public DateTime TimeBlock { get; set; }

        [DataMember(Name = "ApplicationName")]
        public string ApplicationName { get; set; }

        [DataMember(Name = "LogName")]
        public string LogName { get; set; }

        [DataMember(Name = "SnapshotValue")]
        public string SnapshotValue { get; set; }

        // Pending, Queuing, Processing, Complete
        [DataMember(Name = "Status")]
        public string Status { get; set; }
    }
}