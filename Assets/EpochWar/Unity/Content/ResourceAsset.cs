using UnityEngine;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Unity.Content
{
    /// <summary>
    /// A ScriptableObject authoring wrapper for a <see cref="ResourceDef"/> (Req 2.1, 1.5).
    ///
    /// One asset describes one economic <see cref="ResourceType"/>: its display name, the Era at which
    /// it becomes available, and its default per-Nation storage capacity (a value &lt;= 0 means
    /// uncapped, matching <see cref="ResourceStore"/>'s convention). <see cref="ToCore"/> produces the
    /// engine-free <see cref="ResourceDef"/> the Resource_System consumes.
    /// </summary>
    [CreateAssetMenu(menuName = "EpochWar/Resource", fileName = "Resource")]
    public sealed class ResourceAsset : ScriptableObject
    {
        [Tooltip("The economic resource type this asset describes; also the catalog key.")]
        [SerializeField] private ResourceType _type = ResourceType.Food;

        [Tooltip("Human-readable name shown in the HUD.")]
        [SerializeField] private string _displayName = string.Empty;

        [Tooltip("The era at which this resource becomes available to a Nation.")]
        [SerializeField] private Era _era = Era.Prehistoric;

        [Tooltip("Default per-Nation storage capacity. Values <= 0 mean uncapped.")]
        [SerializeField] private float _defaultCapacity = 0f;

        /// <summary>The economic resource type this asset describes.</summary>
        public ResourceType Type => _type;

        /// <summary>Human-readable display name for UI.</summary>
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? _type.ToString() : _displayName;

        /// <summary>The era at which this resource becomes available.</summary>
        public Era Era => _era;

        /// <summary>Converts this authored asset into its engine-free <see cref="ResourceDef"/>.</summary>
        public ResourceDef ToCore()
        {
            return new ResourceDef(_type, DisplayName, _era, _defaultCapacity);
        }
    }
}
