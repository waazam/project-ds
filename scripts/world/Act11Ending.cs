using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Act 11, "The Third Man": the walkie-talkie the player just picked up in the
/// bunker's maze starts speaking. They're carried back outside into the woods,
/// a radio voice asks "Did you see them?", they shout "Who are you!" back, and
/// the radio goes to static — the compass then points at the very first
/// staircase, now rebuilt impossibly tall and already half-swallowed by fog
/// from a distance. The radio keeps asking on the way (six short questions, on
/// the walk, at the fog, twice on the climb, at the top, and once more over
/// black; never answered). The player climbs the flight themselves, and cannot
/// come back down (<see cref="OneWayFlight"/>); the hum rises with them. On the
/// top landing the broken newel post is right there: the cap they have carried
/// since the cabin goes back on it (E), grinds home in a purple flash, the flight
/// is whole, and the giant (Act 7's) is standing in front of them; the view is
/// driven up its body to its eyes, which open red in silence; they tremble as
/// it leans in, and blink out into black looking it in the eyes (the pass-out).
/// Over black, the radio's last "Did you see them?", then the credits, and the menu.
///
/// Restore: once the radio exchange is done (<see cref="StoryManager.Flag.Act11DialogueDone"/>)
/// the stairs are tall on load (the one length rule is <see cref="StairsState"/>) and the flight
/// is one-way, the fog-ramp zone and the cap's E-point waiting (a Continue inside the fog radius
/// ramps the fog at once). <see cref="StoryManager.Flag.NewelSeated"/> is set only at the touch,
/// together with checkpoint 9, so quitting mid-ending replays the placement on Continue (the cap
/// is still in the saved inventory). At checkpoint 8 without the exchange, the radio sequence
/// plays again; after it, the walk-line is simply skipped. The radio's static runs on its own
/// "Radio" bus.
/// </summary>
public partial class Act11Ending : Node3D
{
	[Export] public NodePath OriginalStairsPath = "..";
	[Export] public float BodyScale = 24f;
	/// <summary>How far in front of the player it stands when the cap seats (metres): far enough back that its eyes
	/// (about 47 m up at BodyScale 24) sit in front of and above them, a 45-55 degree look-up, not overhead.</summary>
	[Export] public float GiantDistance = 44f;
	/// <summary>How much closer it leans in over them during the trembling (metres; modest, so it stays in front).</summary>
	[Export] public float GiantLeanIn = 6f;
	/// <summary>Seconds the view takes to climb its body from its shins to its eyes.</summary>
	[Export] public float LookUpSeconds = 4.5f;
	/// <summary>Seconds the trembling builds while it leans in.</summary>
	[Export] public float TrembleSeconds = 4.5f;
	/// <summary>Peak shiver of the view, radians per frame, at the end of the trembling.</summary>
	[Export] public float TrembleRadians = 0.006f;
	/// <summary>Seconds of blinking into black (the pass-out), as Act 1's collapse.</summary>
	[Export] public float PassOutSeconds = 4.5f;
	/// <summary>Seconds the hum takes to die to silence once the screen is black, before the last radio line.</summary>
	[Export] public float HumOutSeconds = 4f;
	[Export] public float BlinkSeconds = 1.4f;
	[Export] public float BlinkVignette = 2.4f;
	[Export] public float FogRampRadius = 70f;
	/// <summary>Silence priority: above the storm's, so the climb is silent whatever else is going on.</summary>
	[Export] public int SilencePriority = 100;
	[ExportGroup("Stairs hum")]
	[Export] public float ClimbHumStartDb = -14f;
	[Export] public float ClimbHumTopDb = 0f;
	[Export] public float EncounterHumDb = 4f;
	[ExportGroup("Ending")]
	[Export] public float EndCardHoldSeconds = 5f;
	/// <summary>Hold of each of the two credit cards after the title.</summary>
	[Export] public float CreditHoldSeconds = 3f;
	/// <summary>The radio on the way up (Dan, 2026-09-22: who is there, don't go up): each line at its point of the
	/// flight's height (0..1), in order, through <see cref="Ask"/> with its ticks.</summary>
	private static readonly (float at, string line)[] ClimbLines =
	{
		(0.15f, "\"Who's there with you?\""),
		(0.38f, "\"Don't. Don't go up there.\""),
		(0.62f, "\"Who's that behind you?\""),
		(0.82f, "\"Is someone with you?\""),
	};
	private int _climbLineNext;
	/// <summary>Where on the way up the stairs call "come up and see" the second (and last) time.</summary>
	[Export] public float CallAtProgress = 0.45f;

	/// <summary>For tests: how many times the last staircase has called "come up and see" (at most two).</summary>
	public int CallCount { get; private set; }

	/// <summary>The end card: what the title screen's tape calls the game.</summary>
	public const string EndCardTitle = "PROJECT DS";
	public const string ClosingLine = "\"Did you see them?\"";
	public const string CreditStudio = "GLHFDD";
	public const string CreditThanks = "Thanks for playing.";

