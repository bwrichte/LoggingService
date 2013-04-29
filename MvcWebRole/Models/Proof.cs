using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace MvcWebRole.Models
{
    public class Proof : TableEntity
    {
        [Required]
        [Display(Name = "Application Name")]
        public string ApplicationName
        {
            get
            {
                return this.PartitionKey;
            }
            set
            {
                this.PartitionKey = value;
            }
        }

        [Required]
        [Display(Name = "Log Name")]
        public string LogName { get; set; }

        [Required]
        [Display(Name = "Snapshot Value")]
        public string SnapshotValue { get; set; }

        [Required]
        [Display(Name = "Proof Location")]
        public string ProofBlobName { get; set; }

        [Required]
        [Display(Name = "Time Block")]
        public string TimeBlock
        {
            get
            {
                if (this.RowKey == null)
                {
                    return null;
                }
                else
                {
                    return new DateTime(long.Parse(this.RowKey.Split(new char[] { '_' })[2])).ToString();
                }
            }
            set
            {
            }
        }

        [Required]
        [Display(Name = "Coinbase Transaction ID")]
        public string CoinbaseTransactionID { get; set; }

        [Display(Name = "Bitcoin Transaction Hash")]
        public string BitcoinTransactionHash { get; set; }

        [Display(Name = "Bitcoin Block Number")]
        public int? BitcoinBlockNumber { get; set; }
    }
}