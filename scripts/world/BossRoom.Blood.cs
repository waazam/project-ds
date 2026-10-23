using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;
using ProjectDS.World.BossParts;

namespace ProjectDS.World;

/// <summary>
/// The fight in blood (2026-10-22, the owner: "leave blood behind on the platform ... the longer the fight goes on the
/// more and more the platform is being caked with blood and its splattering all over the walls and pipes and the wheels
/// you have to turn"). Every slam of a limb on the catwalk (<see cref="Blood"/>):
/// - throws drops and ropes of it up off the steel;
/// - leaves a pool on the deck that spreads out from the blow, and dries darker under the newer ones;
/// - sprays the wall behind it, and runs creep down from the spray;
/// - spatters the nearest pipes, and any valve near enough: its wheel (it turns with the wheel) and its body.
/// The more slams so far, the bigger each blow's blood and the more of it flies, and the pools go caked; by the last
/// valves the catwalk is red end to end. Kept across a death, as the dents are (<see cref="_bleeds"/>).
/// </summary>
public partial class BossRoom
{
	private static readonly List<(Vector3 at, ulong seed, int slam)> _bleeds = new();
	private readonly Queue<Decal> _bloodDecals = new();
	private const int MaxBleeds = 70, MaxBloodDecals = 260;
	/// <summary>For tests: blood decals in the room now, and blows bled.</summary>
	public int BloodDecals => _bloodDecals.Count;
	public static int BleedsKept => _bleeds.Count;

	/// <summary>A slam's blood (<paramref name="at"/> in the room's space, on the deck).</summary>
	private void Bleed(Vector3 at, ulong seed)
	{
		_bleeds.Add((at, seed, Slams));
		while (_bleeds.Count > MaxBleeds) _bleeds.RemoveAt(0);
		BleedAt(at, seed, Slams, true);
	}

	/// <summary>The blood from earlier tries, put back once the room's pipes and valves are built.</summary>
	private void RestoreBlood()
	{
		if (StoryManager.Instance is { } s && s.Current >= Checkpoint.Act18Finished) { _bleeds.Clear(); return; }
		foreach (var (at, seed, slam) in _bleeds) BleedAt(at, seed, slam, false);
		if (_bleeds.Count > 0) GD.Print($"[story] Act 18: the blood as it was left - {_bleeds.Count} blows, {_bloodDecals.Count} stains");
	}

