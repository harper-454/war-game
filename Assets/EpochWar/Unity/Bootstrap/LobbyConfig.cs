using EpochWar.Unity.Net;

namespace EpochWar.Unity.Bootstrap
{
    /// <summary>
    /// A tiny, process-wide handoff of the lobby's choices from <c>Boot.unity</c> to
    /// <c>Match.unity</c> (task 17.1).
    ///
    /// Unity scenes cannot pass constructor arguments to one another, so the lobby records the
    /// Player's selected <see cref="NetworkMatchMode"/> here before loading the match scene, and the
    /// <see cref="MatchSceneController"/> reads it when it assembles the Match. It is deliberately a
    /// plain static holder with no logic: the authoritative human/AI split still derives entirely from
    /// the seeded Nations the <see cref="MatchSceneController"/> builds for the selected mode, so this
    /// only carries the Player's intent across the scene load.
    ///
    /// When <see cref="HasSelection"/> is false (e.g. the match scene is entered directly for
    /// iteration) the match falls back to the mode configured on the scene's components.
    /// </summary>
    public static class LobbyConfig
    {
        /// <summary>True once the lobby has recorded a mode selection for the next Match.</summary>
        public static bool HasSelection { get; private set; }

        /// <summary>The mode the lobby selected for the next Match.</summary>
        public static NetworkMatchMode SelectedMode { get; private set; } = NetworkMatchMode.CompetitiveTwoHuman;

        /// <summary>Records the Player's chosen <paramref name="mode"/> for the Match about to be loaded.</summary>
        public static void Select(NetworkMatchMode mode)
        {
            SelectedMode = mode;
            HasSelection = true;
        }

        /// <summary>Clears any recorded selection (e.g. after the Match consumes it).</summary>
        public static void Clear()
        {
            HasSelection = false;
            SelectedMode = NetworkMatchMode.CompetitiveTwoHuman;
        }
    }
}
