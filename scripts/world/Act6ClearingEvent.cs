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
/// plays the clearing's voice line, repoints the compass onward, and the post
/// (the stone cap missing from the first staircase's newel post) flies from
/// the player's view to this clearing's own copy of that staircase and grinds
/// down onto its broken newel stump, stone on stone, with a purple flash at
/// the joint: the staircase is whole again (<see cref="StaircaseBuilder.NewelCapped"/>). Stepping onto any of the fifteen (entirely
/// optional, and only while Act 6 lasts: after the voice, before the cabin is
/// seen burning) triggers a longer, stranger climb on the original staircase
/// and a jump straight to night; skipping them lets night fall gradually
/// instead while the player heads back on their own.
///
/// Restore: the reveal is derived from the checkpoint + newel post flag, the
/// voice and the cap back on the newel post from
/// <see cref="StoryManager.Flag.ClearingVoiceHeard"/> (set as the cap leaves the
/// player's hands, before it lands), the optional
/// climb from <see cref="StoryManager.Flag.Act6ExtendedClimb"/> (no mini stairs,
/// the original taller) and nightfall from <see cref="StoryManager.Flag.Act6NightFell"/>
/// (a still-pending fallback restarts its timer). The overhead red spotlight
/// belongs to the voice beat only: it follows the player through the clearing
/// and is freed once they leave it (or the climb starts, or Act 7 begins).
///
/// The original flight's length is never accumulated: it is always
/// <see cref="StairsState.ClearingStepsFor"/> of the saved story, applied here before the
/// staircase's own first build (this node is its child, so it readies first),
/// so a Continue builds the flight exactly once.
///
/// The clearing's dressing (the fifteen stairs, the giant firs, the veiny
/// ground) is deterministic, so it is built at load and kept out of the tree
/// until the bridge checkpoint, when it is simply attached.
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
	private Node3D _stand;    // the fifteen mini stairs, pre-built, attached on reveal
	private Node3D _lights;   // the clearing's fill lights, pre-built, attached on reveal
	private bool _revealed;
	private bool _voiceFired;
	private bool _climbFired;
	private bool _fallbackRunning;
	/// <summary>For the autotest: true once the optional extended climb has fired (a scripted
	/// teleport that a still-running approach/return walk needs to know to stop steering through).</summary>
	public bool ExtendedClimbFired => _climbFired;
	/// <summary>For tests: how many mini stairs currently stand in the world.</summary>
	public int MiniStairCount => _stand != null && _stand.IsInsideTree() ? _minis.Count : 0;
	/// <summary>For tests: whether the voice beat's spotlight is still following the player.</summary>
	public bool SpotlightActive => _playerSpot != null;
	/// <summary>For tests: the original flight this clearing surrounds.</summary>
	public StaircaseBuilder OriginalStairs => _original;
	/// <summary>For tests: whether the pre-built dressing is standing in the world.</summary>
	public bool Revealed => _revealed;

	/// <summary>Dev previews only (debug builds): shows the clearing now, without the story reaching it.</summary>
	public void DebugReveal()
	{
		if (!OS.IsDebugBuild() || _revealed) return;
		Reveal(restoring: false);
	}

	private PlayerController _player;
	private SpotLight3D _playerSpot;
	private Area3D _voiceZone;

	public override void _Ready()
	{
		_original = GetNodeOrNull<StaircaseBuilder>(OriginalStairsPath);
		_dressing = GetNodeOrNull<DeepZoneDressing>(DressingPath);
		SetProcess(false);   // only while the spotlight rides
		// Before the parent flight builds itself (children ready first): its first build is already the right
		// length, and its newel post already capped or broken.
		ApplyStairsLength();
		if (_original != null) _original.NewelCapped = StoryManager.Instance?.ClearingVoiceHeard == true;
		Callable.From(() => { PrepareDressing(); Restore(); }).CallDeferred();
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
		// Dressing that was never attached would otherwise outlive the level.
		FreeIfDetached(ref _stand);
		FreeIfDetached(ref _lights);
	}

	private static void FreeIfDetached(ref Node3D n)
	{
		if (n != null && IsInstanceValid(n) && n.GetParent() == null) n.Free();
		n = null;
	}

	/// <summary>Sets the original flight's length to what the saved story says (StairsState), rebuilding
	/// only if it already stands at another length.</summary>
	private void ApplyStairsLength()
	{
		if (_original == null) return;
		int want = StairsState.ClearingStepsFor(StoryManager.Instance, _original.BaseSteps);
		if (_original.Steps == want) return;
		_original.Steps = want;
		if (_original.IsNodeReady()) _original.Build();
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

	/// <summary>Continue: put the clearing back the way the story left it.</summary>
	private void Restore()
	{
		var s = StoryManager.Instance;
		if (!StoryBeat.Act6Revealed(s)) return;
		_voiceFired = s.ClearingVoiceHeard;
		_climbFired = s.HasFlag(StoryManager.Flag.Act6ExtendedClimb);
		Reveal(restoring: true);
		ApplyStairsLength();   // normally a no-op: the flight was built at the right length
		if (_voiceFired && !_climbFired && !s.HasFlag(StoryManager.Flag.Act6NightFell)) StartNightFallback();
	}

	/// <summary>Builds everything the reveal will need, then keeps it out of the tree (no rendering,
	/// no physics) until the story gets there. Deterministic seeds, so nothing depends on when it runs.</summary>
	private void PrepareDressing()
	{
		if (_stand != null) return;
		_stand = new Node3D { Name = "MiniStairs" };
		AddChild(_stand);
		BuildMiniStairs(_stand);
		RemoveChild(_stand);
		_lights = new Node3D { Name = "ClearingLights" };
		AddChild(_lights);
		BuildClearingLights(_lights);
		RemoveChild(_lights);
		// Giants stay well outside the mini-stairs band (StairsMaxReach + one stair's own footprint)
		// so their thick trunks never clip through a staircase.
		_dressing?.Prepare(GlobalPosition, 55f);
	}

	private void Reveal(bool restoring)
	{
		_revealed = true;
		_player ??= StoryBeat.Player(this);
		PrepareDressing();
		// Continue applies the saved mood itself (GameFlow); only a live reveal fades to it.
		if (!restoring) StoryBeat.SetMood(this, ForestAtmosphere.Mood.Menacing, 14f);
		_dressing?.Reveal(GlobalPosition, 55f);
		if (!_climbFired && _stand.GetParent() == null) AddChild(_stand);
		if (_lights.GetParent() == null) AddChild(_lights);
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
		_ = Cutscene.Run(this, VoiceAndFuse);
	}

	/// <summary>A ring of cold, even fill light so the whole stand of fifteen stairs actually reads
	/// clearly, rather than being lost in the Menacing mood's low ambient.</summary>
	private static void BuildClearingLights(Node3D parent)
	{
		const int count = 6;
		for (int i = 0; i < count; i++)
		{
			float ang = Mathf.Tau / count * i;
			parent.AddChild(new OmniLight3D
			{
				Name = $"ClearingFill{i}",
				LightColor = new Color(0.75f, 0.78f, 0.85f),
				LightEnergy = 3.2f,
				OmniRange = StairsMaxReach + 8f,
				Position = new Vector3(Mathf.Cos(ang) * StairsMaxReach * 0.5f, 11f, Mathf.Sin(ang) * StairsMaxReach * 0.5f),
			});
		}
	}

	private void BuildMiniStairs(Node3D parent)
	{
		var rng = new RandomNumberGenerator { Seed = 9001 };
		var terrain = GroundSnap.FindTerrain(this);
		const int count = 15;
		var placed = new List<(Vector2 xz, float clearance)>();
		// The path runs through the clearing: no staircase may stand on it (or so close that its step
		// trigger would catch someone just walking by).
		bool OnPath(Vector2 localXz, float reach)
		{
			if (terrain == null) return false;
			Vector3 w = GlobalPosition + new Vector3(localXz.X, 0, localXz.Y);
			return terrain.TrailDistance(w.X, w.Z, out _) < reach + 2.2f;
		}
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
				if (clear && OnPath(tryXz, tryFootprint)) clear = false;
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
			// Set on the ground at its foot; on a slope the walls and plinth reach down to the lowest ground
			// under the whole flight, so no edge of it stands in the air.
			stair.FoundationDepth = FootingFor(stair, new Vector3(world.X, groundY, world.Z), terrain);
			parent.AddChild(stair);
			stair.AddToGroup("act6_mini_stairs");
			_minis.Add(stair);

			float footLen = stair.Steps * stair.Run * stair.Scale.Z + 1f;
			float footH = stair.TotalHeight * stair.Scale.Y + 1.5f;
			StoryBeat.MakeTrigger(stair,
				new BoxShape3D { Size = new Vector3(stair.Width * stair.Scale.X + 1f, footH, footLen) },
				new Vector3(0, footH * 0.5f, -footLen * 0.5f + 0.4f), OnMiniStepped, "StepTrigger");
		}
	}

	/// <summary>Foundation depth (in the flight's own units) that reaches the lowest ground under its footprint.</summary>
	private static float FootingFor(StaircaseBuilder s, Vector3 origin, ForestTerrain terrain)
	{
		if (terrain == null) return s.FoundationDepth;
		float half = s.Width * 0.5f + s.WallThickness + s.PlinthFlare + 0.15f;
		float z0 = 0.3f, z1 = s.BackZ - 0.5f;
		var basis = Basis.FromEuler(s.RotationDegrees * (Mathf.Pi / 180f)).Scaled(s.Scale);
		float lo = origin.Y;
		for (int j = 0; j <= 12; j++)
			for (int i = 0; i <= 6; i++)
			{
				Vector3 w = origin + basis * new Vector3(Mathf.Lerp(-half, half, i / 6f), 0, Mathf.Lerp(z0, z1, j / 12f));
				lo = Mathf.Min(lo, terrain.HeightAt(w.X, w.Z));
			}
		return Mathf.Max(s.FoundationDepth, (origin.Y - lo) / s.Scale.Y + 0.25f);
	}

	private void OnMiniStepped(PlayerController player)
	{
		// The optional climb belongs to Act 6: after the clearing's voice, before the cabin is seen
		// burning. Never after (Act 11 wakes the player among these very stairs).
		var s = StoryManager.Instance;
		if (_climbFired || !_voiceFired || s == null) return;
		if (!s.ClearingVoiceHeard || s.Current >= Checkpoint.Act7CabinBurning) return;
		_climbFired = true;
		s.SetFlag(StoryManager.Flag.Act6ExtendedClimb);
		_ = Cutscene.Run(this, ct => ExtendedClimb(player, ct), lockInput: true, freezeBody: true);
	}

	private async Task VoiceAndFuse(CancellationToken ct)
	{
		BuildPlayerSpotlight();
		PlayVoice();
		await StoryBeat.Caption(this, "\"Come up and see.\"", 1.6f, 3.2f, 1.6f, ct);
		ct.ThrowIfCancellationRequested();

		var inv = _player.Inventory;
		bool fly = inv is { HasNewelPost: true } && _original is { HasNewel: true };
		if (fly) inv.ConsumeNewelPost();
		StoryManager.Instance.MarkClearingVoiceHeard();
		StartNightFallback();
		if (fly) await FlyCapHome(ct);
		if (_original != null) _original.NewelCapped = true;
	}

	/// <summary>
	/// The post leaves the player's hands: the stone cap flies from in front of their view in an arc to
	/// the clearing staircase's broken newel post, arriving turned a little, then grinds round and down
	/// onto the stump (stone on stone), where the pier's own cap takes its place in a purple flash. The
	/// view drifts after it (a gentle scripted look the player can overrule).
	/// </summary>
	private async Task FlyCapHome(CancellationToken ct)
	{
		var stairs = _original;
		var cam = GetViewport().GetCamera3D();
		Transform3D seat = stairs.NewelSeatGlobal;
		Vector3 scale = seat.Basis.Scale;
		Quaternion qSeat = seat.Basis.Orthonormalized().GetRotationQuaternion();
		Quaternion qArrive = qSeat * new Quaternion(Vector3.Up, -0.6f);
		Vector3 start = cam != null ? cam.GlobalTransform * new Vector3(0.1f, -0.22f, -0.6f) : _player.GlobalPosition + Vector3.Up * 1.3f;
		Quaternion qStart = new Quaternion(Vector3.Up, cam?.GlobalRotation.Y ?? 0f);
		Vector3 hover = seat.Origin + seat.Basis.Y.Normalized() * 0.14f * scale.Y;
		float dist = start.DistanceTo(hover);
		Vector3 ctrl = (start + hover) * 0.5f + Vector3.Up * (1.2f + dist * 0.12f);
		float flight = Mathf.Clamp(dist / 9f, 1.4f, 2.8f);

		var cap = new MeshInstance3D { Name = "FlyingNewelCap", Mesh = StaircaseBuilder.NewelCapMesh };
		Cutscene.SceneRoot(this).AddChild(cap);
		void Place(Vector3 p, Quaternion q) => cap.GlobalTransform = new Transform3D(new Basis(q).Scaled(scale), p);
		Place(start, qStart);
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
				Place(p, qStart.Slerp(qArrive, e));
				FollowWithView(p);
			}
			// grinding round and down onto the stump: the sound's grind runs the length of the drop and its seat lands on contact
			StoryBeat.PlayAt(stairs, "res://assets/audio/sfx/newel_seat.wav", "Unnatural", stairs.NewelSeatLocal, volumeDb: 4f, unitSize: 5f, maxDistance: 60f);
			const float drop = 0.44f;
			t = 0;
			while (t < drop)
			{
				await Cutscene.Frame(this, ct);
				t += GetProcessDeltaTime();
				float u = Mathf.Clamp((float)(t / drop), 0f, 1f);
				Place(hover.Lerp(seat.Origin, u * u), qArrive.Slerp(qSeat, u));
			}
			stairs.NewelCapped = true;
			FlashFusion(seat.Origin);
		}
		finally
		{
			if (IsInstanceValid(cap)) cap.QueueFree();
		}
	}

	/// <summary>Turns the player's view a little each frame toward the flying cap.</summary>
	private void FollowWithView(Vector3 target)
	{
		if (_player == null || !IsInstanceValid(_player)) return;
		var rig = _player.CameraRig;
		Vector3 eye = rig.Camera?.GlobalPosition ?? _player.GlobalPosition + Vector3.Up * 1.6f;
		Vector3 d = target - eye;
		float yaw = Mathf.Atan2(-d.X, -d.Z);
		float pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
		float dy = Mathf.Clamp(Mathf.AngleDifference(rig.Yaw, yaw), -0.03f, 0.03f);
		float dp = Mathf.Clamp(pitch - rig.Pitch, -0.02f, 0.02f);
		_player.PlayerInput.AddCutsceneLook(new Vector2(dy, dp));
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

	/// <summary>If the player never takes the optional stairs, night still falls — just gradually, on the walk back.</summary>
	private void StartNightFallback()
	{
		if (_fallbackRunning) return;
		_fallbackRunning = true;
		_ = Cutscene.Run(this, async ct =>
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
		if (fader != null) await fader.Fade(1f, 1.6f, ct);
		await Cutscene.Wait(this, 5.0, ct);

		SpotLight3D spot = null;
		if (_original != null)
		{
			ApplyStairsLength();   // Act6ExtendedClimb is set: twice the scene's flight
			var top = _original.GetNodeOrNull<Node3D>("TopTrigger");
			Vector3 dest = top?.GlobalPosition ?? _original.GlobalPosition;
			player.GlobalPosition = dest + new Vector3(0, 0.1f, 1.0f);
			spot = new SpotLight3D { LightColor = Colors.White, LightEnergy = 0f, SpotRange = 14f, SpotAngle = 45f };
			Cutscene.SceneRoot(this).AddChild(spot);
			spot.GlobalPosition = dest + Vector3.Up * 8f;
			spot.LookAt(dest, Vector3.Forward);
		}

		if (fader != null) await fader.Fade(0f, 1.8f, ct);
		if (spot != null) spot.CreateTween().TweenProperty(spot, "light_energy", 3.5f, 1.2f);

		StoryBeat.SetMood(this, ForestAtmosphere.Mood.Night, 3f);
		StoryManager.Instance?.SetFlag(StoryManager.Flag.Act6NightFell);

		// Control comes back here (the rest plays while the player can move).
		_ = Cutscene.Run(this, async ct2 =>
		{
			await StoryBeat.Caption(this, "Hours must have passed. It's night now.", 1.2f, 3.0f, 1.2f, ct2);
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
