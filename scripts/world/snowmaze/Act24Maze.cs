using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.SnowMaze;

/// <summary>
/// Act 24 (the owner's "Real Act 24": "the main spectacle of the entire game ... battle through a snow tunnelled
/// maze-like cavern to best the Wendigo ... a 5-15 minute encounter"):
/// <list type="number">
/// <item>Out of the lodge's splintered doorway, its tracks go off across the snow to a drift with a hole in it, black:
/// down into the snow. Into it, and they are in the maze (a save).</item>
/// <item>The maze (<see cref="SnowMazeCave"/>): quiet, the snow muffling everything; dead ends, loops that bring them back
/// round, one right way through. The wendigo hunts them in it (<see cref="WendigoHunter"/>): by what it hears and sees,
/// steered by a director that knows where they are and only ever hints.</item>
/// <item>At its heart the cavern, and the trenches cut through its floor: the old war's dead in them, helmets, rifles,
/// crates. Reaching it is a save. Somewhere in the trenches a crowbar; somewhere a long crate stencilled FLAME THROWER,
/// nailed shut: prised open, the flamethrower (a save).</item>
/// <item>Now it is the prey: burned, it hides, and comes back from behind. Four hits of the fire and it's finished
/// (Act 25: <see cref="Act25Ending"/>).</item>
/// </list>
/// The maze lies far under the lodge (its own pocket of the level); the hole at the surface hands the player down to
/// it behind a moment of black.
/// </summary>
public partial class Act24Maze : Node3D
{
	public static Act24Maze Instance { get; private set; }
	public const string FlagCrowbar = "act24_crowbar", FlagCrate = "act24_crate_open";
	/// <summary>Where the maze lies, from the lodge (lodge-local), and the hole in the drift at the surface.</summary>
	public static readonly Vector3 MazeOffset = new(-45f, -160f, 40f);
	public static readonly Vector3 HoleLocal = new(0f, 0f, SkiLodge.Apothem + 24f);

	public SnowMazeCave Cave { get; private set; }
	public WendigoHunter Hunter { get; private set; }
	public Audio.Act24Score Score { get; private set; }
	public bool InMaze { get; private set; }
	private UI.FrostEdge _frost;
	public bool InCavern { get; private set; }
	public Pickup Crowbar { get; private set; }
	public PickupInteractable CrateUse { get; private set; }
	public Vector3 CrateLocal { get; private set; }
	public Vector3 CrowbarLocal { get; private set; }
	public bool Finished { get; private set; }

	private SkiLodge _lodge;
	private Node3D _crate;
	private bool _handing;
	private Area3D _holeTrigger;
	private Vector3 _holeWorld;
	private AudioStreamPlayer _air;
	/// <summary>(2026-10-10b) Prints in the maze's soft floor: their boots, its claws. They stay (the newest few hundred), so
	/// a tunnel already walked shows it, and so does one it has been down.</summary>
	public SnowPrints Prints { get; private set; }
	private PlayerFootsteps _feet;
	private bool _leftFoot;

	public static Act24Maze Build(SkiLodge lodge)
	{
		var a = new Act24Maze { Name = "Act24Maze" };
		a._lodge = lodge;
		lodge.AddChild(a);
		return a;
	}

	public override void _Ready()
	{
		Instance = this;
		// built during play (as the finale ends) off the main thread; loaded into, all at once behind the black
		Cave = new SnowMazeCave { Name = "SnowMaze", Position = MazeOffset, Async = S == null || S.Current < Checkpoint.Act24Maze };
		AddChild(Cave);
		Prints = new SnowPrints { Name = "MazePrints" };
		AddChild(Prints);
		// the owner's song, through the hunt and the fight (Audio.Act24Score)
		Score = new Audio.Act24Score { Name = "Score", Hunter = () => Hunter, InCavern = () => InCavern, InMaze = () => InMaze };
		AddChild(Score);
		// (2026-10-10b) the tunnels heard as snow: dead and close; the cavern rings
		AudioDirector.Zone(Cave, new Vector3(45f, 4f, 39f), new Vector3(110f, 20f, 122f), AudioDirector.Space.Snow, "MazeVerb");
		AudioDirector.Zone(Cave, SnowMazeLayout.CavernCentre + Vector3.Up * 4f, new Vector3(SnowMazeLayout.CavernRX * 2f, 12f, SnowMazeLayout.CavernRZ * 2f), AudioDirector.Space.IceCavern, "CavernVerb", 1);
		Callable.From(BuildHole).CallDeferred();
		if (Cave.Built) CallDeferred(nameof(Furnish));
		else Cave.OnBuilt += () => CallDeferred(nameof(Furnish));
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		Audio.ForestDirector.Hush = false;
		Audio.DreadDrone.Hush = false;
		if (StoryBeat.Atmosphere(this) is { } atmo) atmo.UndergroundAmbientKeep = 0.08f;
		if (_feet != null && IsInstanceValid(_feet)) _feet.Stepped -= OnStep;
	}

