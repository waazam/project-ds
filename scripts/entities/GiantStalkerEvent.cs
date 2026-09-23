using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// Act 7's one-time set-piece (Dan, 2026-09-22: moved here from the storm walk, where the
/// stalker now keeps the suspense on its own). From the moment checkpoint 6 (the burning cabin in
/// view) arms it, a giant version of the stalker is already walking the sky lane far beyond the
/// cabin, behind the far tree line, coming up out of the murk over seconds (never a pop): the gap
/// across the creek lets only its neck and head show above the trees. It paces the lane, slow
/// pass after slow pass, until the player has stood at the lookout looking at the fire (or passed
/// it); that pass is its last, and it dissolves as it walks on out of the lane. Its deep, distant
/// rattle rides with it, in bursts, with the soft thud of its stride; then it is gone until the last staircase. The player keeps control: it is a
/// glimpse while they stand looking at the fire, not a cutscene.
///
/// Where: <see cref="BeyondCabin"/> metres past the cabin along the lookout-to-cabin line,
/// crossing side to side over <see cref="SweepWidth"/>. Its hide is drawn fog-free
/// (giant_skin.gdshader, haze), so distance never swallows it.
///
/// Restore: done for good once <see cref="StoryManager.Flag.GiantEventDone"/> is saved. A
/// Continue at checkpoint 6 before the flag (the checkpoint saves as the fire comes into view,
/// the flag only after the crossing) replays the sight once the level has started; a save
/// further on without the flag (older saves) skips it silently.
/// </summary>
public partial class GiantStalkerEvent : Node
{
	/// <summary>After checkpoint 6: the line ("Everything he wrote is in there.") has landed first.</summary>
	[Export] public float DelaySeconds = 1.5f;   // the player is already at the lookout looking (2026-09-22)
	[Export] public float AutoTestDelaySeconds = 1.0f;
	/// <summary>Body height is about 2.2x this: at 22 the neck base stands ~44 m up, over the 26-40 m firs beyond the cabin.</summary>
	[Export] public float BodyScale = 25f;   // 22 was invisible over the far canopy, 30 too giant (Dan, 2026-09-22)
	/// <summary>How far past the cabin (from the lookout's side) it crosses: about 150 m from the lookout.</summary>
	[Export] public float BeyondCabin = 70f;
	[Export] public float SweepWidth = 60f;   // within about 11 degrees of the fire's bearing: the one open sky lane from the lookout (2026-09-22)
	[Export] public float DurationSeconds = 50f;   // one slow pass of the lane; it repeats until the player has reached the lookout (2026-09-22)
	/// <summary>Dither-in over this many seconds as it walks in out of the murk (never a pop).</summary>
	[Export] public float EmergeSeconds = 7f;
	/// <summary>Dither-out over this many seconds as it walks on out of the lane, once released.</summary>
	[Export] public float DissolveSeconds = 5f;
	[Export] public Vector2 ThudInterval = new(1.3f, 1.9f);
	/// <summary>Fog over its hide (giant_skin haze): 0.75 = a shadow in the murk, barely more than a shape.</summary>
	[Export] public float Haze = 0.75f;
	/// <summary>Shading left in the shape (0 = a flat cut-out).</summary>
	[Export] public float Contrast = 0.15f;
	/// <summary>Dither coverage: under 1 it never fully resolves.</summary>
	[Export] public float Visibility = 1f;
	/// <summary>Bob per footfall as a fraction of body height (it dips onto each planted foot).</summary>
	[Export] public float BobFraction = 0.025f;
	[Export] public float RollDegrees = 2.5f;
	[Export] public float LeanDegrees = 3f;
	/// <summary>How far the soles sink into the undergrowth, as a fraction of BodyScale.</summary>
	[Export] public float SoleSink = 0.03f;
	/// <summary>Unused since 2026-09-22 (Dan: no roar); kept so scene overrides still load.</summary>
	[Export] public float BellowVolumeDb = 3f;

	private static AudioStream Load(string path) => ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;

	private readonly RandomNumberGenerator _rng = new();
	private bool _done;

	/// <summary>For tests: whether the crossing has started (or the saved story is past it).</summary>
	public bool Done => _done;
	/// <summary>For tests: the giant's body while it is crossing (null before and after).</summary>
	public Node3D Body { get; private set; }

