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

/// <summary>Random index that avoids the last three picks (when there are enough to choose from): for
/// takes that must very rarely come round again (Dan, 2026-09-22).</summary>
public struct RecentPicker
{
	private int _a, _b, _c;   // the last three picks, newest first (-1 = none yet)
	private bool _init;

	public int Next(RandomNumberGenerator rng, int count)
	{
		if (!_init) { _a = _b = _c = -1; _init = true; }
		if (count <= 1) return 0;
		int avoid = count > 3 ? 3 : count - 1;   // with few takes, avoid only as many as we can
		for (int attempt = 0; attempt < 32; attempt++)
		{
			int pick = rng.RandiRange(0, count - 1);
			if (pick == _a) continue;
			if (avoid >= 2 && pick == _b) continue;
			if (avoid >= 3 && pick == _c) continue;
			_c = _b; _b = _a; _a = pick;
			return pick;
		}
		return _a < 0 ? 0 : (_a + 1) % count;
	}
}
