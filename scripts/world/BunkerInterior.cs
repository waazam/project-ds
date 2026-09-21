using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// The bunker's inside: Act 8's long, uncannily long hallway (its overhead
/// lights slowly failing to red the deeper you go), Act 9's room of stacked
/// CRTs (one marked screen shows the burning cabin; shutting it off and
/// waiting turns every screen back on, clearing to the stairs), and Act 10's
/// escape, where the same hallway you arrived through has become a maze with
/// a chanting, half-glimpsed thing in it, ending at a dropped, static-hissing
/// walkie-talkie. Lives at a fixed offset far from the outdoor terrain — the
/// player is only ever teleported here, never walks in from outside — so
/// every position in this file is local to this node.
/// </summary>
[GlobalClass]
public partial class BunkerInterior : Node3D
{
	public static BunkerInterior Instance { get; private set; }

	// ---------------------------------------------------------------- hallway layout
	private const float HallHalfWidth = 2.2f;
	private const float HallKickHeight = 1.2f;     // vertical wall before the vault starts curving
	private const float HallHeight = HallKickHeight + HallHalfWidth;   // visual peak of the vault
	private const float HallCollisionRoof = 2.3f;  // simplified flat collision ceiling, well under the visual peak
	private const int HallArchSegs = 6;
	private const float HallLength = 90f;          // Z: 0 (entrance) .. -90 (vine door)
	private const float LightSpacing = 5f;
	private const float RedFlickerFrac = 0.4f;     // fraction of HallLength where flicker warning starts
	private const float RedTriggerFrac = 0.75f;    // fraction where the lights commit to red for good

	// ---------------------------------------------------------------- CRT room layout
	private const float CrtRoomFrontZ = -90f;      // flush with the hallway's own end (HallLength) — no floor gap between them
	private const float CrtRoomBackZ = -122f;
	private const float CrtRoomHalfWidth = 6f;
	private const float CrtRoomHeight = 4.0f;

	// ---------------------------------------------------------------- maze layout
	private static readonly Vector3 MazeOffset = new(300f, 0f, 0f);
	private const int MazeCols = 6, MazeRows = 10;
	private const float MazeCell = 4f;
	private const float MazeWallHeight = 2.7f;
	private const float MazeWallThickness = 0.2f;

	private class HallwayLight
	{
		public MeshInstance3D Fixture;
		public StandardMaterial3D Mat;
		public OmniLight3D Light;
		public float Z;
		public bool Red;
	}

	private readonly List<HallwayLight> _lights = new();
	private ShaderMaterial _crtNormalMat;
	private ShaderMaterial _crtTargetMat;
	private Node3D _crtTargetNode;
	private Area3D _vineDoorArea;
	private MeshInstance3D _vineDoorMesh;
	private CollisionShape3D _vineDoorCollision;
	private bool _vineDoorOpen;
	private bool _mazeTriggered;
	private PlayerController _player;
	private float _maxDepth;
	private double _clock;

	// maze data, generated up front so "bunker_entrance_marker" is meaningful as soon as
	// the CRT puzzle resolves, even before the maze geometry itself is built.
	private bool[,] _passRight, _passDown;
	private Node3D _mazeRoot;
	private Node3D _bunkerEntranceMarker;
	private Vector3 _mazeExitLocal;
	private readonly List<OmniLight3D> _mazeLights = new();
	private MeshInstance3D _creature;
	private double _nextGlimpse;
	private bool _mazeActive;
	private Area3D _mazeExitTrigger;
	private bool _mazeExited;

	/// <summary>For the autotest: hallway lights have committed to red for good.</summary>
	public bool RedTriggered { get; private set; }
	/// <summary>For the autotest: the vine door at the hallway's end has been pushed open.</summary>
	public bool VineDoorOpenState => _vineDoorOpen;
	/// <summary>For the autotest: the player is currently standing in the vine door's trigger zone.</summary>
	public bool PlayerAtVineDoor => _playerAtVineDoor;
	/// <summary>For the autotest: the player is currently in range to interact with the target CRT.</summary>
	public bool PlayerAtCrtTarget => _playerAtCrtTarget;
	/// <summary>For the autotest: the CRT room's screens are currently off (mid-puzzle).</summary>
	public bool ScreensOff { get; private set; }
	/// <summary>For the autotest: populated once the maze exists, start to exit.</summary>
	public List<Vector3> MazeSolutionWaypointsWorld { get; private set; }
	/// <summary>For the autotest: true once the hallway has turned into the maze.</summary>
	public bool MazeActive => _mazeActive;