	public override void _Ready()
	{
		SetProcess(true);   // the far footfalls poll the story window (they sleep once the cabin is entered)
		Callable.From(Restore).CallDeferred();
		if (StoryManager.Instance is { } s) s.CheckpointReached += OnCheckpoint;
	}

	public override void _ExitTree()
	{
		if (StoryManager.Instance is { } s) s.CheckpointReached -= OnCheckpoint;
	}

	private void Restore()
	{
		var s = StoryManager.Instance;
		if (s == null || _done) return;
		if (s.GiantEventDone) { _done = true; return; }   // the far footfalls keep polling their own window
		if (s.Current == Checkpoint.Act7CabinBurning) Arm();
		else if (s.Current > Checkpoint.Act7CabinBurning) _done = true;
	}

	private void OnCheckpoint(Checkpoint cp)
	{
		if (cp == Checkpoint.Act7CabinBurning) Arm();
	}

	// ------------------------------------------------------------------ when it crosses

	/// <summary>Standing this close to the lookout (metres) and looking at the fire starts the crossing at once.</summary>
	[Export] public float LookoutRadius = 12f;
	/// <summary>Passing this close to the lookout without stopping starts it anyway after <see cref="PassGraceSeconds"/>.</summary>
	[Export] public float PassRadius = 30f;
	[Export] public float PassGraceSeconds = 5f;
	/// <summary>For tests: checkpoint 6 has been reached; the crossing waits for the player at the lookout.</summary>
	public bool Armed => _armed;

	private bool _armed;
	private double _nearSince = -1;

	/// <summary>
	/// Checkpoint 6 (the burning cabin in view) only arms it. The crossing starts when the player is at the
	/// lookout looking at the fire (Dan, 2026-09-22: the sight beat can fire long before the lookout, and the
	/// crossing was over before anyone reached the open sky lane), or a few seconds after they pass the
	/// lookout without stopping.
	/// </summary>
	private void Arm()
	{
		if (_done || _armed) return;
		_armed = true;
		_nearSince = -1;
		SetProcess(true);
		// It is already out there walking, swallowed by the murk, from the moment checkpoint 6 arms it (Dan,
		// 2026-09-22: it must never flash into view): the crossing loop starts now and emerges over seconds.
		GD.Print("[story] Act 7: the giant is out there (emerging); the lookout releases it");
		Cutscene.Run(this, Cross);
	}

	private void TickArmed(double delta)
	{
		var player = StoryBeat.Player(this);
		var lookout = GetTree().GetFirstNodeInGroup("fire_lookout_marker") as Node3D;
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Node3D;
		if (player == null) return;
		if (lookout == null) { Fire(); return; }   // no lookout in this level: the old behaviour (at once)
		float d = new Vector2(player.GlobalPosition.X - lookout.GlobalPosition.X, player.GlobalPosition.Z - lookout.GlobalPosition.Z).Length();
		if (d > PassRadius) { _nearSince = -1; return; }
		if (_nearSince < 0) _nearSince = 0;
		_nearSince += delta;
		bool looking = false;
		if (cabin != null && player.CameraRig?.Camera is { } cam)
		{
			Vector3 to = cabin.GlobalPosition + Vector3.Up * 2.5f - cam.GlobalPosition;
			looking = (-cam.GlobalBasis.Z).Dot(to.Normalized()) > 0.5f;
		}
		if ((d <= LookoutRadius && looking) || _nearSince >= PassGraceSeconds) Fire();
	}

	/// <summary>The player has stood at the lookout (or passed it): the current pass is its last; it walks on out of the lane.</summary>
	private bool _released;
	public bool Released => _released;

	private void Fire()
	{
		if (_released) return;
		_released = true;
		_armed = false;
		GD.Print("[story] Act 7: the lookout: the giant walks on");
	}

	// ------------------------------------------------------------------ its rattle

	/// <summary>The giant's rattle at the crossing (Dan, 2026-09-22: deep, loud, distant), in bursts like the stalker's.</summary>
	[Export] public float RattleDb = 4f;
	/// <summary>The same rattle under the far footfalls on the storm walk, much quieter.</summary>
	[Export] public float FarRattleDb = -10f;
	private const string RattlePath = "res://assets/audio/sfx/creature_giant_rattle_loop_01.wav";

