using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectDS.Audio;

/// <summary>
/// The game's audio QA and spatial layer (the audio sweep). One place that every positional sound in
/// the game goes through, however the scene that made it set it up.
///
/// <list type="bullet">
/// <item><b>Normalising</b>: every <see cref="AudioStreamPlayer3D"/> added to the tree is checked as it
/// arrives. An unknown bus falls back to Events, an unbounded sound gets a sane max distance, and each
/// sound listens to the reverb zones.</item>
/// <item><b>Occlusion</b>: a sound on the far side of a wall is muffled and quieter. A ray from the
/// listener to the sound, round-robin a few sounds per frame, ramps the sound's low-pass down and its
/// level back, so it moves smoothly as you walk round a corner.</item>
/// <item><b>Rooms</b>: <see cref="Zone"/> puts a reverb space round an interior: a small wooden room, a
/// hall, a long tunnel, a cavern. Sounds inside it ring in it, and while the listener is inside, the
/// player's own sounds (footsteps, breath, cloth) take on the room's reverb too.</item>
/// <item><b>Haunting</b>: <see cref="Haunt"/> gives an interior sounds for its dark corners, played now
/// and then at fixed spots in it, only while the player is near.</item>
/// <item><b>Audit</b>: bad buses, unbounded or over-loud sounds, finished players never freed (leaks),
/// footsteps on surfaces with no footstep sound, and the most sounds ever playing at once, for the
/// tests to report (<see cref="Report"/>).</item>
/// </list>
/// </summary>
public partial class AudioDirector : Node
{
	public static AudioDirector Instance { get; private set; }

	/// <summary>The physics layer the reverb zones sit on (20: nothing else uses it, so no ray or body sees them).</summary>
	public const uint ReverbLayer = 1u << 19;

	public enum Space { Room, Hall, Tunnel, Cavern }

	private static readonly Dictionary<Space, (string bus, float room, float damp, float wet, float amount, float preDelay)> Spaces = new()
	{
		[Space.Room] = ("VerbRoom", 0.32f, 0.55f, 0.28f, 0.45f, 0.01f),
		[Space.Hall] = ("VerbHall", 0.62f, 0.45f, 0.3f, 0.55f, 0.02f),
		[Space.Tunnel] = ("VerbTunnel", 0.78f, 0.3f, 0.32f, 0.65f, 0.05f),
		[Space.Cavern] = ("VerbCavern", 0.95f, 0.25f, 0.36f, 0.75f, 0.08f),
	};

	/// <summary>The player's own reverb outdoors (the Player bus's default).</summary>
	private const float OpenRoom = 0.35f, OpenDamp = 0.6f, OpenWet = 0.02f;

	private sealed class Zone3D { public Area3D Area; public Vector3 Half; public Space Kind; }
	private readonly List<Zone3D> _zones = new();

	private sealed class Tracked
	{
		public AudioStreamPlayer3D P;
		public float Occ, Applied, Written = float.NaN, BaseCutoff;
		public double LastPlayed;
	}
	private readonly List<Tracked> _tracked = new();
	private int _cursor;
	private double _clock;
	private AudioEffectReverb _playerVerb;

	// ---- the audit
	public readonly HashSet<string> BadBuses = new();
	public readonly HashSet<string> Unbounded = new();
	public readonly HashSet<string> TooLoud = new();
	public readonly HashSet<string> SilentSurfaces = new();
	public int MaxConcurrent { get; private set; }
	/// <summary>What was playing at the busiest moment, and which finished sounds were never freed (by name, with counts).</summary>
	public string BusiestMix { get; private set; } = "";
	public string LeakNames { get; private set; } = "";
	public int Leaks { get; private set; }
	public int Occluded { get; private set; }
	public int TrackedCount => _tracked.Count;

	public override void _EnterTree()
	{
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;
		EnsureBuses();
		GetTree().NodeAdded += OnNodeAdded;
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		GetTree().NodeAdded -= OnNodeAdded;
	}

	/// <summary>The reverb buses the zones send to: fully wet, into Master.</summary>
	private void EnsureBuses()
	{
		foreach (var (bus, room, damp, _, _, pre) in Spaces.Values)
		{
			if (AudioServer.GetBusIndex(bus) >= 0) continue;
			int i = AudioServer.BusCount;
			AudioServer.AddBus(i);
			AudioServer.SetBusName(i, bus);
			AudioServer.SetBusSend(i, "Master");
			AudioServer.AddBusEffect(i, new AudioEffectReverb { RoomSize = room, Damping = damp, Spread = 0.9f, Dry = 0f, Wet = 1f, PredelayMsec = pre * 1000f, Hipass = 0.08f });
		}
		int pb = AudioServer.GetBusIndex("Player");
		if (pb >= 0)
			for (int e = 0; e < AudioServer.GetBusEffectCount(pb); e++)
				if (AudioServer.GetBusEffect(pb, e) is AudioEffectReverb v) { _playerVerb = v; break; }
	}

