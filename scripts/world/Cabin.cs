using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The old cabin up the hollow: a small round-log cabin with crossed log ends at the corners,
/// a shingled gable roof (ridge parallel to the front) with fascia, rake boards and
/// a ridge cap, a mortared fieldstone chimney on the left gable, and a covered front
/// porch (posts, rails, a bench, steps down to a stone landing). Two front windows
/// (boarded over while the door is boarded); the left one glows faintly from the lamp inside. Inside: plank
/// floor, open rafters under the roof boards, a cast-iron stove, a cot, a shelf and a
/// hanging lamp (warm light). What the friend left at his table (friend.tscn: his chair pushed
/// back, the bandage, the stains, his page, the newel post) sits on the floor at his spot.
/// Papers: R.H.'s four prints over his chair; the page on the table is
/// FriendBody's. All of them join <see cref="PapersGroup"/> and burn away with the cabin.
///
/// Local frame: front (the door) faces +Z, same convention as ParkProp. y = 0 is the
/// INTERIOR FLOOR TOP. At runtime the cabin grounds itself: floor at sill height above
/// the higher of the door-side ground and the footprint average (never below any
/// ground under it), with a stone foundation skirt down to the lowest ground, porch
/// skirt boards, and as many porch steps as the slope needs.
///
/// States (all idempotent, safe to call from save-restore in any order):
/// <see cref="SetBoarded"/>, <see cref="OpenDoor"/>/<see cref="SetOpen"/>,
/// <see cref="SetBurning"/> (FireVfx at <see cref="FireSpots"/> + progressive char),
/// <see cref="SetBurnt"/> (charred shell, part of the roof fallen in).
/// Pickup spots: child markers "LanternSpot"/"CompassSpot" (cabin.tscn), also
/// <see cref="PorchSpot"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class Cabin : Node3D
{
	[Export] public float Width = 4.2f;
	[Export] public float Depth = 5.2f;
	[Export] public float WallHeight = 2.3f;
	[Export] public float DoorWidth = 1.3f;
	[Export] public float DoorHeight = 2.0f;
	[Export] public int Seed = 3;
	[Export] public bool BuildCollision = true;
	/// <summary>Depth of the covered porch in front of the door.</summary>
	[Export] public float PorchDepth = 1.75f;
	/// <summary>Floor height above the door-side / average ground.</summary>
	[Export] public float SillClearance = 0.4f;
	/// <summary>Minimum height of the porch deck above the ground where the steps land (about three risers: two treads and the deck).</summary>
	[Export] public float StepsAboveLanding = 0.5f;
	/// <summary>Raise the cabin to sill height on its slope at runtime (see class doc).</summary>
	[Export] public bool AutoGround = true;
	/// <summary>Ground depth below the floor used in the editor (no terrain there).</summary>
	[Export] public float EditorGroundDepth = 0.55f;

	public bool DoorBoarded { get; private set; }
	public bool IsOpen { get; private set; }
	public bool IsBurnt { get; private set; }
	/// <summary>Porch steps built for the slope in front (0 when the porch is at ground level).</summary>
	public int StepCount => Mathf.Max(0, _steps - 1);
	/// <summary>0 = not burning, 1 = fully engulfed.</summary>
	public float Burning { get; private set; }

	private bool _lightOn = true;
	/// <summary>The hanging lamp inside (and the lit window). Off automatically while burning/burnt.</summary>
	public bool InteriorLightOn { get => _lightOn; set { _lightOn = value; UpdateLamp(); } }

	/// <summary>World-space centre of the door, for triggers to line up against.</summary>
	public Vector3 DoorCenter => GlobalTransform * new Vector3(0, DoorHeight * 0.5f, Hd);
	/// <summary>A walkable point a few metres out from the door, clear of the walls (on the ground or the steps).</summary>
	public Vector3 ApproachPoint => GlobalTransform * new Vector3(0, Mathf.Max(GroundLocal(0, Hd + 2.5f), StepTopAt(Hd + 2.5f)), Hd + 2.5f);
	/// <summary>Well out in front of the door, clear of the building from any direction, for routing around it.</summary>
	public Vector3 WideApproachPoint => GlobalTransform * new Vector3(0, GroundLocal(0, Hd + 12f), Hd + 12f);
	/// <summary>A point just inside the doorway, for the player to be guided or teleported to (on the floor).</summary>
	public Vector3 InsidePoint => GlobalTransform * new Vector3(0, 0, Hd - 1.2f);

	/// <summary>Where the porch pickups sit (world): 0 = lantern (porch floor, left of the door), 1 = compass (porch bench, right).</summary>
	public Vector3 PorchSpot(int i) => GlobalTransform * LocalPorchSpot(i);
	public Vector3 LocalPorchSpot(int i) => i == 0
		? new Vector3(-1.0f, 0f, FrontFace + 0.5f)
		: new Vector3(1.25f, BenchTop, FrontFace + 0.26f);

	/// <summary>Local fire placements (flame base centre, FireVfx.Extent) used by <see cref="SetBurning"/>.</summary>
	public IReadOnlyList<(Vector3 pos, Vector3 size)> FireSpots
	{
		get
		{
			var list = new List<(Vector3, Vector3)>();
			foreach (var s in FireSpotDefs()) list.Add((s.pos, s.size));
			return list;
		}
	}

	// ------------------------------------------------------------------ dimensions
	private const float LogT = 0.22f, Ext = 0.2f, FloorBottom = -0.3f, BenchTop = 0.45f;
	private const int Courses = 9;
	private const float Pitch = 0.535f;              // roof rise per metre (about 28 degrees)
	private float Hw => Width * 0.5f;
	private float Hd => Depth * 0.5f;
	private float Dw => DoorWidth * 0.5f;
	private float LogH => (WallHeight - FloorBottom) / Courses;
	private float FrontFace => Hd + LogT * 0.5f;
	private float PorchEdge => FrontFace + PorchDepth;
	private float EaveY => WallHeight + 0.05f;
	private float RidgeY => EaveY + FrontFace * Pitch;
	private float RoofBottomAt(float z) => EaveY + (FrontFace - Mathf.Abs(z)) * Pitch;
	private float WinCx => (Dw + Hw) * 0.5f + 0.02f;
	private const float WinW = 0.7f, StepW = 1.4f, StepRun = 0.3f;

	private Node3D _gen, _planks, _door, _windowBoards, _debris, _fire, _sashes, _panes, _papers;
	private Readable _sheet;
	private CollisionShape3D _doorCollision;

	/// <summary>The tongue-twister sheet over the cot (P3), for tests and previews.</summary>
	public Readable TwisterSheet => _sheet;
	/// <summary>Every paper prop in the cabin joins this group (the friend's page too); they all burn away with it.</summary>
	public const string PapersGroup = "cabin_papers";
	private OmniLight3D _lamp;
	private MeshInstance3D _lampGlass, _litPane;
	private ShaderMaterial _char;
	private readonly List<FireVfx> _fires = new();
	private ForestTerrain _terrain;
	private bool _grounded;
	private float _doorTop = 2.0f, _winBot = 0.85f, _winTop = 1.72f;
	private int _steps;
	private float _stepRise;

	public override void _Ready()
	{
		AddToGroup("cabin");
		if (!Engine.IsEditorHint())
		{
			_terrain = GroundSnap.FindTerrain(this);
			if (AutoGround && !_grounded) GroundSelf();
			RainVfx.RegisterShelter(this, new Aabb(new Vector3(-Hw, -0.5f, -Hd), new Vector3(Width, RidgeY + 0.5f, Depth)));
		}
		Build();
	}

	// ------------------------------------------------------------------ grounding

	/// <summary>Local ground height at local (x, z): terrain at runtime, a flat plane in the editor.</summary>
	private float GroundLocal(float x, float z)
	{
		if (_terrain == null || !IsInsideTree()) return -EditorGroundDepth;
		Vector3 w = GlobalTransform * new Vector3(x, 0, z);
		return _terrain.HeightAt(w.X, w.Z) - GlobalPosition.Y;
	}

	private void GroundSelf()
	{
		_grounded = true;
		if (_terrain == null) return;
		var xf = GlobalTransform;
		if (!GroundSnap.SampleRect(_terrain, xf, new Vector2(-Hw - 0.2f, -Hd - 0.2f), new Vector2(Hw + 0.2f, Hd + 0.2f), 6, out var foot)) return;
		GroundSnap.SampleRect(_terrain, xf, new Vector2(-1.2f, FrontFace), new Vector2(1.2f, PorchEdge), 3, out var front);
		// the ground just past the porch edge, where the steps land: keep the deck a couple of steps above it
		GroundSnap.SampleRect(_terrain, xf, new Vector2(-0.6f, PorchEdge + 0.3f), new Vector2(0.6f, PorchEdge + 0.6f), 2, out var landing);
		float floor = Mathf.Max(Mathf.Max(foot.Avg, front.Avg) + SillClearance, Mathf.Max(foot.Max + 0.18f, landing.Max + StepsAboveLanding));
		GlobalPosition = GlobalPosition with { Y = floor };
	}

	/// <summary>Lowest local ground over a local rectangle (for foundation and skirt depth).</summary>
	private float LowestLocal(float x0, float z0, float x1, float z1)
	{
		if (_terrain == null || !IsInsideTree()) return -EditorGroundDepth;
		GroundSnap.SampleRect(_terrain, GlobalTransform, new Vector2(x0, z0), new Vector2(x1, z1), 5, out var s);
		return s.Min - GlobalPosition.Y;
	}

	private float StepTopAt(float z)
	{
		if (_steps <= 1 || z < PorchEdge) return z < PorchEdge ? 0f : float.MinValue;
		int k = Mathf.FloorToInt((z - PorchEdge) / StepRun) + 1;
		return k < _steps ? -k * _stepRise : float.MinValue;
	}

	// ------------------------------------------------------------------ build

	public void Build()
	{
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		_gen = new Node3D { Name = "Generated" };
		AddChild(_gen);
		_planks = _door = _windowBoards = _debris = _sashes = _panes = _papers = null;
		_sheet = null;
		_doorCollision = null;

		var shell = new MeshKit();
		var inner = new MeshKit();
		var cols = new List<(Vector3 c, Vector3 s, Basis b)>();

		BuildWalls(shell, cols);
		BuildRoof(shell);
		BuildChimney(shell, cols);
		BuildFoundationAndPorch(shell, cols);
		BuildWindowFrames(shell);
		BuildInterior(inner, cols);
		shell.CommitTo(_gen, "CabinMesh");
		inner.CommitTo(_gen, "InteriorMesh");
		BuildGlazing();
		BuildLamp();

		if (BuildCollision && !Engine.IsEditorHint())
		{
			var body = new StaticBody3D { Name = "CabinBody", CollisionLayer = 1, CollisionMask = 0 };
			body.SetMeta("surface", "wood");
			_gen.AddChild(body);
			foreach (var (c, s, b) in cols)
				body.AddChild(new CollisionShape3D { Position = c, Basis = b, Shape = new BoxShape3D { Size = s } });
			_doorCollision = new CollisionShape3D
			{
				Position = new Vector3(0, _doorTop * 0.5f, Hd),
				Shape = new BoxShape3D { Size = new Vector3(DoorWidth, _doorTop, LogT + 0.1f) },
				Disabled = IsOpen,
			};
			body.AddChild(_doorCollision);
		}

		BuildDoor();
		if (DoorBoarded && !IsOpen) BuildPlanks();
		if (IsOpen) BuildDebris();
		RefreshWindowBoards();
		BuildPapers();
		ApplyChar();
		UpdateLamp();
		UpdatePapers();
	}

	// ---- walls: 9 courses of logs, crossed ends alternating at the corners

	private void BuildWalls(MeshKit k, List<(Vector3, Vector3, Basis)> cols)
	{
		var log = BuildingTextures.LogMat;
		var end = BuildingTextures.LogEndMat;
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 977 + 5) };
		float uLen = LogH * 4f;
		var doorCut = (a0: -Dw, a1: Dw, y0: 0f, y1: DoorHeight);
		var winL = (a0: -WinCx - WinW * 0.5f, a1: -WinCx + WinW * 0.5f, y0: 0.9f, y1: 1.65f);
		var winR = (a0: WinCx - WinW * 0.5f, a1: WinCx + WinW * 0.5f, y0: 0.9f, y1: 1.65f);
		var frontCuts = new[] { doorCut, winL, winR };
		float doorTop = 0, winBot = 99, winTop = 0;

		for (int c = 0; c < Courses; c++)
		{
			float y0 = FloorBottom + c * LogH, y1 = y0 + LogH;
			bool frontLong = c % 2 == 0;
			float shade = rng.RandfRange(0.82f, 1.06f);
			float jit = rng.RandfRange(-0.012f, 0.012f);

			// front and back (along X)
			float xa = frontLong ? -Hw - Ext : -Hw + LogT * 0.5f, xb = -xa;
			foreach (int side in new[] { 1, -1 })
			{
				float z = side * Hd + jit;
				var pieces = new List<(float a, float b, bool ea, bool eb)> { (xa, xb, true, true) };
				if (side == 1)
					foreach (var cut in frontCuts)
					{
						if (!(y1 > cut.y0 + 0.05f && y0 < cut.y1 - 0.05f)) continue;
						if (cut.a0 == doorCut.a0) doorTop = Mathf.Max(doorTop, y1);
						else { winBot = Mathf.Min(winBot, y0); winTop = Mathf.Max(winTop, y1); }
						var next = new List<(float, float, bool, bool)>();
						foreach (var (a, b, ea, eb) in pieces)
						{
							if (cut.a1 <= a || cut.a0 >= b) { next.Add((a, b, ea, eb)); continue; }
							if (cut.a0 > a + 0.02f) next.Add((a, cut.a0, ea, true));
							if (cut.a1 < b - 0.02f) next.Add((cut.a1, b, true, eb));
						}
						pieces = next;
					}
				foreach (var (a, b, ea, eb) in pieces)
				{
					k.Color = new Color(shade, shade * 0.98f, shade * 0.95f);
					BuildKit.Log(k, log, end, false, a, b, z, y0, y1, LogT, uLen, ea, eb);
				}
			}
			// the two sides (along Z)
			float za = frontLong ? -Hd + LogT * 0.5f : -Hd - Ext, zb = -za;
			foreach (int side in new[] { 1, -1 })
			{
				float s2 = rng.RandfRange(0.84f, 1.05f);
				k.Color = new Color(s2, s2 * 0.98f, s2 * 0.95f);
				BuildKit.Log(k, log, end, true, za, zb, side * Hw + jit * 0.5f, y0, y1, LogT, uLen);
			}
		}
		_doorTop = doorTop > 0 ? doorTop : DoorHeight;
		_winBot = winBot; _winTop = winTop;
		k.Color = Colors.White;

		// gables on the two side walls (vertical boards), under the roof line
		k.Mat(BuildingTextures.BoardsMat);
		k.Color = new Color(0.85f, 0.8f, 0.74f);
		foreach (int side in new[] { -1, 1 })
		{
			float x = side * Hw;
			BuildKit.TriPanel(k, new Vector3(x, WallHeight, -FrontFace), new Vector3(x, WallHeight, FrontFace), new Vector3(x, RidgeY - 0.04f, 0),
				new Vector3(side, 0, 0), Vector3.Back, 1.1f, LogT * 0.3f);
		}
		k.Color = Colors.White;

		// collision: solid walls (windows are boarded), door opening handled separately
		float hh = WallHeight - FloorBottom, cy = (WallHeight + FloorBottom) * 0.5f;
		cols.Add((new Vector3(-Hw, cy, 0), new Vector3(LogT + 0.08f, hh, Depth + LogT), Basis.Identity));
		cols.Add((new Vector3(Hw, cy, 0), new Vector3(LogT + 0.08f, hh, Depth + LogT), Basis.Identity));
		cols.Add((new Vector3(0, cy, -Hd), new Vector3(Width + LogT, hh, LogT + 0.08f), Basis.Identity));
		float sideW = Hw + LogT * 0.5f - Dw;
		cols.Add((new Vector3(-(Dw + sideW * 0.5f), cy, Hd), new Vector3(sideW, hh, LogT + 0.08f), Basis.Identity));
		cols.Add((new Vector3(Dw + sideW * 0.5f, cy, Hd), new Vector3(sideW, hh, LogT + 0.08f), Basis.Identity));
		float headH = WallHeight - _doorTop;
		if (headH > 0.02f) cols.Add((new Vector3(0, _doorTop + headH * 0.5f, Hd), new Vector3(DoorWidth, headH, LogT + 0.08f), Basis.Identity));
		// gable/roof mass so nothing can be thrown or walk through the roof space
		cols.Add((new Vector3(0, (WallHeight + RidgeY) * 0.5f, 0), new Vector3(Width, RidgeY - WallHeight, Depth * 0.6f), Basis.Identity));
	}

	// ---- roof: two shingled slabs split in three bays (the middle front bay falls in when burnt)

	private void BuildRoof(MeshKit k)
	{
		var shingle = BuildingTextures.ShingleMat;
		var boards = BuildingTextures.BoardsMat;
		var trim = PropTextures.PostMat;
		float rake = Hw + LogT * 0.5f + 0.3f;
		float cos = 1f / Mathf.Sqrt(1f + Pitch * Pitch), sin = Pitch * cos;
		const float thick = 0.12f;
		float[] bays = { -rake, -0.55f, 0.95f, rake };

		foreach (int s in new[] { 1, -1 })
		{
			float over = s > 0 ? 0.18f : 0.45f;
			float zE = s * (FrontFace + over), yE = RoofBottomAt(FrontFace + over);
			Vector3 n = new(0, cos, s * sin);
			Vector3 lift = n * thick;
			float slopeLen = Mathf.Sqrt(zE * zE + (RidgeY - yE) * (RidgeY - yE));
			for (int b = 0; b < 3; b++)
			{
				bool fallen = IsBurnt && ((s > 0 && b == 1) || (s < 0 && b == 2));
				float x0 = bays[b], x1 = bays[b + 1];
				float zStart = 0f, yStart = RidgeY;
				if (fallen)
				{
					// only a ragged strip survives at the eave; the rest lies inside (see Collapse)
					zStart = zE * 0.62f; yStart = Mathf.Lerp(RidgeY, yE, 0.62f);
				}
				Vector3 r0 = new(x0, yStart, zStart), r1 = new(x1, yStart, zStart), e0 = new(x0, yE, zE), e1 = new(x1, yE, zE);
				float v0 = fallen ? slopeLen * 0.62f : 0f;
				k.Color = new Color(0.9f, 0.9f, 0.88f);
				k.Mat(shingle);
				k.Quad(r0 + lift, r1 + lift, e1 + lift, e0 + lift, n, new Vector2(x0 * 0.9f, v0 * 0.9f), new Vector2(x1 * 0.9f, v0 * 0.9f), new Vector2(x1 * 0.9f, slopeLen * 0.9f), new Vector2(x0 * 0.9f, slopeLen * 0.9f));
				k.Color = new Color(0.62f, 0.56f, 0.5f);
				k.Mat(boards);
				k.Quad(r0, r1, e1, e0, -n, new Vector2(x0, v0), new Vector2(x1, v0), new Vector2(x1, slopeLen), new Vector2(x0, slopeLen));
				if (fallen)
				{
					// broken top edge of the surviving strip
					k.Mat(BuildingTextures.LogEndMat);
					k.Quad(r0, r1, r1 + lift, r0 + lift, new Vector3(0, sin, -s * cos), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 0.2f), new Vector2(0, 0.2f));
				}
			}
			k.Color = new Color(0.7f, 0.64f, 0.56f);
			k.Mat(trim);
			// fascia along the eave
			var fasciaC = new Vector3(0, yE + thick * 0.5f - 0.04f, zE + s * 0.02f);
			BuildKit.Box(k, fasciaC, new Vector3(rake * 2f + 0.06f, 0.22f, 0.04f), 1.4f);
			// rake (barge) boards on both gable ends
			foreach (int g in new[] { -1, 1 })
			{
				Vector3 a = new(g * (rake + 0.02f), RidgeY + thick * 0.4f, 0), c = new(g * (rake + 0.02f), yE + thick * 0.4f, zE + s * 0.03f);
				if (IsBurnt && g > 0 && s > 0) c = a.Lerp(c, 0.55f);
				k.Beam(a, c, 0.04f, 0.24f, 1.4f, new Vector3(0, cos, s * sin));
			}
			// the end faces (thickness) of the slabs at the rakes
			k.Mat(boards);
			foreach (int g in new[] { -1, 1 })
			{
				float x = g * rake;
				k.Quad(new Vector3(x, RidgeY, 0), new Vector3(x, yE, zE), new Vector3(x, yE, zE) + lift, new Vector3(x, RidgeY, 0) + lift, new Vector3(g, 0, 0));
			}
		}
		// ridge cap: a square beam turned 45 degrees, riding the ridge
		k.Color = new Color(0.55f, 0.52f, 0.48f);
		k.Mat(BuildingTextures.ShingleMat);
		float rx = IsBurnt ? 0.6f : 0f;
		k.Beam(new Vector3(-Hw - LogT * 0.5f - 0.32f, RidgeY + 0.12f, 0), new Vector3(Hw + LogT * 0.5f + 0.32f - rx, RidgeY + 0.12f, 0), 0.2f, 0.2f, 1.2f, new Vector3(0, 1, 1).Normalized());
		k.Color = Colors.White;
	}

	// ---- chimney: fieldstone base on the left gable, a shoulder, a tall stack through the eave

	private void BuildChimney(MeshKit k, List<(Vector3, Vector3, Basis)> cols)
	{
		var stone = BuildingTextures.StoneMat;
		float cx = -Hw - LogT * 0.5f - 0.38f, cz = -1.0f;
		float g = LowestLocal(cx - 0.4f, cz - 0.55f, cx + 0.4f, cz + 0.55f) - 0.25f;
		k.Mat(stone);
		k.Color = new Color(0.95f, 0.93f, 0.9f);
		float baseTop = 1.45f;
		BuildKit.Box(k, new Vector3(cx, (g + baseTop) * 0.5f, cz), new Vector3(0.76f, baseTop - g, 1.05f), 1.1f, BuildKit.Face.NY);
		// sloped shoulder
		float sh0 = baseTop, sh1 = baseTop + 0.45f;
		var zy = new List<Vector2> { new(-0.525f, sh0), new(0.525f, sh0), new(0.3f, sh1), new(-0.3f, sh1) };
		var xf = k.Xf;
		k.Xf = new Transform3D(Basis.Identity, new Vector3(0, 0, cz));
		k.ExtrudeX(zy, cx - 0.38f, cx + 0.38f, 1.1f);
		k.Xf = xf;
		float top = RidgeY + 0.55f;
		BuildKit.Box(k, new Vector3(cx + 0.04f, (sh1 + top) * 0.5f, cz), new Vector3(0.56f, top - sh1, 0.6f), 1.1f, BuildKit.Face.NY | BuildKit.Face.PY);
		// crown: a slightly wider mortar cap and the dark flue
		k.Color = new Color(0.6f, 0.58f, 0.55f);
		k.Mat(ProcTextures.ConcreteMat);
		BuildKit.Box(k, new Vector3(cx + 0.04f, top + 0.04f, cz), new Vector3(0.66f, 0.08f, 0.7f), 1.5f, BuildKit.Face.NY);
		k.Color = new Color(0.03f, 0.03f, 0.03f);
		k.Quad(new Vector3(cx - 0.12f, top + 0.085f, cz - 0.14f), new Vector3(cx + 0.2f, top + 0.085f, cz - 0.14f), new Vector3(cx + 0.2f, top + 0.085f, cz + 0.14f), new Vector3(cx - 0.12f, top + 0.085f, cz + 0.14f), Vector3.Up);
		k.Color = Colors.White;
		cols.Add((new Vector3(cx, (g + top) * 0.5f, cz), new Vector3(0.76f, top - g, 1.05f), Basis.Identity));
	}

	// ---- stone foundation skirt, porch deck, posts, rails, porch roof, bench, steps

	private void BuildFoundationAndPorch(MeshKit k, List<(Vector3, Vector3, Basis)> cols)
	{
		var stone = BuildingTextures.StoneMat;
		var deck = PropTextures.DeckMat;
		var post = PropTextures.PostMat;
		var boards = BuildingTextures.BoardsMat;
		float o = Hw + LogT * 0.5f, od = Hd + LogT * 0.5f;

		// stone foundation walls under each log wall, each down to its own lowest ground
		k.Mat(stone);
		k.Color = new Color(0.92f, 0.9f, 0.88f);
		void Found(float x0, float z0, float x1, float z1)
		{
			float g = LowestLocal(x0, z0, x1, z1) - 0.3f;
			if (g > FloorBottom - 0.05f) g = FloorBottom - 0.05f;
			var c = new Vector3((x0 + x1) * 0.5f, (g + FloorBottom) * 0.5f, (z0 + z1) * 0.5f);
			var s = new Vector3(x1 - x0, FloorBottom - g, z1 - z0);
			BuildKit.Box(k, c, s, 1.1f, BuildKit.Face.NY | BuildKit.Face.PY);
			cols.Add((c, s, Basis.Identity));
		}
		const float ft = 0.3f;
		Found(-o - 0.04f, -od - 0.04f, o + 0.04f, -od + ft);          // back
		Found(-o - 0.04f, od - ft, o + 0.04f, od + 0.04f);            // front (under the porch)
		Found(-o - 0.04f, -od + ft, -o + ft, od - ft);                // left
		Found(o - ft, -od + ft, o + 0.04f, od - ft);                  // right
		k.Color = Colors.White;
		// interior floor/foundation mass for collision (floor top at y = 0)
		float gMin = LowestLocal(-o, -od, o, od) - 0.3f;
		cols.Add((new Vector3(0, (gMin + 0f) * 0.5f, 0), new Vector3(Width, -gMin, Depth), Basis.Identity));

		// porch deck (boards run along X)
		float px = o + 0.22f, z0 = FrontFace, z1 = PorchEdge;
		k.Mat(deck);
		k.Color = new Color(0.95f, 0.92f, 0.88f);
		BuildKit.Box(k, new Vector3(0, -0.04f, (z0 + z1) * 0.5f), new Vector3(px * 2f, 0.08f, z1 - z0), 1f / 0.15f, BuildKit.Face.NY | BuildKit.Face.NZ);
		// rim boards around the deck edge
		k.Mat(post);
		k.Color = new Color(0.75f, 0.7f, 0.64f);
		BuildKit.Box(k, new Vector3(0, -0.17f, z1 + 0.02f), new Vector3(px * 2f + 0.04f, 0.26f, 0.04f), 1.4f);
		foreach (int sx in new[] { -1, 1 })
			BuildKit.Box(k, new Vector3(sx * (px + 0.02f), -0.17f, (z0 + z1) * 0.5f), new Vector3(0.04f, 0.26f, z1 - z0), 1.4f);
		float gPorch = LowestLocal(-px, z0, px, z1 + 0.2f);
		cols.Add((new Vector3(0, (gPorch - 0.3f) * 0.5f, (z0 + z1) * 0.5f), new Vector3(px * 2f, -(gPorch - 0.3f), z1 - z0), Basis.Identity));

		// skirt boards from under the rim down into the ground (front, beside the steps, and the two sides)
		k.Mat(boards);
		k.Color = new Color(0.45f, 0.41f, 0.37f);
		void Skirt(Vector3 a, Vector3 b, bool alongZ)
		{
			float g = Mathf.Min(LowestLocal(Mathf.Min(a.X, b.X) - 0.05f, Mathf.Min(a.Z, b.Z) - 0.05f, Mathf.Max(a.X, b.X) + 0.05f, Mathf.Max(a.Z, b.Z) + 0.05f), -0.35f) - 0.2f;
			float top = -0.29f;
			if (top - g < 0.02f) return;
			var c = new Vector3((a.X + b.X) * 0.5f, (top + g) * 0.5f, (a.Z + b.Z) * 0.5f);
			var s = alongZ ? new Vector3(0.03f, top - g, Mathf.Abs(b.Z - a.Z)) : new Vector3(Mathf.Abs(b.X - a.X), top - g, 0.03f);
			BuildKit.Box(k, c, s, 1.1f, BuildKit.Face.NY | BuildKit.Face.PY);
		}
		float sw = StepW * 0.5f;
		Skirt(new Vector3(-px, 0, z1), new Vector3(-sw, 0, z1), false);
		Skirt(new Vector3(sw, 0, z1), new Vector3(px, 0, z1), false);
		Skirt(new Vector3(-px, 0, z0), new Vector3(-px, 0, z1), true);
		Skirt(new Vector3(px, 0, z0), new Vector3(px, 0, z1), true);

		// posts (on the deck up to the porch roof header, and on down to the ground under the deck)
		float postZ = z1 - 0.1f;
		float roofAtPost = PorchRoofBottom(postZ);
		float[] postX = { -px + 0.08f, -sw - 0.08f, sw + 0.08f, px - 0.08f };
		k.Mat(post);
		k.Color = new Color(0.8f, 0.74f, 0.66f);
		foreach (float x in postX)
		{
			float g = Mathf.Min(GroundLocal(x, postZ), -0.3f) - 0.15f;
			bool broken = IsBurnt && x > 0 && x < px - 0.1f;
			float topY = broken ? 1.1f : roofAtPost - 0.18f;
			BuildKit.Box(k, new Vector3(x, (g + topY) * 0.5f, postZ), new Vector3(0.14f, topY - g, 0.14f), 1.3f);
			cols.Add((new Vector3(x, (0.02f + topY) * 0.5f, postZ), new Vector3(0.16f, topY, 0.16f), Basis.Identity));
		}
		// header beam over the posts
		BuildKit.Box(k, new Vector3(0, roofAtPost - 0.09f, postZ), new Vector3(px * 2f, 0.18f, 0.14f), 1.3f);

		// rails: front runs (either side of the steps) and the two ends
		void Rail(Vector3 a, Vector3 b)
		{
			Vector3 d = b - a;
			float len = d.Length();
			bool alongZ = Mathf.Abs(d.Z) > Mathf.Abs(d.X);
			k.Color = new Color(0.78f, 0.72f, 0.64f);
			k.Beam(a + Vector3.Up * 0.92f, b + Vector3.Up * 0.92f, 0.07f, 0.06f, 1.3f);
			k.Beam(a + Vector3.Up * 0.12f, b + Vector3.Up * 0.12f, 0.05f, 0.06f, 1.3f);
			int n = Mathf.Max(1, Mathf.RoundToInt(len / 0.3f) - 1);
			for (int i = 1; i <= n; i++)
			{
				Vector3 p = a.Lerp(b, i / (float)(n + 1));
				BuildKit.Box(k, new Vector3(p.X, 0.52f, p.Z), new Vector3(0.045f, 0.78f, 0.045f), 1.3f, BuildKit.Face.NY | BuildKit.Face.PY);
			}
			Vector3 mid = (a + b) * 0.5f;
			cols.Add((new Vector3(mid.X, 0.5f, mid.Z), alongZ ? new Vector3(0.1f, 1.0f, len) : new Vector3(len, 1.0f, 0.1f), Basis.Identity));
		}
		k.Mat(post);
		Rail(new Vector3(-px + 0.08f, 0, postZ), new Vector3(-sw - 0.08f, 0, postZ));
		if (!IsBurnt) Rail(new Vector3(sw + 0.08f, 0, postZ), new Vector3(px - 0.08f, 0, postZ));
		Rail(new Vector3(-px + 0.08f, 0, z0 + 0.05f), new Vector3(-px + 0.08f, 0, postZ));
		Rail(new Vector3(px - 0.08f, 0, z0 + 0.05f), new Vector3(px - 0.08f, 0, postZ));

		// porch roof: a shallow lean-to tucked under the main eave
		BuildPorchRoof(k, px);

		// bench against the front wall, right of the door (the compass waits on it)
		k.Mat(deck);
		k.Color = new Color(0.9f, 0.85f, 0.8f);
		float bz = FrontFace + 0.26f, bx0 = 0.72f, bx1 = 1.82f;
		BuildKit.Box(k, new Vector3((bx0 + bx1) * 0.5f, BenchTop - 0.025f, bz), new Vector3(bx1 - bx0, 0.05f, 0.34f), 1f / 0.17f);
		k.Mat(post);
		foreach (float x in new[] { bx0 + 0.08f, bx1 - 0.08f })
			BuildKit.Box(k, new Vector3(x, (BenchTop - 0.05f) * 0.5f, bz), new Vector3(0.05f, BenchTop - 0.05f, 0.3f), 1.3f, BuildKit.Face.NY);
		cols.Add((new Vector3((bx0 + bx1) * 0.5f, BenchTop * 0.5f, bz), new Vector3(bx1 - bx0, BenchTop, 0.34f), Basis.Identity));

		BuildSteps(k, cols);
		k.Color = Colors.White;
	}

	private float PorchRoofTopAtWall => EaveY - 0.01f;
	private float PorchRoofBottom(float z) => PorchRoofTopAtWall - 0.08f - (z - FrontFace) * 0.1f;

	private void BuildPorchRoof(MeshKit k, float px)
	{
		float z0 = FrontFace - 0.02f, z1 = PorchEdge + 0.3f;
		float xr = px + 0.18f;
		float yb0 = PorchRoofBottom(z0), yb1 = PorchRoofBottom(z1);
		Vector3 n = new Vector3(0, 1, 0.1f).Normalized();
		Vector3 lift = n * 0.08f;
		float len = new Vector2(z1 - z0, yb1 - yb0).Length();
		float[] bays = { -xr, 0.35f, xr };
		for (int b = 0; b < 2; b++)
		{
			float x0 = bays[b], x1 = bays[b + 1];
			// burnt: the right half has dropped at its outer corner where the post burned through
			float drop = IsBurnt && b == 1 ? 0.75f : 0f;
			Vector3 a0 = new(x0, yb0, z0), a1 = new(x1, yb0, z0), c1 = new(x1, yb1 - drop, z1), c0 = new(x0, yb1 - (drop > 0 ? 0.15f : 0f), z1);
			k.Color = new Color(0.9f, 0.9f, 0.88f);
			k.Mat(BuildingTextures.ShingleMat);
			k.Quad(a0 + lift, a1 + lift, c1 + lift, c0 + lift, n, new Vector2(x0 * 0.9f, 0), new Vector2(x1 * 0.9f, 0), new Vector2(x1 * 0.9f, len * 0.9f), new Vector2(x0 * 0.9f, len * 0.9f));
			k.Color = new Color(0.62f, 0.56f, 0.5f);
			k.Mat(BuildingTextures.BoardsMat);
			k.Quad(a0, a1, c1, c0, -n, new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(x1, len), new Vector2(x0, len));
			k.Mat(PropTextures.PostMat);
			k.Color = new Color(0.7f, 0.64f, 0.56f);
			k.Beam(c0 + lift * 0.5f + new Vector3(0, -0.03f, 0.02f), c1 + lift * 0.5f + new Vector3(0, -0.03f, 0.02f), 0.04f, 0.16f, 1.4f);
		}
		// open rafters under the porch roof
		k.Mat(PropTextures.PostMat);
		k.Color = new Color(0.66f, 0.6f, 0.52f);
		for (float x = -xr + 0.25f; x < xr - 0.1f; x += 0.9f)
		{
			if (IsBurnt && x > 0.35f) continue;
			k.Beam(new Vector3(x, yb0 - 0.06f, z0 + 0.05f), new Vector3(x, yb1 - 0.06f, z1 - 0.05f), 0.06f, 0.11f, 1.4f);
		}
		k.Color = Colors.White;
	}

	private void BuildSteps(MeshKit k, List<(Vector3, Vector3, Basis)> cols)
	{
		float ze = PorchEdge;
		// how far down to the ground in front of the porch; iterate since the steps reach further out on a slope
		int n = 1;
		float drop = 0;
		// Measure where the flight will roughly end; on falling ground walk that point outward as the flight grows.
		for (int it = 0; it < 4; it++)
		{
			float d = -GroundLocal(0, ze + Mathf.Max(0.45f, (n - 1) * StepRun + 0.15f));
			if (it > 0 && d < drop) break;   // ground rises further out: keep the nearer, deeper measurement
			drop = d;
			n = Mathf.Clamp(Mathf.RoundToInt(drop / 0.18f), 1, 10);
		}
		_steps = n;
		_stepRise = drop / n;
		if (drop < 0.12f) { _steps = 0; return; }
		var tread = PropTextures.DeckMat;
		var post = PropTextures.PostMat;
		float sw = StepW * 0.5f;
		for (int i = 1; i < n; i++)
		{
			float top = -i * _stepRise;
			float za = ze + (i - 1) * StepRun, zb = ze + i * StepRun;
			k.Mat(tread);
			k.Color = new Color(0.95f, 0.92f, 0.88f);
			BuildKit.Box(k, new Vector3(0, top - 0.025f, (za + zb) * 0.5f + 0.015f), new Vector3(StepW - 0.1f, 0.05f, StepRun + 0.03f), 1f / 0.15f);
			k.Mat(post);
			k.Color = new Color(0.55f, 0.5f, 0.45f);
			// riser under the front edge of the tread above
			float riserTop = top + _stepRise - 0.05f;
			BuildKit.Box(k, new Vector3(0, (top + riserTop) * 0.5f, za + 0.02f), new Vector3(StepW - 0.14f, riserTop - top + 0.02f, 0.03f), 1.3f, BuildKit.Face.NY | BuildKit.Face.PY);
		}
		// last riser down onto the landing
		// stringers
		float zEnd = ze + (n - 1) * StepRun + 0.25f;
		float gEnd = GroundLocal(0, zEnd);
		k.Mat(post);
		k.Color = new Color(0.7f, 0.64f, 0.56f);
		foreach (int s in new[] { -1, 1 })
			k.Beam(new Vector3(s * (sw - 0.03f), -0.12f, ze - 0.02f), new Vector3(s * (sw - 0.03f), gEnd - 0.05f, zEnd), 0.05f, 0.24f, 1.3f);
		// flat stone landing at the foot
		k.Mat(BuildingTextures.StoneMat);
		k.Color = new Color(0.8f, 0.78f, 0.75f);
		float zl = ze + (n - 1) * StepRun + 0.32f;
		BuildKit.Box(k, new Vector3(0, GroundLocal(0, zl) - 0.02f, zl), new Vector3(StepW + 0.3f, 0.14f, 0.55f), 1.2f, BuildKit.Face.NY);
		// collision: one ramp from the porch edge to the landing (stairs are hard on a capsule)
		Vector3 from = new(0, 0, ze), to = new(0, gEnd, zEnd);
		Vector3 dir = to - from;
		float len = dir.Length();
		Vector3 zAxis = dir / len;
		Vector3 xAxis = Vector3.Right;
		Vector3 yAxis = zAxis.Cross(xAxis).Normalized();
		if (yAxis.Y < 0) yAxis = -yAxis;
		var basis = new Basis(xAxis, yAxis, zAxis);
		cols.Add(((from + to) * 0.5f - yAxis * 0.1f, new Vector3(StepW, 0.2f, len + 0.05f), basis));
	}

	// ---- windows: casing, jamb lining, sash with muntins (panes are built separately)

	private void BuildWindowFrames(MeshKit k)
	{
		var trim = PropTextures.PostMat;
		k.Mat(trim);
		k.Color = new Color(0.6f, 0.55f, 0.5f);
		float zo = FrontFace + 0.015f;
		foreach (float cx in new[] { -WinCx, WinCx })
		{
			float x0 = cx - WinW * 0.5f, x1 = cx + WinW * 0.5f;
			// outer casing
			BuildKit.Box(k, new Vector3(cx, _winTop + 0.05f, zo), new Vector3(WinW + 0.2f, 0.1f, 0.03f), 1.4f);
			BuildKit.Box(k, new Vector3(cx, _winBot - 0.03f, zo + 0.02f), new Vector3(WinW + 0.26f, 0.06f, 0.08f), 1.4f);
			foreach (float x in new[] { x0 - 0.045f, x1 + 0.045f })
				BuildKit.Box(k, new Vector3(x, (_winBot + _winTop) * 0.5f, zo), new Vector3(0.09f, _winTop - _winBot, 0.03f), 1.4f);
			// lining boards in the opening (hide the cut log ends)
			BuildKit.Box(k, new Vector3(cx, _winTop - 0.01f, Hd), new Vector3(WinW, 0.02f, LogT), 1.4f);
			BuildKit.Box(k, new Vector3(cx, _winBot + 0.01f, Hd), new Vector3(WinW, 0.02f, LogT), 1.4f);
			foreach (float x in new[] { x0 + 0.01f, x1 - 0.01f })
				BuildKit.Box(k, new Vector3(x, (_winBot + _winTop) * 0.5f, Hd), new Vector3(0.02f, _winTop - _winBot, LogT), 1.4f);
		}
		// door casing: jambs and head on the outside, lining in the opening
		k.Color = new Color(0.62f, 0.57f, 0.52f);
		BuildKit.Box(k, new Vector3(0, _doorTop + 0.06f, zo), new Vector3(DoorWidth + 0.26f, 0.12f, 0.035f), 1.4f);
		foreach (int s in new[] { -1, 1 })
		{
			BuildKit.Box(k, new Vector3(s * (Dw + 0.055f), _doorTop * 0.5f, zo), new Vector3(0.11f, _doorTop, 0.035f), 1.4f);
			BuildKit.Box(k, new Vector3(s * (Dw - 0.012f), _doorTop * 0.5f, Hd), new Vector3(0.025f, _doorTop, LogT), 1.4f);
		}
		BuildKit.Box(k, new Vector3(0, _doorTop - 0.012f, Hd), new Vector3(DoorWidth, 0.025f, LogT), 1.4f);
		// threshold
		k.Mat(PropTextures.DeckMat);
		BuildKit.Box(k, new Vector3(0, -0.01f, Hd), new Vector3(DoorWidth, 0.03f, LogT + 0.06f), 5f);
		k.Color = Colors.White;
	}

	// ---- glazing: the sashes with their muntins and the panes, small nodes of their own so
	// ignition frees just these (and the window boards) instead of rebuilding the building

	private void BuildGlazing()
	{
		if (_sashes != null && IsInstanceValid(_sashes)) _sashes.QueueFree();
		if (_panes != null && IsInstanceValid(_panes)) _panes.QueueFree();
		_sashes = _panes = null;
		_litPane = null;
		if (Burning > 0f || IsBurnt) return;   // sash burned/blown out, glass gone, so smoke can pour out
		var ks = new MeshKit();
		ks.Mat(PropTextures.PostMat);
		ks.Color = new Color(0.42f, 0.38f, 0.34f);
		float zs = Hd + 0.02f;
		foreach (float cx in new[] { -WinCx, WinCx })
		{
			// sash frame and muntins, set a little back from the outer face
			BuildKit.Box(ks, new Vector3(cx, (_winBot + _winTop) * 0.5f, zs), new Vector3(0.035f, _winTop - _winBot - 0.04f, 0.04f), 2f);
			BuildKit.Box(ks, new Vector3(cx, (_winBot + _winTop) * 0.5f, zs), new Vector3(WinW - 0.04f, 0.035f, 0.04f), 2f);
		}
		_sashes = new Node3D { Name = "WindowSashes" };
		_gen.AddChild(_sashes);
		ks.CommitTo(_sashes, "SashMesh");

		_panes = new Node3D { Name = "Panes" };
		_gen.AddChild(_panes);
		foreach (float cx in new[] { -WinCx, WinCx })
		{
			bool lit = cx < 0;
			var k = new MeshKit();
			k.Mat(lit ? BuildingTextures.LitGlassMat : BuildingTextures.GlassMat);
			float x0 = cx - WinW * 0.5f + 0.02f, x1 = cx + WinW * 0.5f - 0.02f, y0 = _winBot + 0.02f, y1 = _winTop - 0.02f, z = Hd + 0.005f;
			// outside face: lamplit (seen through the boards) or dark; the inside face is always dark glass
			k.Quad(new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z), Vector3.Back);
			var mi = k.CommitTo(_panes, lit ? "LitPane" : "Pane", false);
			if (lit) _litPane = mi;
			var ki = new MeshKit();
			ki.Mat(BuildingTextures.GlassMat);
			ki.Quad(new Vector3(x0, y0, z - 0.004f), new Vector3(x1, y0, z - 0.004f), new Vector3(x1, y1, z - 0.004f), new Vector3(x0, y1, z - 0.004f), Vector3.Forward);
			ki.CommitTo(_panes, "PaneInside", false);
		}
	}

	// ---- interior: plank floor, rafters/purlin, stove + pipe, woodpile, cot, shelf, crate, rug

	private void BuildInterior(MeshKit k, List<(Vector3, Vector3, Basis)> cols)
	{
		float ix = Hw - LogT * 0.5f, iz = Hd - LogT * 0.5f;
		// floor: planks run front to back
		k.Mat(BuildingTextures.FloorMat);
		k.Color = new Color(0.95f, 0.9f, 0.85f);
		BuildKit.Box(k, new Vector3(0, -0.03f, 0), new Vector3(iz * 2f, 0.06f, ix * 2f), 1f / 0.6f, BuildKit.Face.NY, new Basis(Vector3.Up, Mathf.Pi * 0.5f));

		// rafters under both slopes, a ridge beam and a purlin (the lamp hangs from it)
		var beam = PropTextures.PostMat;
		k.Mat(beam);
		k.Color = new Color(0.62f, 0.55f, 0.47f);
		float cos = 1f / Mathf.Sqrt(1f + Pitch * Pitch);
		for (float x = -ix + 0.3f; x <= ix - 0.2f; x += 0.75f)
			foreach (int s in new[] { -1, 1 })
			{
				if (IsBurnt && ((s > 0 && x > -0.55f && x < 0.95f) || (s < 0 && x > 0.95f))) continue;
				float drop = 0.08f / cos;
				Vector3 a = new(x, RoofBottomAt(iz) - drop, s * iz), b = new(x, RidgeY - drop - 0.02f, 0);
				k.Beam(a, b, 0.08f, 0.15f, 1.4f, new Vector3(0, 1, s * Pitch));
			}
		k.Beam(new Vector3(-ix, RidgeY - 0.14f, 0), new Vector3(IsBurnt ? 0.2f : ix, RidgeY - 0.14f - (IsBurnt ? 0.5f : 0f), 0), 0.14f, 0.2f, 1.4f);
		float pz = -0.75f;
		float py = RoofBottomAt(pz) - 0.26f;
		k.Beam(new Vector3(-ix, py, pz), new Vector3(ix, py, pz), 0.12f, 0.16f, 1.4f);
		// tie beams across at wall-plate height
		foreach (float x in new[] { -1.2f, 1.2f })
			k.Beam(new Vector3(x, WallHeight - 0.08f, -iz), new Vector3(x, WallHeight - 0.08f, iz), 0.12f, 0.16f, 1.4f);

		// stove (left wall, in front of the chimney) with its pipe into the wall
		var iron = BuildingTextures.IronMat;
		k.Mat(iron);
		k.Color = new Color(0.35f, 0.33f, 0.32f);
		float sx = -ix + 0.42f, sz = -1.0f;
		BuildKit.Box(k, new Vector3(sx, 0.47f, sz), new Vector3(0.5f, 0.52f, 0.62f), 2f, BuildKit.Face.NY);
		BuildKit.Box(k, new Vector3(sx, 0.745f, sz), new Vector3(0.56f, 0.03f, 0.68f), 2f);
		foreach (var (lx, lz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
			BuildKit.Box(k, new Vector3(sx + lx * 0.2f, 0.105f, sz + lz * 0.25f), new Vector3(0.05f, 0.21f, 0.05f), 2f, BuildKit.Face.NY | BuildKit.Face.PY);
		k.Cylinder(new Vector3(sx - 0.05f, 0.76f, sz), new Vector3(sx - 0.05f, 1.75f, sz), 0.065f, 0.065f, 6, false);
		k.Cylinder(new Vector3(sx - 0.05f, 1.75f, sz), new Vector3(-ix - 0.05f, 1.75f, sz), 0.065f, 0.065f, 6, false);
		// stove door (faces into the room)
		k.Color = new Color(0.22f, 0.21f, 0.2f);
		BuildKit.Box(k, new Vector3(sx + 0.255f, 0.46f, sz), new Vector3(0.02f, 0.26f, 0.3f), 2f);
		cols.Add((new Vector3(sx, 0.4f, sz), new Vector3(0.56f, 0.8f, 0.68f), Basis.Identity));

		// woodpile beside the stove
		k.Mat(BuildingTextures.LogMat);
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 31 + 3) };
		for (int i = 0; i < 6; i++)
		{
			int row = i < 3 ? 0 : (i < 5 ? 1 : 2);
			float lx = sx - 0.05f + (i % 3 - 1) * 0.14f + row * 0.07f;
			if (row == 1) lx = sx - 0.05f + (i - 3.5f) * 0.14f;
			if (row == 2) lx = sx - 0.05f;
			float ly = 0.06f + row * 0.11f;
			float lz = sz + 0.72f + rng.RandfRange(-0.04f, 0.04f);
			float sh = rng.RandfRange(0.8f, 1.05f);
			k.Color = new Color(sh, sh * 0.95f, sh * 0.9f);
			k.Cylinder(new Vector3(lx, ly, lz - 0.2f), new Vector3(lx, ly, lz + 0.2f), 0.06f, 0.06f, 5, true, 2.5f, rng.Randf());
		}

		// cot along the right wall
		var wood = PropTextures.PostMat;
		float cx = ix - 0.42f, cz0 = -iz + 0.1f, cz1 = cz0 + 1.9f, cw = 0.36f;
		k.Mat(wood);
		k.Color = new Color(0.75f, 0.68f, 0.6f);
		foreach (int s in new[] { -1, 1 })
			BuildKit.Box(k, new Vector3(cx + s * cw, 0.34f, (cz0 + cz1) * 0.5f), new Vector3(0.05f, 0.08f, cz1 - cz0), 1.4f);
		foreach (var (lx, lz) in new[] { (-1, 0), (1, 0), (-1, 1), (1, 1) })
			BuildKit.Box(k, new Vector3(cx + lx * cw, 0.19f, lz == 0 ? cz0 + 0.04f : cz1 - 0.04f), new Vector3(0.05f, 0.38f, 0.05f), 1.4f, BuildKit.Face.NY);
		k.Mat(BuildingTextures.CanvasMat);
		k.Color = new Color(0.8f, 0.78f, 0.7f);
		BuildKit.Box(k, new Vector3(cx, 0.44f, (cz0 + cz1) * 0.5f), new Vector3(cw * 2f + 0.02f, 0.12f, cz1 - cz0 - 0.02f), 2f, BuildKit.Face.NY);
		// blanket, rumpled back, and a pillow
		k.Mat(BuildingTextures.Plain("b_blanket", new Color(0.3f, 0.12f, 0.09f)));
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(cx + 0.01f, 0.515f, cz1 - 0.72f), new Vector3(cw * 2f + 0.08f, 0.04f, 1.25f), 1f, BuildKit.Face.NY, new Basis(Vector3.Up, 0.04f));
		k.Color = new Color(0.8f, 0.8f, 0.8f);
		BuildKit.Box(k, new Vector3(cx + 0.02f, 0.545f, cz1 - 1.38f), new Vector3(cw * 2f + 0.04f, 0.06f, 0.2f), 1f, BuildKit.Face.NY, new Basis(Vector3.Right, -0.5f));
		k.Mat(BuildingTextures.Plain("b_pillow", new Color(0.62f, 0.6f, 0.54f)));
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(cx, 0.55f, cz0 + 0.22f), new Vector3(0.5f, 0.1f, 0.3f), 1f, BuildKit.Face.NY);
		cols.Add((new Vector3(cx, 0.28f, (cz0 + cz1) * 0.5f), new Vector3(cw * 2f + 0.06f, 0.56f, cz1 - cz0), Basis.Identity));

		// shelf on the right wall toward the front, with jars, tins and books
		k.Mat(wood);
		k.Color = new Color(0.78f, 0.7f, 0.62f);
		float shx = ix - 0.14f, shz0 = 0.35f, shz1 = 1.55f;
		foreach (float y in new[] { 1.05f, 1.5f })
		{
			BuildKit.Box(k, new Vector3(shx, y, (shz0 + shz1) * 0.5f), new Vector3(0.26f, 0.03f, shz1 - shz0), 1.4f);
			foreach (float z in new[] { shz0 + 0.1f, shz1 - 0.1f })
				BuildKit.Box(k, new Vector3(ix - 0.03f, y - 0.1f, z), new Vector3(0.05f, 0.18f, 0.03f), 1.4f);
		}
		var glass = BuildingTextures.Plain("b_jar", new Color(0.32f, 0.36f, 0.3f), 0.3f);
		var tin = BuildingTextures.Plain("b_tin", new Color(0.4f, 0.37f, 0.33f), 0.5f);
		var book = BuildingTextures.Plain("b_book", new Color(0.26f, 0.2f, 0.16f));
		for (int i = 0; i < 4; i++)
		{
			float z = shz0 + 0.15f + i * 0.14f;
			k.Mat(i % 2 == 0 ? glass : tin);
			k.Color = Colors.White;
			k.Cylinder(new Vector3(shx - 0.02f, 1.065f, z), new Vector3(shx - 0.02f, 1.065f + (i % 2 == 0 ? 0.17f : 0.11f), z), 0.045f, 0.045f, 6, true);
		}
		k.Mat(book);
		for (int i = 0; i < 5; i++)
		{
			float sh = rng.RandfRange(0.7f, 1.2f);
			k.Color = new Color(sh, sh * rng.RandfRange(0.8f, 1f), sh * 0.8f);
			float h = rng.RandfRange(0.17f, 0.24f);
			BuildKit.Box(k, new Vector3(shx - 0.01f, 1.515f + h * 0.5f, shz0 + 0.55f + i * 0.055f), new Vector3(0.17f, h, 0.045f), 3f, BuildKit.Face.NY, new Basis(Vector3.Right, i == 4 ? 0.3f : 0f));
		}
		k.Mat(tin);
		k.Color = Colors.White;
		k.Cylinder(new Vector3(shx - 0.02f, 1.515f, shz0 + 0.2f), new Vector3(shx - 0.02f, 1.63f, shz0 + 0.2f), 0.06f, 0.055f, 6, true);

		// crate in the front-left corner
		k.Mat(BuildingTextures.BoardsMat);
		k.Color = new Color(0.8f, 0.72f, 0.62f);
		BuildKit.Box(k, new Vector3(-ix + 0.34f, 0.25f, iz - 0.45f), new Vector3(0.55f, 0.5f, 0.5f), 1.8f, BuildKit.Face.NY, new Basis(Vector3.Up, 0.12f));
		cols.Add((new Vector3(-ix + 0.34f, 0.25f, iz - 0.45f), new Vector3(0.55f, 0.5f, 0.5f), new Basis(Vector3.Up, 0.12f)));

		// rag rug inside the door
		k.Mat(BuildingTextures.Plain("b_rug", new Color(0.3f, 0.2f, 0.16f)));
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(0, 0.006f, iz - 0.75f), new Vector3(1.3f, 0.012f, 0.8f), 1f, BuildKit.Face.NY, new Basis(Vector3.Up, -0.05f));

		// burnt: the collapsed roof bay lies slanted inside, charred
		if (IsBurnt)
		{
			k.Mat(BuildingTextures.BoardsMat);
			k.Color = new Color(0.3f, 0.28f, 0.26f);
			BuildKit.Box(k, new Vector3(0.25f, 1.1f, 0.9f), new Vector3(1.6f, 0.1f, 2.1f), 1f, BuildKit.Face.None, Basis.FromEuler(new Vector3(0.75f, 0.1f, 0.12f)));
			k.Mat(beam);
			k.Beam(new Vector3(-0.5f, 0.05f, 2.2f), new Vector3(0.6f, 2.0f, 0.3f), 0.08f, 0.15f, 1.4f);
			k.Beam(new Vector3(0.9f, 0.05f, 1.4f), new Vector3(0.2f, 2.5f, 0.0f), 0.08f, 0.15f, 1.4f);
		}
		k.Color = Colors.White;
	}

	// ---- the hanging lamp and its warm light

	private void BuildLamp()
	{
		float pz = -0.75f, lx = 0.3f;
		float py = RoofBottomAt(pz) - 0.34f;
		float ly = 1.95f;
		var k = new MeshKit();
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.4f, 0.38f, 0.35f);
		k.Cylinder(new Vector3(lx, py, pz), new Vector3(lx, ly + 0.2f, pz), 0.008f, 0.008f, 4, false);
		k.Cylinder(new Vector3(lx, ly - 0.02f, pz), new Vector3(lx, ly + 0.02f, pz), 0.07f, 0.07f, 8, true);
		k.Cylinder(new Vector3(lx, ly + 0.14f, pz), new Vector3(lx, ly + 0.2f, pz), 0.05f, 0.02f, 8, true);
		k.CommitTo(_gen, "LampFrame", false);
		var g = new MeshKit();
		g.Mat(BuildingTextures.LitGlassMat);
		g.Color = Colors.White;
		g.Cylinder(new Vector3(lx, ly + 0.02f, pz), new Vector3(lx, ly + 0.14f, pz), 0.05f, 0.04f, 8, false);
		_lampGlass = g.CommitTo(_gen, "LampGlass", false);
		_lamp = new OmniLight3D
		{
			Name = "Lamp",
			LightColor = new Color(1f, 0.66f, 0.36f),
			LightEnergy = 2.1f,
			OmniRange = 7.5f,
			OmniAttenuation = 1.1f,
			ShadowEnabled = true,
			Position = new Vector3(lx, ly + 0.07f, pz),
		};
		_gen.AddChild(_lamp);
		// the lit window's glow spills a little onto the porch
		var spill = new OmniLight3D
		{
			Name = "WindowSpill",
			LightColor = new Color(1f, 0.6f, 0.3f),
			LightEnergy = 0.35f,
			OmniRange = 2.6f,
			Position = new Vector3(-WinCx, (_winBot + _winTop) * 0.5f, FrontFace + 0.35f),
		};
		_gen.AddChild(spill);
		// and a faint warm fill just inside the door, so with the door open the room (the empty chair at
		// the table) reads from the porch and beyond
		var doorFill = new OmniLight3D
		{
			Name = "DoorFill",
			LightColor = new Color(1f, 0.62f, 0.34f),
			LightEnergy = 0.55f,
			OmniRange = 3.2f,
			Position = new Vector3(0.1f, 1.5f, Hd - 0.9f),
		};
		_gen.AddChild(doorFill);
	}

	private void UpdateLamp()
	{
		bool on = _lightOn && Burning <= 0f && !IsBurnt;
		if (_lamp != null && IsInstanceValid(_lamp)) _lamp.Visible = on;
		if (_lampGlass != null && IsInstanceValid(_lampGlass)) _lampGlass.Visible = on;
		var spill = _gen?.GetNodeOrNull<OmniLight3D>("WindowSpill");
		if (spill != null) spill.Visible = on;
		var fill = _gen?.GetNodeOrNull<OmniLight3D>("DoorFill");
		if (fill != null) fill.Visible = on;
		if (_litPane != null && IsInstanceValid(_litPane))
			_litPane.MaterialOverride = on ? null : BuildingTextures.GlassMat;
	}

	// ---- door, planks, window boards, debris

	private void BuildDoor()
	{
		if (_door != null && IsInstanceValid(_door)) { _door.QueueFree(); }
		_door = null;
		if (IsBurnt) return;
		_door = new Node3D { Name = "Door" };
		_gen.AddChild(_door);
		// hinge on the left jamb; the leaf is built extending +X from the hinge
		float hingeX = -Dw + 0.02f, z = Hd + 0.04f;
		_door.Position = new Vector3(hingeX, 0, z);
		if (IsOpen) _door.Rotation = new Vector3(0, Mathf.DegToRad(96f), 0);
		float w = DoorWidth - 0.04f, h = _doorTop - 0.03f;
		var k = new MeshKit();
		k.Mat(BuildingTextures.BoardsMat);
		k.Color = new Color(0.62f, 0.52f, 0.44f);
		BuildKit.Box(k, new Vector3(w * 0.5f, h * 0.5f + 0.005f, 0), new Vector3(w, h, 0.05f), 1.3f);
		// ledgers and a brace on the outside
		k.Mat(PropTextures.PostMat);
		k.Color = new Color(0.55f, 0.48f, 0.42f);
		foreach (float y in new[] { 0.3f, h - 0.3f })
			BuildKit.Box(k, new Vector3(w * 0.5f, y, 0.04f), new Vector3(w - 0.08f, 0.14f, 0.03f), 1.4f);
		k.Beam(new Vector3(0.1f, 0.36f, 0.04f), new Vector3(w - 0.1f, h - 0.36f, 0.04f), 0.03f, 0.12f, 1.4f, Vector3.Back);
		// latch and pull
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.5f, 0.47f, 0.44f);
		BuildKit.Box(k, new Vector3(w - 0.12f, 1.0f, 0.06f), new Vector3(0.03f, 0.16f, 0.03f), 3f);
		BuildKit.Box(k, new Vector3(w - 0.12f, 1.0f, -0.05f), new Vector3(0.03f, 0.12f, 0.03f), 3f);
		foreach (float y in new[] { 0.3f, h - 0.3f })
			BuildKit.Box(k, new Vector3(0.14f, y, 0.058f), new Vector3(0.28f, 0.04f, 0.01f), 3f);
		k.CommitTo(_door, "DoorMesh");
	}

	// ---- the prints (P5)

	/// <summary>
	/// R.H.'s four prints in a row on the back wall over his chair: the three birds he came for
	/// and the first staircase at night. Props only, no text on them.
	/// </summary>
	private void BuildPapers()
	{
		if (_papers != null && IsInstanceValid(_papers)) _papers.QueueFree();
		_papers = null;
		_sheet = null;
		if (Engine.IsEditorHint()) return;
		float iz = Hd - LogT * 0.5f;
		var root = new Node3D { Name = "Papers" };
		root.AddToGroup(PapersGroup);

		// tacks: one per print (the prints' are red pushpins, the stairs' a plain one)
		var tacks = new MeshKit();
		tacks.Mat(BuildingTextures.IronMat);

		float pz = -iz + 0.012f, py = 1.64f;
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 17 + 29) };
		for (int i = 0; i < 4; i++)
		{
			Vector3 at = new(0.3f + (i - 1.5f) * 0.19f, py + rng.RandfRange(-0.012f, 0.012f), pz);
			Print(root, at, Vector3.Back, i, rng.RandfRange(-4f, 4f));
			tacks.Color = i == 3 ? new Color(0.4f, 0.38f, 0.35f) : new Color(0.6f, 0.12f, 0.1f);
			BuildKit.Box(tacks, at + new Vector3(0, 0.045f, 0.009f), new Vector3(0.011f, 0.011f, 0.011f), 1f);
		}
		tacks.CommitTo(root, "Tacks", false);
		_gen.AddChild(root);
		_papers = root;
	}

	/// <summary>One pinned photographic print (3:2 like the viewfinder), image side out.</summary>
	private static void Print(Node3D parent, Vector3 at, Vector3 outward, int kind, float tiltDeg)
	{
		Vector3 z = outward.Normalized();
		Vector3 x = Vector3.Up.Cross(z).Normalized();
		var b = new Basis(x, Vector3.Up, z);
		if (tiltDeg != 0f) b = b.Rotated(z, Mathf.DegToRad(tiltDeg));
		parent.AddChild(new MeshInstance3D
		{
			Name = $"Print{kind}",
			Mesh = new QuadMesh { Size = new Vector2(0.15f, 0.10f) },
			MaterialOverride = BuildingTextures.PrintMat(kind),
			Transform = new Transform3D(b, at + z * 0.006f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	/// <summary>Paper burns first: every note and print in the cabin (group <see cref="PapersGroup"/>) is gone while it burns or once burnt.</summary>
	private void UpdatePapers()
	{
		if (!IsInsideTree()) return;
		bool on = Burning <= 0f && !IsBurnt;
		foreach (var n in GetTree().GetNodesInGroup(PapersGroup))
			if (n is Node3D p && IsInstanceValid(p)) p.Visible = on;
	}

	/// <summary>Planks nailed across the doorway ("boarded up, from the outside"). Visual; the door behind is solid.</summary>
	public void SetBoarded(bool boarded)
	{
		if (DoorBoarded == boarded) return;
		DoorBoarded = boarded;
		if (_planks != null && IsInstanceValid(_planks)) _planks.QueueFree();
		_planks = null;
		if (boarded && !IsOpen && _gen != null) BuildPlanks();
		if (_gen != null) RefreshWindowBoards();
	}

	/// <summary>Chopped or pried open: the planks come off (they lie on the porch) and the door hangs open. Walkable.</summary>
	public void OpenDoor()
	{
		if (IsOpen) return;
		IsOpen = true;
		SetBoarded(false);
		if (_gen != null) RefreshWindowBoards();
		if (_door != null && IsInstanceValid(_door)) _door.Rotation = new Vector3(0, Mathf.DegToRad(96f), 0);
		if (_doorCollision != null) _doorCollision.Disabled = true;
		if (_gen != null) BuildDebris();
	}

	/// <summary>Save-restore helper: open (same as <see cref="OpenDoor"/>) or closed.</summary>
	public void SetOpen(bool open)
	{
		if (open) { OpenDoor(); return; }
		if (!IsOpen) return;
		IsOpen = false;
		if (_door != null && IsInstanceValid(_door)) _door.Rotation = Vector3.Zero;
		if (_doorCollision != null) _doorCollision.Disabled = false;
		if (_debris != null && IsInstanceValid(_debris)) _debris.QueueFree();
		_debris = null;
		if (DoorBoarded && _gen != null) BuildPlanks();
	}

	private void BuildPlanks()
	{
		var k = new MeshKit();
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 131 + 7) };
		float z = FrontFace + 0.035f;
		float span = DoorWidth + 0.55f;
		float[] heights = { 0.28f, 0.62f, 0.98f, 1.34f, 1.7f };
		foreach (float h in heights)
		{
			float tilt = rng.RandfRange(-0.06f, 0.06f);
			float shade = rng.RandfRange(0.75f, 1.05f);
			float off = rng.RandfRange(-0.08f, 0.08f);
			k.Mat(BuildingTextures.FreshPlankMat);
			k.Color = new Color(shade, shade * 0.95f, shade * 0.88f);
			var rot = Basis.FromEuler(new Vector3(0, 0, tilt));
			BuildKit.Box(k, new Vector3(off, h, z), new Vector3(span, 0.19f, 0.035f), 1.4f, BuildKit.Face.None, rot);
			// nail heads at both ends
			k.Mat(BuildingTextures.IronMat);
			k.Color = new Color(0.3f, 0.3f, 0.3f);
			foreach (int s in new[] { -1, 1 })
				BuildKit.Box(k, new Vector3(off, h, z + 0.02f) + rot * new Vector3(s * (span * 0.5f - 0.08f), 0.03f * s, 0), new Vector3(0.018f, 0.018f, 0.01f), 1f);
		}
		k.Mat(BuildingTextures.FreshPlankMat);
		foreach (float side in new[] { -1f, 1f })
		{
			k.Color = new Color(0.72f, 0.65f, 0.56f);
			var rot = Basis.FromEuler(new Vector3(0, 0, side * 0.6f));
			BuildKit.Box(k, new Vector3(0, _doorTop * 0.5f, z + 0.035f), new Vector3(0.17f, _doorTop * 1.05f, 0.03f), 1.4f, BuildKit.Face.None, rot);
		}
		_planks = new Node3D { Name = "Planks" };
		_gen.AddChild(_planks);
		k.CommitTo(_planks, "PlanksMesh");
		ApplyChar();
	}

	private void BuildDebris()
	{
		if (_debris != null && IsInstanceValid(_debris)) _debris.QueueFree();
		_debris = new Node3D { Name = "PryedPlanks" };
		_gen.AddChild(_debris);
		var k = new MeshKit();
		k.Mat(BuildingTextures.FreshPlankMat);
		k.Color = new Color(0.8f, 0.74f, 0.66f);
		float z = FrontFace;
		BuildKit.Box(k, new Vector3(-0.35f, 0.02f, z + 0.9f), new Vector3(1.6f, 0.035f, 0.19f), 1.4f, BuildKit.Face.NY, new Basis(Vector3.Up, 0.35f));
		BuildKit.Box(k, new Vector3(0.45f, 0.055f, z + 1.05f), new Vector3(1.5f, 0.035f, 0.19f), 1.4f, BuildKit.Face.NY, Basis.FromEuler(new Vector3(0, -0.5f, 0.02f)));
		BuildKit.Box(k, new Vector3(1.9f + 0.3f, 0.35f, z + 0.3f), new Vector3(0.19f, 0.8f, 0.035f), 1.4f, BuildKit.Face.None, Basis.FromEuler(new Vector3(-0.45f, 0.1f, 0)));
		k.CommitTo(_debris, "DebrisMesh");
	}

	/// <summary>Boards nailed over both windows, with gaps (the left window's glow shows through).
	/// Whenever the door is boarded, or broken open after; otherwise the windows are bare glass. While burning / burnt they are gone, so smoke can pour out.</summary>
	private void RefreshWindowBoards()
	{
		if (_windowBoards != null && IsInstanceValid(_windowBoards)) _windowBoards.QueueFree();
		_windowBoards = null;
		if (Burning > 0f || IsBurnt || !(DoorBoarded || IsOpen)) return;
		var k = new MeshKit();
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 53 + 11) };
		float z = FrontFace + 0.045f;
		foreach (float cx in new[] { -WinCx, WinCx })
		{
			float h = _winTop - _winBot;
			for (int i = 0; i < 3; i++)
			{
				float y = _winBot + h * (0.17f + i * 0.33f) + rng.RandfRange(-0.03f, 0.03f);
				float tilt = rng.RandfRange(-0.08f, 0.08f);
				float sh = rng.RandfRange(0.7f, 1.0f);
				k.Mat(PropTextures.SignPlankMat);
				k.Color = new Color(sh, sh * 0.95f, sh * 0.88f);
				BuildKit.Box(k, new Vector3(cx + rng.RandfRange(-0.05f, 0.05f), y, z), new Vector3(WinW + 0.34f, 0.15f, 0.03f), 1.4f, BuildKit.Face.None, Basis.FromEuler(new Vector3(0, 0, tilt)));
			}
		}
		_windowBoards = new Node3D { Name = "WindowBoards" };
		_gen.AddChild(_windowBoards);
		k.CommitTo(_windowBoards, "WindowBoardsMesh");
	}

	// ---- fire

	private IEnumerable<(Vector3 pos, Vector3 size, float flames, Vector3 smokeDir, float smoke, bool light, float range, float energy)> FireSpotDefs()
	{
		float yF = RoofBottomAt(1.35f) + 0.1f, yB = RoofBottomAt(1.5f) + 0.1f;
		// the interior: the heart of it, seen through the door and windows, lights the room (shadowed, so it spills out of the openings)
		yield return (new Vector3(0, 0.05f, -0.4f), new Vector3(2.8f, 2.0f, 3.2f), 1f, Vector3.Up, 0.6f, true, 9f, 3.5f);
		// flames licking the roof along the ridge and both slopes
		yield return (new Vector3(-1.1f, RidgeY - 0.3f, 0.1f), new Vector3(1.4f, 1.9f, 1.1f), 1f, Vector3.Up, 1.2f, true, 28f, 7f);
		yield return (new Vector3(1.0f, RidgeY - 0.35f, -0.1f), new Vector3(1.5f, 2.3f, 1.1f), 1f, Vector3.Up, 1.3f, false, 0f, 0f);
		yield return (new Vector3(0.2f, yF, 1.35f), new Vector3(2.2f, 1.3f, 0.8f), 0.9f, Vector3.Up, 0.6f, false, 0f, 0f);
		yield return (new Vector3(-0.5f, yB, -1.5f), new Vector3(1.9f, 1.3f, 0.8f), 0.9f, Vector3.Up, 0.5f, false, 0f, 0f);
		// smoke billowing out of both windows (flames low inside the frame)
		foreach (float cx in new[] { -WinCx, WinCx })
			yield return (new Vector3(cx, _winBot + 0.05f, FrontFace + 0.1f), new Vector3(WinW * 0.8f, 0.8f, 0.25f), 0.45f, new Vector3(0, 0.45f, 1f), 1.6f, false, 0f, 0f);
		// the doorway
		yield return (new Vector3(0, 0.05f, Hd), new Vector3(DoorWidth * 0.8f, 1.8f, 0.4f), 0.8f, new Vector3(0, 0.6f, 1f), 1.1f, true, 14f, 3.5f);
	}

	/// <summary>
	/// Sets the fire: 0 = out (fire removed), up to 1 = fully engulfed. Spawns FireVfx at
	/// <see cref="FireSpots"/> (interior, roof, windows pouring smoke, doorway), chars the
	/// surfaces progressively, takes the window boards/glass out and puts the lamp out.
	/// </summary>
	public void SetBurning(float intensity)
	{
		intensity = Mathf.Clamp(intensity, 0f, 1f);
		bool wasBurning = Burning > 0f;
		Burning = intensity;
		if (intensity <= 0f)
		{
			foreach (var f in _fires) if (IsInstanceValid(f)) f.QueueFree();
			_fires.Clear();
			if (_fire != null && IsInstanceValid(_fire)) _fire.QueueFree();
			_fire = null;
			if (wasBurning && _gen != null) { BuildGlazing(); RefreshWindowBoards(); }
			ApplyChar();
			UpdateLamp();
			UpdatePapers();
			return;
		}
		// Ignition frees just the sashes, glass and window boards (smoke pours out); nothing is rebuilt.
		if (!wasBurning && _gen != null) { BuildGlazing(); RefreshWindowBoards(); }
		if (_fires.Count == 0)
		{
			_fire = new Node3D { Name = "Fire" };
			AddChild(_fire);
			int i = 0;
			foreach (var s in FireSpotDefs())
			{
				var f = new FireVfx
				{
					Name = $"Fire{i}",
					Extent = s.size,
					FlameScale = s.flames,
					SmokeDirection = s.smokeDir,
					SmokeAmount = s.smoke,
					EmitLight = s.light,
					LightRange = s.range > 0 ? s.range : 12f,
					LightEnergy = s.energy > 0 ? s.energy : 3f,
					LightShadows = s.range < 12f,   // only the interior light casts shadows (cheap, and the openings throw light out)
					Embers = s.flames >= 0.9f,
					Seed = Seed * 10 + i,
					Position = s.pos,
				};
				_fire.AddChild(f);
				_fires.Add(f);
				i++;
			}
		}
		foreach (var f in _fires) f.Intensity = intensity;
		ApplyChar();
		UpdateLamp();
		UpdatePapers();
	}

	/// <summary>The burnt-out shell (black, faint embers, part of the roof and the porch roof fallen in). Unused by the story so far.</summary>
	public void SetBurnt(bool burnt)
	{
		if (IsBurnt == burnt) return;
		IsBurnt = burnt;
		if (burnt) { DoorBoarded = false; IsOpen = true; }
		if (_gen != null) Build();
	}

	private void ApplyChar()
	{
		if (_gen == null) return;
		float amount = IsBurnt ? 1f : Burning > 0f ? 0.3f + 0.45f * Burning : 0f;
		float ember = IsBurnt ? 0.35f : Burning;
		if (amount <= 0f)
		{
			SetOverlay(_gen, null);
			return;
		}
		_char ??= BuildingTextures.NewCharOverlay();
		_char.SetShaderParameter("amount", amount);
		_char.SetShaderParameter("ember", ember);
		SetOverlay(_gen, _char);
	}

	private static void SetOverlay(Node n, Material m)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is MeshInstance3D mi && !mi.Name.ToString().Contains("Pane") && !mi.Name.ToString().Contains("Lamp")) mi.MaterialOverlay = m;
			SetOverlay(c, m);
		}
	}
}