	private sealed class GiantRattle
	{
		public AudioStreamPlayer3D Player;
		public AmbienceLoop Loop;
		public float Db;
		public bool On = true;
		public double Timer = 2.0;
	}

	/// <summary>Starts the rattle at a world point, fading in over <paramref name="fadeIn"/> seconds; null if the file is missing.</summary>
	private GiantRattle StartRattle(Node parent, Vector3 at, float db, float fadeIn)
	{
		if (!ResourceLoader.Exists(RattlePath)) return null;
		var p = new AudioStreamPlayer3D { Name = "GiantRattle", Bus = "Unnatural", UnitSize = 60f, MaxDistance = 600f, TopLevel = true, VolumeDb = -40f };
		parent.AddChild(p);
		p.GlobalPosition = at;
		var loop = new AmbienceLoop { Name = "Loop", StreamPath = RattlePath, BaseVolumeDb = db, Gain = 1f };
		p.AddChild(loop);
		StoryBeat.FadeIn(p, 0f, fadeIn);
		return new GiantRattle { Player = p, Loop = loop, Db = db, Timer = _rng.RandfRange(2f, 4f) };
	}

	/// <summary>Bursts on 2-4 s, silences 1-3 s, like the stalker's gating; the source follows <paramref name="at"/>.</summary>
	private void TickRattle(GiantRattle r, Vector3 at, float dt)
	{
		if (r == null || !IsInstanceValid(r.Player)) return;
		r.Player.GlobalPosition = at;
		r.Timer -= dt;
		if (r.Timer <= 0f)
		{
			r.On = !r.On;
			r.Timer = r.On ? _rng.RandfRange(2f, 4f) : _rng.RandfRange(1f, 3f);
		}
		r.Loop.Gain = Mathf.MoveToward(r.Loop.Gain, r.On ? 1f : 0.1f, dt * 1.5f);
	}

	/// <summary>Fades the rattle out over <paramref name="seconds"/> and frees it.</summary>
	private static void StopRattle(GiantRattle r, float seconds)
	{
		if (r == null || !IsInstanceValid(r.Player)) return;
		var tw = r.Player.CreateTween();
		tw.TweenProperty(r.Player, "volume_db", -60f, seconds);
		tw.TweenCallback(Callable.From(r.Player.QueueFree));
	}


	// ================================================================== the storm walk: far footfalls

	/// <summary>Seconds between bouts of far footfalls on the storm walk (the stalker's rattle stays the main scare).</summary>
	[Export] public Vector2 FarBoutInterval = new(30f, 70f);
	[Export] public Vector2 FarBoutIntervalAutoTest = new(8f, 15f);
	/// <summary>Where the far walker is heard from: this far off, at ground level, in a random direction.</summary>
	[Export] public Vector2 FarDistance = new(180f, 280f);
	/// <summary>Level of the far thuds (already sub-only rumbles): felt at the edge of hearing.</summary>
	[Export] public float FarThudDb = -10f;

	/// <summary>For tests: how many bouts of far footfalls have played on the storm walk.</summary>
	public int FarFootfallBouts { get; private set; }

	private double _farClock, _farNextAt = -1;
	private bool _farBusy;

	/// <summary>The window: from the storm (the camp) until the cabin is entered, outdoors. Restore-safe: it only reads the story.</summary>
	private static bool FarWindow(StoryManager s) =>
		s != null && s.HasFlag(StoryManager.Flag.StormStarted) && s.Current >= Checkpoint.Act3DoorBoarded && s.Current < Checkpoint.Act5CabinEntered;

	public override void _Process(double delta)
	{
		if (_armed) { TickArmed(delta); return; }
		var s = StoryManager.Instance;
		// Past the storm walk and not armed: nothing to do until checkpoint 6 arms the crossing (cheap to keep polling).
		if (!FarWindow(s)) return;
		if (_farBusy || ForestAmbienceManager.Instance is { IsIndoor: true }) return;
		_farClock += delta;
		if (_farNextAt < 0)
		{
			var iv = GameSettings.Instance.AutoTest ? FarBoutIntervalAutoTest : FarBoutInterval;
			// The first bout comes sooner than a full interval: the walk out of the camp should not stay empty.
			_farNextAt = _farClock + _rng.RandfRange(iv.X, iv.Y) * (FarFootfallBouts == 0 ? 0.5f : 1f);
			return;
		}
		if (_farClock < _farNextAt) return;
		_farNextAt = -1;
		_ = Cutscene.Run(this, FarBout);
	}