	/// <summary>For tests: 0..1, how far up the flight the player stands (0 off it).</summary>
	public float Progress { get; private set; }
	/// <summary>For the autotest: a point square in front of the stairs' base, to approach from before
	/// aiming at the trigger itself (mirrors the original climb's own AutotestApproach → top route).</summary>
	public Vector3? ApproachWorld => _original?.GetNodeOrNull<Node3D>("AutotestApproach")?.GlobalPosition;
	/// <summary>For tests: whether the stairs have been rebuilt impossibly tall.</summary>
	public bool StairsTall => _original != null && _original.Steps == StairsState.TallSteps;
	/// <summary>For tests: the last staircase itself.</summary>
	public StaircaseBuilder OriginalStairs => _original;
	/// <summary>For tests: the E-point on the broken post at the top landing where the cap goes back
	/// (null until the radio has spoken, and gone once the cap is seated).</summary>
	public Interactable CapSeat { get; private set; }

	public const string SeatPrompt = "Put the cap back";
	public const string SeatBlockedPrompt = "The broken post.";
	public const string WholeLine = "";   // was "It's whole again." (self-talk removed, Dan 2026-09-22)
	/// <summary>For tests: the ending (fade, card, menu) is running.</summary>
	public bool EndingStarted { get; private set; }
	/// <summary>For tests: the view was driven up to its eyes (pitch well above level).</summary>
	public bool LookedUp { get; private set; }
	/// <summary>For tests: the trembling ran.</summary>
	public bool Trembled { get; private set; }
	/// <summary>For tests: they blinked out into black looking at it.</summary>
	public bool PassedOut { get; private set; }
	/// <summary>For tests: the hum has been sent to silence over black (before the last line).</summary>
	public bool HumOut { get; private set; }

	private StaircaseBuilder _original;
	private Area3D _fogZone;
	private bool _stageAStarted;
	/// <summary>The way up is open (the flight is one-way, the fog and the cap's E-point wait).</summary>
	private bool _climbArmed;
	/// <summary>The cap has been placed (or the saved story is past it): the ending is running or done.</summary>
	private bool _climbFired;
	private bool _fogLineArmed;
	private bool _humRiding, _calledAtFoot, _calledMid;
	private Task _lineChain = Task.CompletedTask;

	public override void _Ready()
	{
		_original = GetNodeOrNull<StaircaseBuilder>(OriginalStairsPath);
		SetProcess(false);   // only while the way up is open
		Callable.From(Restore).CallDeferred();
		if (StoryManager.Instance is { } s)
		{
			s.CheckpointReached += OnStoryChanged;
			s.FlagSet += OnStoryChanged;
		}
	}

	public override void _ExitTree()
	{
		if (StoryManager.Instance is { } s)
		{
			s.CheckpointReached -= OnStoryChanged;
			s.FlagSet -= OnStoryChanged;
		}
		ForestAmbienceManager.Instance?.ReleaseSilence(this);
	}

	private void Restore()
	{
		var s = StoryManager.Instance;
		if (s == null || _original == null) return;
		// One rule for the flight's length, whatever restored before or after this node; rebuilt only if it differs.
		if (s.Act11DialogueDone) SetStairs(StairsState.FinalStepsFor(s, _original.BaseSteps));
		// The cap seated in the saved story: the flight is whole from the start.
		_original.NewelCapped = s.HasFlag(StoryManager.Flag.NewelSeated);
		_climbFired = s.Current >= Checkpoint.Act11GiantEncounter;
		OnStoryChanged(0);
		if (s.Act11DialogueDone && !_climbFired) _ = Cutscene.Run(this, RestoreFog);
	}

	private void OnStoryChanged<T>(T unused)
	{
		var s = StoryManager.Instance;
		if (s == null || _original == null) return;
		if (s.Current == Checkpoint.Act10WalkieFound && !s.Act11DialogueDone && !_stageAStarted)
		{
			_stageAStarted = true;
			if (StoryBeat.Player(this) is { } p) _ = Cutscene.Run(this, ct => StageA(p, ct));
			return;
		}
		if (s.Act11DialogueDone && s.Current < Checkpoint.Act11GiantEncounter) EnsureClimbTriggers();
	}

	private void SetStairs(int steps)
	{
		if (_original.Steps == steps) return;
		_original.Steps = steps;
		_original.Build();
	}

	/// <summary>A Continue that lands inside the fog radius gets the height fog at once (no line: the
	/// player never "arrived"); one that lands outside arms the fog-zone question for when they do.</summary>
	private async Task RestoreFog(CancellationToken ct)
	{
		while (GameFlow.Instance is { Started: false }) await Cutscene.Frame(this, ct);
		if (_fogZone == null || _climbFired) return;
		var p = StoryBeat.Player(this);
		if (p != null && new Vector2(p.GlobalPosition.X - _original.GlobalPosition.X, p.GlobalPosition.Z - _original.GlobalPosition.Z).Length() < FogRampRadius)
			RampFog(false);
		else _fogLineArmed = true;
	}

	// ================================================================== the radio, outside again

