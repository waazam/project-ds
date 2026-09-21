using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

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
///
/// Restore: the reveal is derived from the checkpoint + newel post flag, the
/// voice from <see cref="StoryManager.Flag.ClearingVoiceHeard"/>, the optional
/// climb from <see cref="StoryManager.Flag.Act6ExtendedClimb"/> (no mini stairs,
/// the original taller) and nightfall from <see cref="StoryManager.Flag.Act6NightFell"/>
/// (a still-pending fallback restarts its timer). The overhead red spotlight
/// belongs to the voice beat only: it follows the player through the clearing
/// and is freed once they leave it (or the climb starts, or Act 7 begins).
/// </summary>
public partial class Act6ClearingEvent : Node3D
{
	[Export] public NodePath OriginalStairsPath = "..";
	[Export] public NodePath DressingPath = "../Dressing";
	[Export] public float VoiceRadius = 15f;
	[Export] public float SkipNightDelaySeconds = 90f;
	[Export] public float SkipNightDelayAutoTest = 6f;
	/// <summary>The spotlight lets go of the player once they are this far from the clearing.</summary>
	[Export] public float SpotlightReleaseRadius = 60f;

	private const float StairsMaxReach = 30f;   // outer edge of the mini-stairs placement band

	private StaircaseBuilder _original;
	private DeepZoneDressing _dressing;
	private readonly List<Node3D> _minis = new();
	private bool _revealed;
	private bool _voiceFired;
	private bool _climbFired;
	private bool _fallbackRunning;
	/// <summary>For the autotest: true once the optional extended climb has fired (a scripted
	/// teleport that a still-running approach/return walk needs to know to stop steering through).</summary>
	public bool ExtendedClimbFired => _climbFired;
	/// <summary>For tests: how many mini stairs currently stand.</summary>
	public int MiniStairCount => _minis.Count;
	/// <summary>For tests: whether the voice beat's spotlight is still following the player.</summary>
	public bool SpotlightActive => _playerSpot != null;

	private PlayerController _player;
	private SpotLight3D _playerSpot;
	private Area3D _voiceZone;

	public override void _Ready()
	{
		_original = GetNodeOrNull<StaircaseBuilder>(OriginalStairsPath);
		_dressing = GetNodeOrNull<DeepZoneDressing>(DressingPath);
		SetProcess(false);   // only while the spotlight rides
		Callable.From(Restore).CallDeferred();
		if (StoryManager.Instance is { } s)
		{
			s.CheckpointReached += OnCheckpoint;
			s.FlagSet += OnFlag;
		}
	}

	public override void _ExitTree()
	{
		if (StoryManager.Instance is { } s)
		{
			s.CheckpointReached -= OnCheckpoint;
			s.FlagSet -= OnFlag;
		}
	}

	private void OnCheckpoint(Checkpoint cp)
	{
		TryReveal();
		if (cp >= Checkpoint.Act7CabinBurning) ReleaseSpotlight(1.5f);
	}

	private void OnFlag(string _) => TryReveal();

	private void TryReveal()
	{
		if (_revealed || !StoryBeat.Act6Revealed(StoryManager.Instance)) return;
		Reveal(restoring: false);
	}

	/// <summary>Continue: rebuild whatever the clearing had become.</summary>
	private void Restore()
	{
		var s = StoryManager.Instance;
		if (!StoryBeat.Act6Revealed(s)) return;
		_voiceFired = s.ClearingVoiceHeard;
		_climbFired = s.HasFlag(StoryManager.Flag.Act6ExtendedClimb);
		Reveal(restoring: true);
		if (_climbFired && _original != null)
		{
			_original.Steps += Mathf.Max(20, _original.Steps);
			_original.Build();
		}
		if (_voiceFired && !_climbFired && !s.HasFlag(StoryManager.Flag.Act6NightFell)) StartNightFallback();
	}

	private void Reveal(bool restoring)
	{
		_revealed = true;
		_player ??= StoryBeat.Player(this);
		// Continue applies the saved mood itself (GameFlow); only a live reveal fades to it.
		if (!restoring) StoryBeat.SetMood(this, ForestAtmosphere.Mood.Menacing, 14f);
		// Giants stay well outside the mini-stairs band (StairsMaxReach + one stair's own footprint)
		// so their thick trunks never clip through a staircase.
		_dressing?.Reveal(GlobalPosition, 55f);
		if (!_climbFired) BuildMiniStairs();
		BuildClearingLights();
		if (!_voiceFired)
		{
			_voiceZone = StoryBeat.MakeTrigger(this, new CylinderShape3D { Radius = VoiceRadius, Height = 80f }, Vector3.Zero, OnVoiceZoneEntered, "VoiceZone");
			_voiceZone.TopLevel = true;
			_voiceZone.GlobalPosition = GlobalPosition;
		}
		if (!restoring) GD.Print("[story] Act 6: the woods around the clearing turn");
	}