	/// <summary>
	/// Dan (2026-09-22): "some giant footsteps for the hollow path walk to make it spooky". Three to five
	/// thuds 1.4-2.0 s apart from one far direction, the source drifting a few metres between thuds as if
	/// something the size of the trees walked past out there. Never a body, never a bellow.
	/// </summary>
	private async Task FarBout(CancellationToken ct)
	{
		_farBusy = true;
		try
		{
			var player = StoryBeat.Player(this);
			if (player == null) return;
			var steps = new List<AudioStream>();
			foreach (var name in new[] { "giant_step_01", "giant_step_02", "giant_step_03" })
				if (Load($"res://assets/audio/sfx/{name}.wav") is { } st) steps.Add(st);
			if (steps.Count == 0) return;
			var terrain = GroundSnap.FindTerrain(this);
			float ang = _rng.RandfRange(0f, Mathf.Tau);
			float dist = _rng.RandfRange(FarDistance.X, FarDistance.Y);
			Vector3 dir = new(Mathf.Cos(ang), 0, Mathf.Sin(ang));
			Vector3 along = dir.Cross(Vector3.Up) * (_rng.Randf() < 0.5f ? -1f : 1f);   // it walks across, not toward
			Vector3 at = player.GlobalPosition + dir * dist;
			var thud = new AudioStreamPlayer3D { Name = "FarFootfalls", Bus = "Unnatural", UnitSize = 40f, MaxDistance = 500f, TopLevel = true };
			Cutscene.SceneRoot(this).AddChild(thud);
			try
			{
				int count = _rng.RandiRange(3, 5);
				FarFootfallBouts++;
				GD.Print($"[story] the storm walk: far footfalls, {count} thuds {dist:0} m off");
				// Its rattle, far off with the footfalls (much quieter than at the crossing).
				Vector3 first = at; if (terrain != null) first.Y = terrain.HeightAt(first.X, first.Z);
				var rattle = StartRattle(Cutscene.SceneRoot(this), first + Vector3.Up * (BodyScale * 2f), FarRattleDb, 1.5f);
				try
				{
					for (int i = 0; i < count; i++)
					{
						if (!FarWindow(StoryManager.Instance) || ForestAmbienceManager.Instance is { IsIndoor: true }) break;
						Vector3 p = at + along * (i * _rng.RandfRange(6f, 10f));
						if (terrain != null) p.Y = terrain.HeightAt(p.X, p.Z);
						thud.GlobalPosition = p;
						thud.Stream = steps[_rng.RandiRange(0, steps.Count - 1)];
						thud.VolumeDb = FarThudDb + _rng.RandfRange(-2f, 1f);
						thud.PitchScale = _rng.RandfRange(0.85f, 1.0f);
						thud.Play();
						double wait = _rng.RandfRange(1.4f, 2.0f), w = 0;
						while (w < wait) { float dt = (float)GetProcessDeltaTime(); w += dt; TickRattle(rattle, p + Vector3.Up * (BodyScale * 2f), dt); await Cutscene.Frame(this, ct); }
					}
					await Cutscene.Wait(this, 1.5, ct);   // let the last rumble die before the player is free
				}
				finally { StopRattle(rattle, 3f); }
			}
			finally { if (IsInstanceValid(thud)) thud.QueueFree(); }
		}
		finally { _farBusy = false; }
	}

