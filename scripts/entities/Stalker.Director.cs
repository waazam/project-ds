using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;

namespace ProjectDS.Entities;

/// <summary>
/// Its pacing (2026-10-15, the stalker pass; from Alien: Isolation's menace gauge and Left 4 Dead's build, peak and relax).
/// Its episodes of rattle, its spells of steps and its trees used to run each on its own timer, so their peaks never lined
/// up and the average was a steady medium presence: wallpaper. One phase over all of them now:
/// <list type="bullet">
/// <item><b>Build</b>: it comes and stands at its trees, its steps in short spells (2-4), no rattle episodes.</item>
/// <item><b>Peak</b>: everything: the rattle's episodes, full spells of steps, and when you stop, the stop beat.</item>
/// <item><b>Relax</b>: nothing of it at all (it withdraws; no trees, no steps, no rattle): the woods come back. This valley
/// is what keeps the rest of it frightening.</item>
/// </list>
/// A menace gauge rises with how near it is and with being heard or seen; at its top (or when the build runs out) the peak
/// comes, and the peak ends after its sustain, on a sighting, or on the stop beat. Hunted (out of the bunker) the relax is
/// short but never nothing.
/// </summary>
public partial class Stalker
{
	public enum Phase { Build, Peak, Relax }
	private double _buildTickAt;
	/// <summary>Where its pacing stands (for tests and the debug overlay).</summary>
	public Phase Pacing { get; private set; } = Phase.Build;
	public float Menace { get; private set; }
	/// <summary>For tests: phase changes so far, and how many relax phases ran their full length.</summary>
	public int PhaseChanges { get; private set; }
	public int RelaxPhases { get; private set; }
	public int StopBeats { get; private set; }

	private double _phaseUntil = -1, _beatAt = -1, _lastHeard = -10;
	private RecentPicker _beatPick;

	private (Vector2 build, Vector2 peak, Vector2 relax, float fill) Tempo => Hunted || Urgency > 0.8f
		? (new(10f, 20f), new(12f, 20f), new(6f, 12f), 15f)
		: Intro ? (new(12f, 22f), new(16f, 24f), new(8f, 16f), 25f)   // (the path walk: its introduction, more of it)
		: (new(30f, 60f), new(6f, 12f), new(25f, 50f), 40f);

	private void EnterPhase(Phase p, float scale = 1f)
	{
		var t = Tempo;
		Vector2 r = p == Phase.Build ? t.build : p == Phase.Peak ? t.peak : t.relax;
		// the lantern off over half a minute out here: it comes round sooner
		if (p != Phase.Peak && _darkFor > 30f) r *= 0.6f;
		_phaseUntil = _clock + _rng.RandfRange(r.X, r.Y) * scale;
		if (p == Pacing && PhaseChanges > 0) return;
		Pacing = p;
		PhaseChanges++;
		if (p == Phase.Peak)
		{
			// the peak is its: to a tree soon if it isn't at one, and its rattle's episode from the start
			if (Current == State.Hidden) _cooldown = Mathf.Min(_cooldown, _rng.RandfRange(0.5f, 2f));
			if (!_episodeOn) StartEpisode((float)(_phaseUntil - _clock));
		}
		if (p == Phase.Relax)
		{
			RelaxPhases++;
			// it withdraws: whatever it was doing stops, quietly
			_shadowLeft = 0;
			_pendingSteps.Clear();
			if (_episodeOn) EndEpisode();
			_beatAt = -1;
			if (Current == State.Peeking && !_seenThisPeek) Hide((float)(_phaseUntil - _clock) + 1f);
			else _cooldown = Mathf.Max(_cooldown, (float)(_phaseUntil - _clock) + 1f);
		}
		GD.Print($"[stalker] {p} for {_phaseUntil - _clock:0} s (menace {Menace:0.00})");
	}

	/// <summary>The stretch it leaves alone: checkpoint 6, the cabin burning, until the bunker (the herding and the walk on).</summary>
	public static bool QuietStretch => StoryManager.Instance is { Current: Checkpoint.Act7CabinBurning };

