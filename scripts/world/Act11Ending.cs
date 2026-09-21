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

	/// <summary>For the autotest: the base-of-the-stairs trigger it should walk into to start the climb.</summary>
	public Vector3? ClimbTriggerWorld => _climbTrigger?.GlobalPosition;
	/// <summary>For the autotest: a point square in front of the stairs' base, to approach from before
	/// aiming at the trigger itself (mirrors the original climb's own AutotestApproach → top route).</summary>
	public Vector3? ApproachWorld => _original?.GetNodeOrNull<Node3D>("AutotestApproach")?.GlobalPosition;

	private StaircaseBuilder _original;
	private Node3D _bunker;
	private Node3D _clearing;
	private PlayerController _player;
	private Area3D _climbTrigger;
	private bool _stageAStarted;
	private bool _fogRamped;
	private bool _climbFired;

	public override void _Ready() => _original = GetNodeOrNull<StaircaseBuilder>(OriginalStairsPath);

	public override void _Process(double delta)
	{
		var s = StoryManager.Instance;
		if (s == null || _original == null) return;
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player == null) return;
		_bunker ??= GetTree().GetFirstNodeInGroup("bunker_marker") as Node3D;
		_clearing ??= GetTree().GetFirstNodeInGroup("stairs_clearing_marker") as Node3D;

		if (s.Current == Checkpoint.Act10WalkieFound && !s.Act11DialogueDone && !_stageAStarted)
		{
			_stageAStarted = true;
			_ = StageA(_player);
			return;
		}
		if (!s.Act11DialogueDone || s.Current >= Checkpoint.Act11GiantEncounter) return;

		EnsureClimbTrigger();
		MaybeRampFog();
	}

	// ================================================================== the radio, outside again

	private async Task StageA(PlayerController player)
	{
		player.GetNodeOrNull<PlayerInventory>("Inventory")?.TryPickup(ToolKind.Radio);

		player.PlayerInput.SetEnabled(false);
		player.SetPhysicsProcess(false);
		var fader = GetTree().Root.FindChild("ScreenFader", true, false) as ScreenFader;
		if (fader != null) await fader.Fade(1f, 1.0f);

		Vector3 spot = (_bunker?.GlobalPosition ?? player.GlobalPosition) + new Vector3(0, 0, 16f);
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain != null) spot.Y = terrain.HeightAt(spot.X, spot.Z) + 0.2f;
		Vector3 away = _bunker != null ? spot - _bunker.GlobalPosition : Vector3.Forward;
		float yawHome = Mathf.Atan2(-away.X, -away.Z);
		player.Teleport(spot, yawHome);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		if (fader != null) await fader.Fade(0f, 1.2f);
		player.SetPhysicsProcess(true);
		player.PlayerInput.SetEnabled(true);

		await Wait(GameSettings.Instance.AutoTest ? 0.5 : 2.0);

		int voiceIdx = AudioServer.GetBusIndex("Voice");
		PlayStatic(0.3f);
		if (voiceIdx >= 0) AudioServer.SetBusEffectEnabled(voiceIdx, 1, true);
		if (Subtitle.Instance != null) await Subtitle.Instance.Show("\"Did you see them?\"", 0.8f, 3.0f, 0.8f);
		if (voiceIdx >= 0) AudioServer.SetBusEffectEnabled(voiceIdx, 1, false);

		await Wait(0.7);
		if (Subtitle.Instance != null) await Subtitle.Instance.Show("\"Who are you!\"", 0.6f, 2.2f, 0.8f);

		PlayStatic(0.6f);
		await Wait(1.0);

		// Already impossibly tall by the time the compass leads there — not growing at the last second.
		_original.Steps = ClimbStepCount;
		_original.Build();

		StoryManager.Instance.MarkAct11DialogueDone();
		GD.Print("[story] Act 11: the radio speaks");
	}

	private void PlayStatic(float extraGain)
	{
		string path = "res://assets/audio/ambient/radio_static_loop.wav";
		if (!ResourceLoader.Exists(path)) return;
		var p = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(path), Bus = "Voice", VolumeDb = -6f + extraGain * 10f };
		GetTree().Root.AddChild(p);
		p.Play();
		_ = StopAfter(p, 1.4);
	}

	private async Task StopAfter(AudioStreamPlayer p, double seconds)
	{
		await Wait(seconds);
		var tween = CreateTween();
		tween.TweenProperty(p, "volume_db", -60f, 0.4f);
		await ToSignal(tween, Tween.SignalName.Finished);
		p.QueueFree();
	}

	// ================================================================== the walk back to the stairs

	private void EnsureClimbTrigger()
	{
		if (_climbTrigger != null || _original == null) return;
		_climbTrigger = new Area3D { CollisionLayer = 0, CollisionMask = 2, Monitorable = false, Monitoring = true, Position = new Vector3(0, 0.4f, -0.3f) };
		_climbTrigger.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.7f, 0.8f, 0.7f) } });
		_original.AddChild(_climbTrigger);
		_climbTrigger.BodyEntered += OnClimbTriggerEntered;
	}

	/// <summary>The closer the player gets, the more the fog swallows everything above roughly the
	/// stairs' midpoint — from the ground you can never quite see where they end.</summary>
	private void MaybeRampFog()
	{
		if (_fogRamped) return;
		float d = new Vector2(_player.GlobalPosition.X - _original.GlobalPosition.X, _player.GlobalPosition.Z - _original.GlobalPosition.Z).Length();
		if (d > FogRampRadius) return;
		_fogRamped = true;
		if (GetTree().Root.FindChild("Atmosphere", true, false) is ForestAtmosphere atmo)
			atmo.SetHeightFog(_original.GlobalPosition.Y + _original.TotalHeight * 0.4f, 0.1f, 9f);
	}

	private void OnClimbTriggerEntered(Node3D body)
	{
		if (_climbFired || body is not PlayerController player) return;
		if (StoryManager.Instance is not { Act11DialogueDone: true }) return;
		_climbFired = true;
		_ = ClimbAndEncounter(player);
	}

	// ================================================================== the climb, the giant, the touch

	private async Task ClimbAndEncounter(PlayerController player)
	{
		player.PlayerInput.SetEnabled(false);
		player.SetPhysicsProcess(false);
		player.Velocity = Vector3.Zero;
		if (ForestAmbienceManager.Instance != null) ForestAmbienceManager.Instance.SilenceOverride = 1f;

		var top = _original.GetNodeOrNull<Node3D>("TopTrigger");
		Vector3 dest = (top?.GlobalPosition ?? _original.GlobalPosition) + new Vector3(0, 0.1f, 1.0f);
		float climbSeconds = GameSettings.Instance.AutoTest ? ClimbSecondsAutoTest : ClimbSecondsReal;

		var tween = player.CreateTween();
		tween.TweenProperty(player, "global_position", dest, climbSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await player.ToSignal(tween, Tween.SignalName.Finished);

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
		var body = new StalkerBody { Skin = skin, Size = BodyScale, SwaySeconds = 26f, SwayDegrees = 0.5f, HeadDriftDegrees = 0.8f };
		// Must be in the tree before GlobalPosition/LookAt, or Godot can't resolve the transform.
		GetTree().Root.AddChild(body);
		body.GlobalPosition = giantStart;
		body.LookAt(player.GlobalPosition, Vector3.Up);

		await PanTowards(player, body.GlobalPosition, 2.2f);
		await Wait(1.2);

		var eyeTween = CreateTween();
		eyeTween.TweenMethod(Callable.From<float>(v => skin.SetShaderParameter("eye_glow", v)), 0f, 7.5f, 2.4f);
		await ToSignal(eyeTween, Tween.SignalName.Finished);
		await Wait(1.6);

		Vector3 toPlayer = player.GlobalPosition - body.GlobalPosition; toPlayer.Y = 0;
		Vector3 closeSpot = body.GlobalPosition + toPlayer.Normalized() * Mathf.Max(0f, toPlayer.Length() - 2.4f);
		float approachSeconds = GameSettings.Instance.AutoTest ? ApproachSecondsAutoTest : ApproachSecondsReal;
		var approach = CreateTween();
		approach.TweenProperty(body, "global_position", closeSpot, approachSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		await ToSignal(approach, Tween.SignalName.Finished);

		var fader = GetTree().Root.FindChild("ScreenFader", true, false) as ScreenFader;
		if (fader != null) await fader.Fade(1f, 0.25f);
		body.QueueFree();
		await Wait(1.0);

		if (ForestAmbienceManager.Instance != null) ForestAmbienceManager.Instance.SilenceOverride = -1f;
		if (GetTree().Root.FindChild("Atmosphere", true, false) is ForestAtmosphere atmo)
		{
			atmo.ClearHeightFog(0.1f);
			atmo.SetMood(ForestAtmosphere.Mood.Dawn, 0.1f);
		}

		Vector3 wake = FindSafeWakeSpot();
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain != null) wake.Y = terrain.HeightAt(wake.X, wake.Z);
		player.Teleport(wake + new Vector3(0, 0.15f, 0), 0f);

		if (fader != null) await fader.Fade(0f, 1.4f);
		player.SetPhysicsProcess(true);
		player.PlayerInput.SetEnabled(true);

		StoryManager.Instance.ReachCheckpoint(Checkpoint.Act11GiantEncounter, player.GlobalPosition, player.CameraRig.Yaw);
		if (fader != null) await fader.ShowCaption("", "Morning. You don't remember how you got here.", 1.4f, 3.6f, 1.4f);
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
		Vector3 center = _clearing?.GlobalPosition ?? _original.GlobalPosition;
		var hazards = new System.Collections.Generic.List<Vector3>();
		foreach (var n in GetTree().GetNodesInGroup("act6_mini_stairs"))
			if (n is Node3D n3) hazards.Add(n3.GlobalPosition);
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

	/// <summary>Turns the player's own view toward a point over time — a scripted look, not input,
	/// so it works the same with PlayerInput disabled (mirrors AutoTest's own camera steering).</summary>
	private async Task PanTowards(PlayerController player, Vector3 target, float seconds)
	{
		double t = 0;
		while (t < seconds)
		{
			Vector3 to = target - player.GlobalPosition; to.Y = 0;
			if (to.LengthSquared() > 0.01f)
			{
				float want = Mathf.Atan2(-to.X, -to.Z);
				float diff = Mathf.AngleDifference(player.CameraRig.Yaw, want);
				player.PlayerInput.AddScriptedLook(new Vector2(Mathf.Clamp(diff, -0.06f, 0.06f), 0));
			}
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			t += GetProcessDeltaTime();
		}
	}

	private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds, true, true), SceneTreeTimer.SignalName.Timeout);
}