	/// <summary>Their step in the maze: a boot print in the soft floor.</summary>
	private void OnStep()
	{
		if (!InMaze || InCavern) return;
		var player = StoryBeat.Player(this);
		if (player == null) return;
		var v = player.Velocity with { Y = 0 };
		var f = v.LengthSquared() > 0.01f ? v.Normalized() : (-player.CameraRig.Camera.GlobalBasis.Z) with { Y = 0 };
		var side = f.Cross(Vector3.Up).Normalized() * (_leftFoot ? -0.11f : 0.11f);
		LayAt(player.GlobalPosition + side, f, _leftFoot, false, player.Crouching ? 0.35f : 0.55f);
		_leftFoot = !_leftFoot;
	}

	/// <summary>Its claws where it stepped (WendigoHunter's stride).</summary>
	public void LayClaw(Vector3 world, Vector3 facing, bool left) => LayAt(world + facing.Cross(Vector3.Up).Normalized() * (left ? -0.25f : 0.25f), facing, left, true, 0.6f);

	private void LayAt(Vector3 world, Vector3 facing, bool left, bool wendigo, float depth)
	{
		var from = world + Vector3.Up * 0.8f;
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 2f, 1));
		if (hit.Count == 0) return;
		Prints.Lay((Vector3)hit["position"], (Vector3)hit["normal"], facing, left, wendigo, depth);
	}

	private static StoryManager S => StoryManager.Instance;

	// ------------------------------------------------------------------ the hole in the drift

	/// <summary>The drift with the hole in it (its tracks going in), on the snow in front of the lodge.</summary>
	/// <summary>(2026-10-10b, the owner's drawing: "step outside the front door of the ski lodge and you are already incased in a
	/// snow tunnel and you have to traverse it to the entrance of the real maze") The storm has buried the front of the
	/// lodge: out of the splintered doorway it's a vault of snow, wide and high over the porch (its piers and roof swallowed
	/// in it), running straight out twenty-odd metres, the faint cold light of the night coming through its roof; at its end
	/// a wall of snow, and low in it, black, the way down into the maze. All one solid: walked through, nothing to see
	/// round.</summary>
	public const float TunnelZ0 = 0f, TunnelLen = 24f, VaultR = 3.9f;
	private void BuildHole()
	{
		float z0 = SkiLodge.Apothem + 0.02f, zEnd = SkiLodge.Apothem + TunnelLen;
		var space = GetWorld3D().DirectSpaceState;
		float Ground(float z)
		{
			var from = _lodge.ToGlobal(new Vector3(0, 40f, z));
			var h = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 120f, 1));
			return h.Count > 0 ? _lodge.ToLocal((Vector3)h["position"]).Y : 0f;
		}
		var root = new Node3D { Name = "SnowTunnel" };
		AddChild(root);
		root.GlobalTransform = _lodge.GlobalTransform;
		var csg = new CsgCombiner3D { Name = "Snow", UseCollision = true, CollisionLayer = 1, CollisionMask = 0 };
		root.AddChild(csg);
		var snow = SnowMazeLook.PackedSnow();   // (2026-10-13c: packed and gouged; the fresh snow read as white plastic in here)
		// the snow over it all: the long bank, lumpy on top, and the mass it ends in
		const float seg = 4f;
		int n = Mathf.CeilToInt((zEnd - z0) / seg);
		var gz = new float[n + 1];
		for (int i = 0; i <= n; i++) gz[i] = Ground(Mathf.Min(z0 + i * seg + seg * 0.5f, zEnd - 0.5f));
		for (int i = 0; i < n; i++)
		{
			float zc = z0 + i * seg + seg * 0.5f;
			csg.AddChild(new CsgBox3D { Size = new Vector3(11.5f, 9f, seg + 0.2f), Position = new Vector3(0, gz[i] + 3.2f, zc), Material = snow });
			csg.AddChild(new CsgSphere3D { Radius = 1f, RadialSegments = 18, Rings = 9, Scale = new Vector3(5.6f, 2.4f, 2.8f), Position = new Vector3((i % 2 == 0 ? -0.8f : 0.9f), gz[i] + 7.3f, zc), Material = snow });
		}
		float ge = Ground(zEnd + 1f);
		csg.AddChild(new CsgSphere3D { Radius = 1f, RadialSegments = 24, Rings = 12, Scale = new Vector3(7f, 6f, 5.5f), Position = new Vector3(0, ge + 1.5f, zEnd + 3.5f), Material = snow });
		// the vault: a segment at a time, its floor the ground there (round the porch, the landing)
		for (int i = 0; i < n; i++)
		{
			float za = z0 + i * seg, zb = Mathf.Min(za + seg, zEnd);
			float r = Mathf.Lerp(VaultR, VaultR * 0.82f, i / (float)Mathf.Max(1, n - 1));   // (narrowing a little toward its end)
			csg.AddChild(new CsgCylinder3D
			{
				// (the first runs back past the bank's front face, through to the lodge's wall: the door opens straight into it)
				Operation = CsgShape3D.OperationEnum.Subtraction, Radius = r, Height = zb - za + (i == 0 ? 0.6f : 0.3f), Sides = 22, Material = snow,
				Position = new Vector3(0, gz[i] + 0.9f, (za + zb) * 0.5f - (i == 0 ? 0.3f : 0f)), Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0),
			});
			// a rib of packed snow every few metres (the arches in the drawing), the vault scalloped between them
			if (i > 0) csg.AddChild(new CsgTorus3D { Operation = CsgShape3D.OperationEnum.Union, InnerRadius = r - 0.35f, OuterRadius = r + 0.3f, Sides = 20, RingSides = 6, Position = new Vector3(0, gz[i] + 0.9f, za), Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0), Material = snow });
		}
		// the way down: low in the end wall, black, going in and down (the maze's own ice inside)
		var ice = Cave.IceMaterial ?? SnowMazeLook.CaveMaterial();
		csg.AddChild(new CsgCylinder3D
		{
			Name = "Entrance", Operation = CsgShape3D.OperationEnum.Subtraction, Radius = 1.15f, Height = 6.5f, Sides = 20, Material = ice,
			Position = new Vector3(0, ge + 0.56f, zEnd + 2.7f), Rotation = new Vector3(Mathf.DegToRad(96f), 0, 0),
		});
		foreach (var (c, r) in new[] { (new Vector3(0.75f, 1.95f, -0.5f), 0.48f), (new Vector3(-0.85f, 1.75f, -0.45f), 0.42f), (new Vector3(1.0f, 0.55f, -0.55f), 0.36f), (new Vector3(-1.05f, 0.45f, -0.5f), 0.33f) })
			csg.AddChild(new CsgSphere3D { Operation = CsgShape3D.OperationEnum.Subtraction, Radius = r, RadialSegments = 14, Rings = 8, Position = new Vector3(c.X, ge + c.Y - 0.3f, zEnd + c.Z), Material = ice });
		// the night's light through the snow overhead: faint, cold, wavering with nothing
		for (int i = 0; i < 3; i++)
		{
			float z = Mathf.Lerp(z0 + 5f, zEnd - 3f, i / 2f);
			root.AddChild(new OmniLight3D { Name = $"SnowGlow{i}", Position = new Vector3(0, Ground(z) + 3.6f, z), LightColor = new Color(0.55f, 0.65f, 0.85f), LightEnergy = 0.3f, OmniRange = 7f, ShadowEnabled = false });
		}
		// the hole's node: at the entrance (its local -Z toward the lodge), for the trigger, the dark, the icicles
		var hole = new Node3D { Name = "Hole" };
		AddChild(hole);
		hole.GlobalTransform = _lodge.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, ge, zEnd));
		_holeWorld = hole.GlobalPosition;
		BuildHoleThroat(hole);
		var t = GetTree().CreateTimer(0.4);
		t.Timeout += () => { if (IsInstanceValid(hole)) DressMouth(hole); };
		_holeTrigger = StoryBeat.MakeTrigger(hole, new BoxShape3D { Size = new Vector3(2.0f, 3f, 2.6f) }, new Vector3(0, 1f, 0.9f), p => _ = Cutscene.Run(this, ct => GoDown(p, ct), lockInput: true), "HoleTrigger");
		GD.Print($"[story] Act 24: the snow tunnel from the lodge's door to the way down ({zEnd - z0:0} m)");
	}

	/// <summary>(2026-10-15, the scan: the hole's dark was three flat black cards stacked in it, plain cards from any angle) The
	/// throat of the way down: a tube lining the bore (hole-local, along the Entrance cut: in along +Z, dropping a little),
	/// flared at its start so it comes out of the cut's own wall a metre in, its packed ice going grey and then black the
	/// deeper it goes (no shine in it), closed by black at the end. Real depth: the lantern lights its first metre and no
	/// more.</summary>
	private static void BuildHoleThroat(Node3D hole)
	{
		var mat = new StandardMaterial3D
		{
			ResourceName = "maze_hole_throat", VertexColorUseAsAlbedo = true, Roughness = 1f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
			NormalEnabled = true, NormalTexture = GD.Load<Texture2D>("res://assets/textures/snow/snow_normal.png"), NormalScale = 0.7f,
			Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 0.7f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		var k = new MeshKit();
		k.Mat(mat);
		const int seg = 22, rings = 20;
		const float z0 = 0.35f, z1 = 5.6f;
		var wob = new FastNoiseLite { Frequency = 0.9f, Seed = 2441 };
		static float AxisY(float z) => 0.56f - 0.1055f * (z - 2.7f);   // (the Entrance cylinder's axis: 96 degrees off the vertical)
		var p = new Vector3[rings, seg + 1];
		var n = new Vector3[rings, seg + 1];
		var col = new Color[rings];
		for (int r = 0; r < rings; r++)
		{
			float z = Mathf.Lerp(z0, z1, r / (rings - 1f));
			float rad = Mathf.Lerp(1.3f, 1.02f, Mathf.SmoothStep(0.4f, 1.5f, z)) - 0.12f * Mathf.SmoothStep(3f, 5.6f, z);
			float g = Mathf.Lerp(0.34f, 0f, Mathf.SmoothStep(0.9f, 3.4f, z));
			col[r] = new Color(g * 0.9f, g * 0.95f, g, 1f);
			for (int s = 0; s <= seg; s++)
			{
				float th = Mathf.Tau * (s % seg) / seg;
				float w = 1f + 0.07f * wob.GetNoise2D(z * 2f, s % seg * 0.8f);
				var d = new Vector3(Mathf.Cos(th), Mathf.Sin(th), 0f);
				p[r, s] = new Vector3(0f, AxisY(z), z) + d * rad * w;
				n[r, s] = -d;
			}
		}
		for (int r = 0; r < rings - 1; r++)
			for (int s = 0; s < seg; s++)
			{
				k.TriC(p[r, s], p[r + 1, s], p[r + 1, s + 1], n[r, s], n[r + 1, s], n[r + 1, s + 1], Vector2.Zero, Vector2.Zero, Vector2.Zero, col[r], col[r + 1], col[r + 1]);
				k.TriC(p[r, s], p[r + 1, s + 1], p[r, s + 1], n[r, s], n[r + 1, s + 1], n[r, s + 1], Vector2.Zero, Vector2.Zero, Vector2.Zero, col[r], col[r + 1], col[r]);
			}
		// the black at the end of it
		k.Color = Colors.Black;
		var c = new Vector3(0f, AxisY(z1), z1 + 0.4f);
		for (int s = 0; s < seg; s++)
			k.Tri(c, p[rings - 1, s], p[rings - 1, s + 1], Vector3.Back, Vector2.Zero, Vector2.Zero, Vector2.Zero);
		k.CommitTo(hole, "Throat", false);
	}

	/// <summary>Round the mouth, on the drift's face: a crusted lip of packed snow along the cut's edge (so it reads as a
	/// hole in the snow, not a disc cut out of it), and icicles hanging off the lip's top, from the snow itself.</summary>
	private void DressMouth(Node3D hole)
	{
		var space = GetWorld3D().DirectSpaceState;
		var lip = new MeshKit();
		lip.Mat(SnowMazeLook.PackedSnow());
		lip.Color = Colors.White;
		var ice = new MeshKit();
		ice.Mat(WinterWoods.IceMat);
		ice.Color = Colors.White;
		var rng = new RandomNumberGenerator { Seed = 2440 };
		const float cy = 1.09f, r = 1.15f;
		int lips = 0, cicles = 0;
		// (a ray from in front of the face, straight in, at a point just outside the cut: where it meets the drift is the face)
		Vector3? Face(float x, float y)
		{
			var from = hole.ToGlobal(new Vector3(x, y, -4f));
			var to = hole.ToGlobal(new Vector3(x, y, 3f));
			var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, 1));
			return hit.Count > 0 ? hole.ToLocal((Vector3)hit["position"]) : null;
		}
		// (no lumps round the rim: the owner, "remove that igloo rock ring look": the cut's own scallops are the lip)
		for (int i = 0; i < 13; i++)
		{
			float a = Mathf.Pi * (0.14f + 0.72f * i / 12f);
			float x = Mathf.Cos(a) * (r + 0.02f), y = cy + Mathf.Sin(a) * (r + 0.02f);
			if (Face(x, y + 0.04f) is not { } f) continue;
			// (2026-10-14d) hung from the snow itself: straight up from inside the opening, just in from the face, to the
			// underside of the rim (the cut isn't a true circle: off the circle they hung 5-15 cm under the edge, on nothing)
			var probe = hole.ToGlobal(new Vector3(x * 0.85f, cy, f.Z + 0.06f));
			var up = space.IntersectRay(PhysicsRayQueryParameters3D.Create(probe, probe + hole.GlobalBasis.Y * 2.2f, 1));
			if (up.Count == 0) continue;
			var rim = hole.ToLocal((Vector3)up["position"]);
			var top = new Vector3(rim.X, rim.Y + 0.035f, rim.Z);   // (its root a few centimetres up in the snow)
			float len = rng.RandfRange(0.14f, 0.5f) * Mathf.Sin(a);
			ice.Cylinder(top + Vector3.Up * 0.05f, top + Vector3.Down * len, rng.RandfRange(0.025f, 0.045f), 0.003f, 5, false);
			cicles++;
		}
		ice.CommitTo(hole, "Icicles", false);
		_ = lip; _ = lips;
		GD.Print($"[story] Act 24: the drift's mouth dressed ({cicles} icicles off its lip)");
	}

	private void LayTracks()
	{
		var prints = WinterWoods.Instance?.Prints;
		if (prints == null) return;
		var start = _lodge.ToGlobal(new Vector3(0.3f, 0.1f, SkiLodge.Apothem + 6f));
		var end = _holeWorld;
		var dir = (end - start) with { Y = 0 };
		float len = dir.Length();
		dir /= Mathf.Max(len, 0.01f);
		var side = new Vector3(dir.Z, 0, -dir.X);
		int n = Mathf.FloorToInt(len / 1.6f);
		for (int i = 0; i < n; i++)
		{
			var p = start + dir * (i * 1.6f) + side * (i % 2 == 0 ? 0.28f : -0.28f) + side * Mathf.Sin(i * 0.4f) * 0.6f;
			var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 20f, p + Vector3.Down * 40f, 1);
			var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
			if (hit.Count == 0) continue;
			prints.Lay((Vector3)hit["position"], (Vector3)hit["normal"], dir, i % 2 == 0, true, 0.6f);
		}
	}

	/// <summary>In through the hole: a moment's black, and they are in the maze's way in, looking down it.</summary>
	private async Task GoDown(PlayerController player, CancellationToken ct)
	{
		if (_handing || InMaze) return;
		_handing = true;
		HerdAtTheMouth();
		var fader = StoryBeat.Fader(this);
		AudioDirector.OneShot(this, "step_snow", 6, player.GlobalPosition, -2f, "Player", 3f, 0.05f);
		// (2026-10-10b: the black at the hole tightened: a quick slide down into the dark, and out)
		if (fader != null) await fader.Fade(1f, 0.35f, ct);
		while (!Cave.Built || Crowbar == null) await Cutscene.Wait(this, 0.1, ct);   // (still building: wait in the black)
		EnterMaze(player);
		await Cutscene.Wait(this, 0.1, ct);
		if (fader != null) await fader.Fade(0f, 0.6f, ct);
		_handing = false;
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act24Maze);
		_ = StoryBeat.Caption(this, "So quiet down here.", 0.6f, 2.4f, 1f);
	}

	private void EnterMaze(PlayerController player)
	{
		var at = Cave.ToGlobal(Cave.ArriveAt);
		player.GlobalPosition = at;
		player.Velocity = Vector3.Zero;
		var look = Cave.ToGlobal(Cave.ArriveAt + Vector3.Back * 4f);
		player.CameraRig?.SnapBehind(Mathf.Atan2(-(look - at).X, -(look - at).Z));
		InMaze = true;
		StartHunt();
	}

	// ------------------------------------------------------------------ the hunt

	public void StartHunt()
	{
		if (Hunter != null) return;
		Hunter = new WendigoHunter { Name = "Hunter" };
		AddChild(Hunter);
		// it starts far off from them (the cavern's far side, or the maze's far corner)
		var L = Cave.Layout;
		var p = Cave.ToLocal(StoryBeat.Player(this)?.GlobalPosition ?? Cave.GlobalPosition);
		int far = 0;
		float fd = 0f;
		for (int v = 0; v < L.Nodes.Count; v++) { float d = L.Nodes[v].DistanceTo(p); if (d > fd && v != L.Cavern) { fd = d; far = v; } }
		// (2026-10-10b) it drove them down: it follows them in, down the way they came, after a while (on a load into the maze,
		// it's off in the far tunnels as before)
		Hunter.Setup(Cave, L.Nodes[far]);
		// (2026-10-10b) it drove them down: it comes down the hole after them, once they've gone on into the tunnels (out of
		// sight of the way in), and they hear it at the top of the entrance tunnel behind them. Not in the autotest, which
		// places it by hand.
		if (_followedIn && !(GameSettings.Instance?.AutoTest ?? false))
		{
			GD.Print("[story] Act 24: the wendigo will follow them in");
			_ = FollowIn();
		}
		Hunter.Killed += () => _ = Cutscene.Run(this, ct => Defeated(ct), lockInput: true);
		Hunter.Hit += n => GD.Print($"[story] Act 24: the fire on it - {n} of {Hunter.HitsToKill}");
		if (S != null && S.Current >= Checkpoint.Act24Flamethrower) Hunter.ArmedPlayer();
		GD.Print($"[story] Act 24: the wendigo is in the maze (node {far})");
	}

	private async Task FollowIn()
	{
		var start = Cave.ToGlobal(Cave.ArriveAt);
		double waited = 0;
		while (IsInstanceValid(Hunter) && !Hunter.Dead && waited < 60)
		{
			await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
			waited += 1;
			var p = StoryBeat.Player(this);
			if (p == null || waited < 12 || p.GlobalPosition.DistanceTo(start) < 14f) continue;
			if (Hunter.State is not (WendigoHunter.Mode.Patrol or WendigoHunter.Mode.Search)) return;
			Hunter.Body.GlobalPosition = Cave.ToGlobal(Cave.ArriveAt + Vector3.Forward * 1.5f);
			AudioDirector.OneShot(this, "wendigo_howl_far", 3, start + Vector3.Up * 2f, 0f, "Unnatural", 12f, 0.03f);
			Hunter.Lure(p.GlobalPosition, 999f, 2f, "their trail down the hole");
			GD.Print("[story] Act 24: it has come down the hole after them");
			return;
		}
	}

	public override void _Process(double delta)
	{
		var player = StoryBeat.Player(this);
		if (player == null || Cave?.Layout == null) return;
		if (_feet == null && player.Footsteps is { } feet) { _feet = feet; feet.Stepped += OnStep; }
		HerdProcess(player, (float)delta);
		var l = Cave.ToLocal(player.GlobalPosition);
		bool inside = l.Y > -6f && l.Y < 14f && l.X > -10f && l.X < 100f && l.Z > -22f && l.Z < 100f;
		if (inside && !InMaze) { InMaze = true; StartHunt(); }
		InMaze = inside;
		Audio.ForestDirector.Hush = inside;
		Audio.DreadDrone.Hush = inside;
		// the cave's air: underground (no sky, no sun), a cold blue dark, everything muffled
		if (StoryBeat.Atmosphere(this) is { } atmo)
		{
			float dt = (float)delta;
			atmo.Underground = Mathf.MoveToward(atmo.Underground, inside ? 1f : 0f, dt);
			if (inside)
			{
				atmo.UndergroundFogColor = atmo.UndergroundFogColor.Lerp(new Color(0.03f, 0.05f, 0.08f), Mathf.Min(1f, dt * 2f));
				atmo.UndergroundFogDensity = Mathf.MoveToward(atmo.UndergroundFogDensity, 0.035f, dt * 0.05f);
			}
			// (2026-10-11's playthrough: the trenches and the cavern read as pitch black; the ice keeps a cold glow)
			atmo.UndergroundAmbientKeep = Mathf.MoveToward(atmo.UndergroundAmbientKeep, inside ? 0.42f : 0.08f, dt * 0.5f);
		}
		// frost at the screen's edges as it comes near (and as the hunt's menace rises); thawing as it goes
		if (inside && _frost == null) { _frost = new UI.FrostEdge { Name = "FrostEdge" }; AddChild(_frost); }
		if (_frost != null)
		{
			float near = 0f;
			if (inside && Hunter is { Body: not null, Dead: false } h)
				near = Mathf.Clamp(1f - h.Body.GlobalPosition.DistanceTo(player.GlobalPosition) / 16f, 0f, 1f) * 0.85f + h.Menace / 100f * 0.25f;
			_frost.Target = near;
		}
		// the cave's quiet: a low moan of air far off down the tunnels, the ice ticking (the snow muffles the rest)
		if (_air == null && ResourceLoader.Exists("res://assets/audio/ambient/snow_cave_loop.wav"))
		{
			var wav = (AudioStreamWav)GD.Load<AudioStreamWav>("res://assets/audio/ambient/snow_cave_loop.wav").Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopBegin = 0;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			_air = new AudioStreamPlayer { Name = "CaveAir", Stream = wav, Bus = "Wind", VolumeDb = -80f };
			AddChild(_air);
		}
		if (_air != null)
		{
			_air.VolumeDb = Mathf.MoveToward(_air.VolumeDb, inside ? -13f : -80f, (float)delta * 20f);   // (2026-10-11: under the song, not over it)
			if (_air.VolumeDb > -79f && !_air.Playing) _air.Play();
			else if (_air.VolumeDb <= -79f && _air.Playing) _air.Stop();
		}
		if (!inside) return;
		bool cav = SnowMazeLayout.InCavern(l);
		if (cav && !InCavern && S != null && S.Current < Checkpoint.Act24Trenches)
		{
			GD.Print("[story] Act 24: the cavern, and the trenches");
			StoryBeat.ReachCheckpoint(player, Checkpoint.Act24Trenches);
			_ = StoryBeat.Caption(this, "Trenches.", 0.4f, 1.4f, 0.6f);   // (short: it overlapped the crate's prompt)
		}
		InCavern = cav;
	}

	// ------------------------------------------------------------------ the crowbar and the crate

	private void Furnish()
	{
		Vector3 crowbarAt = default, crateAt = default; float crateYaw = 0f;
		FurnitureKit.Clean(() => { Cave.DressTrenches(out crowbarAt, out crateAt, out crateYaw); return 0; });
		CrowbarLocal = crowbarAt;
		// (2026-10-10) the green flares along the way through; the hollows in the dead ends
		MazeExtras.BuildHollows(Cave);
		if (S == null || S.Current < Checkpoint.Act24Trenches) MazeExtras.PlaceFlares(Cave);
		CrateLocal = crateAt;
		bool crateOpen = S != null && (S.HasFlag(FlagCrate) || S.Current >= Checkpoint.Act24Flamethrower);
		// the crowbar
		Crowbar = new Pickup { Name = "Crowbar", Kind = ToolKind.Crowbar, Label = "Crowbar", TakenId = FlagCrowbar, UseSpot = false, SnapToSurface = false };
		Cave.AddChild(Crowbar);
		Crowbar.Position = crowbarAt;   // (2026-10-15: on the duckboards' slats; it hung 3 cm over them)
		Crowbar.Rotation = new Vector3(0, 0.7f, 0);
		// the crate
		_crate = new Node3D { Name = "FlameCrate", Position = crateAt, Rotation = new Vector3(0, crateYaw, 0) };
		Cave.AddChild(_crate);
		FurnitureKit.Clean(() => FurnitureKit.Place(_crate, crateOpen ? "flame_crate_open" : "flame_crate", Transform3D.Identity, SnowMazeDressing.Roles, "Crate"));
		CrateUse = new PickupInteractable
		{
			Name = "CrateUse", PickRadius = 0.8f, MaxDistance = 2.4f, Position = new Vector3(0, 0.45f, 0),
			PromptFor = p => crateOpenNow() ? "" : p?.Inventory is { } inv && inv.HasTool(ToolKind.Crowbar) ? "Prise the crate open" : "Nailed shut. \"FLAME THROWER, PORTABLE\".",
			CanUse = p => !crateOpenNow() && p?.Inventory is { } inv && inv.HasTool(ToolKind.Crowbar),
		};
		bool crateOpenNow() => S != null && S.HasFlag(FlagCrate);
		CrateUse.Interacted += p => { if (!crateOpenNow()) _ = Cutscene.Run(this, ct => OpenCrate(p, ct), lockInput: true); };
		_crate.AddChild(CrateUse);
		// the respawns: at the way in, at the cavern's edge, by the crate
		Marker("Act24MazeMarker", Cave.ToGlobal(Cave.ArriveAt), Cave.GlobalRotation.Y, "respawn_Act24Maze");
		var door = Cave.Layout.EndAt(Cave.Layout.Solution[^2], Cave.Layout.Cavern);
		Marker("Act24TrenchMarker", Cave.ToGlobal(door + (SnowMazeLayout.CavernCentre - door).Normalized() * 1.5f + Vector3.Up * 0.1f), Cave.GlobalRotation.Y, "respawn_Act24Trenches");
		Marker("Act24FlameMarker", Cave.ToGlobal(crateAt + new Vector3(0, 0.1f, 0) + new Basis(Vector3.Up, crateYaw) * new Vector3(1.5f, 0, 0)), Cave.GlobalRotation.Y, "respawn_Act24Flamethrower");   // (along the trench from its end: across it was the wall)
		// a save in here: the level put them down before these markers were up (the maze is built after the lodge), so
		// they're put at the save's own now; the hunt starts as they're found in the maze
		if (S != null && S.Current is >= Checkpoint.Act24Maze and <= Checkpoint.Act24Flamethrower && !InMaze
			&& StoryBeat.Player(this) is { } pl && GetTree().GetFirstNodeInGroup($"respawn_{S.Current}") is Node3D m)
			pl.Teleport(m.GlobalPosition + Vector3.Up * 0.1f, m.GlobalRotation.Y);
		if (S != null && S.Current >= Checkpoint.Act24Flamethrower) EnsureFlamethrower(StoryBeat.Player(this));
	}

	private void Marker(string name, Vector3 at, float yaw, string group)
	{
		var m = new Node3D { Name = name };
		AddChild(m);
		m.GlobalPosition = at;
		m.GlobalRotation = new Vector3(0, yaw, 0);
		m.AddToGroup(group);
	}

	/// <summary>The lid prised up, nail by nail, and off; in the straw, the flamethrower: on with it (a save).</summary>
	private async Task OpenCrate(PlayerController player, CancellationToken ct)
	{
		GD.Print("[story] Act 24: prising the crate open");
		await StoryBeat.PanTowards(this, player, _crate.GlobalPosition + Vector3.Up * 0.3f, 0.5f, ct, pitch: true);
		HeldUse.Do(player, ToolKind.Crowbar, _crate.GlobalPosition + Vector3.Up * 0.45f, 2.3f);
		for (int i = 0; i < 3; i++)
		{
			AudioDirector.OneShot(this, "door_strain", 2, _crate.GlobalPosition, -6f + i * 2f, "Events", 3f, 0.08f);
			await Cutscene.Wait(this, 0.55, ct);
		}
		AudioDirector.OneShot(this, "wood_bump", 3, _crate.GlobalPosition, 0f, "Events", 3f, 0.05f);
		foreach (var c in _crate.GetChildren()) if (c is MeshInstance3D) c.QueueFree();
		FurnitureKit.Clean(() => FurnitureKit.Place(_crate, "flame_crate_open", Transform3D.Identity, SnowMazeDressing.Roles, "CrateOpen"));
		S?.SetFlag(FlagCrate);
		await Cutscene.Wait(this, 0.6, ct);
		EnsureFlamethrower(player);
		AudioDirector.OneShot(this, "flame_ready", 1, null, -4f, "Player");
		_ = StoryBeat.Caption(this, "A flamethrower. It still works.", 0.5f, 2.8f, 1f);
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act24Flamethrower);
		Hunter?.ArmedPlayer();
		// and the scream from the tunnels: it heard
		await Cutscene.Wait(this, 1.2, ct);
		AudioDirector.OneShot(this, "wendigo_howl_04", 1, _crate.GlobalPosition + new Vector3(0, 6f, 20f), 4f, "Unnatural", 20f, 0.02f);
	}

	public static void EnsureFlamethrower(PlayerController player)
	{
		if (player == null) return;
		player.Inventory?.TryPickup(ToolKind.Flamethrower);
		if (player.GetNodeOrNull<Flamethrower>("Flamethrower") == null) player.AddChild(new Flamethrower { Name = "Flamethrower" });
		// in their hands, not just carried: after a Continue (or a death) the HUD would otherwise start on the camera
		UI.EquippedItemHud.Prefer(player.GetTree(), ToolKind.Flamethrower);
	}

	// ------------------------------------------------------------------ the end of it

	private async Task Defeated(CancellationToken ct)
	{
		if (Finished) return;
		Finished = true;
		var player = StoryBeat.Player(this);
		GD.Print("[story] Act 24: the fourth burn - it's finished");
		// the ending decided and saved first, so the checkpoint's save already carries it (a quit in between can't
		// Continue into the fallback, ending 1)
		Act25Ending.Decide(StoryManager.Instance);
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act24Finished);
		await Act25Ending.Play(this, player, Hunter, Cave, ct);
	}
}
