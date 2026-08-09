using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Operations.Refresh
{
    /// <summary>
    /// The configuration settings for the refresh feature.
    /// Refresh settings control the automatic update behavior of documents at specified intervals.
    /// </summary>
    public sealed class RefreshConfiguration : IDynamicJson
    {
        /// <summary>
        /// A value indicating whether the refresh feature is disabled.
        /// When <c>true</c>, the refresh operation will not process any documents.
        /// </summary>
        public bool Disabled { get; set; }

        /// <summary>
        /// The frequency, in seconds, at which documents are refreshed.
        /// If <c>null</c>, the default system refresh interval will be used.
        /// </summary>
        public long? RefreshFrequencyInSec { get; set; }

        /// <summary>
        /// The maximum number of documents to process in a single refresh cycle.
        /// If <c>null</c>, the system default maximum will be used.
        /// </summary>
        public long? MaxItemsToProcess { get; set; }

        private bool Equals(RefreshConfiguration other)
        {
            return Disabled == other.Disabled && RefreshFrequencyInSec == other.RefreshFrequencyInSec && MaxItemsToProcess == other.MaxItemsToProcess;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != this.GetType())
                return false;
            return Equals((RefreshConfiguration)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Disabled.GetHashCode();
                hashCode = (hashCode * 397) ^ (RefreshFrequencyInSec?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (MaxItemsToProcess?.GetHashCode() ?? 0);
                return hashCode;
            }
        }


        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(Disabled)] = Disabled,
                [nameof(RefreshFrequencyInSec)] = RefreshFrequencyInSec,
                [nameof(MaxItemsToProcess)] = MaxItemsToProcess
            };
        }
    }
}