	private async Task StageA(PlayerController player, CancellationToken ct)
	{
		// On Continue this is restored at level load: let the opening fade hand control over first.
		while (GameFlow.Instance is { Started: false }) await Cutscene.Frame(this, ct);
		player.GetNodeOrNull<PlayerInventory>("Inventory")?.TryPickup(ToolKind.Radio);
		var bunker = GetTree().GetFirstNodeInGroup("bunker_marker") as Node3D;
		var fader = StoryBeat.Fader(this);

		Cutscene.Lock(player, true, true);
		try
		{
			if (fader != null) await fader.Fade(1f, 1.0f);
			// 16 m out in front of the bunker door (its local +Z), clear of the mound.
			Vector3 spot = bunker != null ? bunker.GlobalTransform * new Vector3(0, 0, 16f) : player.GlobalPosition;
			var terrain = GroundSnap.FindTerrain(this);
			if (terrain != null) spot.Y = terrain.HeightAt(spot.X, spot.Z) + 0.2f;
			Vector3 away = bunker != null ? spot - bunker.GlobalPosition : Vector3.Forward;
			float yawHome = Mathf.Atan2(-away.X, -away.Z);
			player.Teleport(spot, yawHome);
			// The stairs are rebuilt behind the black: already impossibly tall by the time the compass
			// leads there, and the rebuild never hitches the dialogue.
			SetStairs(StairsState.TallSteps);
			await Cutscene.Frame(this, ct);
			if (fader != null) await fader.Fade(0f, 1.2f);
		}
		finally
		{
			if (IsInstanceValid(player) && player.IsInsideTree()) Cutscene.Unlock(player, true, true);
		}

		await Cutscene.Wait(this, GameSettings.Instance.AutoTest ? 0.5 : 2.0, ct);
		await RadioLine("\"Did you see them?\"", 0.8f, 3.0f, 0.8f, ct);
		await Cutscene.Wait(this, 0.7, ct);
		await RadioLine("\"Who are you!\"", 0.6f, 2.2f, 0.8f, ct);
		await Cutscene.Wait(this, 1.0, ct);

		_fogLineArmed = true;
		StoryManager.Instance.MarkAct11DialogueDone();
		GD.Print("[story] Act 11: the radio speaks");

		// The radio keeps asking: the first question comes on the walk, unless they're already climbing.
		await Cutscene.Wait(this, GameSettings.Instance.AutoTest ? 3.0 : 25.0, ct);
		if (!_climbFired) await Ask("\"Are you alone out there?\"", ct);
	}
	/// <summary>The levels of the radio's crackle ticks and its squelch open/close, on the Radio bus.</summary>
	private const float TickDb = -22f, SquelchDb = -16f;
	/// <summary>The open-channel static under a line: real FM inter-station noise through the little speaker.</summary>
	[Export] public float StaticBedDb = -19f;

	/// <summary>
	/// One line from the other side, heard on this handheld, with its static tied to the text and to
	/// nothing else (Dan, 2026-09-22, from the reference samples): the squelch opens (a click and a
	/// short burst) as the line appears, the open channel rushes underneath the words (radio_static_loop:
	/// bright white noise through the speaker) with two to four crackle ticks at random moments, and the
	/// squelch tail ("kshht", ~200 ms) cuts it all dead the moment the subtitle starts to go. Nothing at
	/// all when no line is on screen.
	/// </summary>
	private async Task RadioLine(string line, float fadeIn, float hold, float fadeOut, CancellationToken ct)
	{
		if (Subtitle.Instance == null) return;
		PlaySquelch(true);
		var bed = StartStaticBed();
		var show = Subtitle.Instance.Show(line, fadeIn, hold, fadeOut, ct);
		// The ticks: random moments inside the line's visible span, at least 0.25 s apart, drawn in order.
		float up = fadeIn + hold;
		int ticks = GD.RandRange(2, 4);
		var at = new System.Collections.Generic.List<float>();
		for (int attempt = 0; attempt < 12 && at.Count < ticks; attempt++)
		{
			float t = (float)GD.RandRange(0.12, up - 0.15);
			bool clear = true;
			foreach (float o in at) if (Mathf.Abs(o - t) < 0.25f) { clear = false; break; }
			if (clear) at.Add(t);
		}
		at.Sort();
		float now = 0f;
		foreach (float t in at)
		{
			await Cutscene.Wait(this, t - now, ct);
			now = t;
			PlayOneShot($"res://assets/audio/sfx/radio_tick_{GD.RandRange(1, 4):00}.wav", TickDb);
		}
		if (up - now > 0f) await Cutscene.Wait(this, up - now, ct);
		StopStaticBed(bed);
		PlaySquelch(false);   // the carrier drops as the words go
		await show;
	}

