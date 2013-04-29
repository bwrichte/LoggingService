using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using MvcWebRole.SharedSrc;

namespace MvcWebRole.Models
{
    public class Snapshot : TableEntity
    {
        private DateTime _tb;
        private string _appName;
        private string _logName;

        public Snapshot()
        {
            this.TimeBlock = Utils.NextOperationTB;
            this.Status = "Pending";
        }

        public DateTime TimeBlock
        {
            get
            {
                return _tb;
            }
            set
            {
                _tb = value;
                this.PartitionKey = value.Ticks.ToString();
                this.RowKey = string.Format(
                    "{0}_{1}_{2}",
                    this.ApplicationName,
                    this.LogName,
                    value.Ticks
                    );
            }
        }

        [Required]
        [RegularExpression(@"[\w]+",
         ErrorMessage = @"Only alphanumeric characters and underscore (_) are allowed.")]
        [MaxLength(128, ErrorMessage = "Application Name cannot be longer than 128 characters")]
        [Display(Name = "Application Name")]
        public string ApplicationName
        {
            get 
            {
                return _appName;
            }
            set
            {
                this._appName = value;
                this.TimeBlock = _tb; // Refresh with app name?
            }
        }

        [Required]
        [RegularExpression(@"[\w]+",
         ErrorMessage = @"Only alphanumeric characters and underscore (_) are allowed.")]
        [MaxLength(128, ErrorMessage = "Log Name cannot be longer than 128 characters")]
        [Display(Name = "Log Name")]
        public string LogName
        {
            get
            {
                return this._logName;
            }
            set
            {
                this._logName = value;
                this.TimeBlock = _tb; // Refresh with log name?
            }
        }

        [Required]
        [RegularExpression(@"^(\{{0,1}([0-9a-fA-F]){8}-([0-9a-fA-F]){4}-([0-9a-fA-F]){4}-([0-9a-fA-F]){4}-([0-9a-fA-F]){12}\}{0,1})$",
         ErrorMessage = @"Must be a valid APIKey (GUID-format)")]
        [Display(Name = "APIKey")]
        public string APIKey { get; set; }

        [Required]
        [RegularExpression(@"^([0-9A-Fa-f]{2})+$",
         ErrorMessage = @"Only hex encoded strings are allowed")]
        [MaxLength(1024, ErrorMessage="Snapshot Value cannot be longer than 1024 characters")]
        [Display(Name = "Snapshot Value")]
        public string SnapshotValue { get; set; }

        // Pending, Queuing, Processing, Complete
        public string Status { get; set; }
    }
}