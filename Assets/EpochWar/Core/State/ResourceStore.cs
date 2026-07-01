namespace EpochWar.Core.State
{
    /// <summary>
    /// The stored quantity of a single <see cref="ResourceType"/> for a single
    /// <see cref="Nation"/> (Req 2.1).
    ///
    /// Each Nation holds one store per resource type so quantities are tracked independently
    /// (Req 2.1, Property 7). <see cref="Capacity"/> is optional: a value &lt;= 0 means the
    /// store is uncapped, while a positive value caps <see cref="Amount"/> and causes the
    /// ResourceSystem (task 4) to discard production that would exceed it (Req 2.5).
    ///
    /// This is a mutable value type holding only data; all production/affordability/deduction
    /// rules live in the ResourceSystem. Because it is a struct stored by value in a
    /// dictionary, callers mutate it by reassigning the entry (the ResourceSystem owns that).
    /// </summary>
    public struct ResourceStore
    {
        /// <summary>The current stored quantity (never negative once managed by the ResourceSystem).</summary>
        public float Amount;

        /// <summary>The maximum storable quantity; <c>&lt;= 0</c> means uncapped (Req 2.5).</summary>
        public float Capacity;

        public ResourceStore(float amount, float capacity = 0f)
        {
            Amount = amount;
            Capacity = capacity;
        }

        /// <summary>True when this store has no capacity limit.</summary>
        public bool IsUncapped => Capacity <= 0f;

        public override string ToString()
            => IsUncapped ? $"{Amount:0.##}/∞" : $"{Amount:0.##}/{Capacity:0.##}";
    }
}