	/// <summary>The open channel's rush, started just behind the squelch click (a 40 ms fade in).</summary>
	private AudioStreamPlayer StartStaticBed()
	{
		const string path = "res://assets/audio/ambient/radio_static_loop.wav";
		if (!ResourceLoader.Exists(path)) return null;
		var s = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(path), Bus = "Radio", VolumeDb = StaticBedDb };
		Cutscene.SceneRoot(this).AddChild(s);
		s.Play((float)GD.RandRange(0.0, 15.0));
		StoryBeat.FadeIn(s, StaticBedDb, 0.04f);
		return s;
	}

	/// <summary>The gate shuts: the rush is cut in 30 ms (the squelch tail plays over the cut).</summary>
	private static void StopStaticBed(AudioStreamPlayer s)
	{
		if (s == null || !GodotObject.IsInstanceValid(s)) return;
		var tw = s.CreateTween();
		tw.TweenProperty(s, "volume_db", -60f, 0.03f);
		tw.TweenCallback(Callable.From(s.QueueFree));
	}

	/// <summary>The squelch gate opening or closing (one of two takes each), at a modest level on the Radio bus.</summary>
	private void PlaySquelch(bool open) =>
		PlayOneShot($"res://assets/audio/sfx/squelch_{(open ? "open" : "close")}_{GD.RandRange(1, 2):00}.wav", SquelchDb);

	/// <summary>A short sound on the Radio bus (the handheld's own speaker), freed when it ends.</summary>
	private void PlayOneShot(string path, float db)
	{
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(path), Bus = "Radio", VolumeDb = db };
		Cutscene.SceneRoot(this).AddChild(s);
		s.Finished += s.QueueFree;
		s.Play();
	}

	/// <summary>One of the radio's questions, on the subtitle band with its own squelch and crackle.
	/// Questions queue behind each other so two never fight over the band.</summary>
	private Task Ask(string line, CancellationToken ct)
	{
		var prev = _lineChain;
		async Task Go()
		{
			try { await prev; } catch (System.OperationCanceledException) { }
			ct.ThrowIfCancellationRequested();
			await RadioLine(line, 0.6f, 2.6f, 0.8f, ct);
		}
		_lineChain = Go();
		return _lineChain;
	}

	// ================================================================== the way up, on foot

	/// <summary>
	/// After the radio: the flight is open, and one-way (<see cref="OneWayFlight"/>: a couple of treads
	/// up there is no coming back down), the fog-ramp zone waits, and on the top landing the broken
	/// newel post carries the cap's E-point. The per-frame watch (the hum rising with the climb, the
	/// radio's two questions by progress) runs from here.
	/// </summary>
	private void EnsureClimbTriggers()
	{
		if (_original == null || _climbArmed || _climbFired) return;
		_climbArmed = true;
		OneWayFlight.Attach(_original);
		// The closer the player gets, the more the fog swallows everything above roughly the
		// stairs' midpoint — from the ground you can never quite see where they end.
		_fogZone = StoryBeat.MakeTrigger(_original, new CylinderShape3D { Radius = FogRampRadius, Height = 200f },
			Vector3.Zero, _ => RampFog(_fogLineArmed), "Act11FogZone");
		if (_original.HasNewel && StoryManager.Instance?.HasFlag(StoryManager.Flag.NewelSeated) != true)
		{
			var seat = new PickupInteractable
			{
				Name = "CapSeat",
				Prompt = SeatPrompt,
				MaxDistance = 3f,
				PickRadius = 0.6f,
				Position = _original.NewelSeatLocal,
				HighlightRoot = new NodePath("."),   // nothing to highlight: the prompt is the cue
				CanUse = p => !_climbFired && p?.Inventory is { HasNewelPost: true } && StoryManager.Instance is { Act11DialogueDone: true },
				PromptFor = p => p?.Inventory is { HasNewelPost: true } && StoryManager.Instance is { Act11DialogueDone: true } ? SeatPrompt : SeatBlockedPrompt,
			};
			seat.Interacted += OnCapPlaced;
			_original.AddChild(seat);
			CapSeat = seat;
		}
		SetProcess(true);
	}

	/// <summary>
	/// The climb, on the player's own feet: how far up the flight they stand drives the hum (from
	/// <see cref="ClimbHumStartDb"/> at the foot to <see cref="ClimbHumTopDb"/> at the top), the silence
	/// (the woods go dead once they are on the stairs) and the radio's two questions on the way.
	/// </summary>
	public override void _Process(double delta)
	{
		if (!_climbArmed || _climbFired || _original == null) { SetProcess(false); return; }
		var p = StoryBeat.Player(this);
		if (p == null) return;
		Vector3 local = _original.ToLocal(p.GlobalPosition);
		float h = _original.TotalHeight;
		bool onFlight = local.Z <= 0.4f && local.Z >= _original.BackZ - 1.5f && Mathf.Abs(local.X) < _original.Width * 0.5f + 0.6f && local.Y > -0.5f;
		float prog = onFlight && h > 0.01f ? Mathf.Clamp(local.Y / h, 0f, 1f) : 0f;
		Progress = prog;
		var hum = StairsHum.Instance;
		if (prog > 0.02f)
		{
			_humRiding = true;
			hum?.SetOverrideDb(Mathf.Lerp(ClimbHumStartDb, ClimbHumTopDb, prog));
			ForestAmbienceManager.Instance?.RequestSilence(this, 1f, SilencePriority);
			if (_climbLineNext < ClimbLines.Length && prog >= ClimbLines[_climbLineNext].at)
			{
				string line = ClimbLines[_climbLineNext++].line;
				_ = Cutscene.Run(this, ct => Ask(line, ct));
			}
			if (!_calledMid && prog >= CallAtProgress) { _calledMid = true; PlayCall("distant", -6f); }
		}
		else if (_humRiding)
		{
			// Back on the ground at the foot (the flight is one-way, so only from its first treads).
			_humRiding = false;
			hum?.SetOverrideDb(null);
			ForestAmbienceManager.Instance?.ReleaseSilence(this);
		}
	}

	private void RampFog(bool ask)
	{
		if (_fogZone == null) return;
		_fogZone.QueueFree();
		_fogZone = null;
		StoryBeat.Atmosphere(this)?.SetHeightFog(_original.GlobalPosition.Y + _original.TotalHeight * 0.4f, 0.1f, 9f);
		if (ask) _ = Cutscene.Run(this, ct => Ask("\"Don't go up.\"", ct));
		// The stairs call once as the player comes under them (the second time is half way up). Not at the
		// top: the cap's own beat calls RampFog too, after the climb is over.
		if (!_climbFired && !_calledAtFoot) { _calledAtFoot = true; PlayCall("medium", -4f); }
	}

	/// <summary>
	/// "Come up and see", from somewhere up the flight, lost in the fog. The last staircase says it
	/// once or twice and no more (Dan, 2026-09-22: the voice is only heard where stairs stand; the
	/// clearing's loop is where it calls and calls). Deep, unhurried, no caption.
	/// </summary>
	private void PlayCall(string take, float volumeDb)
	{
		if (_original == null) return;
		string path = $"res://assets/audio/voice/come_and_see_{take}.mp3";
		if (!ResourceLoader.Exists(path)) return;
		CallCount++;
		var voice = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = "Voice",
			UnitSize = 10f, MaxDistance = 260f, VolumeDb = volumeDb,
			PitchScale = (float)GD.RandRange(0.84, 0.92),
		};
		_original.AddChild(voice);
		voice.Position = new Vector3(0, _original.TotalHeight + 2f, (_original.TopFrontZ + _original.BackZ) * 0.5f);
		voice.Finished += voice.QueueFree;
		voice.Play();
		StoryBeat.FadeIn(voice, volumeDb, 0.6f);
	}

	// ================================================================== the cap, the giant, the touch

	/// <summary>E on the broken post at the top with the cap in hand: the ending starts, and control never comes back.</summary>
	private void OnCapPlaced(PlayerController player)
	{
		if (_climbFired || player?.Inventory is not { HasNewelPost: true } || StoryManager.Instance is not { Act11DialogueDone: true }) return;
		_climbFired = true;
		SetProcess(false);
		if (CapSeat != null) { CapSeat.Enabled = false; CapSeat.QueueFree(); CapSeat = null; }
		player.Inventory.ConsumeNewelPost();
		RampFog(false);
		_ = Cutscene.Run(this, async ct =>
		{
			await SeatCap(player, ct);
			await Encounter(player, ct);
		}, lockInput: true, freezeBody: true);
	}

	/// <summary>
	/// The cap flies from in front of the player's view the short way to the stump beside them, turns,
	/// and grinds down onto it (stone on stone), the pier's own cap taking its place in a purple flash.
	/// The hum surges as it seats; then the one line, and the flight is whole.
	/// </summary>
	private async Task SeatCap(PlayerController player, CancellationToken ct)
	{
		var stairs = _original;
		var cam = player.CameraRig?.Camera;
		Transform3D seat = stairs.NewelSeatGlobal;
		Vector3 scale = seat.Basis.Scale;
		// No spin (Dan, 2026-09-22): the cap lifts and sets straight, already the way it will sit.
		Quaternion qSeat = seat.Basis.Orthonormalized().GetRotationQuaternion();
		Vector3 start = cam != null ? cam.GlobalTransform * new Vector3(0.1f, -0.22f, -0.6f) : player.GlobalPosition + Vector3.Up * 1.3f;
		Vector3 hover = seat.Origin + seat.Basis.Y.Normalized() * 0.14f * scale.Y;
		float dist = start.DistanceTo(hover);
		Vector3 ctrl = (start + hover) * 0.5f + Vector3.Up * (1.0f + dist * 0.12f);
		float flight = Mathf.Clamp(dist / 3f, 1.5f, 2.5f);

		var cap = new MeshInstance3D { Name = "FlyingNewelCap", Mesh = StaircaseBuilder.NewelCapMesh };
		Cutscene.SceneRoot(this).AddChild(cap);
		void Place(Vector3 p, Quaternion q) => cap.GlobalTransform = new Transform3D(new Basis(q).Scaled(scale), p);
		Place(start, qSeat);
		var hum = StairsHum.Instance;
		ForestAmbienceManager.Instance?.RequestSilence(this, 1f, SilencePriority);
		try
		{
			double t = 0;
			while (t < flight)
			{
				await Cutscene.Frame(this, ct);
				t += GetProcessDeltaTime();
				float u = Mathf.Clamp((float)(t / flight), 0f, 1f);
				float e = u * u * (3f - 2f * u);
				Vector3 p = (1 - e) * (1 - e) * start + 2 * (1 - e) * e * ctrl + e * e * hover;
				Place(p, qSeat);
				FollowWithView(player, p);
			}
			// Straight down onto the stump: the sound's grind runs the length of the drop and its seat lands on contact.
			StoryBeat.PlayAt(stairs, "res://assets/audio/sfx/newel_seat.wav", "Unnatural", stairs.NewelSeatLocal, volumeDb: 4f, unitSize: 5f, maxDistance: 60f);
			const float drop = 0.44f;
			t = 0;
			while (t < drop)
			{
				await Cutscene.Frame(this, ct);
				t += GetProcessDeltaTime();
				float u = Mathf.Clamp((float)(t / drop), 0f, 1f);
				Place(hover.Lerp(seat.Origin, u * u), qSeat);
			}
			stairs.NewelCapped = true;
			FlashFusion(seat.Origin);
			if (hum != null)
			{
				var surge = CreateTween();
				surge.TweenMethod(Callable.From<float>(db => hum.SetOverrideDb(db)), hum.LevelDb, EncounterHumDb, 1.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
			}
			await Cutscene.Wait(this, 2.4, ct);   // no thought (self-talk removed, Dan 2026-09-22); the hum surge says it
			GD.Print("[story] Act 11: the cap is back on the post");
		}
		finally
		{
			if (IsInstanceValid(cap)) cap.QueueFree();
		}
	}

	/// <summary>Turns the player's view a little each frame toward the flying cap (a cutscene look: input is locked).</summary>
	private static void FollowWithView(PlayerController player, Vector3 target)
	{
		if (player == null || !GodotObject.IsInstanceValid(player)) return;
		var rig = player.CameraRig;
		Vector3 eye = rig.Camera?.GlobalPosition ?? player.GlobalPosition + Vector3.Up * 1.6f;
		Vector3 d = target - eye;
		float yaw = Mathf.Atan2(-d.X, -d.Z);
		float pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
		float dy = Mathf.Clamp(Mathf.AngleDifference(rig.Yaw, yaw), -0.03f, 0.03f);
		float dp = Mathf.Clamp(pitch - rig.Pitch, -0.02f, 0.02f);
		player.PlayerInput.AddCutsceneLook(new Vector2(dy, dp));
	}

	/// <summary>A burst of purple light at the joint where the cap has fused back on (the stone sound is played by the drop).</summary>
	private void FlashFusion(Vector3 joint)
	{
		var light = new OmniLight3D { LightColor = new Color(0.7f, 0.25f, 0.95f), LightEnergy = 6f, OmniRange = 6f };
		Cutscene.SceneRoot(this).AddChild(light);
		light.GlobalPosition = joint + Vector3.Up * 0.1f;
		var tween = light.CreateTween();
		tween.TweenProperty(light, "light_energy", 0f, 2.2f).SetDelay(0.15f);
		tween.TweenCallback(Callable.From(light.QueueFree));
	}

	/// <summary>
	/// The flight whole, the giant is standing on the landing in front of the player, close, its head far
	/// above. Control is taken. The view is driven up its body to its head (Dan, 2026-09-22: they have to
	/// look up at it); the radio asks once more; its eyes open red, in silence, the view on them. The hum
	/// is the whole sound: it rises to its maximum through the beat, holds, and hums out over black.
	/// Then the trembling: the view shakes, harder and harder, the hum as loud as it gets, as it leans in
	/// over them. Then their eyes fall shut, blink by blink, each deeper than the last (Act 1's collapse),
	/// looking it in the eyes the whole way down, into black. Over black: the last question, the seated cap
	/// and checkpoint 9 saved together, then the credits and the menu. Control never comes back.
	/// </summary>
	private async Task Encounter(PlayerController player, CancellationToken ct)
	{
		player.Velocity = Vector3.Zero;
		var hum = StairsHum.Instance;
		var rig = player.CameraRig;
		Vector3 fwd = -_original.GlobalTransform.Basis.Z; fwd.Y = 0;
		if (fwd.LengthSquared() < 0.01f) fwd = Vector3.Forward; else fwd = fwd.Normalized();
		Vector3 giantStart = player.GlobalPosition + fwd * GiantDistance;

		var skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stalker_skin.gdshader") };
		skin.SetShaderParameter("albedo", new Color(0.0f, 0.0f, 0.0f));
		skin.SetShaderParameter("face_tint", new Color(0.05f, 0.05f, 0.055f));
		skin.SetShaderParameter("visibility", 1f);
		skin.SetShaderParameter("wetness", 0f);
		skin.SetShaderParameter("eye_color", new Color(1f, 0.04f, 0.02f));
		skin.SetShaderParameter("eye_glow", 0f);
		var body = new StalkerBody { Name = "Act11Giant", Skin = skin, Size = BodyScale, SwaySeconds = 26f, SwayDegrees = 0.5f, HeadDriftDegrees = 0.8f };
		// Must be in the tree before GlobalPosition/LookAt, or Godot can't resolve the transform.
		Cutscene.SceneRoot(this).AddChild(body);
		var fader = StoryBeat.Fader(this);
		var postMat = StoryBeat.PostMaterial(this);
		float baseVignette = postMat != null ? (float)postMat.GetShaderParameter("vignette") : 0f;
		// Its eyes are far above the mode's pitch ceiling: the view is allowed all the way up for this beat.
		rig.MaxPitchOverride = 60f;   // never near the zenith: past ~85 degrees the yaw toward a point overhead whips round
		bool auto = GameSettings.Instance.AutoTest;
		float tremble = 0f;
		try
		{
			body.GlobalPosition = giantStart;
			// The figure faces +Z (StalkerBody's contract), so it is yawed to the player, not LookAt-ed (that would
			// turn its back). Its head is driven each frame to look down at the player's eyes.
			Vector3 toP = player.GlobalPosition - giantStart;
			body.Rotation = new Vector3(0, Mathf.Atan2(toP.X, toP.Z), 0);
			body.LookTarget = rig.Camera?.GlobalPosition ?? player.GlobalPosition + Vector3.Up * 1.6f;
			// The drone is the whole soundtrack of the end: from the cap's surge it rises steadily through the
			// look-up and the trembling to the hum's maximum, holds through the pass-out, then hums out.
			float riseSeconds = (auto ? LookUpSeconds * 0.6f : LookUpSeconds) + 1.0f + (auto ? TrembleSeconds * 0.5f : TrembleSeconds);
			if (hum != null)
			{
				var rise = CreateTween();
				rise.TweenMethod(Callable.From<float>(db => hum.SetOverrideDb(db)), hum.LevelDb, hum.MaxDb, riseSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
			}
			await Cutscene.Frame(this, ct);

			// 1. Up its body to its eyes: the view climbs from its shins to its face over a few seconds, the
			//    radio asking half way. The look never snaps: a bounded step each frame, like a head turning.
			float lookSeconds = auto ? LookUpSeconds * 0.6f : LookUpSeconds;
			double t = 0;
			bool asked = false;
			while (t < lookSeconds)
			{
				await Cutscene.Frame(this, ct);
				t += GetProcessDeltaTime();
				float u = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, (float)(t / lookSeconds)));
				Vector3 shins = body.GlobalPosition + Vector3.Up * BodyScale * 0.25f;
				DriveView(player, shins.Lerp(body.EyesWorld, u), 0.09f, 0.05f);
				if (!asked && t > lookSeconds * 0.45f) { asked = true; _ = Ask("\"Do you see him now?\"", ct); }
			}
			LookedUp = rig.Pitch > Mathf.DegToRad(30f);
			GD.Print($"[story] Act 11: looking up at it (pitch {Mathf.RadToDeg(rig.Pitch):0} deg)");

			// 2. The eyes open, in silence (Dan, 2026-09-22: no growl at the end; the drone is the whole sound).
			var eyeTween = body.CreateTween();
			eyeTween.TweenMethod(Callable.From<float>(v => skin.SetShaderParameter("eye_glow", v)), 0f, 7.5f, 2.4f);
			body.GlowEyes(new Color(1f, 0.06f, 0.02f), 9f);   // two lit points looking down through the fog
			t = 0;
			while (t < 1.0)
			{
				await Cutscene.Frame(this, ct);
				t += GetProcessDeltaTime();
				DriveView(player, body.EyesWorld, 0.09f, 0.05f);
			}

			// 3. Trembling: a small shiver comes up from nothing over the beat (pitch and yaw jitter only, no
			//    roll, no drift: the drive re-centres on its eyes every frame) while it leans in over them.
			Trembled = true;
			float trembleSeconds = auto ? TrembleSeconds * 0.5f : TrembleSeconds;
			Vector3 toPlayer = player.GlobalPosition - body.GlobalPosition; toPlayer.Y = 0;
			Vector3 closeSpot = body.GlobalPosition + toPlayer.Normalized() * Mathf.Max(0f, toPlayer.Length() - GiantLeanIn);
			var lean = body.CreateTween();
			lean.TweenProperty(body, "global_position", closeSpot, trembleSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			t = 0;
			while (t < trembleSeconds)
			{
				await Cutscene.Frame(this, ct);
				t += GetProcessDeltaTime();
				tremble = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, (float)(t / trembleSeconds)));
				DriveView(player, body.EyesWorld, 0.09f, 0.05f);
				Shiver(player, tremble * TrembleRadians);
			}

			// 4. Passing out: vision falls shut and drifts open again, each blink deeper than the last, the
			//    shiver dying away, its eyes the last thing seen. Then black.
			float outSeconds = auto ? PassOutSeconds * 0.6f : PassOutSeconds;
			t = 0;
			while (t < outSeconds)
			{
				await Cutscene.Frame(this, ct);
				t += GetProcessDeltaTime();
				float u = Mathf.Min(1f, (float)(t / outSeconds));
				float phase = (float)(t / Mathf.Max(BlinkSeconds, 0.1f)) * Mathf.Tau;
				float closed = Mathf.Sin(phase - Mathf.Pi / 2f) * 0.5f + 0.5f;   // 0 = open .. 1 = closed
				float floor = Mathf.SmoothStep(0.3f, 1f, u);                       // each blink closes further; the last never open
				float shut = Mathf.Max(closed, floor);
				postMat?.SetShaderParameter("vignette", Mathf.Lerp(baseVignette, BlinkVignette, shut));
				if (fader != null) fader.BlackAlpha = shut * Mathf.SmoothStep(0.15f, 0.9f, u);
				DriveView(player, body.EyesWorld, 0.09f, 0.05f);
				Shiver(player, Mathf.Lerp(TrembleRadians, TrembleRadians * 0.2f, u));
			}
			PassedOut = true;
			EndingStarted = true;
			if (fader != null) fader.BlackAlpha = 1f;
		}
		finally
		{
			if (IsInstanceValid(body)) body.QueueFree();
			if (IsInstanceValid(rig)) rig.MaxPitchOverride = null;
			postMat?.SetShaderParameter("vignette", baseVignette);
		}
		// Black. The hum, held at its maximum through the pass-out, hums out: a slow fall to silence, and
		// it never comes back (the override stays at silence; proximity would raise it again up here).
		GD.Print("[story] Act 11: passed out looking it in the eyes");
		HumOut = true;
		if (hum != null)
		{
			var outTween = CreateTween();
			outTween.TweenMethod(Callable.From<float>(db => hum.SetOverrideDb(db)), hum.LevelDb, -80f, HumOutSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
			await Cutscene.Tween(this, outTween, ct);
		}
		else await Cutscene.Wait(this, HumOutSeconds, ct);
		ForestAmbienceManager.Instance?.ReleaseSilence(this);
		await Cutscene.Wait(this, 0.6, ct);

		// Over black: the last question, and the story's end saved in one go.
		await Ask(ClosingLine, ct);
		var s = StoryManager.Instance;
		s?.SetFlag(StoryManager.Flag.NewelSeated);
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act11GiantEncounter);
		await Cutscene.Wait(this, 1.0, ct);
		await Credits(fader, ct);
	}

	/// <summary>Hard ceiling on the driven pitch (degrees): well short of the zenith, where yaw is ill-defined.</summary>
	private const float DrivePitchMaxDeg = 58f;

	/// <summary>For tests: the view inverted at any point during the encounter (camera up vs world up, or pitch past 70 degrees).</summary>
	public bool ViewFlipped { get; private set; }

	/// <summary>
	/// Turns the view a bounded step toward a world point, like a head turning. Yaw is taken in the XZ
	/// plane only, and only while the target has a real horizontal offset (a point nearly overhead has no
	/// yaw: chasing it spins the view round, which reads as the camera flipping); pitch is capped at
	/// <see cref="DrivePitchMaxDeg"/> so it never approaches 90. The rig applies (pitch, yaw, 0): no roll.
	/// Every call also watches the camera for an inversion (<see cref="ViewFlipped"/>).
	/// </summary>
	private void DriveView(PlayerController player, Vector3 target, float maxYawStep, float maxPitchStep)
	{
		if (player == null || !GodotObject.IsInstanceValid(player)) return;
		var rig = player.CameraRig;
		Vector3 eye = rig.Camera?.GlobalPosition ?? player.GlobalPosition + Vector3.Up * 1.6f;
		Vector3 d = target - eye;
		float horiz = new Vector2(d.X, d.Z).Length();
		float dy = 0f;
		if (horiz > 2f)
		{
			float yaw = Mathf.Atan2(-d.X, -d.Z);
			dy = Mathf.Clamp(Mathf.AngleDifference(rig.Yaw, yaw), -maxYawStep, maxYawStep);
		}
		float pitch = Mathf.Min(Mathf.Atan2(d.Y, Mathf.Max(horiz, 0.001f)), Mathf.DegToRad(DrivePitchMaxDeg));
		float dp = Mathf.Clamp(pitch - rig.Pitch, -maxPitchStep, maxPitchStep);
		if (rig.Pitch + dp > Mathf.DegToRad(DrivePitchMaxDeg)) dp = Mathf.DegToRad(DrivePitchMaxDeg) - rig.Pitch;
		player.PlayerInput.AddCutsceneLook(new Vector2(dy, dp));
		WatchView(rig);
	}

	/// <summary>Flags an inverted view: the camera's up leaning past 72 degrees from world up, or the pitch outside -70..70.</summary>
	private void WatchView(PlayerCameraRig rig)
	{
		if (rig == null || !GodotObject.IsInstanceValid(rig)) return;
		float up = rig.Camera != null ? rig.Camera.GlobalBasis.Y.Dot(Vector3.Up) : 1f;
		if (up < 0.3f || Mathf.Abs(rig.Pitch) > Mathf.DegToRad(70f))
		{
			if (!ViewFlipped) GD.PushWarning($"[story] Act 11: the view inverted (up {up:0.00}, pitch {Mathf.RadToDeg(rig.Pitch):0} deg)");
			ViewFlipped = true;
		}
	}

	/// <summary>One frame of trembling: a small random kick to the view. The drive re-centres it next frame,
	/// so the shake is a shiver around the eyes, never a drift.</summary>
	private static void Shiver(PlayerController player, float radians)
	{
		if (radians <= 0f || player == null || !GodotObject.IsInstanceValid(player)) return;
		player.PlayerInput.AddCutsceneLook(new Vector2((GD.Randf() - 0.5f) * 2f * radians, (GD.Randf() - 0.5f) * 2f * radians * 0.7f));
	}

	/// <summary>The end card, the studio, the thanks; then the menu. The pause menu is locked meanwhile.</summary>
	private async Task Credits(ScreenFader fader, CancellationToken ct)
	{
		var pause = FindPauseMenu();
		if (pause != null) pause.Locked = true;
		try
		{
			if (fader != null)
			{
				await ShowEndCard(fader, EndCardHoldSeconds);
				ct.ThrowIfCancellationRequested();
				await fader.ShowCaption(CreditStudio, "", 1.2f, CreditHoldSeconds, 1.2f, ct);
				await fader.ShowCaption("", CreditThanks, 1.2f, CreditHoldSeconds, 1.2f, ct);
			}
			ct.ThrowIfCancellationRequested();
			GD.Print("[story] the end: back to the menu");
			StoryManager.Instance?.ReturnToMenu();
		}
		finally
		{
			if (pause != null && IsInstanceValid(pause)) pause.Locked = false;
		}
	}

	/// <summary>The single end card in the fader's caption style (hold &lt; 0 keeps it up, for previews).</summary>
	public static Task ShowEndCard(ScreenFader fader, float hold) => fader.ShowCaption(EndCardTitle, "", 1.5f, hold, 1.5f);

	private PauseMenu FindPauseMenu()
	{
		var tree = GetTree();
		if (tree.GetFirstNodeInGroup("pause_menu") is PauseMenu grouped) return grouped;
		// Nothing registers the pause menu in a group yet: look once under the level, then register it.
		if (tree.CurrentScene?.FindChild("PauseMenu", true, false) is not PauseMenu found) return null;
		found.AddToGroup("pause_menu");
		return found;
	}
}
