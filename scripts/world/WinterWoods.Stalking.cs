using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The wendigo's stalking on the road to the lodge (the owner: it never touches the player in Act 22; they may
/// catch it for a frame or two if they turn round; if they look anywhere near it, it's up into the trees at a
/// supernatural speed; it mimics human voices, badly; its footsteps are very quiet).
/// <list type="bullet">
/// <item>The first time, it is on the road ahead, far off in the murk, standing in the middle of it: it goes the
/// moment they look at it.</item>
/// <item>After that it takes its place behind them, beside a trunk 16-40 m back, facing them, and waits to be
/// seen. Look toward it (within ~38 degrees, with a clear line) and a few frames later it leaps, up the tree
/// into the crown; the bough dumps its snow. If they never turn, it slips away and comes back later, closer.</item>
/// <item>Between times: a voice from the trees (a copied human line: "hello?", "help me", "wait for me"...),
/// and its steps behind them, barely there, that stop when they stop.</item>
/// </list>
/// </summary>
public partial class WinterWoods
{
	public enum StalkState { Hidden, Watching }
	public Wendigo Wendigo { get; private set; }
	public StalkState Stalk { get; private set; } = StalkState.Hidden;
	/// <summary>For tests: how often it has shown itself, been seen (and leapt), and spoken.</summary>
	public int Appearances { get; private set; }
	public int Glimpses { get; private set; }
	public int Mimics { get; private set; }
	public int StalkSteps { get; private set; }

	private double _stalkClock, _nextAppear = 22, _nextVoice = 40, _watchUntil, _seenAt = -1, _nextStep, _stepBurstEnd;
	private int _stepsLeft;
	private float _closer;
	private readonly List<int> _voicesLeft = new();
	private AudioStreamPlayer3D _voice;
	private GpuParticles3D _snowDump;
	private PhysicsRayQueryParameters3D _ray;