	private async Task Cross(CancellationToken ct)
	{
		// On Continue this starts at level load: let the opening fade hand control over first.
		while (GameFlow.Instance is { Started: false }) await Cutscene.Frame(this, ct);
		await Cutscene.Wait(this, GameSettings.Instance.AutoTest ? AutoTestDelaySeconds : DelaySeconds, ct);
		_done = true;

		var player = StoryBeat.Player(this);
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Node3D;
		var lookout = GetTree().GetFirstNodeInGroup("fire_lookout_marker") as Node3D;
		if (player == null || cabin == null) { StoryManager.Instance?.MarkGiantEventDone(); return; }
		var terrain = GroundSnap.FindTerrain(this);

		// Beyond the cabin, on the far side of it from the lookout (from the player, if the level has no lookout).
		Vector3 from = lookout?.GlobalPosition ?? player.GlobalPosition;
		Vector3 fwd = cabin.GlobalPosition - from; fwd.Y = 0;
		fwd = fwd.LengthSquared() > 0.01f ? fwd.Normalized() : Vector3.Forward;
		Vector3 centre = cabin.GlobalPosition + fwd * BeyondCabin;
		Vector3 right = fwd.Cross(Vector3.Up).Normalized();
		Vector3 start = centre - right * SweepWidth * 0.5f;
		Vector3 end = centre + right * SweepWidth * 0.5f;
		Vector3 walkDir = right;
		float Ground(Vector3 p) => terrain?.HeightAt(p.X, p.Z) ?? p.Y;
		start.Y = Ground(start); end.Y = Ground(end);

		var skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/giant_skin.gdshader") };
		// A shadow in the murk (Dan, 2026-09-22: "way more foggy, more like a shadow, less visible"): most of
		// the fog colour over it, the shading flattened, the eyes dark, and a dither that never fully resolves.
		skin.SetShaderParameter("haze", Haze);
		skin.SetShaderParameter("haze_color", new Color(0.02f, 0.02f, 0.03f)   /* near black: darker than the dim sky, so it reads as a silhouette over the trees */);
		skin.SetShaderParameter("contrast", Contrast);
		skin.SetShaderParameter("albedo", new Color(0.02f, 0.02f, 0.03f));
		skin.SetShaderParameter("face_tint", new Color(0.14f, 0.14f, 0.15f));
		skin.SetShaderParameter("visibility", 0f);   // it walks in out of the murk: the dither brings it up over EmergeSeconds
		skin.SetShaderParameter("wetness", 0.1f);
		skin.SetShaderParameter("eye_glow", 0f);
		var body = new StalkerBody { Name = "Act7Giant", Skin = skin, Size = BodyScale, SwaySeconds = 22f, SwayDegrees = 0.8f, HeadDriftDegrees = 1.5f, WalkAmount = 1f };
		// Must be in the tree before GlobalPosition/LookAt: Godot can't resolve a global transform
		// for an orphan node, and silently no-ops (with a console warning) instead.
		Cutscene.SceneRoot(this).AddChild(body);
		Body = body;
		try
		{
			body.GlobalPosition = start;
			// StalkerBody faces +Z: LookAt aims -Z, so look away from the walk to face along it.
			body.LookAt(body.GlobalPosition - walkDir, Vector3.Up);
			Basis facing = body.GlobalBasis;

			var steps = new List<AudioStream>();
			foreach (var name in new[] { "giant_step_01", "giant_step_02", "giant_step_03" })
			{
				string p = $"res://assets/audio/sfx/{name}.wav";
				if (ResourceLoader.Exists(p)) steps.Add(GD.Load<AudioStream>(p));
			}
			var voice = new AudioStreamPlayer3D { Bus = "Unnatural", UnitSize = 40f, MaxDistance = 400f, TopLevel = true };
			body.AddChild(voice);
			// No voice at all (Dan, 2026-09-22: the roar is not needed): the thuds carry it, and the woods stay quiet around it.

			GD.Print("[story] Act 7: the giant crosses beyond the burning cabin");
			// Its rattle from its head as it walks: deep, loud, distant, in bursts; fades out over 3 s as it goes.
			var rattle = StartRattle(Cutscene.SceneRoot(this), body.EyesWorld, RattleDb, 2f);
			// The stride: the gait phase advances pi per step; a step's length in time is rolled at each
			// footfall (ThudInterval), and the thud plays exactly where the phase crosses a footfall, so the
			// sound lands when the foot does. The body dips onto each planted foot and rises between.
			double t = 0, phase = 0, stepSeconds = _rng.RandfRange(ThudInterval.X, ThudInterval.Y);
			int footfalls = 0;
			float height = BodyScale * 2.2f;
			float bobAmp = height * BobFraction, rollAmp = Mathf.DegToRad(RollDegrees), leanAmp = Mathf.DegToRad(LeanDegrees);
			// Where the soles sit under the origin: the feet are authored around y = 0, so sink them a little
			// into the undergrowth rather than letting a toe hover on it.
			float sink = BodyScale * SoleSink;
			// The timeline (Dan, 2026-09-22: it must already be seen from a distance, never flash in): it is walking the
			// lane from the moment checkpoint 6 arms it, dithering IN over EmergeSeconds as it comes out of the murk; each
			// pass of the lane takes DurationSeconds and the passes repeat (turning at each end) until the player has
			// stood at the lookout (Released); the pass under way then finishes, and it dithers OUT over DissolveSeconds
			// as it walks on out of the lane. Every appearance and disappearance is a fade, never a jump.
			int pass = 0;
			bool dissolving = false;
			double dissolveT = 0;
			float vis = 0f;
			double life = 0;
			while (true)
			{
				float dt = (float)GetProcessDeltaTime();
				t += dt; life += dt;
				if (t >= DurationSeconds)
				{
					if (_released || dissolving) break;
					// Turn at the end of the lane and walk back (a new pass); the player has not reached the lookout yet.
					t = 0; pass++;
					(start, end) = (end, start);
					walkDir = -walkDir;
					body.LookAt(body.GlobalPosition - walkDir, Vector3.Up);
					facing = body.GlobalBasis;
				}
				// Released: begin dissolving so that it is gone by the time this pass ends (or after DissolveSeconds at least).
				if (_released && !dissolving && (DurationSeconds - t) <= DissolveSeconds) { dissolving = true; dissolveT = 0; }
				if (dissolving) { dissolveT += dt; if (dissolveT >= DissolveSeconds) break; }
				vis = dissolving
					? Visibility * Mathf.Clamp(1f - (float)(dissolveT / DissolveSeconds), 0f, 1f)
					: Visibility * Mathf.Clamp((float)(life / EmergeSeconds), 0f, 1f);
				skin.SetShaderParameter("visibility", vis);
				double before = phase;
				phase += Mathf.Pi * dt / stepSeconds;
				bool footDown = (int)(phase / Mathf.Pi) > (int)(before / Mathf.Pi);
				float u = (float)(t / DurationSeconds);
				Vector3 p = start.Lerp(end, u);
				// Feet on the ground: the lowest of the two soles' ground heights, so a foot never hangs over a dip.
				Vector3 side = fwd * (0.15f * BodyScale);   // across the walk (the walk runs along right)
				float ground = Mathf.Min(Ground(p - side), Ground(p + side));
				float land = Mathf.Abs(Mathf.Sin((float)phase));   // 0 at each footfall, 1 mid-stride
				p.Y = ground - sink + bobAmp * land;
				body.GlobalPosition = p;
				// Roll onto the planted foot (alternating sides), lean into the walk, a breath of yaw with the swing.
				float swing = Mathf.Sin((float)phase);
				body.GlobalBasis = facing * Basis.FromEuler(new Vector3(leanAmp, 0.02f * swing, rollAmp * swing));
				body.WalkPhase = (float)phase;
				voice.GlobalPosition = p;
				TickRattle(rattle, body.EyesWorld, dt);
				if (dissolving && rattle != null) { StopRattle(rattle, DissolveSeconds); rattle = null; }
				if (footDown && steps.Count > 0)
				{
					footfalls++;
					stepSeconds = _rng.RandfRange(ThudInterval.X, ThudInterval.Y);
					voice.Stream = steps[_rng.RandiRange(0, steps.Count - 1)];
					voice.VolumeDb = _rng.RandfRange(-4f, 0f);   // felt more than heard: never a gunshot
					voice.PitchScale = _rng.RandfRange(0.85f, 1.0f);
					voice.Play();
				}
				await Cutscene.Frame(this, ct);
			}
			GD.Print($"[story] Act 7: the giant is gone ({footfalls} footfalls, {pass + 1} pass(es), {life:0} s out there)");
			StopRattle(rattle, 3f);
		}
		finally
		{
			Body = null;
			if (IsInstanceValid(body)) body.QueueFree();
		}
		StoryManager.Instance?.MarkGiantEventDone();
	}
}