	private void OnVoiceZoneEntered(PlayerController player)
	{
		if (_voiceFired) return;
		_voiceFired = true;
		_player = player;
		_voiceZone?.QueueFree();
		_voiceZone = null;
		Cutscene.Run(this, VoiceAndFuse);
	}

	/// <summary>A ring of cold, even fill light so the whole stand of fifteen stairs actually reads
	/// clearly, rather than being lost in the Menacing mood's low ambient.</summary>
	private void BuildClearingLights()
	{
		const int count = 6;
		for (int i = 0; i < count; i++)
		{
			float ang = Mathf.Tau / count * i;
			AddChild(new OmniLight3D
			{
				Name = $"ClearingFill{i}",
				LightColor = new Color(0.75f, 0.78f, 0.85f),
				LightEnergy = 3.2f,
				OmniRange = StairsMaxReach + 8f,
				Position = new Vector3(Mathf.Cos(ang) * StairsMaxReach * 0.5f, 11f, Mathf.Sin(ang) * StairsMaxReach * 0.5f),
			});
		}
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
				float tryDist = rng.RandfRange(10f, StairsMaxReach);
				Vector2 tryXz = new(Mathf.Cos(tryAng) * tryDist, Mathf.Sin(tryAng) * tryDist);
				float tryFootprint = 4.5f; // generous half-diagonal estimate for the larger builds below, before real Steps/Width are rolled
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
				// Bigger and longer than a normal flight — these should loom, not look like garden steps.
				Steps = rng.RandiRange(9, 18),
				Rise = rng.RandfRange(0.16f, 0.2f),
				Run = rng.RandfRange(0.26f, 0.32f),
				Width = rng.RandfRange(1.1f, 1.8f),
				PlinthSteps = 1,
				WallHeight = rng.RandfRange(0.35f, 0.55f),
				Ruined = ruined,
				CrumbleSide = rng.Randf() < 0.5f ? -1 : 1,
				CrumbleAfterStep = rng.RandiRange(1, 3),
				Seed = 100 + i,
				Scale = Vector3.One * rng.RandfRange(1.15f, 1.7f),
				Position = new Vector3(local.X, groundY - GlobalPosition.Y, local.Z),
				RotationDegrees = new Vector3(0, rng.RandfRange(0f, 360f), 0),
			};
			AddChild(stair);
			stair.AddToGroup("act6_mini_stairs");
			_minis.Add(stair);