	private void BuildStalker()
	{
		Wendigo = new Wendigo { Name = "Wendigo", Visible = false };
		AddChild(Wendigo);
		_voice = new AudioStreamPlayer3D { Name = "MimicVoice", Bus = "Voice", UnitSize = 9f, MaxDistance = 90f, VolumeDb = -3f };
		AddChild(_voice);
		_snowDump = new GpuParticles3D
		{
			Name = "SnowDump", Amount = 160, Lifetime = 2.6f, OneShot = true, Emitting = false, Explosiveness = 0.85f, LocalCoords = false,
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 1.6f,
				Direction = Vector3.Down, Spread = 40f, InitialVelocityMin = 0.4f, InitialVelocityMax = 2.2f, Gravity = new Vector3(0, -5f, 0),
				ScaleMin = 1f, ScaleMax = 3f, TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.4f,
			},
			DrawPass1 = new QuadMesh
			{
				Size = new Vector2(0.09f, 0.09f),
				Material = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.83f, 0.9f, 0.85f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles },
			},
			VisibilityAabb = new Aabb(new Vector3(-6, -20, -6), new Vector3(12, 24, 12)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_snowDump);
	}

	private void UpdateStalker(float dt, PlayerController player, Camera3D cam, bool outside)
	{
		if (Wendigo == null) return;
		_stalkClock += dt;
		bool live = outside && player != null && cam != null && StoryManager.Instance is { Current: Checkpoint.Act21Finished }
			&& player.PlayerInput.Enabled && PlayerProgress < 0.985f;   // (not in a cutscene: it has the input)
		if (!live)
		{
			if (outside && (int)(_stalkClock * 0.2) != (int)((_stalkClock - dt) * 0.2))
				GD.Print($"[stalk-debug] not live: player {player != null} cam {cam != null} cp {StoryManager.Instance?.Current} input {player?.PlayerInput.Enabled} progress {PlayerProgress:0.00}");
			if (Stalk == StalkState.Watching && !Wendigo.Leaping) Vanish();
			return;
		}
		switch (Stalk)
		{
			case StalkState.Hidden:
				if (_stalkClock >= _nextAppear && TryAppear(player, cam)) break;
				Voices(player, cam);
				Steps(player, cam);
				break;
			case StalkState.Watching:
				Watch(player, cam);
				break;
		}
	}

	private bool Seen(Camera3D cam, Vector3 point, float coneDeg)
	{
		var to = point - cam.GlobalPosition;
		if (to.Length() < 0.5f) return true;
		var fwd = -cam.GlobalBasis.Z;
		if (fwd.AngleTo(to) > Mathf.DegToRad(coneDeg)) return false;
		return Clear(cam.GlobalPosition, point);
	}

	private bool Clear(Vector3 from, Vector3 to)
	{
		_ray ??= new PhysicsRayQueryParameters3D { CollisionMask = 1 };
		_ray.From = from; _ray.To = to;
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(_ray);
		return hit.Count == 0;
	}

	/// <summary>Takes its place: the first time on the road ahead, far off; after that, behind them by a tree.</summary>
	private bool TryAppear(PlayerController player, Camera3D cam)
	{
		var pl = ToLocal(player.GlobalPosition);
		var fwd = -cam.GlobalBasis.Z; fwd.Y = 0; fwd = fwd.Normalized();
		Nearest(pl.X, pl.Z, out float s, out _);
		if (Appearances == 0)
		{
			// ahead down the road, standing in the middle of it, at the edge of the murk
			var at = RoadAt(Mathf.Min(s + 36f, Length - 60f), out _);
			var world = ToGlobal(at);
			if (Seen(cam, world + Vector3.Up * 2.4f, 50f)) { _nextAppear = _stalkClock + 2; return false; }   // wait until they're not looking that way
			Place(world, player.GlobalPosition);
			return true;
		}
		// behind: a tree 16-40 m back (a little closer each time it gets away unseen), off the road, in a clear line
		float near = Mathf.Max(12f, 16f - _closer), far = Mathf.Max(near + 8f, 28f - _closer);
		var pxz = new Vector2(pl.X, pl.Z);
		(Vector2 at, float height, bool fir) best = default; float bestScore = float.MaxValue; bool found = false;
		foreach (var t in TreeSpots)
		{
			float d = t.at.DistanceTo(pxz);
			if (d < near || d > far) continue;
			if (WinterGlade.OutsideChurch(t.at.X, t.at.Y) < 20f) continue;   // never in or against the church
			var w = ToGlobal(new Vector3(t.at.X, Height(t.at.X, t.at.Y), t.at.Y));
			var to = (w - cam.GlobalPosition) with { Y = 0 };
			if (fwd.AngleTo(to.Normalized()) < Mathf.DegToRad(115f)) continue;   // only behind them
			float score = Mathf.Abs(d - (near + far) * 0.5f) + _rng.RandfRange(0f, 8f);
			if (score < bestScore) { bestScore = score; best = t; found = true; }
		}
		if (!found) { _nextAppear = _stalkClock + 3; return false; }
		// beside the trunk, on the player's side of it, half hidden
		var trunk = new Vector3(best.at.X, 0, best.at.Y);
		var side = (new Vector3(pl.X, 0, pl.Z) - trunk).Normalized().Cross(Vector3.Up) * (_rng.Randf() < 0.5f ? 1f : -1f);
		var stand = trunk + side * 0.9f + (new Vector3(pl.X, 0, pl.Z) - trunk).Normalized() * 0.4f;
		stand.Y = Height(stand.X, stand.Z) - 0.05f;
		var sw = ToGlobal(stand);
		if (!Clear(cam.GlobalPosition, sw + Vector3.Up * 2.4f)) { _nextAppear = _stalkClock + 2; return false; }
		Place(sw, player.GlobalPosition);
		_perchTree = best;
		return true;
	}

	private (Vector2 at, float height, bool fir) _perchTree;

	private void Place(Vector3 ground, Vector3 face)
	{
		Wendigo.StandAt(ground, face);
		Stalk = StalkState.Watching;
		Appearances++;
		_seenAt = -1;
		_watchUntil = _stalkClock + (Appearances == 1 ? 60 : 22);
		GD.Print($"[story] Act 22: the wendigo takes its place ({(Appearances == 1 ? "on the road ahead" : "behind, by a tree")}, {Wendigo.GlobalPosition.DistanceTo(StoryBeat.Player(this)?.GlobalPosition ?? Vector3.Zero):0} m)");
	}

	private void Watch(PlayerController player, Camera3D cam)
	{
		if (Wendigo.Leaping) return;
		if (_seenAt < 0)
		{
			// anywhere near it: the chest or the head inside a wide cone, with nothing in the way
			if (Seen(cam, Wendigo.ChestWorld, 38f) || Seen(cam, Wendigo.ChestWorld + Vector3.Up * 1f, 38f)) { _seenAt = _stalkClock; Glimpses++; }
			else if (_stalkClock > _watchUntil) { Vanish(); _closer = Mathf.Min(_closer + 4f, 10f); return; }
			else if (Wendigo.GlobalPosition.DistanceTo(player.GlobalPosition) < 9f) { _seenAt = _stalkClock; }   // they walked right up to it: gone
			return;
		}
		// a few frames to register it, then gone
		if (_stalkClock - _seenAt < 0.07) return;
		LeapAway();
	}

	private void LeapAway()
	{
		// into the nearest crown: its own tree, or one close by, 8-13 m up
		var from = ToLocal(Wendigo.GlobalPosition);
		var bestXZ = new Vector2(from.X, from.Z) + new Vector2(_rng.RandfRange(-3f, 3f), _rng.RandfRange(-3f, 3f));
		float h = 11f;
		float bd = float.MaxValue;
		foreach (var t in TreeSpots)
		{
			float d = t.at.DistanceTo(new Vector2(from.X, from.Z));
			if (d < bd && d < 9f) { bd = d; bestXZ = t.at; h = t.height; }
		}
		var perch = new Vector3(bestXZ.X, Height(bestXZ.X, bestXZ.Y) + Mathf.Clamp(h * 0.62f, 7f, 14f), bestXZ.Y);
		var perchW = ToGlobal(perch);
		Wendigo.Leap(perchW, 0.17f, () =>
		{
			_snowDump.GlobalPosition = perchW;
			_snowDump.Restart();
			AudioDirector.OneShot(this, "snow_whump", 3, perchW + Vector3.Down * 3f, -6f, "Events", 6f, 0.06f);
			AudioDirector.OneShot(this, "trunk_creak", 1, perchW, -10f, "Events", 6f, 0.1f);
		});
		GD.Print($"[story] Act 22: seen - the wendigo leaps into the trees ({Wendigo.Leaps})");
		Stalk = StalkState.Hidden;
		_nextAppear = _stalkClock + _rng.RandfRange(20f, 38f);
		_nextVoice = Mathf.Max(_nextVoice, _stalkClock + 10);
	}

	private void Vanish()
	{
		Wendigo.Visible = false;
		Stalk = StalkState.Hidden;
		_nextAppear = _stalkClock + _rng.RandfRange(12f, 24f);
	}

	/// <summary>A copied voice from the trees, off to one side or behind.</summary>
	private void Voices(PlayerController player, Camera3D cam)
	{
		if (_stalkClock < _nextVoice || _voice.Playing) return;
		_nextVoice = _stalkClock + _rng.RandfRange(34f, 62f);
		if (_voicesLeft.Count == 0) for (int i = 1; i <= 14; i++) _voicesLeft.Add(i);
		int pick = _voicesLeft[_rng.RandiRange(0, _voicesLeft.Count - 1)];
		_voicesLeft.Remove(pick);
		var path = $"res://assets/audio/sfx/wendigo_mimic_{pick:00}.wav";
		if (!ResourceLoader.Exists(path)) return;
		var fwd = -cam.GlobalBasis.Z; fwd.Y = 0; fwd = fwd.Normalized();
		float a = _rng.RandfRange(100f, 170f) * (_rng.Randf() < 0.5f ? 1f : -1f);
		var dir = fwd.Rotated(Vector3.Up, Mathf.DegToRad(a));
		_voice.Stream = GD.Load<AudioStream>(path);
		_voice.GlobalPosition = player.GlobalPosition + dir * _rng.RandfRange(22f, 38f) + Vector3.Up * _rng.RandfRange(1.5f, 6f);
		_voice.PitchScale = _rng.RandfRange(0.96f, 1.02f);
		_voice.Play();
		Mimics++;
		GD.Print($"[story] Act 22: a voice from the trees (mimic {pick})");
	}

	/// <summary>Its steps: a few soft ones behind them while they walk, stopping when they stop.</summary>
	private void Steps(PlayerController player, Camera3D cam)
	{
		float speed = new Vector2(player.Velocity.X, player.Velocity.Z).Length();
		if (_stepsLeft <= 0)
		{
			if (_stalkClock >= _stepBurstEnd && speed > 1.2f && _rng.Randf() < 0.004f)
			{
				_stepsLeft = _rng.RandiRange(4, 7);
				_nextStep = _stalkClock + 0.3;
			}
			return;
		}
		if (speed < 0.5f) { _stepsLeft = 0; _stepBurstEnd = _stalkClock + 12; return; }   // they stopped: so does it
		if (_stalkClock < _nextStep) return;
		_nextStep = _stalkClock + 0.62 * Mathf.Lerp(1.1f, 0.7f, Mathf.Clamp(speed / 5f, 0f, 1f));
		var back = cam.GlobalBasis.Z; back.Y = 0; back = back.Normalized();
		var at = player.GlobalPosition + back * _rng.RandfRange(12f, 18f) + back.Cross(Vector3.Up) * _rng.RandfRange(-4f, 4f);
		AudioDirector.OneShot(this, "wendigo_step", 6, at, -19f, "Events", 3f, 0.08f);
		StalkSteps++;
		if (--_stepsLeft <= 0) _stepBurstEnd = _stalkClock + _rng.RandfRange(18f, 40f);
	}

	/// <summary>For tests: make it appear behind the player now (as the director would).</summary>
	public bool DebugAppearBehind()
	{
		var player = StoryBeat.Player(this);
		var cam = GetViewport()?.GetCamera3D();
		if (player == null || cam == null) return false;
		if (Appearances == 0) Appearances = 1;   // skip the first, on the road
		return TryAppear(player, cam);
	}
}
