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
/// the radio goes to silence — the compass then points at the very first
/// staircase, now rebuilt impossibly tall and already half-swallowed by fog
/// from a distance. Touching its base starts a long, silent climb; at the top,
/// Act 4's giant is waiting, its eyes opening on the last step before it closes
/// the distance and reaches out. The touch cuts to black and wakes the player,
/// at dawn, back in the Act 6 clearing among the small stairs — this is the
/// end of the story as written so far.
///
/// Restore: once the radio exchange is done (<see cref="StoryManager.Flag.Act11DialogueDone"/>)
/// the stairs are tall on load, and until the giant's checkpoint the climb trigger
/// and the fog-ramp zone are waiting. At checkpoint 8 without the exchange, the
/// radio sequence plays again. The radio's static runs on its own "Radio" bus.
/// </summary>
public partial class Act11Ending : Node3D
{
	[Export] public NodePath OriginalStairsPath = "..";
	[Export] public float BodyScale = 28f;
	[Export] public int ClimbStepCount = 220;
	[Export] public float ClimbSecondsReal = 46f;
	[Export] public float ClimbSecondsAutoTest = 6f;
	[Export] public float ApproachSecondsReal = 7f;
	[Export] public float ApproachSecondsAutoTest = 1.5f;
	[Export] public float FogRampRadius = 70f;
	/// <summary>Silence priority: above the storm's, so the climb is silent whatever else is going on.</summary>
	[Export] public int SilencePriority = 100;
	[ExportGroup("Stairs hum")]
	[Export] public float ClimbHumStartDb = -14f;
	[Export] public float ClimbHumTopDb = 0f;
	[Export] public float EncounterHumDb = 4f;

	/// <summary>For the autotest: the base-of-the-stairs trigger it should walk into to start the climb.</summary>
	public Vector3? ClimbTriggerWorld => _climbTrigger?.GlobalPosition;
	/// <summary>For the autotest: a point square in front of the stairs' base, to approach from before
	/// aiming at the trigger itself (mirrors the original climb's own AutotestApproach → top route).</summary>
	public Vector3? ApproachWorld => _original?.GetNodeOrNull<Node3D>("AutotestApproach")?.GlobalPosition;
	/// <summary>For tests: whether the stairs have been rebuilt impossibly tall.</summary>
	public bool StairsTall => _original != null && _original.Steps == ClimbStepCount;

	private StaircaseBuilder _original;
	private Area3D _climbTrigger;
	private Area3D _fogZone;
	private bool _stageAStarted;
	private bool _climbFired;

	public override void _Ready()
	{
		_original = GetNodeOrNull<StaircaseBuilder>(OriginalStairsPath);
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
		if (s.Act11DialogueDone) MakeStairsTall();
		_climbFired = s.Current >= Checkpoint.Act11GiantEncounter;
		OnStoryChanged(0);
	}

	private void OnStoryChanged<T>(T _)
	{
		var s = StoryManager.Instance;
		if (s == null || _original == null) return;
		if (s.Current == Checkpoint.Act10WalkieFound && !s.Act11DialogueDone && !_stageAStarted)
		{
			_stageAStarted = true;
			if (StoryBeat.Player(this) is { } p) Cutscene.Run(this, ct => StageA(p, ct));
			return;
		}
		if (s.Act11DialogueDone && s.Current < Checkpoint.Act11GiantEncounter) EnsureClimbTriggers();
	}

	private void MakeStairsTall()
	{
		if (_original.Steps == ClimbStepCount) return;
		_original.Steps = ClimbStepCount;
		_original.Build();
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
			await Cutscene.Frame(this, ct);
			if (fader != null) await fader.Fade(0f, 1.2f);
		}
		finally
		{
			if (IsInstanceValid(player) && player.IsInsideTree()) Cutscene.Unlock(player, true, true);
		}

		await Cutscene.Wait(this, GameSettings.Instance.AutoTest ? 0.5 : 2.0, ct);

		PlayStatic(0.3f);
		if (Subtitle.Instance != null) await Subtitle.Instance.Show("\"Did you see them?\"", 0.8f, 3.0f, 0.8f);

		await Cutscene.Wait(this, 0.7, ct);
		if (Subtitle.Instance != null) await Subtitle.Instance.Show("\"Who are you!\"", 0.6f, 2.2f, 0.8f);

		PlayStatic(0.6f);
		await Cutscene.Wait(this, 1.0, ct);

		// Already impossibly tall by the time the compass leads there — not growing at the last second.
		MakeStairsTall();