			float footLen = stair.Steps * stair.Run * stair.Scale.Z + 1f;
			float footH = stair.TotalHeight * stair.Scale.Y + 1.5f;
			StoryBeat.MakeTrigger(stair,
				new BoxShape3D { Size = new Vector3(stair.Width * stair.Scale.X + 1f, footH, footLen) },
				new Vector3(0, footH * 0.5f, -footLen * 0.5f + 0.4f), OnMiniStepped, "StepTrigger");
		}
	}

	private void OnMiniStepped(PlayerController player)
	{
		if (_climbFired || !_voiceFired) return;
		_climbFired = true;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.Act6ExtendedClimb);
		Cutscene.Run(this, ct => ExtendedClimb(player, ct), lockInput: true, freezeBody: true);
	}

	private async Task VoiceAndFuse(CancellationToken ct)
	{
		BuildPlayerSpotlight();
		PlayVoice();
		await StoryBeat.Caption(this, "\"Come up and see.\"", 1.6f, 3.2f, 1.6f);
		ct.ThrowIfCancellationRequested();

		var inv = _player.GetNodeOrNull<PlayerInventory>("Inventory");
		if (inv is { HasNewelPost: true } && _minis.Count > 0)
		{
			var target = _minis[new RandomNumberGenerator().RandiRange(0, _minis.Count - 1)];
			FlashFusion(target);
			inv.ConsumeNewelPost();
		}
		StoryManager.Instance.MarkClearingVoiceHeard();
		StartNightFallback();
	}

	/// <summary>The clearing's line, heard once: clear but dreamlike-distant, so it plays
	/// unpositioned (inside the head rather than from a spot in the trees), slightly slowed.</summary>
	private void PlayVoice()
	{
		const string path = "res://assets/audio/voice/come_and_see_distant.mp3";
		if (!ResourceLoader.Exists(path)) return;
		var voice = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(path), Bus = "Voice", VolumeDb = -4f, PitchScale = 0.92f };
		AddChild(voice);
		voice.Finished += voice.QueueFree;
		voice.Play();
	}

	/// <summary>A giant spotlight snaps on directly overhead the instant the voice speaks, catching
	/// the player in a harsh red glare — the clearing's payoff beat. It rides with them while they
	/// remain in the clearing.</summary>
	private void BuildPlayerSpotlight()
	{
		if (_playerSpot != null || _player == null) return;
		_playerSpot = new SpotLight3D
		{
			Name = "Act6PlayerSpot",
			LightColor = new Color(1f, 0.05f, 0.02f),
			LightEnergy = 12f,
			SpotRange = 30f,
			SpotAngle = 12f,
			SpotAngleAttenuation = 2f,
			ShadowEnabled = true,
		};
		Cutscene.SceneRoot(this).AddChild(_playerSpot);
		FollowPlayer();
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		if (_playerSpot == null || _player == null || !IsInstanceValid(_player)) { SetProcess(false); return; }
		FollowPlayer();
		float d = new Vector2(_player.GlobalPosition.X - GlobalPosition.X, _player.GlobalPosition.Z - GlobalPosition.Z).Length();
		if (d > SpotlightReleaseRadius) ReleaseSpotlight(2.5f);
	}

	private void FollowPlayer()
	{
		// A giant spotlight rides directly overhead, always aimed straight down at the player.
		_playerSpot.GlobalPosition = _player.GlobalPosition + Vector3.Up * 22f;
		_playerSpot.LookAt(_player.GlobalPosition, Vector3.Forward);
	}

	/// <summary>The voice beat is over: fade the spotlight out and free it.</summary>
	private void ReleaseSpotlight(float fadeSeconds)
	{
		if (_playerSpot == null) return;
		var spot = _playerSpot;
		_playerSpot = null;
		SetProcess(false);
		if (!IsInstanceValid(spot)) return;
		var t = spot.CreateTween();
		t.TweenProperty(spot, "light_energy", 0f, fadeSeconds);
		t.TweenCallback(Callable.From(spot.QueueFree));
	}

	/// <summary>A wooden creak and a burst of purple light where the post has fused on.</summary>
	private void FlashFusion(Node3D at)
	{
		var light = new OmniLight3D { LightColor = new Color(0.7f, 0.25f, 0.95f), LightEnergy = 6f, OmniRange = 6f, Position = Vector3.Up * 0.4f };
		at.AddChild(light);
		var tween = light.CreateTween();
		tween.TweenProperty(light, "light_energy", 0f, 2.2f).SetDelay(0.15f);
		tween.TweenCallback(Callable.From(light.QueueFree));

		string path = $"res://assets/audio/sfx/trunk_creak_{new RandomNumberGenerator().RandiRange(1, 3):00}.wav";
		StoryBeat.PlayAt(at, path, "Unnatural", Vector3.Zero, volumeDb: 2f, unitSize: 3f, maxDistance: 60f);
	}

	/// <summary>If the player never takes the optional stairs, night still falls — just gradually, on the walk back.</summary>
	private void StartNightFallback()
	{
		if (_fallbackRunning) return;
		_fallbackRunning = true;
		Cutscene.Run(this, async ct =>
		{
			float delay = GameSettings.Instance.AutoTest ? SkipNightDelayAutoTest : SkipNightDelaySeconds;
			await Cutscene.Wait(this, delay, ct);
			if (_climbFired) return;
			StoryBeat.SetMood(this, ForestAtmosphere.Mood.Night, 20f);
			StoryManager.Instance?.SetFlag(StoryManager.Flag.Act6NightFell);
		});
	}

	private async Task ExtendedClimb(PlayerController player, CancellationToken ct)
	{
		player.Velocity = Vector3.Zero;
		ReleaseSpotlight(1.2f);

		foreach (var m in _minis) m.QueueFree();
		_minis.Clear();

		var fader = StoryBeat.Fader(this);
		if (fader != null) await fader.Fade(1f, 1.6f);
		await Cutscene.Wait(this, 5.0, ct);

		SpotLight3D spot = null;
		if (_original != null)
		{
			_original.Steps += Mathf.Max(20, _original.Steps);
			_original.Build();
			var top = _original.GetNodeOrNull<Node3D>("TopTrigger");
			Vector3 dest = top?.GlobalPosition ?? _original.GlobalPosition;
			player.GlobalPosition = dest + new Vector3(0, 0.1f, 1.0f);
			spot = new SpotLight3D { LightColor = Colors.White, LightEnergy = 0f, SpotRange = 14f, SpotAngle = 45f };
			Cutscene.SceneRoot(this).AddChild(spot);
			spot.GlobalPosition = dest + Vector3.Up * 8f;
			spot.LookAt(dest, Vector3.Forward);
		}

		if (fader != null) await fader.Fade(0f, 1.8f);
		if (spot != null) spot.CreateTween().TweenProperty(spot, "light_energy", 3.5f, 1.2f);

		StoryBeat.SetMood(this, ForestAtmosphere.Mood.Night, 3f);
		StoryManager.Instance?.SetFlag(StoryManager.Flag.Act6NightFell);

		// Control comes back here (the rest plays while the player can move).
		Cutscene.Run(this, async ct2 =>
		{
			await StoryBeat.Caption(this, "Hours must have passed. It's night now.", 1.2f, 3.0f, 1.2f);
			if (spot != null && IsInstanceValid(spot))
			{
				var t2 = spot.CreateTween();
				t2.TweenProperty(spot, "light_energy", 0f, 2.5f).SetDelay(2.5f);
				t2.TweenCallback(Callable.From(spot.QueueFree));
			}
		});
		GD.Print("[story] Act 6: the extended climb");
	}
}