	private void BleedAt(Vector3 at, ulong seed, int slam, bool fresh)
	{
		var rng = new RandomNumberGenerator { Seed = seed ^ 0xB100D };
		// how far into the fight: the more it has bled, the more each blow throws
		float g = Mathf.Clamp(slam / 28f, 0f, 1f);
		Vector3 up = GlobalBasis.Y.Normalized();
		Vector3 world = ToGlobal(at);
		if (fresh) Blood.Burst(this, world + up * 0.2f, up, 60 + (int)(70 * g), 6.5f + 2f * g);

		// the pool on the deck: spreading out from the blow, drying darker as newer blood lands on it
		float size = rng.RandfRange(1.7f, 2.4f) * (1f + 0.8f * g);
		var kind = g > 0.35f && rng.Randf() < 0.5f + 0.4f * g ? Blood.Kind.Caked : Blood.Kind.Pool;
		Keep(Blood.Splat(this, world + up * 0.05f, up, size, kind, rng.RandfRange(0, Mathf.Tau), spread: fresh ? 1.6f : 0f, depth: 0.6f), fresh, 50f);
		int extra = (int)(g * 3f);
		for (int i = 0; i < extra; i++)
		{
			float a = rng.RandfRange(0, Mathf.Tau), r = rng.RandfRange(0.8f, 2.2f);
			Vector3 p = at + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
			if (!OnDeck(p)) continue;
			Keep(Blood.Splat(this, ToGlobal(p) + up * 0.05f, up, rng.RandfRange(0.7f, 1.4f), Blood.Kind.Pool, a, spread: fresh ? 2.2f : 0f, depth: 0.6f), fresh, 50f);
		}

		// the outer wall behind the blow: sprayed, and running down
		bool alongX = Mathf.Abs(at.X) > Mathf.Abs(at.Z);
		Vector3 inward = alongX ? new Vector3(-Mathf.Sign(at.X), 0, 0) : new Vector3(0, 0, -Mathf.Sign(at.Z));
		Vector3 lateral = alongX ? Vector3.Back : Vector3.Right;
		Vector3 onWall = alongX ? new Vector3(Mathf.Sign(at.X) * Half, 0, at.Z) : new Vector3(at.X, 0, Mathf.Sign(at.Z) * Half);
		int sprays = 2 + (int)(4 * g) + rng.RandiRange(0, 1);
		for (int i = 0; i < sprays; i++)
		{
			Vector3 p = onWall + lateral * rng.RandfRange(-2.6f, 2.6f) + Vector3.Up * rng.RandfRange(0.3f, 3.6f);
			// its streaks thrown away from the blow, across the wall
			Vector3 from = onWall + Vector3.Up * 0.1f;
			Vector3 along = p - from;
			float delay = fresh ? (p - at).Length() / 16f : 0f;
			float sz = rng.RandfRange(1.0f, 2.1f) * (0.8f + 0.5f * g);
			float ang = rng.Randf();
			Later(delay, () => Keep(Blood.Splat(this, ToGlobal(p + inward * 0.02f), GlobalBasis * inward, sz, Blood.Kind.Spray, ang, GlobalBasis * along, spread: fresh ? 0.25f : 0f, depth: 0.5f), fresh, 70f));
		}
		int runs = 1 + (int)(2 * g);
		for (int i = 0; i < runs; i++)
		{
			Vector3 top = onWall + lateral * rng.RandfRange(-2f, 2f) + Vector3.Up * rng.RandfRange(1.2f, 3.4f);
			float w = rng.RandfRange(0.45f, 0.85f), len = rng.RandfRange(1.0f, Mathf.Min(top.Y, 2.8f));
			float secs = rng.RandfRange(5f, 10f);
			Later(fresh ? 0.3f : 0f, () => Keep(Blood.Run(this, ToGlobal(top + inward * 0.02f), GlobalBasis * inward, w, len, fresh ? secs : 0f), fresh, 90f));
		}

		// the pipes near it: the nearest point on each run within reach, spattered on the face toward the catwalk
		int piped = 0;
		foreach (var (centre, along, s0, s1, r, _, pin, _) in _runs)
		{
			if (piped >= 2 + (int)(3 * g)) break;
			float s = Mathf.Clamp((at - centre).Dot(along), s0, s1);
			Vector3 q = centre + along * s;
			if ((q - at).Length() > 5f + 2f * g || rng.Randf() < 0.3f) continue;
			piped++;
			Vector3 face = q + pin * r;
			float sz = Mathf.Max(0.5f, r * 6f) * rng.RandfRange(0.9f, 1.5f);
			Vector3 awayUp = (q - at).Normalized();
			Later(fresh ? (q - at).Length() / 16f : 0f, () => Keep(Blood.Splat(this, ToGlobal(face), GlobalBasis * pin, sz, Blood.Kind.Spray, 0f, GlobalBasis * awayUp, depth: r * 2.6f + 0.2f), fresh, 70f));
		}

		// the valves near it: the wheel (turning with it) and the body
		foreach (var v in Valves)
		{
			if (!IsInstanceValid(v)) continue;
			float dist = (v.WheelWorld - world).Length();
			if (dist > 6.5f + 3f * g || rng.Randf() < 0.25f) continue;
			ulong vs = rng.Randi();
			Later(fresh ? dist / 16f : 0f, () => { if (IsInstanceValid(v)) foreach (var d in v.Bloody(vs, world)) Keep(d, fresh, 70f); });
		}
	}

	/// <summary>Kept (the oldest let go past the cap), drying over <paramref name="drySeconds"/> (already dry if put back).</summary>
	private void Keep(Decal d, bool fresh, float drySeconds)
	{
		if (d == null) return;
		_bloodDecals.Enqueue(d);
		while (_bloodDecals.Count > MaxBloodDecals) { var old = _bloodDecals.Dequeue(); if (IsInstanceValid(old)) old.QueueFree(); }
		if (fresh) Blood.Dry(d, drySeconds);
		else d.Modulate = new Color(0.66f, 0.6f, 0.6f);
	}

	private void Later(float seconds, System.Action a)
	{
		if (seconds <= 0.01f) { a(); return; }
		GetTree().CreateTimer(seconds).Timeout += () => { if (IsInstanceValid(this)) a(); };
	}
}