		StoryManager.Instance.MarkAct11DialogueDone();
		GD.Print("[story] Act 11: the radio speaks");
	}

	/// <summary>A burst of walkie-talkie static on the Radio bus (band-limited and distorted), faded out after 1.4 s.</summary>
	private void PlayStatic(float extraGain)
	{
		string path = "res://assets/audio/ambient/radio_static_loop.wav";
		if (!ResourceLoader.Exists(path)) return;
		var p = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(path), Bus = "Radio", VolumeDb = -6f + extraGain * 10f };
		Cutscene.SceneRoot(this).AddChild(p);
		p.Play();
		Cutscene.Run(this, async ct =>
		{
			try
			{
				await Cutscene.Wait(this, 1.4, ct);
				var tween = p.CreateTween();
				tween.TweenProperty(p, "volume_db", -60f, 0.4f);
				await Cutscene.Tween(this, tween, ct);
			}
			finally
			{
				if (IsInstanceValid(p)) p.QueueFree();
			}
		});
	}

	// ================================================================== the walk back to the stairs

	private void EnsureClimbTriggers()
	{
		if (_climbTrigger != null || _original == null || _climbFired) return;
		_climbTrigger = StoryBeat.MakeTrigger(_original, new BoxShape3D { Size = new Vector3(1.7f, 0.8f, 0.7f) },
			new Vector3(0, 0.4f, -0.3f), OnClimbTriggerEntered, "Act11ClimbTrigger");
		// The closer the player gets, the more the fog swallows everything above roughly the
		// stairs' midpoint — from the ground you can never quite see where they end.
		_fogZone = StoryBeat.MakeTrigger(_original, new CylinderShape3D { Radius = FogRampRadius, Height = 200f },
			Vector3.Zero, _ => RampFog(), "Act11FogZone");
	}

	private void RampFog()
	{
		if (_fogZone == null) return;
		_fogZone.QueueFree();
		_fogZone = null;
		StoryBeat.Atmosphere(this)?.SetHeightFog(_original.GlobalPosition.Y + _original.TotalHeight * 0.4f, 0.1f, 9f);
	}

	private void OnClimbTriggerEntered(PlayerController player)
	{
		if (_climbFired || StoryManager.Instance is not { Act11DialogueDone: true }) return;
		_climbFired = true;
		RampFog();   // in case the player came from inside the fog radius on load
		Cutscene.Run(this, ct => ClimbAndEncounter(player, ct), lockInput: true, freezeBody: true);
	}

	// ================================================================== the climb, the giant, the touch

	private async Task ClimbAndEncounter(PlayerController player, CancellationToken ct)
	{
		player.Velocity = Vector3.Zero;
		ForestAmbienceManager.Instance?.RequestSilence(this, 1f, SilencePriority);

		var top = _original.GetNodeOrNull<Node3D>("TopTrigger");
		Vector3 dest = (top?.GlobalPosition ?? _original.GlobalPosition) + new Vector3(0, 0.1f, 1.0f);
		float climbSeconds = GameSettings.Instance.AutoTest ? ClimbSecondsAutoTest : ClimbSecondsReal;

		// "The hum is very loud" on the way up, and "so loud now" at the top (STORY.md Act 11).
		var hum = StairsHum.Instance;
		var humTween = CreateTween();
		humTween.TweenMethod(Callable.From<float>(db => hum?.SetOverrideDb(db)), ClimbHumStartDb, ClimbHumTopDb, climbSeconds);

		var tween = player.CreateTween();
		tween.TweenProperty(player, "global_position", dest, climbSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await Cutscene.Tween(this, tween, ct);

		Vector3 fwd = -_original.GlobalTransform.Basis.Z; fwd.Y = 0;
		if (fwd.LengthSquared() < 0.01f) fwd = Vector3.Forward; else fwd = fwd.Normalized();
		Vector3 giantStart = dest + fwd * 9f;

		var skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stalker_skin.gdshader") };
		skin.SetShaderParameter("albedo", new Color(0.02f, 0.02f, 0.03f));
		skin.SetShaderParameter("face_tint", new Color(0.14f, 0.14f, 0.15f));
		skin.SetShaderParameter("visibility", 1f);
		skin.SetShaderParameter("wetness", 0.4f);
		skin.SetShaderParameter("eye_color", new Color(1f, 0.04f, 0.02f));
		skin.SetShaderParameter("eye_glow", 0f);
		var body = new StalkerBody { Name = "Act11Giant", Skin = skin, Size = BodyScale, SwaySeconds = 26f, SwayDegrees = 0.5f, HeadDriftDegrees = 0.8f };
		// Must be in the tree before GlobalPosition/LookAt, or Godot can't resolve the transform.
		Cutscene.SceneRoot(this).AddChild(body);
		Tween louder = null;
		try
		{
			body.GlobalPosition = giantStart;
			body.LookAt(player.GlobalPosition, Vector3.Up);

			await StoryBeat.PanTowards(this, player, body.GlobalPosition, 2.2f, ct);
			await Cutscene.Wait(this, 1.2, ct);

			var eyeTween = body.CreateTween();
			eyeTween.TweenMethod(Callable.From<float>(v => skin.SetShaderParameter("eye_glow", v)), 0f, 7.5f, 2.4f);
			louder = CreateTween();
			louder.TweenMethod(Callable.From<float>(db => hum?.SetOverrideDb(db)), ClimbHumTopDb, EncounterHumDb, 4f);
			await Cutscene.Tween(this, eyeTween, ct);
			await Cutscene.Wait(this, 1.6, ct);

			Vector3 toPlayer = player.GlobalPosition - body.GlobalPosition; toPlayer.Y = 0;
			Vector3 closeSpot = body.GlobalPosition + toPlayer.Normalized() * Mathf.Max(0f, toPlayer.Length() - 2.4f);
			float approachSeconds = GameSettings.Instance.AutoTest ? ApproachSecondsAutoTest : ApproachSecondsReal;
			var approach = body.CreateTween();
			approach.TweenProperty(body, "global_position", closeSpot, approachSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
			await Cutscene.Tween(this, approach, ct);

			var fader = StoryBeat.Fader(this);
			if (fader != null) await fader.Fade(1f, 0.25f);
		}
		finally
		{
			if (IsInstanceValid(body)) body.QueueFree();
		}
		louder?.Kill();
		hum?.SetOverrideDb(null);   // cut with the blackout; proximity takes over again in the clearing
		await Cutscene.Wait(this, 1.0, ct);

		ForestAmbienceManager.Instance?.ReleaseSilence(this);
		if (StoryBeat.Atmosphere(this) is { } atmo)
		{
			atmo.ClearHeightFog(0.1f);
			atmo.SetMood(ForestAtmosphere.Mood.Dawn, 0.1f);
		}

		Vector3 wake = FindSafeWakeSpot();
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain != null) wake.Y = terrain.HeightAt(wake.X, wake.Z);
		player.Teleport(wake + new Vector3(0, 0.15f, 0), 0f);

		if (StoryBeat.Fader(this) is { } f2) await f2.Fade(0f, 1.4f);

		StoryBeat.ReachCheckpoint(player, Checkpoint.Act11GiantEncounter);
		Cutscene.Run(this, _ => StoryBeat.Caption(this, "Morning. You don't remember how you got here.", 1.4f, 3.6f, 1.4f));
		GD.Print("[story] Act 11: the giant touches the player");
	}

	/// <summary>
	/// The Clearing marker's own position sits only ~5.6 m from the original stairs' reference point —
	/// comfortably inside that structure's footprint now that it has been rebuilt to 220 steps, and the
	/// fifteen Act 6 mini-stairs (deterministically seeded, so this is the same every playthrough) can
	/// extend well past their own 10 m minimum placement distance too. Rather than trust a single fixed
	/// point, search outward in rings for a spot with real clearance from every one of them.
	/// </summary>
	private Vector3 FindSafeWakeSpot()
	{
		var clearing = GetTree().GetFirstNodeInGroup("stairs_clearing_marker") as Node3D;
		Vector3 center = clearing?.GlobalPosition ?? _original.GlobalPosition;
		var hazards = new System.Collections.Generic.List<Vector3>();
		foreach (var n in GetTree().GetNodesInGroup("act6_mini_stairs"))
			if (n is Node3D n3 && !n3.IsQueuedForDeletion()) hazards.Add(n3.GlobalPosition);
		if (_original != null) hazards.Add(_original.GlobalPosition);

		bool Clear(Vector3 p)
		{
			foreach (var h in hazards)
				if (new Vector2(p.X - h.X, p.Z - h.Z).Length() < 7f) return false;
			return true;
		}

		if (Clear(center)) return center;
		for (int ring = 0; ring < 4; ring++)
		{
			float radius = 5f + ring * 3f;
			for (int i = 0; i < 8; i++)
			{
				float ang = Mathf.Tau / 8 * i;
				Vector3 candidate = center + new Vector3(Mathf.Cos(ang) * radius, 0, Mathf.Sin(ang) * radius);
				if (Clear(candidate)) return candidate;
			}
		}
		return center;   // every ring failed (implausible) — a rare overlap beats an unbounded search
	}
}
