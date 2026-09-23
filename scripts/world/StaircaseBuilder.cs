using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Builds an old outdoor park staircase: a long, straight, steep flight of
/// weathered poured-concrete steps between low coursed-stone cheek walls with
/// a jointed coping, standing on a wider two-step plinth, ending at a short
/// top landing with nothing beyond it. Treads are slightly uneven, with damp
/// dark noses and back joints, moss at the wall edges and a few dead leaves.
/// Nothing glows.
///
/// Local frame: origin on the ground at the centre of the first riser; the
/// stairs climb toward -Z. Collision (layer 1, meta surface = "stone"): a
/// hidden ramp through the step noses, plinth wedge, both cheek walls (taller
/// than they look) and an invisible guard at the top edge.
///
/// Ruined = a short broken fragment: no landing, one cheek wall crumbled away
/// part way up with rubble at its foot, the flight simply stopping.
///
/// The newel post (not when Ruined): at the top of the right-hand cheek wall
/// (+X), in place of that side's landing pier, a squat square pier of the same
/// coursed stone with a two-step cast-stone cap, carrying a round stone cap: a
/// short round spigot, a small square plinth, a neck and a ball (~0.3 m). With
/// <see cref="NewelCapped"/> false only the spigot's broken stump stands there,
/// a jagged top of pale fresh break. The cap is its own child node
/// ("Generated/Newel/Cap"), shown or hidden without rebuilding the flight; the
/// stump stays under it, and its break matches the cap's underside exactly, so
/// the same mesh is the item on R.H.'s table (<see cref="BuildNewelCap"/>).
/// The pier stands outside the clear width, behind the wall's collider line;
/// its own collider only covers its outer side.
///
/// Restore: <see cref="NewelCapRestore"/> = AfterNewelSeated caps the post
/// from <see cref="StoryManager.Flag.NewelSeated"/> (the last staircase only:
/// it is whole again once the player has put the cap back at its top in Act 11;
/// <see cref="Act11Ending"/> seats it live, this only restores a save).
///
/// Runtime only: a child Area3D "TopTrigger" is moved onto the top landing,
/// Marker3D "AutotestApproach" 1.5 m in front of the first step, and (when not
/// Ruined) Node3D "SilenceZone" to the middle of the flight.
/// </summary>
[Tool]
[GlobalClass]
public partial class StaircaseBuilder : Node3D
{
	[Export] public int Steps = 36;            // risers, plinth steps included
	[Export] public float Rise = 0.17f;
	[Export] public float Run = 0.28f;
	/// <summary>Clear width between the cheek walls.</summary>
	[Export] public float Width = 1.35f;
	[Export] public int PlinthSteps = 2;
	/// <summary>Extra width per side of the bottom plinth step beyond the walls; each higher plinth step loses a share.</summary>
	[Export] public float PlinthFlare = 0.32f;
	[Export] public float LandingDepth = 1.25f;
	[Export] public float WallThickness = 0.22f;
	/// <summary>Height of the wall (coping included) above the line of step noses.</summary>
	[Export] public float WallHeight = 0.52f;
	[Export] public bool Ruined = false;
	/// <summary>Ruined only: -1 = left wall crumbles, +1 = right.</summary>
	[Export] public int CrumbleSide = -1;
	/// <summary>Ruined only: the crumbling wall breaks off after this many steps.</summary>
	[Export] public int CrumbleAfterStep = 3;
	[Export] public int Seed = 7;
	[Export] public bool BuildCollision = true;
	/// <summary>How far the walls, plinth and front riser go below the local ground (covers terrain dips
	/// under the footprint; a flight set on a slope needs more).</summary>
	[Export] public float FoundationDepth = 0.7f;

	public enum CapRestore { Never, AfterNewelSeated }

	/// <summary>Whether the newel post's round stone cap sits on its pier. Setting it only shows/hides
	/// the cap's node; the flight is not rebuilt.</summary>
	[Export]
	public bool NewelCapped
	{
		get => _newelCapped;
		set
		{
			_newelCapped = value;
			if (_capNode != null && IsInstanceValid(_capNode)) _capNode.Visible = value;
		}
	}
	private bool _newelCapped = true;

	/// <summary>How this flight puts its cap back from the story on load (and live when the flag is set).</summary>
	[Export] public CapRestore NewelCapRestore = CapRestore.Never;

	/// <summary>The flight's length as authored in the scene, captured when it enters the tree (before
	/// any story code lengthens it). <see cref="StairsState.ClearingStepsFor"/> takes it as the base.</summary>
	public int BaseSteps { get; private set; } = -1;
	/// <summary>For tests: how many times <see cref="Build"/> has run.</summary>
	public int BuildCount { get; private set; }

	private float Found => FoundationDepth;   // how far walls/plinth go below local ground (covers terrain dips)
	/// <summary>Metres of run per metre of foundation for the collider apron in front of the first step (the
	/// stairs' own pitch), so a flight whose base stands proud of the ground in front is still walked onto:
	/// a vertical lip of more than about 12 cm stops a walking character dead.</summary>
	private float ApronRun => Run / Mathf.Max(Rise, 0.05f);
	private const float CopingH = 0.075f, CopingOver = 0.035f, PierD = 0.42f, PierExtra = 0.05f, Nose = 0.015f;

	public float TotalHeight => Steps * Rise;
	public float TopFrontZ => -(Steps - 1) * Run;
	public float BackZ => TopFrontZ - (Ruined ? Run : LandingDepth);
	private float Hw => Width * 0.5f;
	private float OuterHalf => Hw + WallThickness;
	private float NoseY(float z) => Rise * (1f - z / Run);
	private float WallTop(float z) => (z >= TopFrontZ ? NoseY(z) : TotalHeight) + WallHeight - CopingH;
	private float PlinthHalf(int k) => OuterHalf + PierExtra + PlinthFlare * (PlinthSteps - k) / Mathf.Max(1, PlinthSteps);
	private float PlinthBackZ => -PlinthSteps * Run - 0.3f;
	private float WallStartZ => -PlinthSteps * Run + 0.06f;
	private float CrumbleZ => -(CrumbleAfterStep + 0.5f) * Run;

	private enum Kind { Tread, Riser, Wall, Coping, Plinth }

	private FastNoiseLite _noise;
	private RandomNumberGenerator _rng;

	private bool _capListening;

	public override void _EnterTree()
	{
		if (BaseSteps < 0) BaseSteps = Steps;
		if (!Engine.IsEditorHint() && NewelCapRestore == CapRestore.AfterNewelSeated && StoryManager.Instance is { } s && !_capListening)
		{
			s.FlagSet += OnStoryFlag;
			_capListening = true;
		}
	}

	public override void _ExitTree()
	{
		if (_capListening && StoryManager.Instance is { } s) s.FlagSet -= OnStoryFlag;
		_capListening = false;
	}

