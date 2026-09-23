using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The one place that says how long a story staircase is for a given story state, so every
/// restore path and every beat sets the same number instead of accumulating on whatever ran
/// before it. The Hollow has two story staircases:
/// - the clearing's (Act 6): its own flight (doubled only on an old save carrying the legacy
///   Act6ExtendedClimb flag; the loop of 2026-09-22 never lengthens it);
/// - the last one beyond the bunker (Act 11): its own flight until the radio exchange is done,
///   then <see cref="TallSteps"/>.
/// </summary>
public static class StairsState
{
	/// <summary>Act 11's impossible flight.</summary>
	public const int TallSteps = 220;

	/// <summary>The Act 6 clearing's staircase.</summary>
	public static int ClearingStepsFor(StoryManager s, int baseSteps)
		=> s != null && s.HasFlag(StoryManager.Flag.Act6ExtendedClimb) ? baseSteps * 2 : baseSteps;

	/// <summary>The Act 11 staircase beyond the bunker.</summary>
	public static int FinalStepsFor(StoryManager s, int baseSteps)
		=> s != null && s.HasFlag(StoryManager.Flag.Act11DialogueDone) ? TallSteps : baseSteps;
}
