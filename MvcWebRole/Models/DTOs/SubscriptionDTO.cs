using System.Runtime.Serialization;
using MvcWebRole.Models;

namespace MvcWebRole.Models.DTOs
{
    [DataContract(Name="Subscription")]
    public class SubscriptionDTO
    {
        public SubscriptionDTO() { }
        public SubscriptionDTO(Subscription subscription)
            : this(subscription, false)
        {
        }

        public SubscriptionDTO(Subscription subscription, bool emailIncluded)
        {
            ApplicationName = subscription.ApplicationName;
            LogName = subscription.LogName;
            Description = subscription.Description;
            Verified = subscription.Verified;
            VerificationsSent = subscription.VerificationsSent;
            if (emailIncluded)
            {
                // ONLY INCLUDE EMAIL IF SPECIFIED
                EmailAddress = subscription.EmailAddress;
            }
            // DO NOT COPY API KEY
        }

        [DataMember(Name = "ApplicationName")]
        public string ApplicationName { get; set; }

        [DataMember(Name = "LogName")]
        public string LogName { get; set; }

        [DataMember(Name = "Description")]
        public string Description { get; set; }

        [DataMember(Name = "EmailAddress", EmitDefaultValue = false)]
        public string EmailAddress { get; set; }

        [DataMember(Name = "Verified", EmitDefaultValue = false)]
        public bool? Verified { get; set; }

        [DataMember(Name = "VerificationsSent", EmitDefaultValue = false)]
        public int? VerificationsSent { get; set; }
    }
}