	public override void _Ready()
	{
		// Restore: the cap back on from the saved story (before the first build, so it builds that way).
		if (!Engine.IsEditorHint() && NewelCapRestore == CapRestore.AfterNewelSeated && StoryManager.Instance is { } s)
			_newelCapped = s.HasFlag(StoryManager.Flag.NewelSeated);
		Build();
	}

	private void OnStoryFlag(string flag)
	{
		if (flag == StoryManager.Flag.NewelSeated) NewelCapped = true;
	}

	// ------------------------------------------------------------------ mesh helper

	/// <summary>Minimal mesh builder with per-vertex colour and world-projected UVs (1 tile per metre).</summary>
	private class Stone
	{
		private class Surf { public readonly List<Vector3> V = new(), N = new(); public readonly List<Vector2> UV = new(); public readonly List<Color> C = new(); }
		private readonly Dictionary<Material, Surf> _s = new();
		private readonly List<Material> _order = new();
		private Surf _cur;

		public Stone Mat(Material m)
		{
			if (!_s.TryGetValue(m, out _cur)) { _cur = new Surf(); _s[m] = _cur; _order.Add(m); }
			return this;
		}

		public static Vector2 Project(Vector3 p, Vector3 n)
		{
			float ax = Mathf.Abs(n.X), ay = Mathf.Abs(n.Y), az = Mathf.Abs(n.Z);
			if (ay >= ax && ay >= az) return new Vector2(p.X, p.Z);
			if (ax >= az) return new Vector2(p.Z * Mathf.Sign(n.X), -p.Y);
			return new Vector2(p.X * -Mathf.Sign(n.Z), -p.Y);
		}

		public void Tri(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc, Vector3 n, Vector2? ua = null, Vector2? ub = null, Vector2? uc = null)
		{
			Vector2 A = ua ?? Project(a, n), B = ub ?? Project(b, n), C = uc ?? Project(c, n);
			if ((b - a).Cross(c - a).Dot(n) > 0f) { (b, c) = (c, b); (cb, cc) = (cc, cb); (B, C) = (C, B); }
			_cur.V.Add(a); _cur.V.Add(b); _cur.V.Add(c);
			_cur.N.Add(n); _cur.N.Add(n); _cur.N.Add(n);
			_cur.UV.Add(A); _cur.UV.Add(B); _cur.UV.Add(C);
			_cur.C.Add(ca); _cur.C.Add(cb); _cur.C.Add(cc);
		}

