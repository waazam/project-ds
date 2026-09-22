using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The one place that says how long the original staircase is for a given story
/// state, so every restore path and every beat sets the same number instead of
/// accumulating on whatever ran before it:
/// - the original flight (the scene's own Steps) until anything happens to it;
/// - twice that after the Act 6 optional climb (Act6ExtendedClimb);
/// - <see cref="TallSteps"/> once the Act 11 radio exchange is done, whatever came before.
/// </summary>
public static class StairsState
{
	/// <summary>Act 11's impossible flight.</summary>
	public const int TallSteps = 220;

	public static int StepsFor(StoryManager s, int baseSteps)
	{
		if (s == null) return baseSteps;
		if (s.HasFlag(StoryManager.Flag.Act11DialogueDone)) return TallSteps;
		if (s.HasFlag(StoryManager.Flag.Act6ExtendedClimb)) return baseSteps * 2;
		return baseSteps;
	}
}
