using System;
using System.Runtime.Serialization;
using MvcWebRole.Models;

namespace MvcWebRole.Models.DTOs
{
    [DataContract(Name="Proof")]
    public class ProofDTO
    {
        public ProofDTO() { }

        public ProofDTO(Proof proof)
        {
            ApplicationName = proof.ApplicationName;
            LogName = proof.LogName;
            SnapshotValue = proof.SnapshotValue;
            ProofBlobName = proof.ProofBlobName;
            CoinbaseTransactionID = proof.CoinbaseTransactionID;
            TimeBlock = proof.TimeBlock;
            BitcoinTransactionHash = proof.BitcoinTransactionHash;
            BitcoinBlockNumber = proof.BitcoinBlockNumber;
        }

        [DataMember(Name = "ApplicationName")]
        public string ApplicationName { get; set; }

        [DataMember(Name = "LogName")]
        public string LogName { get; set; }

        [DataMember(Name = "SnapshotValue")]
        public string SnapshotValue { get; set; }

        [DataMember(Name = "ProofLocation")]
        public string ProofBlobName { get; set; }

        [DataMember(Name = "TimeBlock")]
        public string TimeBlock { get; set; }

        [DataMember(Name = "CoinbaseTransactionID")]
        public string CoinbaseTransactionID { get; set; }

        [DataMember(Name = "BitcoinHash", EmitDefaultValue = false)]
        public string BitcoinTransactionHash { get; set; }

        [DataMember(Name = "BlockNumber", EmitDefaultValue = false)]
        public int? BitcoinBlockNumber { get; set; }
    }
}