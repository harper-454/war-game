namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// An engine-free definition describing one economic <see cref="ResourceType"/> (Req 2).
    ///
    /// While the runtime stored quantity lives in a per-Nation <c>ResourceStore</c> (task 2.2),
    /// this definition supplies the authored, static facts about a resource: the Era at which
    /// it becomes available (Req 1.5) and its default storage capacity. A
    /// <see cref="DefaultCapacity"/> of zero or less means the resource is uncapped, matching
    /// the design's <c>ResourceStore.Capacity &lt;= 0 means uncapped</c> convention and the
    /// capacity-capping rule in Req 2.5.
    /// </summary>
    public sealed class ResourceDef
    {
        /// <summary>The economic resource this definition describes (also the catalog key).</summary>
        public ResourceType Type { get; }

        /// <summary>Human-readable name for UI display (Req 7.1).</summary>
        public string DisplayName { get; }

        /// <summary>The Era at which this resource becomes available to a Nation (Req 1.5).</summary>
        public Era Era { get; }

        /// <summary>
        /// Default per-Nation storage capacity. Values &lt;= 0 mean uncapped; otherwise
        /// production beyond this amount is discarded (Req 2.5).
        /// </summary>
        public float DefaultCapacity { get; }

        public ResourceDef(ResourceType type, string displayName, Era era, float defaultCapacity)
        {
            Type = type;
            DisplayName = displayName;
            Era = era;
            DefaultCapacity = defaultCapacity;
        }

        /// <summary>True when this resource has no storage cap.</summary>
        public bool IsUncapped => DefaultCapacity <= 0f;

        public override string ToString() => $"Resource({Type}, {Era})";
    }
}