	public Vector3 VineDoorApproachWorld => ToGlobal(new Vector3(0, 0, -86f));
	public Vector3 CrtRoomInteriorWorld => ToGlobal(new Vector3(0, 0, -95f));
	public Vector3 CrtTargetApproachWorld => _crtTargetNode?.GlobalPosition ?? ToGlobal(new Vector3(0, 1.5f, CrtRoomBackZ + 2f));

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		BuildHallway();
		BuildVineDoor();
		BuildCrtRoom();
		GenerateMazeData();
		// Built now, at level load, and simply left sitting (unnoticed) at its own far-off offset
		// until TransformToMaze() teleports the player there — building ~120 collision shapes and
		// a wall mesh live, mid-transition, was a visible stutter right when the scene should read
		// as a clean cut.
		BuildMazeGeometry();
	}

	public override void _Process(double delta)
	{
		_clock += delta;
		AnimateHallwayLights();
		if (_mazeActive) AnimateMaze(delta);
		if (StoryManager.Instance is { CrtPuzzleDone: true } && !_mazeTriggered)
			CheckVineDoorReturn();

		// Interact polling, edge-tracked by hand (not IsActionJustPressed) so a scripted
		// autotest tap behaves the same as a real one regardless of node processing order.
		bool pressed = Input.IsActionPressed("interact");
		bool justPressed = pressed && !_wasPressed;
		_wasPressed = pressed;
		if (_playerAtVineDoor && !_vineDoorOpen)
		{
			InteractPrompt.Instance?.ShowPrompt("[E] Push through the vines");
			if (justPressed) OpenVineDoor();
		}
		else if (_playerAtCrtTarget && !_crtSolved)
		{
			InteractPrompt.Instance?.ShowPrompt("[E] Turn off the screen");
			if (justPressed) { _crtSolved = true; _ = RunCrtPuzzle(); }
		}
	}

	// ================================================================== Act 8: hallway

	private void BuildHallway()
	{
		var gen = new Node3D { Name = "Hallway" };
		AddChild(gen);
		var k = new MeshKit();
		var concrete = ProcTextures.ConcreteMat;
		k.Mat(concrete);

		// Floor.
		k.Color = new Color(0.5f, 0.5f, 0.48f);
		k.Box(new Vector3(0, -0.05f, -HallLength * 0.5f), new Vector3(HallHalfWidth * 2f, 0.1f, HallLength), 0.6f);

		// A low-poly barrel vault: short vertical kick walls, then a faceted semicircular arch
		// over the top (rather than a flat ceiling), ribbed like a real bunker tunnel.
		k.Color = new Color(0.58f, 0.58f, 0.56f);
		foreach (float side in new[] { -1f, 1f })
			k.Box(new Vector3(side * (HallHalfWidth + 0.1f), HallKickHeight * 0.5f, -HallLength * 0.5f), new Vector3(0.2f, HallKickHeight, HallLength), 0.6f);

		for (int i = 0; i < HallArchSegs; i++)
		{
			float a0 = Mathf.Pi * (1f - (float)i / HallArchSegs);
			float a1 = Mathf.Pi * (1f - (float)(i + 1) / HallArchSegs);
			Vector3 p0 = new(Mathf.Cos(a0) * HallHalfWidth, HallKickHeight + Mathf.Sin(a0) * HallHalfWidth, 0);
			Vector3 p1 = new(Mathf.Cos(a1) * HallHalfWidth, HallKickHeight + Mathf.Sin(a1) * HallHalfWidth, 0);
			Vector3 mid = (p0 + p1) * 0.5f;
			float chord = p0.DistanceTo(p1);
			float angle = Mathf.Atan2(p1.Y - p0.Y, p1.X - p0.X);
			// Alternate shading per facet so the ribs read clearly, like the reference photo's segments.
			k.Color = i % 2 == 0 ? new Color(0.58f, 0.58f, 0.56f) : new Color(0.5f, 0.5f, 0.48f);
			k.Box(new Vector3(mid.X, mid.Y, -HallLength * 0.5f), new Vector3(chord, 0.18f, HallLength), 0.6f, Basis.FromEuler(new Vector3(0, 0, angle)));
		}

		// Entrance cap at Z=0: the vault is otherwise open at this end, and without a cap the
		// outside sky and terrain are visible the moment the player turns to look back.
		var profile = new List<Vector3> { new(-HallHalfWidth, 0, 0) };
		for (int i = 0; i <= HallArchSegs; i++)
		{
			float a = Mathf.Pi * (1f - (float)i / HallArchSegs);
			profile.Add(new Vector3(Mathf.Cos(a) * HallHalfWidth, HallKickHeight + Mathf.Sin(a) * HallHalfWidth, 0));
		}
		profile.Add(new Vector3(HallHalfWidth, 0, 0));
		Vector3 capCenter = new(0, HallKickHeight + HallHalfWidth * 0.4f, 0);
		k.Color = new Color(0.5f, 0.5f, 0.48f);
		for (int i = 0; i < profile.Count - 1; i++)
			k.Tri(capCenter, profile[i], profile[i + 1], Vector3.Forward, new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(1, 0));
		k.CommitTo(gen, "HallwayShell");

		var body = new StaticBody3D { Name = "HallwayBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		gen.AddChild(body);
		// Collision stays a simple rectangular tunnel comfortably inside the visual vault — the
		// curved upper facets are well above head height and never need their own collision.
		body.AddChild(new CollisionShape3D { Position = new Vector3(-HallHalfWidth - 0.1f, HallCollisionRoof * 0.5f, -HallLength * 0.5f), Shape = new BoxShape3D { Size = new Vector3(0.2f, HallCollisionRoof, HallLength) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(HallHalfWidth + 0.1f, HallCollisionRoof * 0.5f, -HallLength * 0.5f), Shape = new BoxShape3D { Size = new Vector3(0.2f, HallCollisionRoof, HallLength) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, -0.05f, -HallLength * 0.5f), Shape = new BoxShape3D { Size = new Vector3(HallHalfWidth * 2f, 0.1f, HallLength) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, HallCollisionRoof + 0.05f, -HallLength * 0.5f), Shape = new BoxShape3D { Size = new Vector3(HallHalfWidth * 2f, 0.1f, HallLength) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, HallCollisionRoof * 0.5f, 0.1f), Shape = new BoxShape3D { Size = new Vector3(HallHalfWidth * 2f, HallCollisionRoof, 0.2f) } });

		int count = Mathf.FloorToInt(HallLength / LightSpacing);
		for (int i = 0; i < count; i++)
		{
			float z = -LightSpacing * (i + 0.5f);
			var mat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.85f, 0.85f, 0.9f),
				EmissionEnabled = true,
				Emission = new Color(0.85f, 0.85f, 0.9f),
				EmissionEnergyMultiplier = 1.6f,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			};
			var fixture = new MeshInstance3D
			{
				Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.08f, 0.28f), Material = mat },
				Position = new Vector3(0, HallHeight - 0.06f, z),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			gen.AddChild(fixture);
			OmniLight3D light = null;
			if (i % 2 == 0)
			{
				light = new OmniLight3D { LightColor = new Color(0.85f, 0.85f, 0.9f), LightEnergy = 2.2f, OmniRange = 6.5f, Position = new Vector3(0, HallHeight - 0.3f, z) };
				gen.AddChild(light);
			}
			_lights.Add(new HallwayLight { Fixture = fixture, Mat = mat, Light = light, Z = z });
		}
	}

	private void AnimateHallwayLights()
	{
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player == null || _player.GlobalPosition.DistanceTo(GlobalPosition) > 500f) return;
		Vector3 local = ToLocal(_player.GlobalPosition);
		if (local.Z <= 0.01f && local.Z >= -HallLength - 1f) _maxDepth = Mathf.Max(_maxDepth, -local.Z);
		float frac = Mathf.Clamp(_maxDepth / HallLength, 0f, 1f);
		if (frac >= RedTriggerFrac) RedTriggered = true;

		foreach (var l in _lights)
		{
			float depthHere = -l.Z;
			bool aheadFlicker = !RedTriggered && frac >= RedFlickerFrac && depthHere <= _maxDepth + 15f && depthHere >= _maxDepth - 2f;
			bool permRed = l.Red || (RedTriggered && depthHere <= _maxDepth + 0.5f);
			if (permRed) l.Red = true;

			Color target = l.Red ? new Color(0.9f, 0.05f, 0.03f) : new Color(0.85f, 0.85f, 0.9f);
			float energy = 1.6f;
			if (aheadFlicker)
			{
				// A lazy, dying-fluorescent pulse — not a strobe. Rate barely varies per light so
				// they don't all flicker in dead unison, but stays slow throughout.
				float flickRate = 1.1f + (depthHere % 7f) * 0.08f;
				float flick = Mathf.Sin((float)_clock * flickRate) > (1f - (frac - RedFlickerFrac) / (RedTriggerFrac - RedFlickerFrac)) ? 0.1f : 1.6f;
				energy = flick;
				target = flick < 0.5f ? new Color(0.7f, 0.04f, 0.02f) : target;
			}
			l.Mat.Emission = target;
			l.Mat.AlbedoColor = target;
			l.Mat.EmissionEnergyMultiplier = energy;
			if (l.Light != null)
			{
				l.Light.LightColor = target;
				l.Light.LightEnergy = energy * 1.3f;
			}
		}
	}

	// ================================================================== the vine door

	private void BuildVineDoor()
	{
		var gen = new Node3D { Name = "VineDoor" };
		AddChild(gen);
		var wood = ProcTextures.WoodMat;
		var k = new MeshKit();
		k.Color = new Color(0.28f, 0.2f, 0.13f);
		k.Mat(wood);
		// Fan-triangulated to match the vault's own arch profile exactly (a flat rectangle here
		// left gaps at the curved upper corners — the outside world showed straight through them).
		var doorProfile = new List<Vector3> { new(-HallHalfWidth, 0, -HallLength) };
		for (int i = 0; i <= HallArchSegs; i++)
		{
			float a = Mathf.Pi * (1f - (float)i / HallArchSegs);
			doorProfile.Add(new Vector3(Mathf.Cos(a) * HallHalfWidth, HallKickHeight + Mathf.Sin(a) * HallHalfWidth, -HallLength));
		}
		doorProfile.Add(new Vector3(HallHalfWidth, 0, -HallLength));
		Vector3 doorCenter = new(0, HallKickHeight + HallHalfWidth * 0.4f, -HallLength);
		for (int i = 0; i < doorProfile.Count - 1; i++)
			k.Tri(doorCenter, doorProfile[i], doorProfile[i + 1], Vector3.Back, new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(1, 0));
		// A thin back panel a hair behind the fan, so the door reads with real thickness front-on.
		k.Box(new Vector3(0, HallKickHeight + HallHalfWidth * 0.4f, -HallLength - 0.06f), new Vector3(HallHalfWidth * 1.9f, HallHeight - 0.15f, 0.1f), 1.1f);
		var vine = ProcTextures.Flat("bunker_vine", new Color(0.1f, 0.2f, 0.08f), 1f);
		var rng = new RandomNumberGenerator { Seed = 88 };
		k.Mat(vine);
		for (int i = 0; i < 12; i++)
		{
			k.Color = new Color(rng.RandfRange(0.08f, 0.16f), rng.RandfRange(0.18f, 0.3f), rng.RandfRange(0.06f, 0.13f));
			Vector3 c = new(rng.RandfRange(-1.3f, 1.3f), rng.RandfRange(0.3f, HallHeight - 0.3f), -HallLength + 0.12f);
			k.Blob(c, new Vector3(rng.RandfRange(0.2f, 0.4f), rng.RandfRange(0.35f, 0.7f), 0.12f), 200 + i, 0.3f);
		}
		k.Color = Colors.White;
		_vineDoorMesh = k.CommitTo(gen, "VineDoorMesh");

		var body = new StaticBody3D { Name = "VineDoorBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		gen.AddChild(body);
		_vineDoorCollision = new CollisionShape3D { Position = new Vector3(0, HallCollisionRoof * 0.5f, -HallLength), Shape = new BoxShape3D { Size = new Vector3(HallHalfWidth * 2f + 0.2f, HallCollisionRoof, 0.2f) } };
		body.AddChild(_vineDoorCollision);

		_vineDoorArea = new Area3D { Name = "VineDoorArea", CollisionLayer = 0, CollisionMask = 2, Monitorable = false, Monitoring = true };
		_vineDoorArea.Position = new Vector3(0, HallHeight * 0.5f, -HallLength);
		_vineDoorArea.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(HallHalfWidth * 1.8f, HallHeight, 1.4f) } });
		gen.AddChild(_vineDoorArea);
		_vineDoorArea.BodyEntered += b => { if (b is PlayerController) _playerAtVineDoor = true; };
		_vineDoorArea.BodyExited += b => { if (b is PlayerController) { _playerAtVineDoor = false; if (!_vineDoorOpen) InteractPrompt.Instance?.HidePrompt(); } };
	}

	private bool _playerAtVineDoor;
	private bool _wasPressed;

	private void OpenVineDoor()
	{
		_vineDoorOpen = true;
		InteractPrompt.Instance?.HidePrompt();
		if (_vineDoorMesh != null) _vineDoorMesh.Visible = false;
		if (_vineDoorCollision != null) _vineDoorCollision.Disabled = true;
	}

	/// <summary>Once the CRT puzzle is solved, walking back over the vine door's threshold toward the
	/// hallway (having come from the CRT room) turns the hallway you're about to re-enter into the maze.</summary>
	private void CheckVineDoorReturn()
	{
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player == null) return;
		Vector3 local = ToLocal(_player.GlobalPosition);
		if (local.Z < -HallLength - 0.5f && local.Z > -HallLength - 6f)
		{
			_mazeTriggered = true;
			_ = TransformToMaze();
		}
	}

	// ================================================================== Act 9: the CRT room

	private void BuildCrtRoom()
	{
		var gen = new Node3D { Name = "CrtRoom" };
		AddChild(gen);
		var concrete = ProcTextures.ConcreteMat;
		var k = new MeshKit();
		float midZ = (CrtRoomFrontZ + CrtRoomBackZ) * 0.5f, depth = CrtRoomFrontZ - CrtRoomBackZ;
		// Dark, grimy industrial concrete — not the clean white surveillance room this riffs on.
		k.Color = new Color(0.22f, 0.22f, 0.23f);
		k.Mat(concrete);
		k.Box(new Vector3(-CrtRoomHalfWidth - 0.1f, CrtRoomHeight * 0.5f, midZ), new Vector3(0.2f, CrtRoomHeight, depth), 0.6f);
		k.Box(new Vector3(CrtRoomHalfWidth + 0.1f, CrtRoomHeight * 0.5f, midZ), new Vector3(0.2f, CrtRoomHeight, depth), 0.6f);
		k.Box(new Vector3(0, CrtRoomHeight * 0.5f, CrtRoomBackZ - 0.1f), new Vector3(CrtRoomHalfWidth * 2f, CrtRoomHeight, 0.2f), 0.6f);
		k.Color = new Color(0.16f, 0.16f, 0.17f);
		k.Box(new Vector3(0, -0.05f, midZ), new Vector3(CrtRoomHalfWidth * 2f, 0.1f, depth), 0.6f);
		k.Color = new Color(0.18f, 0.18f, 0.19f);
		k.Box(new Vector3(0, CrtRoomHeight + 0.05f, midZ), new Vector3(CrtRoomHalfWidth * 2f, 0.1f, depth), 0.6f);

		// Rust and damp streaks running down the walls, and a bare, dim bulb every so often.
		var rng = new RandomNumberGenerator { Seed = 909 };
		for (int i = 0; i < 16; i++)
		{
			bool rust = rng.Randf() < 0.6f;
			k.Color = rust ? new Color(0.28f, 0.16f, 0.08f) : new Color(0.1f, 0.1f, 0.11f);
			float wx = rng.Randf() < 0.5f ? -CrtRoomHalfWidth - 0.09f : CrtRoomHalfWidth + 0.09f;
			float wz = rng.RandfRange(CrtRoomBackZ + 1f, CrtRoomFrontZ - 1f);
			float len = rng.RandfRange(0.6f, 1.8f);
			k.Box(new Vector3(wx, CrtRoomHeight - len * 0.5f, wz), new Vector3(0.02f, len, rng.RandfRange(0.12f, 0.3f)), 1f);
		}
		k.CommitTo(gen, "CrtRoomShell");

		for (float z = CrtRoomFrontZ - 8f; z > CrtRoomBackZ + 4f; z -= 12f)
		{
			var bulb = new OmniLight3D { LightColor = new Color(0.85f, 0.78f, 0.6f), LightEnergy = 1.1f, OmniRange = 8f, Position = new Vector3(0, CrtRoomHeight - 0.3f, z) };
			gen.AddChild(bulb);
		}

		var body = new StaticBody3D { Name = "CrtRoomBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		gen.AddChild(body);
		body.AddChild(new CollisionShape3D { Position = new Vector3(-CrtRoomHalfWidth - 0.1f, CrtRoomHeight * 0.5f, midZ), Shape = new BoxShape3D { Size = new Vector3(0.2f, CrtRoomHeight, depth) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(CrtRoomHalfWidth + 0.1f, CrtRoomHeight * 0.5f, midZ), Shape = new BoxShape3D { Size = new Vector3(0.2f, CrtRoomHeight, depth) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, CrtRoomHeight * 0.5f, CrtRoomBackZ - 0.1f), Shape = new BoxShape3D { Size = new Vector3(CrtRoomHalfWidth * 2f, CrtRoomHeight, 0.2f) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, -0.05f, midZ), Shape = new BoxShape3D { Size = new Vector3(CrtRoomHalfWidth * 2f, 0.1f, depth) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, CrtRoomHeight + 0.05f, midZ), Shape = new BoxShape3D { Size = new Vector3(CrtRoomHalfWidth * 2f, 0.1f, depth) } });

		var shader = GD.Load<Shader>("res://assets/shaders/crt_static.gdshader");
		_crtNormalMat = new ShaderMaterial { Shader = shader };
		_crtNormalMat.SetShaderParameter("tint", new Color(0.72f, 0.76f, 0.8f));
		_crtTargetMat = new ShaderMaterial { Shader = shader };
		_crtTargetMat.SetShaderParameter("tint", new Color(1.0f, 0.45f, 0.1f));

		// Back wall and both side walls, stacked with CRTs, leaving the centre floor clear to walk.
		BuildCrtWall(gen, new Vector3(0, 0, CrtRoomBackZ + 0.25f), Vector3.Right, 5, 3, false);
		BuildCrtWall(gen, new Vector3(-CrtRoomHalfWidth + 0.25f, 0, midZ), Vector3.Forward, 4, 3, true);
		BuildCrtWall(gen, new Vector3(CrtRoomHalfWidth - 0.25f, 0, midZ), Vector3.Forward, 4, 3, true);
	}

	/// <summary>A grid of stacked CRT boxes against a wall, screens facing inward. One specific
	/// screen (the centre of the back wall's bottom row, within easy reach) is the target,
	/// tagged for the compass.</summary>
	private void BuildCrtWall(Node3D gen, Vector3 wallCenter, Vector3 along, int cols, int rows, bool sideWall)
	{
		float cellW = 1.15f, cellH = 1.05f;
		Vector3 outward = sideWall ? (wallCenter.X < 0 ? Vector3.Right : Vector3.Left) : Vector3.Back;
		for (int c = 0; c < cols; c++)
		{
			for (int r = 0; r < rows; r++)
			{
				Vector3 offset = along * (cellW * (c - (cols - 1) * 0.5f)) + Vector3.Up * (0.9f + r * cellH);
				Vector3 pos = wallCenter + offset;
				var k = new MeshKit();
				var body = ProcTextures.Flat("crt_body", new Color(0.1f, 0.1f, 0.11f), 0.7f);
				k.Color = Colors.White;
				k.Mat(body).Box(Vector3.Zero, new Vector3(1.0f, 0.9f, 0.9f));
				var screenMesh = new MeshInstance3D { Position = outward * 0.46f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
				bool isTarget = !sideWall && c == cols / 2 && r == 0;
				screenMesh.Mesh = new QuadMesh { Size = new Vector2(0.7f, 0.6f), Orientation = PlaneMesh.OrientationEnum.Z };
				screenMesh.RotationDegrees = sideWall ? new Vector3(0, wallCenter.X < 0 ? 90 : -90, 0) : Vector3.Zero;
				screenMesh.MaterialOverride = isTarget ? _crtTargetMat : _crtNormalMat;
				var box = new Node3D { Position = pos };
				k.CommitTo(box, "Body");
				box.AddChild(screenMesh);
				gen.AddChild(box);
				if (isTarget)
				{
					_crtTargetNode = box;
					box.AddToGroup("crt_target_marker");
					var interact = new Area3D { CollisionLayer = 0, CollisionMask = 2, Monitorable = false };
					interact.Position = outward * 1.6f;
					interact.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 2.2f } });
					box.AddChild(interact);
					interact.BodyEntered += b => { if (b is PlayerController) _playerAtCrtTarget = true; };
					interact.BodyExited += b => { if (b is PlayerController) { _playerAtCrtTarget = false; if (!_crtSolved) InteractPrompt.Instance?.HidePrompt(); } };
				}
			}
		}
	}

	private bool _playerAtCrtTarget;
	private bool _crtSolved;

	private async Task RunCrtPuzzle()
	{
		InteractPrompt.Instance?.HidePrompt();
		TurnOffAllScreens();
		string clickPath = "res://assets/audio/sfx/camera_shutter.wav";
		if (ResourceLoader.Exists(clickPath) && _crtTargetNode != null)
		{
			var click = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(clickPath), UnitSize = 2f, MaxDistance = 15f, PitchScale = 0.6f };
			_crtTargetNode.AddChild(click);
			click.Finished += click.QueueFree;
			click.Play();
		}
		await ToSignal(GetTree().CreateTimer(GameSettings.Instance.AutoTest ? 1.5 : 5.0), SceneTreeTimer.SignalName.Timeout);
		TurnOnShowingStairs();
		if (GetTree().Root.FindChild("ScreenFader", true, false) is ScreenFader fader)
			await fader.ShowCaption("", "Every screen shows the same thing: the stairs, in the woods.", 1.2f, 3.0f, 1.2f);
		StoryManager.Instance?.MarkCrtPuzzleDone();
		GD.Print("[story] Act 9: the screens show the stairs");
	}

	private void TurnOffAllScreens()
	{
		ScreensOff = true;
		_crtNormalMat.SetShaderParameter("power", 0f);
		_crtTargetMat.SetShaderParameter("power", 0f);
	}

	private void TurnOnShowingStairs()
	{
		ScreensOff = false;
		foreach (var mat in new[] { _crtNormalMat, _crtTargetMat })
		{
			mat.SetShaderParameter("power", 1f);
			mat.SetShaderParameter("clarity", 1f);
			mat.SetShaderParameter("tint", new Color(0.6f, 0.66f, 0.72f));
		}
	}

	// ================================================================== teleport in

	public void AdmitPlayer(PlayerController player) => _ = AdmitPlayerAsync(player);

	private async Task AdmitPlayerAsync(PlayerController player)
	{
		player.PlayerInput.SetEnabled(false);
		player.SetPhysicsProcess(false);
		var fader = GetTree().Root.FindChild("ScreenFader", true, false) as ScreenFader;
		if (fader != null) await fader.Fade(1f, 0.8f);
		// The hallway floor only spans Z=0..-HallLength: land just inside it, not just outside at +Z (empty void there).
		player.Teleport(ToGlobal(new Vector3(0, 0, -0.6f)), 0f);
		if (GetTree().Root.FindChild("Atmosphere", true, false) is ForestAtmosphere atmo)
			atmo.SetMood(ForestAtmosphere.Mood.Night, 1f);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (fader != null) await fader.Fade(0f, 1.0f);
		player.SetPhysicsProcess(true);
		player.PlayerInput.SetEnabled(true);
	}

	// ================================================================== Act 10: the maze

	private void GenerateMazeData()
	{
		_passRight = new bool[MazeCols, MazeRows];
		_passDown = new bool[MazeCols, MazeRows];
		var visited = new bool[MazeCols, MazeRows];
		var parent = new Dictionary<Vector2I, Vector2I>();
		var rng = new RandomNumberGenerator { Seed = 4242 };
		var stack = new Stack<Vector2I>();
		var start = new Vector2I(0, 0);
		visited[0, 0] = true;
		stack.Push(start);
		while (stack.Count > 0)
		{
			var cur = stack.Peek();
			var options = new List<(Vector2I next, int dir)>();
			void Try(int dx, int dy, int dir) { var n = new Vector2I(cur.X + dx, cur.Y + dy); if (n.X >= 0 && n.X < MazeCols && n.Y >= 0 && n.Y < MazeRows && !visited[n.X, n.Y]) options.Add((n, dir)); }
			Try(1, 0, 0); Try(-1, 0, 1); Try(0, 1, 2); Try(0, -1, 3);
			if (options.Count == 0) { stack.Pop(); continue; }
			var (next, dir) = options[rng.RandiRange(0, options.Count - 1)];
			if (dir == 0) _passRight[cur.X, cur.Y] = true;
			else if (dir == 1) _passRight[next.X, next.Y] = true;
			else if (dir == 2) _passDown[cur.X, cur.Y] = true;
			else _passDown[next.X, next.Y] = true;
			visited[next.X, next.Y] = true;
			parent[next] = cur;
			stack.Push(next);
		}

		var exit = new Vector2I(MazeCols - 1, MazeRows - 1);
		var path = new List<Vector2I> { exit };
		var walk = exit;
		while (walk != start) { walk = parent[walk]; path.Add(walk); }
		path.Reverse();

		_mazeExitLocal = MazeOffset + new Vector3(exit.X * MazeCell, 0, -exit.Y * MazeCell);
		MazeSolutionWaypointsWorld = new List<Vector3>();
		foreach (var cell in path)
			MazeSolutionWaypointsWorld.Add(ToGlobal(MazeOffset + new Vector3(cell.X * MazeCell, 0.1f, -cell.Y * MazeCell)));

		_bunkerEntranceMarker = new Node3D { Name = "BunkerEntranceMarker" };
		_bunkerEntranceMarker.Position = _mazeExitLocal;
		AddChild(_bunkerEntranceMarker);
		_bunkerEntranceMarker.AddToGroup("bunker_entrance_marker");
	}

	private async Task TransformToMaze()
	{
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player == null) return;
		_player.PlayerInput.SetEnabled(false);
		_player.SetPhysicsProcess(false);
		var fader = GetTree().Root.FindChild("ScreenFader", true, false) as ScreenFader;
		if (fader != null) await fader.Fade(1f, 1.4f);

		GetNodeOrNull("Hallway")?.QueueFree();

		_player.Teleport(ToGlobal(new Vector3(MazeOffset.X, 0, MazeOffset.Z + 0.5f)), 0f);
		_mazeActive = true;
		if (GetTree().Root.FindChild("Atmosphere", true, false) is ForestAtmosphere atmo)
			atmo.SetMood(ForestAtmosphere.Mood.Menacing, 2f);

		string choirPath = "res://assets/audio/ambient/choir_chant_loop.wav";
		if (ResourceLoader.Exists(choirPath))
		{
			var choir = new AudioStreamPlayer { Bus = "Master" };
			_mazeRoot.AddChild(choir);
			choir.AddChild(new AmbienceLoop { StreamPath = choirPath, BaseVolumeDb = -6f });
		}

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (fader != null) await fader.Fade(0f, 1.4f);
		if (fader != null)
			await fader.ShowCaption("", "\"...come and see...\"", 1.0f, 2.4f, 1.0f);
		_player.SetPhysicsProcess(true);
		_player.PlayerInput.SetEnabled(true);
		GD.Print("[story] Act 10: the hallway has become a maze");
	}

	private void BuildMazeGeometry()
	{
		_mazeRoot = new Node3D { Name = "Maze", Position = MazeOffset };
		AddChild(_mazeRoot);
		var k = new MeshKit();
		var concrete = ProcTextures.ConcreteMat;
		k.Color = new Color(0.32f, 0.3f, 0.3f);
		k.Mat(concrete);

		float half = MazeCell * 0.5f;
		// Exact-length wall segments plus separate small corner posts, rather than the oversized,
		// deliberately-overlapping segments used before: two overlapping convex collision shapes
		// meeting at every corner made MoveAndSlide's resolution genuinely erratic against this
		// many walls (it read as the camera flying around on its own), and this also cuts the
		// shape count sharply.
		var wallMids = new List<(Vector3 pos, Vector3 faceOut, float len)>();
		void WallX(float x, float z0, float z1)
		{
			float zm = (z0 + z1) * 0.5f, len = Mathf.Abs(z1 - z0);
			k.Box(new Vector3(x, MazeWallHeight * 0.5f, zm), new Vector3(MazeWallThickness, MazeWallHeight, len), 0.6f);
			wallMids.Add((new Vector3(x, 0, zm), Vector3.Right, len));
		}
		void WallZ(float z, float x0, float x1)
		{
			float xm = (x0 + x1) * 0.5f, len = Mathf.Abs(x1 - x0);
			k.Box(new Vector3(xm, MazeWallHeight * 0.5f, z), new Vector3(len, MazeWallHeight, MazeWallThickness), 0.6f);
			wallMids.Add((new Vector3(xm, 0, z), Vector3.Forward, len));
		}
		void Post(float x, float z) => k.Box(new Vector3(x, MazeWallHeight * 0.5f, z), new Vector3(MazeWallThickness, MazeWallHeight, MazeWallThickness), 0.6f);

		// Floor and a solid ceiling under/over the whole footprint — closed overhead throughout,
		// so nothing of the outside world is ever visible from in here.
		k.Box(new Vector3((MazeCols - 1) * half, -0.05f, -(MazeRows - 1) * half), new Vector3(MazeCols * MazeCell, 0.1f, MazeRows * MazeCell), 0.5f);
		k.Color = new Color(0.26f, 0.25f, 0.25f);
		k.Box(new Vector3((MazeCols - 1) * half, MazeWallHeight + 0.05f, -(MazeRows - 1) * half), new Vector3(MazeCols * MazeCell, 0.1f, MazeRows * MazeCell), 0.5f);
		k.Color = new Color(0.32f, 0.3f, 0.3f);

		for (int c = 0; c < MazeCols; c++)
		{
			for (int r = 0; r < MazeRows; r++)
			{
				float cx = c * MazeCell, cz = -r * MazeCell;
				// right wall of this cell (also the left boundary at c == -1, handled by c == 0's own left edge below)
				if (c == MazeCols - 1 || !_passRight[c, r]) WallX(cx + half, cz - half, cz + half);
				if (r == MazeRows - 1 || !_passDown[c, r]) WallZ(cz - half, cx - half, cx + half);
				if (c == 0) WallX(cx - half, cz - half, cz + half);
				if (r == 0) WallZ(cz + half, cx - half, cx + half);
			}
		}
		for (int c = 0; c <= MazeCols; c++)
			for (int r = 0; r <= MazeRows; r++)
				Post(c * MazeCell - half, -r * MazeCell + half);

		// Cracks fanning across the floor.
		var decalRng = new RandomNumberGenerator { Seed = 5151 };
		k.Color = new Color(0.08f, 0.08f, 0.08f);
		for (int i = 0; i < 26; i++)
		{
			float cx2 = decalRng.RandfRange(-half, (MazeCols - 1) * MazeCell + half), cz2 = -decalRng.RandfRange(-half, (MazeRows - 1) * MazeCell + half);
			float ang = decalRng.RandfRange(0f, Mathf.Tau);
			int segs = decalRng.RandiRange(2, 4);
			Vector3 p = new(cx2, 0.002f, cz2);
			for (int s = 0; s < segs; s++)
			{
				float len = decalRng.RandfRange(0.4f, 1.0f);
				ang += decalRng.RandfRange(-0.6f, 0.6f);
				Vector3 d = new(Mathf.Cos(ang), 0, Mathf.Sin(ang));
				Vector3 mid = p + d * len * 0.5f;
				k.Box(mid, new Vector3(len, 0.008f, decalRng.RandfRange(0.03f, 0.07f)), 1f, Basis.FromEuler(new Vector3(0, -ang, 0)));
				p += d * len;
			}
		}

		// Ooze weeping down from the ceiling seams along a handful of wall faces.
		var oozeMat = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.09f, 0.04f), Roughness = 0.15f, Metallic = 0.1f };
		k.Mat(oozeMat);
		for (int i = 0; i < 18; i++)
		{
			var (pos, faceOut, len) = wallMids[decalRng.RandiRange(0, wallMids.Count - 1)];
			float along = decalRng.RandfRange(-len * 0.35f, len * 0.35f);
			Vector3 alongDir = new(faceOut.Z, 0, -faceOut.X);
			Vector3 basePos = pos + alongDir * along + faceOut * (MazeWallThickness * 0.5f + 0.005f);
			float drop = decalRng.RandfRange(0.6f, MazeWallHeight - 0.3f);
			k.Color = new Color(0.05f + decalRng.RandfRange(0f, 0.05f), 0.09f, 0.04f);
			k.Box(basePos + new Vector3(0, MazeWallHeight - drop * 0.5f, 0), new Vector3(decalRng.RandfRange(0.08f, 0.16f), drop, 0.015f), 1f);
			k.Blob(basePos + new Vector3(0, MazeWallHeight - drop - 0.05f, 0), new Vector3(0.06f, 0.05f, 0.03f), 300 + i, 0.25f);
		}
		k.Mat(concrete);
		k.Color = Colors.White;
		k.CommitTo(_mazeRoot, "MazeShell");

		var body = new StaticBody3D { Name = "MazeBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		_mazeRoot.AddChild(body);
		body.AddChild(new CollisionShape3D { Position = new Vector3((MazeCols - 1) * half, -0.05f, -(MazeRows - 1) * half), Shape = new BoxShape3D { Size = new Vector3(MazeCols * MazeCell, 0.1f, MazeRows * MazeCell) } });
		body.AddChild(new CollisionShape3D { Position = new Vector3((MazeCols - 1) * half, MazeWallHeight + 0.05f, -(MazeRows - 1) * half), Shape = new BoxShape3D { Size = new Vector3(MazeCols * MazeCell, 0.1f, MazeRows * MazeCell) } });
		foreach (var (pos, faceOut, len) in wallMids)
		{
			Vector3 size = faceOut == Vector3.Right ? new Vector3(MazeWallThickness, MazeWallHeight, len) : new Vector3(len, MazeWallHeight, MazeWallThickness);
			body.AddChild(new CollisionShape3D { Position = pos + Vector3.Up * MazeWallHeight * 0.5f, Shape = new BoxShape3D { Size = size } });
		}
		for (int c = 0; c <= MazeCols; c++)
			for (int r = 0; r <= MazeRows; r++)
				body.AddChild(new CollisionShape3D { Position = new Vector3(c * MazeCell - half, MazeWallHeight * 0.5f, -r * MazeCell + half), Shape = new BoxShape3D { Size = new Vector3(MazeWallThickness, MazeWallHeight, MazeWallThickness) } });

		for (int i = 0; i < 3; i++)
		{
			var light = new OmniLight3D { LightColor = new Color(0.45f, 0.5f, 0.42f), LightEnergy = 1.4f, OmniRange = 9f, Position = new Vector3((MazeCols - 1) * half, 1.8f, -(MazeRows - 1) * half * (i / 2f)) };
			_mazeRoot.AddChild(light);
			_mazeLights.Add(light);
		}

		var creatureMat = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.02f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		var ck = new MeshKit();
		ck.Color = Colors.White;
		ck.Mat(creatureMat);
		ck.Blob(new Vector3(0, 0.9f, 0), new Vector3(0.35f, 0.95f, 0.3f), 900, 0.2f);
		_creature = ck.CommitTo(_mazeRoot, "Creature", false);
		_creature.Visible = false;

		var exitMarker = new Node3D { Position = new Vector3((MazeCols - 1) * MazeCell, 0, -(MazeRows - 1) * MazeCell) };
		_mazeRoot.AddChild(exitMarker);
		_mazeExitTrigger = new Area3D { CollisionLayer = 0, CollisionMask = 2, Monitorable = false, Monitoring = true };
		_mazeExitTrigger.Position = exitMarker.Position;
		_mazeExitTrigger.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(MazeCell * 0.8f, 2f, MazeCell * 0.8f) } });
		_mazeRoot.AddChild(_mazeExitTrigger);
		_mazeExitTrigger.BodyEntered += OnMazeExit;
	}

	private void AnimateMaze(double delta)
	{
		// The lights hold a single fixed, moody colour — no cycling. A shifting-colour effect here
		// read as a seizure-risk strobe rather than atmosphere, so it's gone for good, not just toned down.
		if (_clock >= _nextGlimpse)
		{
			_nextGlimpse = _clock + GD.RandRange(8.0, 18.0);
			_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
			if (_player != null && _creature != null)
			{
				Vector3 local = ToLocal(_player.GlobalPosition) - MazeOffset;
				int cc = Mathf.Clamp(Mathf.RoundToInt(local.X / MazeCell) + GD.RandRange(-2, 2), 0, MazeCols - 1);
				int rr = Mathf.Clamp(Mathf.RoundToInt(-local.Z / MazeCell) + GD.RandRange(1, 3), 0, MazeRows - 1);
				_creature.Position = new Vector3(cc * MazeCell, 0, -rr * MazeCell);
				_creature.Visible = true;
				string stingPath = "res://assets/audio/sfx/stalker_seen_01.wav";
				if (ResourceLoader.Exists(stingPath))
				{
					var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(stingPath), UnitSize = 4f, MaxDistance = 30f, VolumeDb = -6f };
					GetTree().Root.AddChild(s);
					s.GlobalPosition = _creature.GlobalPosition;
					s.Finished += s.QueueFree;
					s.Play();
				}
				_ = HideGlimpseAfter(0.6);
			}
		}
	}

	private async Task HideGlimpseAfter(double seconds)
	{
		await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
		if (_creature != null) _creature.Visible = false;
	}

	private void OnMazeExit(Node3D body)
	{
		if (_mazeExited || body is not PlayerController player) return;
		_mazeExited = true;
		_mazeActive = false;
		if (GetTree().Root.FindChild("ScreenFader", true, false) is ScreenFader fader)
			_ = fader.ShowCaption("", "The corridor ends where it began.", 1.0f, 2.4f, 1.0f);

		var walkie = new WalkiePickup { Position = _mazeExitTrigger.Position + new Vector3(1.2f, 0.3f, -1.5f), Name = "WalkiePickup" };
		_mazeRoot.AddChild(walkie);
		GD.Print("[story] Act 10: the maze ends");
	}
}