		public ArrayMesh Commit()
		{
			var mesh = new ArrayMesh();
			foreach (var m in _order)
			{
				var s = _s[m];
				if (s.V.Count == 0) continue;
				var arr = new Godot.Collections.Array();
				arr.Resize((int)Mesh.ArrayType.Max);
				arr[(int)Mesh.ArrayType.Vertex] = s.V.ToArray();
				arr[(int)Mesh.ArrayType.Normal] = s.N.ToArray();
				arr[(int)Mesh.ArrayType.TexUV] = s.UV.ToArray();
				arr[(int)Mesh.ArrayType.Color] = s.C.ToArray();
				mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
				mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, m);
			}
			return mesh;
		}
	}

	private Stone _m;
	private static Color Gray(float v) => new(v, v, v, 1);
	private static Color Cl(Color c) => new(Mathf.Clamp(c.R, 0, 1), Mathf.Clamp(c.G, 0, 1), Mathf.Clamp(c.B, 0, 1), 1);

	private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Kind k)
	{
		Color ca = Cl(Paint(a, n, k)), cb = Cl(Paint(b, n, k)), cc = Cl(Paint(c, n, k)), cd = Cl(Paint(d, n, k));
		_m.Tri(a, b, c, ca, cb, cc, n);
		_m.Tri(a, c, d, ca, cc, cd, n);
	}

	/// <summary>Oriented box: centre, basis (columns = local axes), full size.</summary>
	private void OBox(Vector3 c, Basis b, Vector3 s, Kind k, bool bottom = false)
	{
		Vector3 h = s * 0.5f;
		Vector3 P(float x, float y, float z) => c + b * new Vector3(x * h.X, y * h.Y, z * h.Z);
		Vector3 ax = b.X.Normalized(), ay = b.Y.Normalized(), az = b.Z.Normalized();
		Quad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), ay, k);
		if (bottom) Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), -ay, k);
		Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), az, k);
		Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), -az, k);
		Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), ax, k);
		Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), -ax, k);
	}

	// ------------------------------------------------------------------ painting

	private float N3(Vector3 p, float f, float off = 0f) => _noise.GetNoise3D(p.X * f + off, p.Y * f, p.Z * f);

	private static float Smooth(float a, float b, float x) => Mathf.SmoothStep(a, b, x);

	/// <summary>Vertex colour: multiplied into the stone textures. Damp dark joints, moss tint near walls and ground, wear in the middle.</summary>
	private Color Paint(Vector3 p, Vector3 n, Kind k)
	{
		float nz = N3(p, 1.3f), nf = N3(p, 5f, 50f);
		var moss = new Color(0.44f, 0.50f, 0.33f);
		var dampMoss = new Color(0.30f, 0.36f, 0.24f);
		float edgeD = Hw - Mathf.Abs(p.X);                      // distance to the inner wall face
		float edge = 1f - Smooth(0f, 0.3f, edgeD);
		switch (k)
		{
			case Kind.Tread:
			{
				float fz = p.Z > TopFrontZ - Run ? Mathf.PosMod(-p.Z, Run) / Run : 0.5f;
				float nose = 1f - Smooth(0f, 0.12f, fz);
				float back = Smooth(0.78f, 1f, fz);
				float wear = 1f - Smooth(0.12f, 0.42f, Mathf.Abs(p.X));
				float g = 0.93f + nz * 0.07f + nf * 0.05f;
				g *= 1f - 0.30f * nose;
				g *= 1f - 0.22f * back;
				g += wear * 0.05f * (1f - nose);
				var c = new Color(g, g, g * 0.98f);
				float mossAmt = Mathf.Clamp(edge * (0.55f + 0.45f * nz) + back * 0.18f * (nf + 0.3f), 0f, 0.8f);
				if (Mathf.Abs(p.X) > Hw) mossAmt = Mathf.Clamp(0.25f + nz * 0.3f, 0f, 0.5f); // plinth wings
				return c.Lerp(moss * (0.8f + g * 0.2f), mossAmt);
			}
			case Kind.Riser:
			{
				float fy = Mathf.PosMod(p.Y, Rise) / Rise;
				float g = 0.58f + nz * 0.07f + nf * 0.04f;
				g *= 1f - 0.25f * Smooth(0.6f, 1f, fy);   // shadowed under the nose
				g *= 1f - 0.2f * (1f - Smooth(0f, 0.35f, fy)); // damp at the joint
				var c = new Color(g, g, g * 0.98f);
				return c.Lerp(dampMoss, Mathf.Clamp(edge * 0.6f + (nz > 0.35f ? 0.2f : 0f), 0f, 0.7f));
			}
			case Kind.Coping:
			{
				float g = 0.95f + nz * 0.05f + nf * 0.04f;
				if (n.Y < -0.5f) g *= 0.55f;
				var c = new Color(g, g * 0.99f, g * 0.96f);
				float m = n.Y > 0.5f ? Smooth(0.15f, 0.55f, nf + nz * 0.5f) * 0.75f : Smooth(0.35f, 0.7f, nf) * 0.4f;
				return c.Lerp(moss, m);
			}
			default: // Wall / Plinth
			{
				float g = 0.93f + nz * 0.07f + nf * 0.05f;
				float ground = 1f - Smooth(-0.2f, 1.5f, p.Y);
				float streak = Smooth(0.2f, 0.6f, _noise.GetNoise2D(p.Z * 7f + (p.X > 0 ? 300f : 0f), 9f)) * 0.18f;
				g *= 1f - streak;
				if (k == Kind.Wall && n.Y < 0.5f && WallTop(p.Z) - p.Y < 0.14f && WallTop(p.Z) - p.Y > -0.01f) g *= 0.85f;
				g *= 1f - 0.3f * ground;
				var c = new Color(g, g * 0.99f, g * 0.95f);
				return c.Lerp(dampMoss, Mathf.Clamp(ground * 0.75f + (nz > 0.4f ? 0.25f : 0f), 0f, 0.85f));
			}
		}
	}

	// ------------------------------------------------------------------ build

	public void Build()
	{
		BuildCount++;
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		_newel = null;
		_capNode = null;
		var gen = new Node3D { Name = "Generated" };
		AddChild(gen);

		_noise = new FastNoiseLite { Seed = Seed, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 1f, FractalOctaves = 2 };
		_rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 7919 + 17) };
		_m = new Stone();

		int N = Steps;
		float H = TotalHeight, zTop = TopFrontZ, zBack = BackZ;
		var concrete = StairTextures.ConcreteMat;
		var blocks = StairTextures.BlockMat;

		// ---------------- steps ----------------
		var stepDy = new float[N];
		var stepRoll = new float[N];
		for (int i = 0; i < N; i++)
		{
			stepDy[i] = _rng.RandfRange(-0.009f, 0.009f);
			stepRoll[i] = _rng.RandfRange(-0.007f, 0.007f);
		}
		float TreadY(int i, float x) => (i + 1) * Rise + stepDy[i] + stepRoll[i] * x;

		_m.Mat(concrete);
		for (int i = 0; i < N; i++)
		{
			bool plinth = i < PlinthSteps;
			bool last = i == N - 1;
			float half = plinth ? PlinthHalf(i) : Hw + 0.06f;
			float zf = -i * Run, zb = last ? zBack : -(i + 1) * Run;
			int cols = Mathf.Max(4, Mathf.RoundToInt(half * 2f / 0.24f));
			var zs = new List<float> { zf + Nose, zf - 0.045f };
			if (last) for (float z = zf - 0.3f; z > zb + 0.15f; z -= 0.3f) zs.Add(z);
			else zs.Add(zf - Run * 0.6f);
			zs.Add(zb);
			int rows = zs.Count;
			var grid = new Vector3[cols + 1, rows];
			for (int c = 0; c <= cols; c++)
			{
				float x = -half + 2f * half * c / cols;
				for (int r = 0; r < rows; r++)
				{
					float jy = (Hash(i, c, r) - 0.5f) * 0.008f;
					float y = TreadY(i, x) + jy;
					float z = zs[r];
					if (r == 0)
					{
						// worn, slightly chipped nose
						y -= 0.006f + Hash(i, c, 91) * 0.012f;
						z += (Hash(i, c, 92) - 0.6f) * 0.016f;
					}
					if (r == rows - 1 && !last) y = Mathf.Min(y, (i + 1) * Rise + 0.004f);
					grid[c, r] = new Vector3(x, y, z);
				}
			}
			for (int c = 0; c < cols; c++)
				for (int r = 0; r < rows - 1; r++)
					Quad(grid[c, r], grid[c + 1, r], grid[c + 1, r + 1], grid[c, r + 1], Vector3.Up, Kind.Tread);

			// riser: nose lip, then the recessed face down into the step below
			float yBot = i == 0 ? -Found : i * Rise - 0.03f;
			for (int c = 0; c < cols; c++)
			{
				Vector3 a = grid[c, 0], b = grid[c + 1, 0];
				Vector3 a1 = new(a.X, a.Y - 0.035f, zf), b1 = new(b.X, b.Y - 0.035f, zf);
				Quad(a, b, b1, a1, Vector3.Back, Kind.Riser);
				Vector3 a2 = new(a.X, yBot, zf), b2 = new(b.X, yBot, zf);
				Quad(a1, b1, b2, a2, Vector3.Back, Kind.Riser);
			}

			// exposed step ends where no wall covers them (plinth, or a crumbled wall in Ruined mode)
			for (int s = -1; s <= 1; s += 2)
			{
				bool exposed = plinth || !WallCovers(s, zf) || !WallCovers(s, zb);
				if (!exposed) continue;
				float x = s * half;
				float zEnd = plinth ? PlinthBackZ : zb;
				float yTop = TreadY(i, x);
				Quad(new Vector3(x, -Found, zf), new Vector3(x, yTop, zf + (plinth ? Nose : 0)), new Vector3(x, yTop, zEnd), new Vector3(x, -Found, zEnd),
					new Vector3(s, 0, 0), plinth ? Kind.Plinth : Kind.Wall);
			}

			if (plinth)
			{
				// wing tops and back faces of the wider plinth steps (beside the narrower step above)
				float innerHalf = i + 1 < PlinthSteps ? PlinthHalf(i + 1) : OuterHalf + PierExtra;
				float yTop = (i + 1) * Rise + stepDy[i];
				for (int s = -1; s <= 1; s += 2)
				{
					float x0 = s * innerHalf, x1 = s * half, xm = (x0 + x1) * 0.5f;
					float zm = (zb + PlinthBackZ) * 0.5f;
					foreach (var (za, zc) in new[] { (zb, zm), (zm, PlinthBackZ) })
						foreach (var (xa, xc) in new[] { (x0, xm), (xm, x1) })
							Quad(new Vector3(xa, yTop + stepRoll[i] * xa, za), new Vector3(xc, yTop + stepRoll[i] * xc, za),
								new Vector3(xc, yTop + stepRoll[i] * xc, zc), new Vector3(xa, yTop + stepRoll[i] * xa, zc), Vector3.Up, Kind.Tread);
					Quad(new Vector3(x0, -Found, PlinthBackZ), new Vector3(x1, -Found, PlinthBackZ), new Vector3(x1, yTop + stepRoll[i] * x1, PlinthBackZ),
						new Vector3(x0, yTop + stepRoll[i] * x0, PlinthBackZ), Vector3.Forward, Kind.Plinth);
				}
			}
		}

		// back face of the top (the flight just stops)
		_m.Mat(blocks);
		{
			float hl = WallCovers(-1, zBack + 0.01f) ? OuterHalf : Hw + 0.06f, hr = WallCovers(1, zBack + 0.01f) ? OuterHalf : Hw + 0.06f;
			for (int c = 0; c < 4; c++)
			{
				float xa = Mathf.Lerp(-hl, hr, c / 4f), xb = Mathf.Lerp(-hl, hr, (c + 1) / 4f);
				float yTopA = Ruined ? TreadY(N - 1, xa) : H + 0.004f, yTopB = Ruined ? TreadY(N - 1, xb) : H + 0.004f;
				float[] ys = { -Found, 0.3f, H * 0.5f, H - 0.3f };
				for (int r = 0; r < ys.Length; r++)
				{
					float y0 = ys[r], y1a = r + 1 < ys.Length ? ys[r + 1] : yTopA, y1b = r + 1 < ys.Length ? ys[r + 1] : yTopB;
					if (y0 >= Mathf.Min(y1a, y1b)) continue;
					Quad(new Vector3(xb, y0, zBack), new Vector3(xa, y0, zBack), new Vector3(xa, y1a, zBack), new Vector3(xb, y1b, zBack), Vector3.Forward, Kind.Wall);
				}
			}
		}

		// ---------------- cheek walls, piers, coping ----------------
		for (int s = -1; s <= 1; s += 2)
			BuildWall(s, concrete, blocks);

		// ---------------- decals: moss, leaves ----------------
		BuildDecals(TreadY);

		var mi = new MeshInstance3D { Name = "StairMesh", Mesh = _m.Commit() };
		gen.AddChild(mi);

		if (Ruined) BuildRubble(gen);
		else BuildNewel(gen);

		if (BuildCollision && !Engine.IsEditorHint()) BuildColliders(gen);

		// Move the scene's hook nodes (runtime only, so the editor never dirties the scene).
		if (Engine.IsEditorHint()) return;
		if (GetNodeOrNull<Node3D>("TopTrigger") is Node3D trig)
		{
			float depth = Ruined ? Run : LandingDepth;
			trig.Position = new Vector3(0, H, (zTop + zBack) * 0.5f);
			if (trig.GetNodeOrNull<CollisionShape3D>("Shape") is CollisionShape3D cs)
			{
				cs.Position = new Vector3(0, 0.9f, 0);
				cs.Shape = new BoxShape3D { Size = new Vector3(Width, 1.8f, Mathf.Max(0.3f, depth * 0.9f)) };
			}
		}
		if (GetNodeOrNull<Node3D>("AutotestApproach") is Node3D appr)
			appr.Position = new Vector3(0, 0, 1.5f);
		if (!Ruined && GetNodeOrNull<Node3D>("SilenceZone") is Node3D sz)
			sz.Position = new Vector3(0, H * 0.5f, zBack * 0.5f);
	}

	private static float Hash(int a, int b, int c)
	{
		unchecked
		{
			uint h = (uint)(a * 374761393 + b * 668265263 + c * 1442695041);
			h = (h ^ (h >> 13)) * 1274126177u;
			h ^= h >> 16;
			return (h & 0xFFFFFF) / (float)0xFFFFFF;
		}
	}

	private bool WallCovers(int side, float z)
	{
		if (z > WallStartZ + 0.001f) return false;
		if (Ruined && side == CrumbleSide && z < CrumbleZ - 0.25f) return false;
		return true;
	}

	/// <summary>Wall profile in (z, y), front to back along the top, for the given side.</summary>
	private List<Vector2> WallProfileTop(int side)
	{
		float zTop = TopFrontZ, zBack = BackZ, z0 = WallStartZ;
		var top = new List<Vector2>();
		bool crumbles = Ruined && side == CrumbleSide;
		float zEnd = crumbles ? CrumbleZ : zBack;
		for (float z = z0; z > Mathf.Max(zEnd, zTop) + 0.01f; z -= Run)
			top.Add(new Vector2(z, WallTop(z)));
		if (!crumbles)
		{
			top.Add(new Vector2(zTop, WallTop(zTop)));
			for (float z = zTop - 0.4f; z > zBack + 0.05f; z -= 0.4f) top.Add(new Vector2(z, WallTop(z)));
			top.Add(new Vector2(zBack, WallTop(zBack)));
		}
		else
		{
			// broken end: the top falls away in a rough slope down to tread level
			top.Add(new Vector2(zEnd, WallTop(zEnd)));
			top.Add(new Vector2(zEnd - 0.12f, WallTop(zEnd - 0.12f) - 0.2f));
			top.Add(new Vector2(zEnd - 0.25f, NoseY(zEnd - 0.25f) - 0.1f));
		}
		return top;
	}

	private void BuildWall(int s, Material concrete, Material blocks)
	{
		float xi = s * Hw, xo = s * OuterHalf;
		var top = WallProfileTop(s);
		bool crumbles = Ruined && s == CrumbleSide;
		_m.Mat(blocks);
		var nOut = new Vector3(s, 0, 0);
		for (int j = 0; j < top.Count - 1; j++)
		{
			Vector2 a = top[j], b = top[j + 1];
			// outer face: rows from below ground to the top line
			float[] rowsA = { -Found, 0.05f, 0.45f, a.Y - 0.3f, a.Y };
			float[] rowsB = { -Found, 0.05f, 0.45f, b.Y - 0.3f, b.Y };
			for (int r = 0; r < rowsA.Length - 1; r++)
			{
				float ya0 = Mathf.Min(rowsA[r], a.Y), ya1 = Mathf.Min(rowsA[r + 1], a.Y);
				float yb0 = Mathf.Min(rowsB[r], b.Y), yb1 = Mathf.Min(rowsB[r + 1], b.Y);
				if (ya1 - ya0 < 0.005f && yb1 - yb0 < 0.005f) continue;
				Quad(new Vector3(xo, ya0, a.X), new Vector3(xo, yb0, b.X), new Vector3(xo, yb1, b.X), new Vector3(xo, ya1, a.X), nOut, Kind.Wall);
			}
			// inner face: from below the treads up to the top line
			float ia = Mathf.Min(NoseY(Mathf.Max(a.X, TopFrontZ)) - Rise - 0.06f, a.Y), ib = Mathf.Min(NoseY(Mathf.Max(b.X, TopFrontZ)) - Rise - 0.06f, b.Y);
			if (a.X < TopFrontZ) ia = TotalHeight - 0.1f;
			if (b.X < TopFrontZ) ib = TotalHeight - 0.1f;
			float ma = Mathf.Min(ia + 0.32f, a.Y), mb = Mathf.Min(ib + 0.32f, b.Y);
			Quad(new Vector3(xi, ia, a.X), new Vector3(xi, ib, b.X), new Vector3(xi, mb, b.X), new Vector3(xi, ma, a.X), -nOut, Kind.Wall);
			Quad(new Vector3(xi, ma, a.X), new Vector3(xi, mb, b.X), new Vector3(xi, b.Y, b.X), new Vector3(xi, a.Y, a.X), -nOut, Kind.Wall);
			// top of the wall (under the coping; visible where the coping is missing)
			Vector3 tn = new Vector3(0, b.X - a.X, -(b.Y - a.Y)).Normalized();
			if (tn.Y < 0) tn = -tn;
			Quad(new Vector3(xi, a.Y, a.X), new Vector3(xo, a.Y, a.X), new Vector3(xo, b.Y, b.X), new Vector3(xi, b.Y, b.X), tn, Kind.Wall);
		}
		// back end face (only visible at the crumbled end or beyond the top pier)
		{
			var e = top[^1];
			float yb = crumbles ? e.Y - 0.3f : -Found;
			Quad(new Vector3(xi, yb, e.X), new Vector3(xo, yb, e.X), new Vector3(xo, e.Y, e.X), new Vector3(xi, e.Y, e.X), Vector3.Forward, Kind.Wall);
		}

		// piers: at the foot, and at the top landing
		float xc = s * (Hw + (WallThickness + PierExtra) * 0.5f);
		float pw = WallThickness + PierExtra;
		float z0 = WallStartZ;
		float footTop = WallTop(z0 - PierD) + CopingH + 0.08f;
		OBox(new Vector3(xc, (footTop - Found) * 0.5f, z0 - PierD * 0.5f), Basis.Identity, new Vector3(pw, footTop + Found, PierD), Kind.Wall);
		_m.Mat(concrete);
		OBox(new Vector3(xc, footTop + 0.04f, z0 - PierD * 0.5f), Basis.Identity, new Vector3(pw + 0.07f, 0.08f, PierD + 0.07f), Kind.Coping, true);
		if (!Ruined && s == NewelSide)
		{
			// the newel post: a squat square pier, taller and broader than the other, with a two-step
			// cast-stone cap; the round cap (or its stump) sits on top as its own node (BuildNewel)
			var c = NewelPierCentre;
			float pierTop = NewelPierTop;
			_m.Mat(blocks);
			OBox(new Vector3(c.X, (pierTop - Found) * 0.5f, c.Z), Basis.Identity, new Vector3(NewelW, pierTop + Found, PierD), Kind.Wall);
			_m.Mat(concrete);
			OBox(new Vector3(c.X, pierTop + NewelSlab * 0.5f, c.Z), Basis.Identity, new Vector3(NewelW + 0.07f, NewelSlab, PierD + 0.07f), Kind.Coping, true);
			OBox(new Vector3(c.X, pierTop + NewelSlab + NewelSlab2 * 0.5f, c.Z), Basis.Identity, new Vector3(NewelW - 0.08f, NewelSlab2, PierD - 0.08f), Kind.Coping, true);
		}
		else if (!Ruined)
		{
			float zb = BackZ;
			float topTop = TotalHeight + WallHeight + 0.08f;
			_m.Mat(blocks);
			OBox(new Vector3(xc, (topTop - Found) * 0.5f, zb + PierD * 0.5f), Basis.Identity, new Vector3(pw, topTop + Found, PierD), Kind.Wall);
			_m.Mat(concrete);
			OBox(new Vector3(xc, topTop + 0.04f, zb + PierD * 0.5f), Basis.Identity, new Vector3(pw + 0.07f, 0.08f, PierD + 0.07f), Kind.Coping, true);
		}

		// coping: jointed slabs along the top, from the foot pier to the top pier (or the break)
		_m.Mat(concrete);
		float xm = s * (Hw + WallThickness * 0.5f);
		float cw = WallThickness + CopingOver * 2f;
		float zStart = z0 - PierD;
		float zStop = Ruined ? (crumbles ? CrumbleZ + 0.1f : BackZ) : BackZ + PierD;
		var pts = new List<Vector2> { new(zStart, WallTop(zStart)) };
		if (zStop < TopFrontZ) pts.Add(new Vector2(TopFrontZ, WallTop(TopFrontZ)));
		pts.Add(new Vector2(zStop, WallTop(zStop)));
		int piece = 0;
		for (int j = 0; j < pts.Count - 1; j++)
		{
			Vector2 a = pts[j], b = pts[j + 1];
			float len = a.DistanceTo(b);
			if (len < 0.05f) continue;
			int n = Mathf.Max(1, Mathf.RoundToInt(len / 0.85f));
			for (int q = 0; q < n; q++, piece++)
			{
				Vector2 pa = a.Lerp(b, (float)q / n), pb = a.Lerp(b, (float)(q + 1) / n);
				Vector3 A = new(xm, pa.Y + CopingH * 0.5f, pa.X), B = new(xm, pb.Y + CopingH * 0.5f, pb.X);
				Vector3 dir = (B - A).Normalized();
				Vector3 xAx = Vector3.Up.Cross(dir).Normalized();
				Vector3 yAx = dir.Cross(xAx).Normalized();
				float yaw = (Hash(Seed, piece, 5) - 0.5f) * 0.02f;
				var basis = new Basis(xAx, yAx, dir).Rotated(Vector3.Up, yaw);
				Vector3 c = (A + B) * 0.5f + new Vector3((Hash(Seed, piece, 6) - 0.5f) * 0.012f, (Hash(Seed, piece, 7) - 0.5f) * 0.008f, 0);
				OBox(c, basis, new Vector3(cw, CopingH, (B - A).Length() - 0.012f), Kind.Coping, true);
			}
		}
		if (crumbles)
		{
			// one fallen coping slab lying in the leaves beside the break
			var b = Basis.FromEuler(new Vector3(0.08f, 0.9f * -s, 0.35f * s));
			OBox(new Vector3(s * (OuterHalf + 0.55f), 0.02f, CrumbleZ - 0.4f), b, new Vector3(cw, CopingH, 0.7f), Kind.Coping, true);
		}
	}

	private void BuildDecals(System.Func<int, float, float> treadY)
	{
		var moss = StairTextures.MossMat;
		var leaves = StairTextures.LeafMat;
		int N = Steps;

		void FlatDecal(Material m, Vector3 c, float sx, float sz, float yaw, Color col, Vector2 uv0, Vector2 uv1, float tiltX = 0f)
		{
			_m.Mat(m);
			var b = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, tiltX);
			Vector3 ex = b * new Vector3(sx * 0.5f, 0, 0), ez = b * new Vector3(0, 0, sz * 0.5f);
			Vector3 n = b * Vector3.Up;
			Vector3 p0 = c - ex - ez, p1 = c + ex - ez, p2 = c + ex + ez, p3 = c - ex + ez;
			_m.Tri(p0, p1, p2, col, col, col, n, new Vector2(uv0.X, uv0.Y), new Vector2(uv1.X, uv0.Y), new Vector2(uv1.X, uv1.Y));
			_m.Tri(p0, p2, p3, col, col, col, n, new Vector2(uv0.X, uv0.Y), new Vector2(uv1.X, uv1.Y), new Vector2(uv0.X, uv1.Y));
		}

		void WallDecal(Material m, float x, float nx, float z0, float z1, float y0, float y1, Color col)
		{
			_m.Mat(m);
			var n = new Vector3(nx, 0, 0);
			Vector3 a = new(x, y0, z0), b = new(x, y0, z1), c = new(x, y1, z1), d = new(x, y1, z0);
			_m.Tri(a, b, c, col, col, col, n, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
			_m.Tri(a, c, d, col, col, col, n, new Vector2(0, 1), new Vector2(1, 0), new Vector2(0, 0));
		}

		Vector2 Flip(float r) => r < 0.5f ? new Vector2(0, 0) : new Vector2(1, 0);

		for (int i = PlinthSteps; i < N; i++)
		{
			float zf = -i * Run;
			bool last = i == N - 1;
			float depth = last ? (Ruined ? Run : LandingDepth) : Run;
			// moss in the corners where tread meets wall
			for (int s = -1; s <= 1; s += 2)
			{
				if (!WallCovers(s, zf)) continue;
				if (_rng.Randf() > 0.55f) continue;
				float sx = _rng.RandfRange(0.14f, 0.34f), sz = Mathf.Min(depth, _rng.RandfRange(0.12f, 0.26f));
				float x = s * (Hw - sx * 0.35f);
				float z = zf - depth + sz * 0.5f + _rng.RandfRange(0f, Mathf.Max(0f, depth - sz - 0.03f));
				var col = Gray(_rng.RandfRange(0.5f, 0.75f));
				Vector2 u0 = Flip(_rng.Randf());
				FlatDecal(moss, new Vector3(x, treadY(i, x) + 0.012f, z), sx, sz, _rng.RandfRange(-0.3f, 0.3f), col, u0, new Vector2(1 - u0.X, 1));
				if (_rng.Randf() < 0.5f)
				{
					float y0 = treadY(i, x) - 0.01f;
					WallDecal(moss, s * (Hw - 0.004f), -s, z + sz * 0.5f, z - sz * 0.5f, y0, y0 + _rng.RandfRange(0.08f, 0.2f), col * 0.9f);
				}
			}
			// dead leaves: mostly at the edges and against the next riser
			int count = _rng.Randf() < 0.35f ? _rng.RandiRange(1, 4) : 0;
			if (last) count += 6;
			for (int l = 0; l < count; l++)
			{
				float edgeBias = _rng.Randf();
				float ax = edgeBias < 0.7f ? Hw - _rng.RandfRange(0.03f, 0.3f) : _rng.RandfRange(0f, Hw - 0.1f);
				float x = (_rng.Randf() < 0.5f ? -1 : 1) * ax;
				float z = zf - depth * Mathf.Sqrt(_rng.RandfRange(0.1f, 0.97f));
				float size = _rng.RandfRange(0.08f, 0.13f);
				int cell = _rng.RandiRange(0, 3);
				var uv0 = new Vector2((cell % 2) * 0.5f, (cell / 2) * 0.5f);
				var col = Gray(_rng.RandfRange(0.7f, 1.15f));
				FlatDecal(leaves, new Vector3(x, treadY(i, x) + 0.014f + l * 0.001f, z), size, size, _rng.RandfRange(0, Mathf.Tau), col,
					uv0, uv0 + new Vector2(0.5f, 0.5f), _rng.RandfRange(-0.15f, 0.15f));
			}
		}
		// leaves drifted on the plinth
		for (int l = 0; l < 14; l++)
		{
			int k = _rng.RandiRange(0, PlinthSteps - 1);
			float half = PlinthHalf(k);
			float x = _rng.RandfRange(-half + 0.05f, half - 0.05f);
			float z = -k * Run - _rng.RandfRange(0.03f, Run - 0.03f);
			float size = _rng.RandfRange(0.08f, 0.13f);
			int cell = _rng.RandiRange(0, 3);
			var uv0 = new Vector2((cell % 2) * 0.5f, (cell / 2) * 0.5f);
			FlatDecal(leaves, new Vector3(x, (k + 1) * Rise + 0.03f, z), size, size, _rng.RandfRange(0, Mathf.Tau),
				Gray(_rng.RandfRange(0.7f, 1.1f)), uv0, uv0 + new Vector2(0.5f, 0.5f), _rng.RandfRange(-0.1f, 0.1f));
		}
		// moss creeping up the outer wall faces from the ground
		for (int s = -1; s <= 1; s += 2)
		{
			var top = WallProfileTop(s);
			float zEnd = top[^1].X;
			for (float z = WallStartZ - 0.2f; z > zEnd + 0.3f; z -= _rng.RandfRange(0.5f, 1.4f))
			{
				if (_rng.Randf() < 0.35f) continue;
				float len = _rng.RandfRange(0.4f, 1.0f);
				float h = _rng.RandfRange(0.25f, 0.7f);
				WallDecal(moss, s * (OuterHalf + 0.004f), s, z, z - len, -0.1f, h, Gray(_rng.RandfRange(0.55f, 0.8f)));
			}
			// moss on the coping tops
			for (float z = WallStartZ - PierD - 0.2f; z > zEnd + 0.2f; z -= _rng.RandfRange(0.6f, 1.6f))
			{
				if (_rng.Randf() < 0.7f) continue;
				float len = _rng.RandfRange(0.2f, 0.45f);
				float zc = z - len * 0.5f;
				float y = WallTop(zc) + CopingH + 0.006f;
				float slope = zc > TopFrontZ ? Mathf.Atan2(Rise, Run) : 0f;
				FlatDecal(moss, new Vector3(s * (Hw + WallThickness * 0.5f), y, zc), WallThickness + 0.05f, len, 0f,
					Gray(_rng.RandfRange(0.55f, 0.8f)), new Vector2(0, 0), new Vector2(1, 1), slope);
			}
		}
	}

	// ------------------------------------------------------------------ newel post

	/// <summary>The newel post stands at the top of this cheek wall (+1 = right, looking up the flight).</summary>
	private const int NewelSide = 1;
	/// <summary>Pier width across the flight (it grows outward from the wall's inner face), how much taller
	/// it stands than the other landing pier, and its two cast-stone cap slabs.</summary>
	private const float NewelW = 0.38f, NewelRaise = 0.2f, NewelSlab = 0.08f, NewelSlab2 = 0.04f;
	private float NewelPierTop => TotalHeight + WallHeight + NewelRaise;
	private Vector3 NewelPierCentre => new(NewelSide * (Hw + NewelW * 0.5f), 0f, BackZ + PierD * 0.5f);
	/// <summary>Where the round cap seats (local): the top of the upper slab, centred on the pier.</summary>
	public Vector3 NewelSeatLocal => NewelPierCentre + new Vector3(0f, NewelPierTop + NewelSlab + NewelSlab2, 0f);
	/// <summary>The cap's seat in world space: the cap mesh (<see cref="NewelCapMesh"/>) placed with this transform sits exactly where the pier's own cap does.</summary>
	public Transform3D NewelSeatGlobal => GlobalTransform * new Transform3D(Basis.Identity, NewelSeatLocal);
	public bool HasNewel => !Ruined;

	private Node3D _newel;
	private MeshInstance3D _capNode;

	private void BuildNewel(Node3D gen)
	{
		_newel = new Node3D { Name = "Newel", Position = NewelSeatLocal };
		gen.AddChild(_newel);
		var k = new MeshKit();
		BuildNewelStump(k);
		k.CommitTo(_newel, "Stump");
		_capNode = new MeshInstance3D { Name = "Cap", Mesh = NewelCapMesh, Visible = _newelCapped };
		_newel.AddChild(_capNode);
	}

	// The cap, in "cap space": y = 0 on the seat (the top of the pier's upper slab). A round spigot
	// rises from the seat and snapped part way up; above the break: the rest of the spigot, a small
	// square plinth, a neck, and the ball. The break height wanders around the spigot, and the stump's
	// top and the cap's underside are the same surface, so the cap sits back on exactly.
	private const int CapSides = 12;
	private const float SpigotR = 0.07f, SpigotTop = 0.065f, BreakCentreY = 0.04f;
	private const float CapPlinthW = 0.19f, CapPlinthTop = 0.1f, CapStepW = 0.16f, CapStepTop = 0.109f;
	private const float BallR = 0.1f, BallY = 0.223f;

	/// <summary>Height of the break (cap space) at angle <paramref name="a"/> around the spigot.</summary>
	private static float BreakY(float a) => 0.036f + 0.013f * Mathf.Sin(3f * a + 1.3f) + 0.007f * Mathf.Sin(7f * a + 0.4f) + 0.004f * Mathf.Sin(11f * a + 2.1f);
	private static Vector3 BreakPt(int i) { float a = Mathf.Tau * i / CapSides; return new Vector3(Mathf.Cos(a) * SpigotR, BreakY(a), Mathf.Sin(a) * SpigotR); }
	/// <summary>The lowest point of the cap's broken underside (cap space): where it rests when set down on a table.</summary>
	public static float NewelBreakLowY
	{
		get { float lo = BreakCentreY; for (int i = 0; i < CapSides; i++) lo = Mathf.Min(lo, BreakPt(i).Y); return lo; }
	}
	/// <summary>Full height of the cap above its lowest break point.</summary>
	public static float NewelCapHeight => BallY + BallR - NewelBreakLowY;

	// Weathered cast stone, painted like the coping: pale above, damp and dark underneath, a little lichen on top.
	private static readonly Color CapStone = new(0.93f, 0.92f, 0.88f);
	private static readonly Color CapUnder = new(0.6f, 0.6f, 0.57f);
	private static readonly Color CapLichen = new(0.7f, 0.74f, 0.58f);
	private static readonly Color FreshBreak = new(0.92f, 0.9f, 0.85f);

	private static ArrayMesh _capMesh;
	/// <summary>The cap as one mesh (cap space), shared by every staircase and by the flying cap in Act 6.</summary>
	public static ArrayMesh NewelCapMesh
	{
		get
		{
			if (_capMesh != null) return _capMesh;
			var k = new MeshKit();
			BuildNewelCap(k);
			_capMesh = k.Commit();
			return _capMesh;
		}
	}

	/// <summary>The round stone cap, broken off its spigot, in cap space (see above); honours <see cref="MeshKit.Xf"/>.
	/// The item on R.H.'s table is this, set down on its break.</summary>
	public static void BuildNewelCap(MeshKit k)
	{
		// the upper part of the spigot, from the break up into the plinth
		k.Mat(StairTextures.ConcreteMat);
		k.Color = CapUnder;
		for (int i = 0; i < CapSides; i++)
		{
			Vector3 a = BreakPt(i), b = BreakPt(i + 1);
			Vector3 na = new Vector3(a.X, 0, a.Z).Normalized(), nb = new Vector3(b.X, 0, b.Z).Normalized();
			Vector3 a1 = new(a.X, SpigotTop, a.Z), b1 = new(b.X, SpigotTop, b.Z);
			float u0 = (float)i / CapSides * 0.43f, u1 = (float)(i + 1) / CapSides * 0.43f;
			k.Tri(a, b, b1, na, nb, nb, new Vector2(u0, a.Y), new Vector2(u1, b.Y), new Vector2(u1, SpigotTop));
			k.Tri(a, b1, a1, na, nb, na, new Vector2(u0, a.Y), new Vector2(u1, SpigotTop), new Vector2(u0, SpigotTop));
		}
		// the square plinth and a small step above it
		k.Color = CapStone * 0.9f;
		k.Box(new Vector3(0, (SpigotTop + CapPlinthTop) * 0.5f, 0), new Vector3(CapPlinthW, CapPlinthTop - SpigotTop, CapPlinthW), 1f);
		k.Color = CapStone * 0.95f;
		k.Box(new Vector3(0, (CapPlinthTop + CapStepTop) * 0.5f, 0), new Vector3(CapStepW, CapStepTop - CapPlinthTop, CapStepW), 1f);
		// the neck, a ring, and the ball
		var prof = new List<Vector2> { new(0.052f, CapStepTop), new(0.042f, 0.118f), new(0.036f, 0.126f), new(0.047f, 0.133f), new(0.047f, 0.138f) };
		foreach (float deg in new[] { -58f, -40f, -20f, 0f, 20f, 40f, 58f, 74f, 84f })
		{
			float t = Mathf.DegToRad(deg);
			prof.Add(new Vector2(BallR * Mathf.Cos(t), BallY + BallR * Mathf.Sin(t)));
		}
		Color Tone(int r, float ang)
		{
			if (ang < 0f) return CapStone;                           // the little flat top
			float y = prof[Mathf.Min(r + 1, prof.Count - 1)].Y;
			float up = Mathf.Clamp((y - BallY) / BallR, -1f, 1f);    // -1 under the ball .. 1 on top
			var c = CapUnder.Lerp(CapStone, Mathf.SmoothStep(-0.9f, 0.1f, up));
			float lichen = Mathf.Max(0f, up - 0.35f) * (0.6f + 0.4f * Mathf.Sin(ang * 3f + 0.7f));
			return c.Lerp(CapLichen, Mathf.Clamp(lichen, 0f, 0.55f)) * (0.96f + 0.04f * Mathf.Sin(ang * 5f + r));
		}
		ItemMeshes.Lathe(k, prof, CapSides, false, true, 1f, 1f, 1.5f, Tone);
		// the break: pale fresh stone, facing down
		k.Mat(StairTextures.BreakMat);
		k.Color = FreshBreak;
		BreakFan(k, Vector3.Down);
		k.Color = Colors.White;
	}

	/// <summary>What is left on the pier when the cap is gone: the spigot's stump with a jagged, pale top.</summary>
	public static void BuildNewelStump(MeshKit k)
	{
		k.Mat(StairTextures.ConcreteMat);
		k.Color = CapUnder * 1.1f;
		for (int i = 0; i < CapSides; i++)
		{
			Vector3 a = BreakPt(i), b = BreakPt(i + 1);
			Vector3 na = new Vector3(a.X, 0, a.Z).Normalized(), nb = new Vector3(b.X, 0, b.Z).Normalized();
			Vector3 a0 = new(a.X, -0.01f, a.Z), b0 = new(b.X, -0.01f, b.Z);
			float u0 = (float)i / CapSides * 0.43f, u1 = (float)(i + 1) / CapSides * 0.43f;
			k.Tri(a0, b0, b, na, nb, nb, new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, b.Y));
			k.Tri(a0, b, a, na, nb, na, new Vector2(u0, 0), new Vector2(u1, b.Y), new Vector2(u0, a.Y));
		}
		k.Mat(StairTextures.BreakMat);
		k.Color = FreshBreak;
		BreakFan(k, Vector3.Up);
		k.Color = Colors.White;
	}

	private static void BreakFan(MeshKit k, Vector3 facing)
	{
		var c = new Vector3(0, BreakCentreY, 0);
		for (int i = 0; i < CapSides; i++)
		{
			Vector3 a = BreakPt(i), b = BreakPt(i + 1);
			Vector3 n = (a - c).Cross(b - c).Normalized();
			if (n.Dot(facing) < 0f) n = -n;
			Vector2 U(Vector3 p) => new(0.5f + p.X / (SpigotR * 2.2f), 0.5f + p.Z / (SpigotR * 2.2f));
			k.Tri(c, a, b, n, U(c), U(a), U(b));
		}
	}

	private void BuildRubble(Node3D gen)
	{
		var k = new MeshKit();
		int s = CrumbleSide;
		// each block lies on the ground where it fell (the flight's foot is level, the ground beside it may not be)
		var terrain = !Engine.IsEditorHint() && IsInsideTree() ? GroundSnap.FindTerrain(this) : null;
		float Gnd(float x, float z)
		{
			if (terrain == null) return 0f;
			Vector3 w = GlobalTransform * new Vector3(x, 0, z);
			return ToLocal(new Vector3(w.X, terrain.HeightAt(w.X, w.Z), w.Z)).Y;
		}
		k.Mat(StairTextures.ConcreteMat);
		for (int r = 0; r < 7; r++)
		{
			float z = CrumbleZ - _rng.RandfRange(0.1f, 1.6f);
			float x = s * (OuterHalf + _rng.RandfRange(0.05f, 0.9f));
			float sz = _rng.RandfRange(0.1f, 0.24f);
			float g = _rng.RandfRange(0.55f, 0.8f);
			k.Color = new Color(g, g * 0.99f, g * 0.93f);
			k.Blob(new Vector3(x, Gnd(x, z) + sz * 0.3f, z), new Vector3(sz * 1.3f, sz * 0.8f, sz), Seed * 13 + r, 0.25f, true, 1f, 0.35f);
		}
		k.CommitTo(gen, "Rubble");
	}

	private void BuildColliders(Node3D gen)
	{
		float H = TotalHeight, zTop = TopFrontZ, zBack = BackZ;
		var body = new StaticBody3D { Name = "StairBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		body.SetMeta("stair_owner", GetPath());   // the clearing reads which flight the player's feet are on (Act 6)
		gen.AddChild(body);

		// ramp through the step noses, over the whole clear width, down to the landing
		var ramp = new List<Vector3>();
		foreach (float x in new[] { -Hw - 0.02f, Hw + 0.02f })
		{
			ramp.Add(new Vector3(x, 0, Run));
			ramp.Add(new Vector3(x, H, zTop));
			ramp.Add(new Vector3(x, H, zBack));
			ramp.Add(new Vector3(x, -Found, zBack));
			ramp.Add(new Vector3(x, -Found, Run + Mathf.Min(Found, 0.6f) * ApronRun));   // an apron at the stairs' own pitch: no lip where the ground in front sits lower
		}
		body.AddChild(new CollisionShape3D { Name = "Ramp", Shape = new ConvexPolygonShape3D { Points = ramp.ToArray() } });

		// the plinth: one gentle wedge over its full width
		float ph = PlinthHalf(0), pTop = PlinthSteps * Rise, zpf = -(PlinthSteps - 1) * Run;
		var pl = new List<Vector3>();
		foreach (float x in new[] { -ph, ph })
		{
			pl.Add(new Vector3(x, 0, Run));
			pl.Add(new Vector3(x, pTop, zpf));
			pl.Add(new Vector3(x, pTop, PlinthBackZ));
			pl.Add(new Vector3(x, -Found, PlinthBackZ));
			pl.Add(new Vector3(x, -Found, Run + Mathf.Min(Found, 0.6f) * ApronRun));
		}
		body.AddChild(new CollisionShape3D { Name = "Plinth", Shape = new ConvexPolygonShape3D { Points = pl.ToArray() } });

		// cheek walls: profile extruded across the wall thickness, taller than they look (0.4 m) so nobody hops out
		for (int s = -1; s <= 1; s += 2)
		{
			var top = WallProfileTop(s);
			var pts = new List<Vector3>();
			foreach (float x in new[] { s * (Hw - 0.01f), s * (OuterHalf + PierExtra) })
			{
				pts.Add(new Vector3(x, -Found, top[0].X));
				foreach (var t in top) pts.Add(new Vector3(x, t.Y + CopingH + 0.4f, t.X));
				pts.Add(new Vector3(x, -Found, top[^1].X));
			}
			body.AddChild(new CollisionShape3D { Name = s < 0 ? "WallL" : "WallR", Shape = new ConvexPolygonShape3D { Points = pts.ToArray() } });
		}

		// the newel pier's outer part (the wall's collider already covers its inner part)
		if (!Ruined)
		{
			var c = NewelPierCentre;
			float top = NewelPierTop + NewelSlab;
			body.AddChild(new CollisionShape3D
			{
				Name = "NewelPier", Position = new Vector3(c.X, (top - Found) * 0.5f, c.Z),
				Shape = new BoxShape3D { Size = new Vector3(NewelW, top + Found, PierD) },
			});
		}

		// invisible guard at the top edge: the stairs end, the player doesn't fall off them
		if (!Ruined)
			body.AddChild(new CollisionShape3D
			{
				Name = "TopGuard", Position = new Vector3(0, H + 0.6f, zBack + 0.05f),
				Shape = new BoxShape3D { Size = new Vector3(OuterHalf * 2f, 1.2f, 0.1f) },
			});
	}
}