	/// <summary>Its sound was just heard (a step or a phrase): the menace climbs faster while it's heard.</summary>
	private void Heard() => _lastHeard = _clock;

	private void TickDirector(float dt)
	{
		if (!_awake || Inert) return;
		if (_phaseUntil < 0) EnterPhase(Phase.Build);
		// (2026-10-19, the level pass) from the burning cabin to the bunker it keeps away: the giant has the herding to itself,
		// and the walk on from the lookout is the breath before Acts 8 to 11 (it was peaking under the trees coming down)
		if (QuietStretch)
		{
			if (Pacing != Phase.Relax) EnterPhase(Phase.Relax);
			_phaseUntil = System.Math.Max(_phaseUntil, _clock + 5.0);
			Menace = Mathf.Max(0f, Menace - dt / 20f);
			return;
		}
		float near = Present ? Mathf.Clamp(1f - Flat(GlobalPosition).DistanceTo(Flat(_player.GlobalPosition)) / 30f, 0f, 1f) : 0f;
		bool heard = _clock - _lastHeard < 2.0;
		if (Pacing == Phase.Relax) Menace = Mathf.Max(0f, Menace - dt / 20f);
		else Menace = Mathf.Min(1f, Menace + dt * near * (heard ? 1f : 0.4f) * (_seenThisPeek ? 3f : 1f) / Tempo.fill);

		switch (Pacing)
		{
			case Phase.Build:
				// (2026-10-15, the owner: more of the rattle) sparse ticks while it builds, from its tree, every 5-10 s
				if (Present && _clock >= _buildTickAt) { PlayPhrase(_rng.Randf() < 0.7f ? 0 : 3, GlobalPosition + Vector3.Up * 1.5f, RattleMaxDb - 4f); _buildTickAt = _clock + _rng.RandfRange(5f, 10f); }
				if (Menace >= 0.6f || _clock >= _phaseUntil) EnterPhase(Phase.Peak);
				break;
			case Phase.Peak:
				if (_clock >= _phaseUntil) EnterPhase(Phase.Relax);
				break;
			case Phase.Relax:
				if (_clock >= _phaseUntil) EnterPhase(Phase.Build);
				break;
		}
		if (_beatAt > 0 && _clock >= _beatAt) StopBeat();
	}

	/// <summary>Seen: the peak is over, and the relax after it at least 20 s (10 on the path walk).</summary>
	private void OnSighted()
	{
		if (Pacing == Phase.Relax) return;
		EnterPhase(Phase.Relax);
		double min = _clock + (Intro ? 10.0 : 20.0);
		if (_phaseUntil < min) _phaseUntil = min;
	}

	/// <summary>The player stopped mid-spell in a peak: its last answer only if it was already falling, then 1.2-2.5 s of
	/// nothing from it, then one of: nothing (most often), one more step, a few clicks from the same spot, or a weight
	/// shifting in the litter. Then the peak is over.</summary>
	private void ArmStopBeat()
	{
		_beatAt = _clock + _rng.RandfRange(1.2f, 2.5f);
	}

	private void StopBeat()
	{
		_beatAt = -1;
		if (!Present) { EnterPhase(Phase.Relax); return; }
		StopBeats++;
		// nothing 45 %, one more step 20 %, a few clicks 20 %, a scuff 15 % (never the same twice running)
		int[] bag = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3 };
		int pick = bag[_beatPick.Next(_rng, bag.Length)];
		switch (pick)
		{
			case 1: QueueSteps(1, 0f); break;
			case 2: PlayPhrase(0, GlobalPosition + Vector3.Up * 1.5f, RattleMaxDb - 6f); break;
			case 3:
			{
				var s = PickStep();
				if (s != null) Play(s, GlobalPosition + Vector3.Up * 0.3f, (_player.Footsteps?.StepVolumeDb ?? -26f) - 8f, _rng.RandfRange(0.7f, 0.8f));
				break;
			}
		}
		GD.Print($"[stalker] the stop beat: {(pick == 0 ? "nothing" : pick == 1 ? "one more step" : pick == 2 ? "a few clicks" : "a scuff")}");
		EnterPhase(Phase.Relax);
	}
}