	private void OnNodeAdded(Node n)
	{
		if (n is not AudioStreamPlayer3D p) return;
		// the creator may still be setting it up this frame: look at it once it has settled
		Callable.From(() => Adopt(p)).CallDeferred();
	}

	private void Adopt(AudioStreamPlayer3D p)
	{
		if (!IsInstanceValid(p) || !p.IsInsideTree()) return;
		if (AudioServer.GetBusIndex(p.Bus) < 0) { BadBuses.Add($"{p.Bus} ({Where(p)})"); p.Bus = "Events"; }
		if (p.AttenuationModel != AudioStreamPlayer3D.AttenuationModelEnum.Disabled && p.MaxDistance <= 0f)
		{
			Unbounded.Add(Where(p));
			p.MaxDistance = Mathf.Max(40f, p.UnitSize * 22f);
		}
		if (p.VolumeDb > 8f && p.AttenuationModel != AudioStreamPlayer3D.AttenuationModelEnum.Disabled) TooLoud.Add($"{Where(p)} {p.VolumeDb:0} dB");   // a flat, falloff-free event is loud by design
		p.AreaMask |= ReverbLayer;
		// strict 3D (the audio spatialization sweep): full left/right separation, so a whisper at your left
		// ear is at your left ear and steps behind you sound behind you. The player's own sounds stay centred.
		if (p.Bus != "Player" && p.AttenuationModel != AudioStreamPlayer3D.AttenuationModelEnum.Disabled && Mathf.IsEqualApprox(p.PanningStrength, 1f)) p.PanningStrength = 1.6f;
		// air absorbs the top end over distance (a touch more than Godot's default, for the dark woods)
		if (Mathf.IsEqualApprox(p.AttenuationFilterCutoffHz, 5000f)) { p.AttenuationFilterCutoffHz = 6000f; p.AttenuationFilterDb = -18f; }
		_tracked.Add(new Tracked { P = p, BaseCutoff = p.AttenuationFilterCutoffHz, LastPlayed = _clock });
		p.TreeExiting += () => _tracked.RemoveAll(t => t.P == p);
	}

	private static string Where(Node n)
	{
		var parent = n.GetParent();
		string s = n.Name;
		if (n is AudioStreamPlayer3D a && a.Stream != null) s = System.IO.Path.GetFileNameWithoutExtension(a.Stream.ResourcePath);
		return parent != null ? $"{parent.Name}/{s}" : s;
	}

	// ------------------------------------------------------------------ rooms and haunting

	/// <summary>A reverb space round part of a level: <paramref name="center"/> and <paramref name="size"/>
	/// in <paramref name="parent"/>'s space.</summary>
	public static Area3D Zone(Node3D parent, Vector3 center, Vector3 size, Space kind, string name = "Reverb")
	{
		var (bus, _, _, _, amount, _) = Spaces[kind];
		var a = new Area3D
		{
			Name = name, CollisionLayer = ReverbLayer, CollisionMask = 0, Monitoring = false, Monitorable = true,
			ReverbBusEnabled = true, ReverbBusName = bus, ReverbBusAmount = amount, ReverbBusUniformity = 0.35f,
			Position = center,
		};
		a.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		parent.AddChild(a);
		Instance?._zones.Add(new Zone3D { Area = a, Half = size * 0.5f, Kind = kind });
		a.TreeExiting += () => Instance?._zones.RemoveAll(z => z.Area == a);
		return a;
	}

	/// <summary>Sounds for a space's dark corners: one of <paramref name="sounds"/> now and then at one of
	/// <paramref name="spots"/> (in <paramref name="parent"/>'s space), while the player is within
	/// <paramref name="radius"/> of the first spot and <paramref name="active"/> says so.</summary>
	public static void Haunt(Node3D parent, Vector3[] spots, string[] sounds, Vector2 interval, float db, float radius, System.Func<bool> active = null, string bus = "Unnatural")
	{
		var h = new Haunting { Name = "Haunting", Spots = spots, Sounds = sounds, Interval = interval, Db = db, Radius = radius, Active = active, Bus = bus };
		parent.AddChild(h);
	}

	private partial class Haunting : Node3D
	{
		public Vector3[] Spots;
		public string[] Sounds;
		public Vector2 Interval;
		public float Db, Radius;
		public string Bus;
		public System.Func<bool> Active;
		private double _t = -1;
		private readonly RandomNumberGenerator _rng = new();

