using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Act 6's clearing. Once the player has crossed the bridge with the newel
/// post in hand, the woods around the original staircase turn menacing and
/// fifteen small stair variations appear around it. Getting close enough
/// plays the clearing's voice line and fuses the post onto one of them,
/// repointing the compass home. Stepping onto any of the fifteen (entirely
/// optional) triggers a longer, stranger climb on the original staircase and
/// a jump straight to night; skipping them lets night fall gradually instead
/// while the player heads back on their own.
/// </summary>
public partial class Act6ClearingEvent : Node3D
{
	[Export] public NodePath OriginalStairsPath = "..";
	[Export] public NodePath DressingPath = "../Dressing";
	[Export] public float VoiceRadius = 15f;
	[Export] public float SkipNightDelaySeconds = 90f;
	[Export] public float SkipNightDelayAutoTest = 6f;

	private StaircaseBuilder _original;
	private DeepZoneDressing _dressing;
	private readonly List<Node3D> _minis = new();
	private bool _revealed;
	private bool _voiceFired;
	private bool _climbFired;
	private PlayerController _player;

	public override void _Ready()
	{
		_original = GetNodeOrNull<StaircaseBuilder>(OriginalStairsPath);
		_dressing = GetNodeOrNull<DeepZoneDressing>(DressingPath);
	}

	public override void _Process(double delta)
	{
		if (StoryManager.Instance == null) return;
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player == null) return;

