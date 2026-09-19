using Godot;

namespace ProjectDS.Audio;

/// <summary>Random index that never repeats the previous pick (when there's a choice).</summary>
public struct SamplePicker
{
	private int _last;

	public int Next(RandomNumberGenerator rng, int count)
	{
		if (count <= 1) return 0;
		int pick = rng.RandiRange(0, count - 2);
		if (pick >= _last) pick++;   // skip the last one without biasing the rest
		_last = pick;
		return pick;
	}
}