		public override void _Ready() { _rng.Randomize(); _t = _rng.RandfRange(Interval.X * 0.5f, Interval.Y); }

		public override void _Process(double delta)
		{
			var cam = GetViewport()?.GetCamera3D();
			var parent = GetParent<Node3D>();
			if (cam == null || parent == null || Spots.Length == 0) return;
			if (parent.ToGlobal(Spots[0]).DistanceTo(cam.GlobalPosition) > Radius || (Active != null && !Active())) return;
			_t -= delta;
			if (_t > 0) return;
			_t = _rng.RandfRange(Interval.X, Interval.Y);
			string name = Sounds[_rng.RandiRange(0, Sounds.Length - 1)];
			string path = $"res://assets/audio/sfx/{name}_{_rng.RandiRange(1, 3):00}.wav";
			if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}_01.wav";
			if (!ResourceLoader.Exists(path)) return;
			var p = new AudioStreamPlayer3D
			{
				Stream = GD.Load<AudioStream>(path), Bus = Bus, VolumeDb = Db, UnitSize = 6f, MaxDistance = Radius * 1.3f,
				PitchScale = _rng.RandfRange(0.85f, 1.05f),
			};
			parent.AddChild(p);
			p.Position = Spots[_rng.RandiRange(0, Spots.Length - 1)];
			p.Finished += p.QueueFree;
			p.Play();
		}
	}

	// ------------------------------------------------------------------ per frame

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_clock += delta;
		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return;
		Vector3 ear = cam.GlobalPosition;

		// the player's own reverb follows the space they're in
		Space? here = null;
		foreach (var z in _zones)
		{
			if (!IsInstanceValid(z.Area) || !z.Area.IsInsideTree()) continue;
			Vector3 l = z.Area.ToLocal(ear);
			if (Mathf.Abs(l.X) <= z.Half.X && Mathf.Abs(l.Y) <= z.Half.Y && Mathf.Abs(l.Z) <= z.Half.Z) { if (here == null || z.Kind > here) here = z.Kind; }
		}
		if (_playerVerb != null)
		{
			var (_, room, damp, wet, _, _) = here is { } k ? Spaces[k] : (null, OpenRoom, OpenDamp, OpenWet, 0f, 0f);
			float s = Mathf.Min(1f, dt * 3f);
			_playerVerb.RoomSize = Mathf.Lerp(_playerVerb.RoomSize, room, s);
			_playerVerb.Damping = Mathf.Lerp(_playerVerb.Damping, damp, s);
			_playerVerb.Wet = Mathf.Lerp(_playerVerb.Wet, wet * 0.7f, s);
		}

		// occlusion, a few sounds a frame
		int playing = 0, occluded = 0, leaks = 0;
		List<string> playingNames = null, leakNames = null;
		var space = cam.GetWorld3D()?.DirectSpaceState;
		int budget = Mathf.Min(_tracked.Count, 12);
		for (int i = 0; i < _tracked.Count; i++)
		{
			var t = _tracked[i];
			if (!IsInstanceValid(t.P)) continue;
			if (t.P.Playing) { playing++; t.LastPlayed = _clock; (playingNames ??= new()).Add(Where(t.P)); }
			// a leak: a one-shot dropped into the level (not an emitter's reusable voice) that finished long ago and was never freed
			else if (!t.P.Autoplay && _clock - t.LastPlayed > 60 && !IsLooping(t.P) && t.P.GetParent() == GetTree().CurrentScene) { leaks++; (leakNames ??= new()).Add(Where(t.P)); }
			if (t.Occ > 0.5f) occluded++;
		}
		for (int n = 0; n < budget && _tracked.Count > 0; n++)
		{
			_cursor = (_cursor + 1) % _tracked.Count;
			var t = _tracked[_cursor];
			if (!IsInstanceValid(t.P) || !t.P.Playing || space == null) continue;
			if (t.P.AttenuationModel == AudioStreamPlayer3D.AttenuationModelEnum.Disabled || t.P.HasMeta("no_occlusion") || t.P.Bus == "Player" || t.P.Bus == "Music" || t.P.Bus == "Radio") continue;
			float target = Blocked(space, ear, t.P.GlobalPosition) ? 1f : 0f;
			t.Occ = Mathf.MoveToward(t.Occ, target, 0.35f);
			ApplyOcclusion(t);
		}
		if (playing > MaxConcurrent) { MaxConcurrent = playing; BusiestMix = Summarise(playingNames); }
		if (leaks > Leaks) LeakNames = Summarise(leakNames);
		Leaks = Mathf.Max(Leaks, leaks);
		Occluded = occluded;
	}

	private static string Summarise(List<string> names) => names == null ? "" :
		string.Join(", ", names.GroupBy(n => n).OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key} x{g.Count()}"));

	private static bool IsLooping(AudioStreamPlayer3D p) => p.Stream is AudioStreamWav w && w.LoopMode != AudioStreamWav.LoopModeEnum.Disabled;

	/// <summary>Is there level geometry between the ear and the sound? (The last metre by the sound is
	/// skipped, so a sound set just inside a door, a wall or its own prop isn't counted as behind it.)</summary>
	private static bool Blocked(PhysicsDirectSpaceState3D space, Vector3 ear, Vector3 src)
	{
		Vector3 d = src - ear;
		float len = d.Length();
		if (len < 1.5f) return false;
		var q = PhysicsRayQueryParameters3D.Create(ear, ear + d * ((len - 1f) / len), 1u);
		q.CollideWithAreas = false;
		q.HitFromInside = false;
		var hit = space.IntersectRay(q);
		return hit.Count > 0;
	}

	/// <summary>Muffled and pulled back while behind something: the low-pass drops to a dull thud and the
	/// level falls about 8 dB (on top of whatever the sound's owner does with its volume).</summary>
	private static void ApplyOcclusion(Tracked t)
	{
		var p = t.P;
		float offset = -8f * t.Occ;
		float cur = p.VolumeDb;
		float baseDb = !float.IsNaN(t.Written) && Mathf.IsEqualApprox(cur, t.Written) ? cur - t.Applied : cur;
		p.VolumeDb = baseDb + offset;
		t.Applied = offset;
		t.Written = p.VolumeDb;
		if (t.BaseCutoff < 20000f) p.AttenuationFilterCutoffHz = Mathf.Lerp(t.BaseCutoff, 650f, t.Occ);
		else if (t.Occ > 0.01f) { p.AttenuationFilterCutoffHz = Mathf.Lerp(20000f, 650f, t.Occ); p.AttenuationFilterDb = -24f; }
		else p.AttenuationFilterCutoffHz = 20500f;
	}

	/// <summary>A one-shot at a world point (<paramref name="name"/>_01.._nn, or the bare name), freed when it
	/// ends. Null world point: in the listener's head (2D), for the player's own hands.</summary>
	public static void OneShot(Node from, string name, int variants, Vector3? at, float db, string bus = "Events", float unit = 3f, float pitchJitter = 0.05f)
	{
		string path = $"res://assets/audio/sfx/{name}_{GD.RandRange(1, Mathf.Max(1, variants)):00}.wav";
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}.wav";
		if (!ResourceLoader.Exists(path) || from?.GetTree()?.CurrentScene is not { } root) return;
		float pitch = 1f + (float)GD.RandRange(-pitchJitter, pitchJitter);
		if (at is { } w)
		{
			var p = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = bus, VolumeDb = db, UnitSize = unit, MaxDistance = unit * 16f, PitchScale = pitch };
			root.AddChild(p);
			p.GlobalPosition = w;
			p.Finished += p.QueueFree;
			p.Play();
		}
		else
		{
			var p = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(path), Bus = bus, VolumeDb = db, PitchScale = pitch };
			root.AddChild(p);
			p.Finished += p.QueueFree;
			p.Play();
		}
	}

	/// <summary>A hit that must land at full weight but still come from somewhere: no distance falloff and
	/// no occlusion (a fist on the wall right beside you is loud through the logs), only panned to where it is.</summary>
	public static void Directional(Node from, string path, Vector3 at, float db, float pitch = 1f, string bus = "Events")
	{
		if (!ResourceLoader.Exists(path) || from?.GetTree()?.CurrentScene is not { } root) return;
		var p = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = bus, VolumeDb = db, PitchScale = pitch,
			AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled, AttenuationFilterCutoffHz = 20500f,
		};
		p.SetMeta("no_occlusion", true);
		root.AddChild(p);
		p.GlobalPosition = at;
		p.Finished += p.QueueFree;
		p.Play();
	}

	/// <summary>Footsteps landed on something with no footstep sound of its own (reported once per collider).</summary>
	public void NoteSilentSurface(string what) => SilentSurfaces.Add(what);

	/// <summary>A one-line summary for the tests.</summary>
	public string Report() =>
		$"bad buses [{string.Join(", ", BadBuses)}], unbounded [{string.Join(", ", Unbounded.Take(8))}], too loud [{string.Join(", ", TooLoud)}], " +
		$"silent surfaces [{string.Join(", ", SilentSurfaces.Take(8))}], leaks {Leaks} [{LeakNames}], max {MaxConcurrent} playing at once [{BusiestMix}], {_tracked.Count} tracked, {_zones.Count} rooms";
}