		if (!_revealed)
		{
			if (StoryManager.Instance is not { NewelPostTaken: true } || StoryManager.Instance.Current < Checkpoint.Act6BridgeCrossed) return;
			_revealed = true;
			Reveal();
			return;
		}
		if (_voiceFired) return;
		float d = new Vector2(_player.GlobalPosition.X - GlobalPosition.X, _player.GlobalPosition.Z - GlobalPosition.Z).Length();
		if (d > VoiceRadius) return;
		_voiceFired = true;
		_ = VoiceAndFuse();
	}

	private void Reveal()
	{
		if (GetTree().Root.FindChild("Atmosphere", true, false) is ForestAtmosphere atmo)
			atmo.SetMood(ForestAtmosphere.Mood.Menacing, 14f);
		_dressing?.Reveal(GlobalPosition, 34f);
		BuildMiniStairs();
		NavMeshBaker.Instance?.Bake();
		GD.Print("[story] Act 6: the woods around the clearing turn");
	}

	private void BuildMiniStairs()
	{
		var rng = new RandomNumberGenerator { Seed = 9001 };
		var terrain = GroundSnap.FindTerrain(this);
		const int count = 15;
		var placed = new List<(Vector2 xz, float clearance)>();
		for (int i = 0; i < count; i++)
		{
			// Reject placements that would crowd or overlap an already-placed staircase: with
			// fifteen of these packed into one clearing, pure random placement collides often
			// enough to be a real (and very literal) player-facing clipping bug.
			Vector2 xz = Vector2.Zero;
			float footprint = 0f;
			for (int attempt = 0; attempt < 40; attempt++)
			{
				float tryAng = (Mathf.Tau / count) * i + rng.RandfRange(-0.2f, 0.2f);
				float tryDist = rng.RandfRange(9f, 24f);
				Vector2 tryXz = new(Mathf.Cos(tryAng) * tryDist, Mathf.Sin(tryAng) * tryDist);
				float tryFootprint = 2.2f; // generous half-diagonal estimate before real Steps/Width are rolled
				bool clear = true;
				foreach (var p in placed)
					if (tryXz.DistanceTo(p.xz) < tryFootprint + p.clearance + 1.2f) { clear = false; break; }
				if (clear || attempt == 39) { xz = tryXz; footprint = tryFootprint; break; }
			}
			placed.Add((xz, footprint));
			Vector3 local = new(xz.X, 0, xz.Y);
			Vector3 world = GlobalPosition + local;
			float groundY = terrain?.HeightAt(world.X, world.Z) ?? world.Y;

			bool ruined = rng.Randf() < 0.4f;
			var stair = new StaircaseBuilder
			{
				Name = $"MiniStair{i}",
				Steps = rng.RandiRange(3, 9),
				Rise = rng.RandfRange(0.15f, 0.19f),
				Run = rng.RandfRange(0.24f, 0.3f),
				Width = rng.RandfRange(0.9f, 1.5f),
				PlinthSteps = 1,
				WallHeight = rng.RandfRange(0.3f, 0.5f),
				Ruined = ruined,
				CrumbleSide = rng.Randf() < 0.5f ? -1 : 1,
				CrumbleAfterStep = rng.RandiRange(1, 3),
				Seed = 100 + i,
				Scale = Vector3.One * rng.RandfRange(0.55f, 0.85f),
				Position = new Vector3(local.X, groundY - GlobalPosition.Y, local.Z),
				RotationDegrees = new Vector3(0, rng.RandfRange(0f, 360f), 0),
			};
			AddChild(stair);
			stair.AddToGroup("act6_mini_stairs");
			_minis.Add(stair);

			float footLen = stair.Steps * stair.Run * stair.Scale.Z + 1f;
			float footH = stair.TotalHeight * stair.Scale.Y + 1.5f;
			var trigger = new Area3D { CollisionLayer = 0, CollisionMask = 2, Monitorable = false };
			trigger.Position = new Vector3(0, footH * 0.5f, -footLen * 0.5f + 0.4f);
			trigger.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(stair.Width * stair.Scale.X + 1f, footH, footLen) } });
			stair.AddChild(trigger);
			trigger.BodyEntered += OnMiniStepped;
		}
	}

	private void OnMiniStepped(Node3D body)
	{
		if (_climbFired || !_voiceFired || body is not PlayerController player) return;
		_climbFired = true;
		_ = ExtendedClimb(player);
	}

	private async Task VoiceAndFuse()
	{
		if (GetTree().Root.FindChild("ScreenFader", true, false) is ScreenFader fader)
			await fader.ShowCaption("", "\"Come up and see.\"", 1.6f, 3.2f, 1.6f);

		var inv = _player.GetNodeOrNull<PlayerInventory>("Inventory");
		if (inv is { HasNewelPost: true } && _minis.Count > 0)
		{
			var target = _minis[new RandomNumberGenerator().RandiRange(0, _minis.Count - 1)];
			FlashFusion(target);
			inv.ConsumeNewelPost();
		}
		StoryManager.Instance.MarkClearingVoiceHeard();
		_ = SkipNightFallback();
	}

	/// <summary>A wooden creak and a burst of purple light where the post has fused on.</summary>
	private void FlashFusion(Node3D at)
	{
		var light = new OmniLight3D { LightColor = new Color(0.7f, 0.25f, 0.95f), LightEnergy = 6f, OmniRange = 6f, Position = Vector3.Up * 0.4f };
		at.AddChild(light);
		var tween = CreateTween();
		tween.TweenProperty(light, "light_energy", 0f, 2.2f).SetDelay(0.15f);
		tween.TweenCallback(Callable.From(light.QueueFree));

		string path = $"res://assets/audio/sfx/trunk_creak_{new RandomNumberGenerator().RandiRange(1, 3):00}.wav";
		if (ResourceLoader.Exists(path))
		{
			var voice = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), UnitSize = 3f, MaxDistance = 60f, VolumeDb = 2f };
			at.AddChild(voice);
			voice.Finished += voice.QueueFree;
			voice.Play();
		}
	}

	/// <summary>If the player never takes the optional stairs, night still falls — just gradually, on the walk back.</summary>
	private async Task SkipNightFallback()
	{
		float delay = GameSettings.Instance.AutoTest ? SkipNightDelayAutoTest : SkipNightDelaySeconds;
		await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
		if (_climbFired) return;
		if (GetTree().Root.FindChild("Atmosphere", true, false) is ForestAtmosphere atmo)
			atmo.SetMood(ForestAtmosphere.Mood.Night, 20f);
	}

	private async Task ExtendedClimb(PlayerController player)
	{
		player.PlayerInput.SetEnabled(false);
		player.SetPhysicsProcess(false);
		player.Velocity = Vector3.Zero;

		foreach (var m in _minis) m.QueueFree();
		_minis.Clear();

		var fader = GetTree().Root.FindChild("ScreenFader", true, false) as ScreenFader;
		if (fader != null) await fader.Fade(1f, 1.6f);
		await ToSignal(GetTree().CreateTimer(5.0), SceneTreeTimer.SignalName.Timeout);

		SpotLight3D spot = null;
		if (_original != null)
		{
			_original.Steps += Mathf.Max(20, _original.Steps);
			_original.Build();
			var top = _original.GetNodeOrNull<Node3D>("TopTrigger");
			Vector3 dest = top?.GlobalPosition ?? _original.GlobalPosition;
			player.GlobalPosition = dest + new Vector3(0, 0.1f, 1.0f);
			spot = new SpotLight3D
			{
				LightColor = Colors.White, LightEnergy = 0f, SpotRange = 14f, SpotAngle = 45f,
				GlobalPosition = dest + Vector3.Up * 8f,
			};
			spot.LookAt(dest, Vector3.Forward);
			GetTree().Root.AddChild(spot);
		}

		if (fader != null) await fader.Fade(0f, 1.8f);
		if (spot != null) CreateTween().TweenProperty(spot, "light_energy", 3.5f, 1.2f);

		if (GetTree().Root.FindChild("Atmosphere", true, false) is ForestAtmosphere atmo2)
			atmo2.SetMood(ForestAtmosphere.Mood.Night, 3f);

		player.SetPhysicsProcess(true);
		player.PlayerInput.SetEnabled(true);

		if (fader != null)
			await fader.ShowCaption("", "Hours must have passed. It's night now.", 1.2f, 3.0f, 1.2f);

		if (spot != null)
		{
			var t2 = CreateTween();
			t2.TweenProperty(spot, "light_energy", 0f, 2.5f).SetDelay(2.5f);
			t2.TweenCallback(Callable.From(spot.QueueFree));
		}
		GD.Print("[story] Act 6: the extended climb");
	}
}
