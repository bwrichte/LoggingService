using System.ComponentModel.DataAnnotations;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace MvcWebRole.Models
{
    public class Subscription : TableEntity
    {
        /*
         * No need to override (as they can be accessed via existing properties):
         *   - PartitionKey
         *   - RowKey
         *   - TimeStamp
         *   - ETag
         */

        //
        // PartitionKey is the ApplicationName and can be accessed by either
        // Note: If you wanted the list name format to be less restrictive, 
        // you could allow other characters and URL-encode list names when they 
        // are used in query strings. However, certain characters are not allowed 
        // in Windows Azure Table partition keys or row keys, and you would have 
        // to exclude at least those characters. 
        //
        [Required]
        [RegularExpression(@"[\w]+",
         ErrorMessage = @"Only alphanumeric characters and underscore (_) are allowed.")]
        [MaxLength(128, ErrorMessage = "Application Name cannot be longer than 128 characters")]
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

        //
        // RowKey is the LogName and can be accessed by either
        //
        [Required]
        [RegularExpression(@"[\w]+",
         ErrorMessage = @"Only alphanumeric characters and underscore (_) are allowed.")]
        [MaxLength(128, ErrorMessage = "Log Name cannot be longer than 128 characters")]
        [Display(Name = "Log Name")]
        public string LogName
        {
            get
            {
                return this.RowKey;
            }
            set
            {
                this.RowKey = value;
            }
        }

        [Required]
        [EmailAddressAttribute]
        [Display(Name = "Email Address")]
        [MaxLength(128, ErrorMessage = "Email Address cannot be longer than 128 characters")]
        public string EmailAddress { get; set; }

        [MaxLength(256, ErrorMessage = "Description cannot be longer than 256 characters")]
        public string Description { get; set; }
        
        public string APIKey { get; set; }

        public bool? Verified { get; set; }

        public int? VerificationsSent { get; set; }
    }
}