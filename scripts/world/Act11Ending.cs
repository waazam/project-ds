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
/// is whole, and the giant (Act 7's) rises out of the woods beyond the landing,
/// all wrong and stop-start, the view following its head up; its eyes open red;
/// then it grabs them in juddering haste (<see cref="GiantRise"/>, 2026-10-03), and (2026-10-16, STORY.md Rev 5) its
/// hand lifts them high over the treetops and throws them in a long arc down into the woods (<see cref="Throw"/>);
/// black. Over black, the radio's last "Did you see them?", the fourth chapter's title, and they wake at the lake by
/// the crater they made (Act 12, <see cref="WakeAtLake"/>).
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
	/// <summary>How far beyond the landing it rises (metres, level): its size is then set so its eyes stand well
	/// above the player's (about a 38 degree look-up), whatever the drop to the woods' floor.</summary>
	[Export] public float GiantDistance = 40f;
	/// <summary>Peak shiver of the view, radians per frame, at the end of the trembling.</summary>
	[Export] public float TrembleRadians = 0.006f;
	/// <summary>The silence over black after the grab is 0.6 of this (seconds), before the last radio line.</summary>
	[Export] public float HumOutSeconds = 4f;
	[Export] public float FogRampRadius = 70f;
	/// <summary>How near the foot of the flight (m) they are "under the stairs": the stairs' first call and the radio's
	/// "Don't go up." (STORY.md, Act 11; 2026-10-13: both came at the fog's edge, 70 m out).</summary>
	[Export] public float UnderStairsRadius = 14f;
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
	public const string EndCardTitle = "DEAD SILENT";
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
	/// <summary>For tests: its hand reached them (the grab's last pose).</summary>
	public bool Grabbed { get; private set; }
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
		AddToGroup("act11_ending");
		// the thing that follows them out of the bunker, its eyes lit, drawn once in the load's black (the lag pass,
		// 2026-10-13: made on the spot, its first sight cost 140 ms)
		if (StoryManager.Instance is { Current: < Checkpoint.Act11GiantEncounter })
			Systems.ShaderWarmup.Prewarm(this, new Entities.StalkerBody { Name = "PursuerWarmUp", Seed = 2077, Size = 1.08f, Idle = true },
				b => ((Entities.StalkerBody)b).GlowEyes(new Color(1f, 0.16f, 0.05f), 6f));
		_original = GetNodeOrNull<StaircaseBuilder>(OriginalStairsPath);
		// (2026-10-29, the owner: "four spotlights beaming straight down from above ... along the way to the big staircase")
		// laid out at load, dark: Continue's respawn looks for their markers, and the scatter keeps its trees out of them
		if (_original != null && GetTree().GetFirstNodeInGroup("bunker_marker") is Node3D bunkerAt)
		{
			Lights = new Act11Lights { Name = "Act11Lights" };
			AddChild(Lights);
			Lights.Build(bunkerAt.GlobalTransform * new Vector3(0, 0, 16f), _original.ToGlobal(new Vector3(0, 0, 1.5f)));
		}
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
		await PlayerShout("who_are_you", ct);
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
	/// <summary>The player's own sharp breath before their shout (dB, non-positional on the dry Player bus).</summary>
	[Export] public float ShoutBreathDb = -7f;

	/// <summary>
	/// The only line the player ever says (Dan, 2026-10-13: their own shout, not the radio's): dry and close, in
	/// their own head. No squelch, no open-channel rush, no crackle, nothing on the Radio bus; a quick hard breath in on
	/// the Player bus (its reverb all but dry) as it comes, and the words come up fast, the way a shout does.
	/// </summary>
	private async Task PlayerShout(string line, CancellationToken ct)
	{
		if (Subtitle.Instance == null) return;
		string breath = Audio.AudioDirector.Take("pbreath_gasp", 16);   // the player's own gasp set (was the one breath_in_04)
		if (ResourceLoader.Exists(breath))
		{
			var s = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(breath), Bus = "Player", VolumeDb = ShoutBreathDb, PitchScale = 1.12f };
			Cutscene.SceneRoot(this).AddChild(s);
			s.Finished += s.QueueFree;
			s.Play();
		}
		await Cutscene.Wait(this, 0.18, ct);
		await PlayerVoice.Say(this, line, 2.0f, ct);
	}

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
		_underZone = StoryBeat.MakeTrigger(_original, new CylinderShape3D { Radius = UnderStairsRadius, Height = 40f },
			Vector3.Zero, _ => UnderTheStairs(), "Act11UnderStairs");
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
		// once the game is running (a Continue restores this before the player is placed)
		_ = Cutscene.Run(this, async ct =>
		{
			while (GameFlow.Instance is { Started: false }) await Cutscene.Frame(this, ct);
			await Cutscene.Wait(this, 1.5, ct);
			SpawnPursuer();
		});
	}

	/// <summary>For tests: the thing from the bunker, following them to the stairs.</summary>
	public Act11Pursuer Pursuer { get; private set; }
	/// <summary>The four lights on the way to the stairs (null: no bunker in this level).</summary>
	public Act11Lights Lights { get; private set; }

	/// <summary>The thing from the bunker's rooms comes out after them (the owner's addition): out of the
	/// bunker's door if they are still near it, otherwise somewhere behind them, well back.</summary>
	private void SpawnPursuer()
	{
		if (Pursuer != null || _climbFired || _original == null) return;
		var player = StoryBeat.Player(this);
		if (player == null) return;
		var bunker = GetTree().GetFirstNodeInGroup("bunker_marker") as Node3D;
		Vector3 at;
		bool outOfTheDoor = bunker != null && bunker.GlobalPosition.DistanceTo(player.GlobalPosition) < 45f;
		if (outOfTheDoor) at = bunker.GlobalTransform * new Vector3(0, 0, 3f);
		else
		{
			Vector3 back = player.CameraRig != null ? player.CameraRig.GlobalBasis.Z with { Y = 0 } : Vector3.Back;
			at = player.GlobalPosition + back.Normalized() * 24f;
		}
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain != null) at.Y = terrain.HeightAt(at.X, at.Z);
		Pursuer = new Act11Pursuer { Name = "Act11Pursuer", Stairs = _original, Lights = Lights, OutOfTheDoor = outOfTheDoor };
		Cutscene.SceneRoot(this).AddChild(Pursuer);
		Pursuer.GlobalPosition = at;
		// the way drawn in coloured light as it comes out (lit at once on a Continue: the chase is already on)
		Lights?.SwitchOn(!outOfTheDoor);
		GD.Print("[story] Act 11: the thing from the bunker follows them out");
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
		Veil();
		// (the radio's "Don't go up." and the stairs' first call wait until they're under the stairs, UnderTheStairs:
		// 2026-10-13, both came here, at the fog's edge 70 m out)
		_askUnder = ask;
	}

	/// <summary>For tests: the fog that swallows the flight above the eye.</summary>
	public FogZone StairsVeil { get; private set; }

	/// <summary>(2026-10-19, the owner: "the fog should be obscuring how tall the staircase actually is until the player
	/// traverses it") a veil of thick fog over the whole flight and the woods round it: clear to a few metres above the
	/// eye, solid above, the line rising with them as they climb, so the top is never seen until it's reached. A dim body
	/// of its own: at night it reads as a pale wall, not as plain dark.</summary>
	private void Veil()
	{
		if (StairsVeil != null || _original == null) return;
		float h = _original.TotalHeight;
		StairsVeil = new FogZone
		{
			Name = "StairsVeil", Mode = FogZone.Kind.Veil, Density = 0.2f, NoiseAmount = 0.35f, NoiseScale = 0.03f,
			Albedo = new Color(0.62f, 0.64f, 0.68f), Emission = new Color(0.05f, 0.055f, 0.065f), EmissionEnergy = 1.8f,
			RevealAbove = 5f, VeilRamp = 8f, Edge = 0.2f, VolumetricLength = 90f, StartStrength = 0f,
			Size = new Vector3(FogRampRadius * 2f, h + 80f, FogRampRadius * 2f),
			Position = new Vector3(0, h * 0.5f, _original.BackZ * 0.5f),
		};
		_original.AddChild(StairsVeil);
		StairsVeil.FadeTo(1f, 6f);
	}

	private Area3D _underZone;
	private bool _askUnder;

	/// <summary>Under the stairs, at the foot of the flight: the radio says "Don't go up." (when they walked here: not on a
	/// Continue that lands inside the fog) and the stairs call once from up in the fog (the second time is half way up).
	/// Not after the cap is placed.</summary>
	private void UnderTheStairs()
	{
		if (_underZone == null || _climbFired) return;
		_underZone.QueueFree();
		_underZone = null;
		if (_fogZone != null) RampFog(_fogLineArmed);   // (in from a side the fog zone somehow missed)
		if (_askUnder) _ = Cutscene.Run(this, ct => Ask("\"Don't go up.\"", ct));
		if (!_calledAtFoot) { _calledAtFoot = true; PlayCall("medium", -4f); }
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
		if (Pursuer != null && IsInstanceValid(Pursuer)) { Pursuer.QueueFree(); Pursuer = null; }
		player.Inventory.ConsumeNewelPost();
		RampFog(false);
		// (the veil thins to a third: the treetops still deep in fog, the giant a shape coming up through it, never lost in it)
		Veil();
		StairsVeil?.FadeTo(0.35f, 2.5f);
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
	/// The flight whole (the owner, 2026-10-03): the giant rises out of the woods beyond the landing, slowly and all
	/// wrong (<see cref="GiantRise"/>: held, lurching, bent double, its head upside down and snapping upright a quarter
	/// at a time), the view following its head up; the radio asks once more; it stands still and its eyes open red, the
	/// view trembling; then it grabs, in juddering stop-motion poses, its hand filling the view, and the screen cuts to
	/// black. The hum is the sound throughout, rising to its maximum, cut dead with the grab. Over black: the last
	/// question, the seated cap and checkpoint 9 saved together, and they wake at the lake (Act 12).
	/// </summary>
	private async Task Encounter(PlayerController player, CancellationToken ct)
	{
		player.Velocity = Vector3.Zero;
		var hum = StairsHum.Instance;
		var rig = player.CameraRig;
		Vector3 fwd = -_original.GlobalTransform.Basis.Z; fwd.Y = 0;
		if (fwd.LengthSquared() < 0.01f) fwd = Vector3.Forward; else fwd = fwd.Normalized();
		Vector3 eye0 = rig.Camera?.GlobalPosition ?? player.GlobalPosition + Vector3.Up * 1.6f;
		// the woods' floor beyond the landing, where it comes up from
		Vector3 spot = player.GlobalPosition + fwd * GiantDistance;
		float groundY = player.GlobalPosition.Y - 40f;
		var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(spot + Vector3.Up * 300f, spot + Vector3.Down * 600f, 1));
		if (hit.Count > 0) groundY = ((Vector3)hit["position"]).Y;
		float size = GiantRise.SizeFor(eye0.Y, groundY, GiantDistance);

		var skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stalker_skin.gdshader") };
		skin.SetShaderParameter("albedo", new Color(0.0f, 0.0f, 0.0f));
		skin.SetShaderParameter("face_tint", new Color(0.05f, 0.05f, 0.055f));
		skin.SetShaderParameter("visibility", 1f);
		skin.SetShaderParameter("wetness", 0f);
		skin.SetShaderParameter("eye_color", new Color(1f, 0.04f, 0.02f));
		skin.SetShaderParameter("eye_glow", 0f);
		var body = new StalkerBody { Name = "Act11Giant", Skin = skin, Size = size, SwaySeconds = 26f, SwayDegrees = 0.5f, HeadDriftDegrees = 0.8f };
		Cutscene.SceneRoot(this).AddChild(body);
		var fader = StoryBeat.Fader(this);
		var postMat = StoryBeat.PostMaterial(this);
		float baseVignette = postMat != null ? (float)postMat.GetShaderParameter("vignette") : 0f;
		rig.MaxPitchOverride = 60f;   // never near the zenith: past ~85 degrees the yaw toward a point overhead whips round
		bool auto = GameSettings.Instance.AutoTest;
		var stage = new GiantRise.Stage
		{
			Eye = () => rig.Camera?.GlobalPosition ?? player.GlobalPosition + Vector3.Up * 1.6f,
			Forward = fwd,
			GroundY = groundY,
			Distance = GiantDistance,
			// (2026-10-19) up from the landing's line, not the forest floor: seen from the top treads too, the whole rise
			HeadStartY = _original.NewelSeatGlobal.Origin.Y + 1.5f,
			Speed = auto ? 0.5f : 1f,
			Look = target => DriveView(player, target, 0.09f, 0.05f),
			Tremble = u => { Trembled = true; Shiver(player, u * TrembleRadians); },
			Sound = (path, at, db, pitch) => StoryBeat.PlayAt(this, path, "Unnatural", ToLocal(at), db, 12f, 400f, pitch),
			Beat = beat =>
			{
				if (beat == "rising" && hum != null)
				{
					var rise = CreateTween();
					rise.TweenMethod(Callable.From<float>(db => hum.SetOverrideDb(db)), hum.LevelDb, hum.MaxDb, auto ? 7f : 13f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
				}
				else if (beat == "risen")
				{
					LookedUp = rig.Pitch > Mathf.DegToRad(20f);
					GD.Print($"[story] Act 11: it has risen out of the woods (pitch {Mathf.RadToDeg(rig.Pitch):0} deg, size {size:0})");
					_ = Ask("\"Do you see him now?\"", ct);
				}
				else if (beat == "grabbed") Grabbed = true;
			},
		};
		try
		{
			await GiantRise.Run(this, body, skin, stage, ct);
			EndingStarted = true;
			// (2026-10-16, STORY.md Rev 5) no more cut to black on the grab: its hand closes round them, lifts them over the
			// treetops and throws them, and the black comes at the end of the fall, the hum cut dead with it
			await Throw(player, body, fader, hum, ct);
			PassedOut = true;
		}
		finally
		{
			if (IsInstanceValid(body)) body.QueueFree();
			if (IsInstanceValid(rig)) { rig.MaxPitchOverride = null; rig.FovSwim = 0f; }
			if (_slit != null && IsInstanceValid(_slit)) _slit.QueueFree();
			_slit = null;
			postMat?.SetShaderParameter("vignette", baseVignette);
		}
		GD.Print("[story] Act 11: thrown, into black");
		// (2026-10-22, the debug run: built after the chapter card, the station and all under it held the wake-up on black for
		// up to half a minute on a busy machine) begun now, at the start of this black: the silence, the radio's last line
		// and the chapter's title cover most of it, and the wake waits only for what's left (WhenBuilt)
		if (StationInterior.Instance is { Built: false } underground) underground.EnsureBuilt();
		HumOut = true;
		await Cutscene.Wait(this, HumOutSeconds * 0.6f, ct);   // silence over black
		ForestAmbienceManager.Instance?.ReleaseSilence(this);
		await Cutscene.Wait(this, 0.6, ct);

		// Over black: the last question, and the story's end saved in one go.
		await Ask(ClosingLine, ct);
		var s = StoryManager.Instance;
		s?.SetFlag(StoryManager.Flag.NewelSeated);
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act11GiantEncounter);
		// (2026-10-16) the fourth chapter's title over the black (it was on the wake at the lake); the morning comes up as it goes
		UI.ChapterCard.Show(this, 4, 0.6f, overBlack: true);
		await Cutscene.Wait(this, GameSettings.Instance.AutoTest ? 1.0 : 6.4, ct);
		// Act 12 picks up from here: the lake, at sunrise.
		await WakeAtLake(player, fader, ct);
	}

	// ================================================================== the throw

	/// <summary>For tests: they were lifted over the treetops and thrown (the arc flown to its end).</summary>
	public bool Thrown { get; private set; }
	/// <summary>For tests: how high over the landing the lift took them (m), and how far the throw carried them (m).</summary>
	public float LiftHeight { get; private set; }
	public float ThrowDistance { get; private set; }
	/// <summary>For tests: the highest they were over the ground from 40% of the flight on (m): in among the crowns, not over them.</summary>
	public float LateClearance { get; private set; }

	[ExportGroup("The throw")]
	/// <summary>Seconds: the hand closing, the lift over the treetops, the swing back, and the flight.</summary>
	[Export] public float CloseSeconds = 0.7f;
	[Export] public float LiftSeconds = 3.2f;
	[Export] public float WindSeconds = 1.3f;
	[Export] public float FlightSeconds = 3.6f;   // (2026-10-23: was 4.6, a long glide over the canopy)
	/// <summary>How far the throw carries them (m, level). (2026-10-23, the owner: "they look too high up and can see the open
	/// game world space above the trees": thrown down, not up and over; see Arc)</summary>
	[Export] public float ThrowReach = 120f;
	/// <summary>How far above the woods' floor the fall ends, in among the crowns, when the black comes.</summary>
	[Export] public float FallEndsAbove = 9f;

	private CanvasLayer _slit;
	private Control _slitTop, _slitBottom;

	/// <summary>
	/// STORY.md Rev 5, Act 11, the throw: control taken, the motion smooth throughout (no shake, no flash).
	/// 1. Its hand closes round them: the view held in the black of its fingers, a slit of fog and treetops.
	/// 2. It lifts them high over the treetops, the woods falling away below (the hand carries the eye; the view tips down).
	/// 3. A swing back, the view turning to where it will throw; the fingers open and they fly: a long arc, rising and then
	///    down, the view along the way they go, faster as they fall, the trees rushing past at the edges as the woods come up.
	/// 4. Black, the hum cut dead.
	/// The arc goes the way with the most woods under it (the terrain's own extent), leaning toward the lake.
	/// </summary>
	private async Task Throw(PlayerController player, StalkerBody body, ScreenFader fader, StairsHum hum, CancellationToken ct)
	{
		var rig = player.CameraRig;
		bool auto = GameSettings.Instance.AutoTest;
		float sp = auto ? 0.5f : 1f;
		const string side = "R";
		Vector3 Eye() => rig.Camera?.GlobalPosition ?? player.GlobalPosition + Vector3.Up * rig.EyeHeight;
		Vector3 eye0 = Eye();
		Vector3 feetToEye = eye0 - player.GlobalPosition;
		Vector3 shoulder = body.BoneWorld("upper_" + side);
		Vector3 handOff = body.BoneWorld("hand_" + side) - eye0;
		Vector3 fingers = (eye0 - shoulder) with { Y = 0f };
		fingers = fingers.LengthSquared() > 0.01f ? fingers.Normalized() : Vector3.Forward;
		float reach = body.ArmLength(side);
		body.Hold = true;

		void PutEye(Vector3 eye) => player.GlobalPosition = eye - feetToEye;
		void Hand(Vector3 eye)
		{
			body.Grab(eye + handOff, (eye - shoulder).Normalized(), side);
			body.Step();
		}
		void Look(Vector3 dir, float yawStep, float pitchStep)
		{
			// never steeper than 50 degrees either way (the drive's watch flags past 70)
			float h = new Vector2(dir.X, dir.Z).Length();
			float pitch = Mathf.Clamp(Mathf.Atan2(dir.Y, Mathf.Max(h, 0.001f)), Mathf.DegToRad(-50f), Mathf.DegToRad(50f));
			Vector3 flat = h > 0.001f ? new Vector3(dir.X, 0, dir.Z) / h : -rig.GlobalBasis.Z with { Y = 0 };
			Vector3 eye = Eye();
			Vector3 target = eye + flat * 10f + Vector3.Up * Mathf.Tan(pitch) * 10f;
			DriveView(player, target, yawStep, pitchStep);
		}

		// ---- 1. the fingers close: the view held in a slit of fog and treetops
		BuildSlit();
		var close = CreateTween().SetParallel();
		close.TweenProperty(_slitTop, "anchor_bottom", 0.41f, CloseSeconds * sp).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		close.TweenProperty(_slitBottom, "anchor_top", 0.6f, CloseSeconds * sp).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		StoryBeat.PlayAt(this, AudioDirector.Take("giant_sinew_creak", 8), "Unnatural", ToLocal(eye0 + handOff), -2f, 6f, 120f, 0.8f);

		// ---- 2. the lift: up and back toward its shoulder, over the treetops
		Vector3 fromShoulder = (eye0 - shoulder) with { Y = 0f };
		fromShoulder = fromShoulder.LengthSquared() > 0.01f ? fromShoulder.Normalized() : Vector3.Forward;
		Vector3 lifted = shoulder + Vector3.Up * reach * 0.8f + fromShoulder * reach * 0.4f;
		lifted.Y = Mathf.Max(lifted.Y, eye0.Y + 14f);   // over the treetops, whatever its size (2026-10-23: was 22 m, the open sky round the world's edge in view)
		Vector3 liftCtrl = eye0.Lerp(lifted, 0.5f) + Vector3.Up * 4f;
		Vector3 throwDir = ThrowDirection(eye0, fingers);
		Vector3 view0 = -rig.GlobalBasis.Z;
		double t = 0;
		float liftT = LiftSeconds * sp;
		while (t < liftT)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((float)(t / liftT), 0f, 1f));
			Vector3 eye = (1 - u) * (1 - u) * eye0 + 2 * (1 - u) * u * liftCtrl + u * u * lifted;
			PutEye(eye);
			Hand(eye);
			// the view tips down over the woods falling away
			Look(view0 with { Y = 0 } + Vector3.Down * Mathf.Lerp(0.1f, 0.75f, u), 0.012f, 0.01f);
		}
		LiftHeight = lifted.Y - eye0.Y;

		// ---- 3. the swing back, the view turning to where it throws
		Vector3 wound = lifted - throwDir * 3.5f + Vector3.Up * 1.5f;
		t = 0;
		float windT = WindSeconds * sp;
		while (t < windT)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((float)(t / windT), 0f, 1f));
			Vector3 eye = lifted.Lerp(wound, u);
			PutEye(eye);
			Hand(eye);
			Look(throwDir + Vector3.Down * 0.2f, 0.045f, 0.012f);
		}

		// ---- the flight: the fingers open, a long arc up and then down into the woods
		var terrain = GroundSnap.FindTerrain(this);
		float Ground(Vector3 p) => terrain != null ? terrain.HeightAt(p.X, p.Z) : eye0.Y - 40f;
		Vector3 start = wound;
		Vector3 end = start + throwDir * ThrowReach;
		end.Y = Ground(end) + FallEndsAbove;
		// (2026-10-23, the owner: thrown "a little lower than the tree tops") the way out level, the height falling away at once:
		// down to the crowns within the first 40% of it (the stair's top stands far over the woods: an arc from up there flew
		// them 40 m over the canopy, the open sky round the world's edge in view), then on through them; never into a hill
		Vector3 Arc(float u)
		{
			Vector3 p = start.Lerp(end, u);
			float fall = 1f - Mathf.Pow(1f - u, 4f);
			p.Y = Mathf.Lerp(start.Y, end.Y, fall);
			return p with { Y = Mathf.Max(p.Y, Ground(p) + FallEndsAbove * 0.8f) };
		}
		var open = CreateTween().SetParallel();
		open.TweenProperty(_slitTop, "anchor_bottom", 0f, 0.5f * sp).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		open.TweenProperty(_slitBottom, "anchor_top", 1f, 0.5f * sp).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		StoryBeat.PlayAt(this, AudioDirector.Take("giant_sinew_creak", 8), "Unnatural", ToLocal(start), 2f, 10f, 200f, 0.7f);
		float flightT = FlightSeconds * sp;
		bool blacking = false;
		t = 0;
		Task fade = Task.CompletedTask;
		while (t < flightT)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			float s = Mathf.Clamp((float)(t / flightT), 0f, 1f);
			// thrown down: quick off the hand, falling at once (the height's own ease in Arc)
			float u = s;
			Vector3 eye = Arc(u);
			PutEye(eye);
			if (s > 0.4f) LateClearance = Mathf.Max(LateClearance, eye.Y - Ground(eye));
			Vector3 vel = Arc(Mathf.Min(1f, u + 0.02f)) - Arc(Mathf.Max(0f, u - 0.02f));
			// (looking down along the way they go: the woods coming up, not the sky over them)
			Look(vel.Normalized() + Vector3.Down * 0.35f, 0.03f, 0.016f);
			// the speed in the view: a little wider as they fall (eased, never a jump)
			rig.FovSwim = Mathf.Lerp(0f, 12f, Mathf.SmoothStep(0.45f, 1f, s));
			if (!blacking && s > 0.86f && fader != null)
			{
				blacking = true;
				fade = fader.Fade(1f, flightT * 0.14f, ct);
			}
		}
		await fade;
		if (fader != null) fader.BlackAlpha = 1f;
		hum?.SetOverrideDb(-80f);
		ThrowDistance = new Vector2(end.X - eye0.X, end.Z - eye0.Z).Length();
		Thrown = true;
		GD.Print($"[story] Act 11: lifted {LiftHeight:0} m and thrown {ThrowDistance:0} m");
	}

	/// <summary>Which way the giant throws them: of sixteen ways, the one with the most of the woods' ground under the arc
	/// (inside the terrain's extent), leaning toward the lake, and never back through the giant itself.</summary>
	private Vector3 ThrowDirection(Vector3 from, Vector3 towardPlayer)
	{
		var terrain = GroundSnap.FindTerrain(this);
		var lake = GetTree().GetFirstNodeInGroup("lake_marker") as Node3D;
		Vector3 toLake = lake != null ? (lake.GlobalPosition - from) with { Y = 0 } : Vector3.Zero;
		if (toLake.LengthSquared() > 1f) toLake = toLake.Normalized();
		Vector3 best = towardPlayer;
		float bestScore = float.MinValue;
		for (int i = 0; i < 16; i++)
		{
			float a = Mathf.Tau * i / 16f;
			var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
			float score = 0f;
			if (terrain != null)
				for (float r = 20f; r <= ThrowReach + 30f; r += 15f)
				{
					Vector3 l = terrain.ToLocal(from + d * r);
					if (l.X > terrain.MinXZ.X + 10f && l.X < terrain.MaxXZ.X - 10f && l.Z > terrain.MinXZ.Y + 10f && l.Z < terrain.MaxXZ.Y - 10f) score += 1f;
				}
			score += 1.5f * d.Dot(toLake);
			if (d.Dot(-towardPlayer) > 0.5f) score -= 4f;   // (back the way of its body)
			if (score > bestScore) { bestScore = score; best = d; }
		}
		return best;
	}

	/// <summary>The black of its fingers: two bands closing from the top and the bottom of the picture, their edges soft
	/// (no hard line), leaving a slit across the middle.</summary>
	private void BuildSlit()
	{
		if (_slit != null && IsInstanceValid(_slit)) _slit.QueueFree();
		_slit = new CanvasLayer { Name = "GiantHand", Layer = 8 };
		Cutscene.SceneRoot(this).AddChild(_slit);
		var dark = new Color(0.012f, 0.009f, 0.008f, 1f);
		TextureRect Band(bool top)
		{
			var g = new Gradient();
			g.SetColor(0, top ? dark : dark with { A = 0f });
			g.SetColor(1, top ? dark with { A = 0f } : dark);
			g.AddPoint(top ? 0.82f : 0.18f, dark);
			var r = new TextureRect
			{
				Texture = new GradientTexture2D { Gradient = g, FillFrom = Vector2.Zero, FillTo = new Vector2(0, 1), Width = 4, Height = 128 },
				StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore,
				AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = top ? 0f : 1f, AnchorBottom = top ? 0f : 1f,
			};
			_slit.AddChild(r);
			return r;
		}
		_slitTop = Band(true);
		_slitBottom = Band(false);
	}


	/// <summary>
	/// (2026-10-16, STORY.md Rev 5, Act 12: the wake) Still black after the throw: they come to at sunrise on the ground
	/// at the head of the lake's dock, lying where the long furrow their body dragged ends, looking back up it to the crater
	/// where they came down (<see cref="Lake.WakeSpotWorld"/>, the crater and furrow from Lake.Wake.cs). They get up, turn
	/// round, and the dock is there, the mist on the water and the rowboat tied at its end: "Dad's rowboat?". Control comes
	/// back once they're facing it. All of it smooth (the eye rising, the head turning), no snaps.
	/// </summary>
	private async Task WakeAtLake(PlayerController player, ScreenFader fader, CancellationToken ct)
	{
		if (GetTree().GetFirstNodeInGroup("lake_marker") is not Lake lake) return;
		var rig = player.CameraRig;
		bool auto = GameSettings.Instance.AutoTest;
		var story = StoryManager.Instance;
		// the line is said here (the dock's own trigger, for a Continue, stands down)
		bool sayLine = story?.HasFlag(Lake.RowboatLineFlag) != true;
		story?.SetFlag(Lake.RowboatLineFlag);
		if (fader != null) fader.SetBlack(true);
		// lying at the end of the furrow, facing back up it (away from the dock)
		float facingFurrow = Mathf.Wrap(lake.WakeYaw + Mathf.Pi, -Mathf.Pi, Mathf.Pi);
		player.Teleport(lake.WakeSpotWorld, facingFurrow);
		StoryBeat.SetMood(this, ForestAtmosphere.Mood.Dawn, 4.0f);
		float standEye = rig.EyeHeight;
		const float lyingEye = 0.34f;
		rig.EyeHeight = lyingEye;
		rig.SetPitch(Mathf.DegToRad(-5f));
		try
		{
			// (2026-10-15) the station across the water built now, in this black, so its front door opens onto its lobby
			// (it was built in the black after the door; Lake.FrontDoor.cs)
			if (StationInterior.Instance is { Built: false } station)
			{
				station.EnsureBuilt();
				await station.WhenBuilt(ct);
			}
			await Cutscene.Wait(this, auto ? 0.4 : 1.2, ct);
			if (fader != null) await fader.Fade(0f, auto ? 1.5f : 3.4f, ct);
			// a while on the ground, looking back up the furrow to the crater
			await Cutscene.Wait(this, auto ? 0.4 : 2.4, ct);
			// getting up: the eye rises off the ground, the head coming level
			double up = auto ? 1.0 : 2.6, t = 0;
			while (t < up)
			{
				await Cutscene.Frame(this, ct);
				t += GetProcessDeltaTime();
				float u = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, (float)(t / up)));
				rig.EyeHeight = Mathf.Lerp(lyingEye, standEye, u);
				rig.SetPitch(Mathf.Lerp(Mathf.DegToRad(-5f), 0f, u));
			}
			rig.EyeHeight = standEye;
			// turning round: the dock, the mist on the water, the boat
			double turn = auto ? 1.2 : 2.8;
			t = 0;
			float turned = 0f;
			while (t < turn)
			{
				await Cutscene.Frame(this, ct);
				t += GetProcessDeltaTime();
				// (each frame only the step along the turn: the view's own easing follows it, never overshoots)
				float u = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, (float)(t / turn)));
				player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.Pi * u - turned, 0f));
				turned = Mathf.Pi * u;
			}
		}
		finally
		{
			if (IsInstanceValid(rig)) rig.EyeHeight = standEye;
		}
		GD.Print("[story] Act 12: woke at the lake, by the crater; turned round to the dock");
		if (sayLine)
			_ = Cutscene.Run(this, async lineCt =>
			{
				await Cutscene.Wait(this, 0.5, lineCt);
				await PlayerVoice.Say(this, "dads_rowboat", 2.2f, lineCt);
			});
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

	/// <summary>The end card, the studio, the thanks; then the menu (the pause menu is locked
	/// meanwhile). Called once the real ending (Act 12's lake crossing) hands back checkpoint 10,
	/// not directly by this class any more.</summary>
	public async Task Credits(ScreenFader fader, CancellationToken ct)
	{
		var pause = FindPauseMenu();
		if (pause != null) pause.Locked = true;
		try
		{
			if (fader != null)
			{
				await ShowEndCard(fader, EndCardHoldSeconds);
				ct.ThrowIfCancellationRequested();
				// every picture taken, polaroid by polaroid (the owner), and what the roll came to
				if (PhotoLog.Instance is { RecordedCount: > 0 } log)
				{
					var montage = new PolaroidMontage { Name = "PolaroidMontage" };
					AddChild(montage);
					await montage.Play(log.Photos, ct);
					montage.QueueFree();
					await fader.ShowCaption($"{log.SubjectsFound} of {PhotoCatalog.Total}", $"{log.TotalScore} points", 1.2f, CreditHoldSeconds, 1.2f, ct);
				}
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
