using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Act 1 wayfinding from the fallen tree that blocks the main trail, across trackless
/// woods to the main stairs, with nothing
/// overt: the friend's things dropped along the way (a fleece hat snagged on a dead
/// sapling, a dented water bottle, a torn map corner and a receipt, one work glove, a
/// snapped trekking pole, a boot print in a patch of mud), and old stonework in the
/// stairs' own weathered stone: a few sunken, moss-covered pavers at first, more and
/// more intact toward the stairs, with a crumbled low retaining edge along one side.
/// Each belonging is visible from the one before.
/// (forest_world sets no belongings: past the fallen tree the way is trackless, with
/// only the rare stones and the forest going quiet to go by.)
///
/// Purely decorative: no collision, no interaction (so nothing blocks the autotest
/// route and nothing changes in the story). Keeps trees out of a narrow corridor
/// along the line (ClearZones, created in _Ready, before ForestScatter's deferred build)
/// while leaving the forest close either side, so the stairs stay hidden until near.
///
/// <see cref="Waypoints"/> are world XZ (x, z), from the path end to the stairs' foot.
/// The retaining edge runs on the <see cref="EdgeSide"/> side (-1 = left of travel).
/// </summary>
[GlobalClass]
public partial class FriendTrail : Node3D
{
	[Export] public Vector2[] Waypoints = System.Array.Empty<Vector2>();
	[Export] public int EdgeSide = -1;
	/// <summary>Radius kept clear of trees round each belonging, so it can be seen (m). No lane along the line.</summary>
	[Export] public float CorridorRadius = 1.2f;
	/// <summary>Distances (m along the line) of the belongings, in order: hat, bottle, map+receipt, glove, pole, then boot prints for every entry after that.</summary>
	[Export] public float[] ItemAt = { 8f, 18f, 28.5f, 38f, 47.5f, 56.5f };
	/// <summary>Keep the edge stones this far from any point of these world XZ points (the autotest route).</summary>
	[Export] public NodePath AvoidPath = "";
	[Export] public int Seed = 71;

	private ForestTerrain _terrain;
	private readonly List<Vector2> _pts = new();
	private readonly List<float> _cum = new();
	private readonly List<Vector2> _avoid = new();

	public float Length => _cum.Count > 0 ? _cum[^1] : 0f;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		_terrain = GroundSnap.FindTerrain(this);
		if (_terrain == null || Waypoints.Length < 2) return;
		_pts.AddRange(Waypoints);
		_cum.Add(0f);
		for (int i = 1; i < _pts.Count; i++) _cum.Add(_cum[^1] + _pts[i].DistanceTo(_pts[i - 1]));
		if (!AvoidPath.IsEmpty && GetNodeOrNull<Path3D>(AvoidPath) is { Curve: not null } ap)
			foreach (var p in ap.Curve.GetBakedPoints()) { var w = ap.GlobalTransform * p; _avoid.Add(new Vector2(w.X, w.Z)); }

		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		BuildCorridor();
		BuildStonework();
		BuildBelongings();
	}

	// ------------------------------------------------------------------ line helpers

	/// <summary>World XZ at distance s along the line, and the unit travel direction there.</summary>
	public Vector2 At(float s, out Vector2 dir)
	{
		s = Mathf.Clamp(s, 0f, Length);
		for (int i = 1; i < _pts.Count; i++)
			if (s <= _cum[i] || i == _pts.Count - 1)
			{
				float seg = _cum[i] - _cum[i - 1];
				float t = seg > 0 ? (s - _cum[i - 1]) / seg : 0f;
				dir = (_pts[i] - _pts[i - 1]).Normalized();
				return _pts[i - 1].Lerp(_pts[i], Mathf.Clamp(t, 0, 1));
			}
		dir = Vector2.Down;
		return _pts[^1];
	}

	private Vector3 Ground(Vector2 p) => new(p.X, _terrain.HeightAt(p.X, p.Y), p.Y);
	private static Vector2 Left(Vector2 dir) => new(dir.Y, -dir.X);   // left of travel, seen from above (x right, z toward viewer)

	private float AvoidDistance(Vector2 p)
	{
		float best = float.MaxValue;
		foreach (var a in _avoid) best = Mathf.Min(best, a.DistanceSquaredTo(p));
		return Mathf.Sqrt(best);
	}

	/// <summary>Basis sitting on the terrain slope at p, yawed to face yaw (radians about Y).</summary>
	private Basis OnSlope(Vector2 p, float yaw)
	{
		Vector3 n = _terrain.NormalAt(p.X, p.Y);
		Basis yawB = new(Vector3.Up, yaw);
		Vector3 fwd = yawB * Vector3.Back;
		Vector3 right = n.Cross(fwd).Normalized();
		fwd = right.Cross(n).Normalized();
		return new Basis(right, n, fwd);
	}

	private void BuildCorridor()
	{
		foreach (float s in ItemAt)
		{
			var p = At(s, out _);
			var cz = new ClearZone { Radius = CorridorRadius, ClearFoliage = true, Name = $"ClearItem{(int)s}" };
			AddChild(cz);
			cz.GlobalPosition = Ground(p);
		}
	}

	// ------------------------------------------------------------------ stonework

	private void BuildStonework()
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)Seed };
		var k = new MeshKit();
		var slab = StairTextures.ConcreteMat;
		var block = StairTextures.BlockMat;
		var moss = StairTextures.MossMat;
		var mossTint = new Color(0.44f, 0.50f, 0.33f);
		float L = Length;

		// pavers: two across the old path, sparse and broken at first, nearly continuous at the end
		for (float s = 1.5f; s < L - 0.4f; s += 1.5f)
		{
			float t = s / L;
			var c = At(s, out Vector2 dir);
			Vector2 left = Left(dir);
			for (int lane = 0; lane < 2; lane++)
			{
				float p = 0.02f + 0.55f * t * t * t * t;   // a stray stone now and then; only near the stairs any real run of them
				if (rng.Randf() > p) continue;
				bool broken = rng.Randf() > 0.25f + 0.65f * t;
				float w = broken ? rng.RandfRange(0.22f, 0.42f) : 0.56f;
				float d = broken ? rng.RandfRange(0.2f, 0.4f) : 0.5f;
				Vector2 pos = c + left * ((lane - 0.5f) * 0.6f + rng.RandfRange(-0.06f, 0.06f)) + dir * rng.RandfRange(-0.05f, 0.05f);
				float yaw = Mathf.Atan2(dir.X, dir.Y) + rng.RandfRange(-0.08f, 0.08f) * (broken ? 4f : 1f);
				var b = OnSlope(pos, yaw) * Basis.FromEuler(new Vector3(rng.RandfRange(-0.05f, 0.05f), 0, rng.RandfRange(-0.05f, 0.05f)) * (broken ? 2f : 1f));
				// sunk: most of the slab below the litter, a 2-5 cm lip showing
				float lip = broken ? rng.RandfRange(0.0f, 0.02f) : rng.RandfRange(0.012f, 0.035f);
				Vector3 top = Ground(pos) + b.Y * lip;
				float g = rng.RandfRange(0.32f, 0.42f) + 0.06f * t;
				float m = Mathf.Clamp(0.7f - 0.3f * t + rng.RandfRange(-0.15f, 0.15f), 0.3f, 0.85f);
				k.Mat(slab);
				k.Color = new Color(g, g, g * 0.98f).Lerp(mossTint * 0.55f, m);
				BuildKit.Box(k, top - b.Y * 0.06f, new Vector3(w, 0.12f, d), 1f, BuildKit.Face.NY, b);
				// moss creeping over the top
				if (rng.Randf() < 0.6f)
				{
					k.Mat(moss);
					k.Color = mossTint * 0.7f;
					Flat(k, top + b.Y * 0.004f, b, w * rng.RandfRange(0.9f, 1.3f), d * rng.RandfRange(0.9f, 1.3f), rng.Randf() < 0.5f);
				}
			}
		}

		// crumbled low retaining edge along one side: starts after a third of the way, rising and closing up toward the stairs
		for (float s = L * 0.7f; s < L - 0.5f; s += 0.7f)
		{
			float t = s / L;
			if (rng.Randf() > 0.05f + 0.7f * t * t) continue;          // gaps where it has collapsed
			var c = At(s, out Vector2 dir);
			Vector2 pos = c + Left(dir) * EdgeSide * -1f * 1.05f;
			if (_avoid.Count > 0 && AvoidDistance(pos) < 1.6f) continue;
			float h = Mathf.Lerp(0.12f, 0.42f, t) * rng.RandfRange(0.6f, 1f);
			bool fallen = rng.Randf() > 0.35f + 0.6f * t;
			float yaw = Mathf.Atan2(dir.X, dir.Y);
			var b = new Basis(Vector3.Up, yaw + (fallen ? rng.RandfRange(-0.6f, 0.6f) : rng.RandfRange(-0.04f, 0.04f)));
			if (fallen) b *= Basis.FromEuler(new Vector3(rng.RandfRange(-0.4f, 0.4f), 0, rng.RandfRange(-0.5f, 0.5f)));
			float gy = _terrain.HeightAt(pos.X, pos.Y);
			Vector3 center = new(pos.X, gy + (fallen ? 0.06f : h * 0.5f - 0.1f), pos.Y);
			float g = rng.RandfRange(0.3f, 0.4f);
			k.Mat(block);
			k.Color = new Color(g, g * 0.99f, g * 0.95f).Lerp(new Color(0.30f, 0.36f, 0.24f), Mathf.Clamp(0.45f - 0.2f * t + rng.RandfRange(-0.1f, 0.1f), 0.1f, 0.7f));
			Vector3 size = new(0.3f, fallen ? 0.22f : h + 0.1f, 0.48f);
			BuildKit.Box(k, center, size, 1f, BuildKit.Face.NY, b);
			if (!fallen && rng.Randf() < 0.55f)
			{
				// coping stone on the intact stretches, mossy on top
				k.Mat(slab);
				k.Color = new Color(0.4f, 0.39f, 0.37f).Lerp(mossTint * 0.6f, 0.55f);
				BuildKit.Box(k, center + b.Y * (size.Y * 0.5f + 0.03f), new Vector3(0.36f, 0.06f, 0.5f), 1f, BuildKit.Face.NY, b);
			}
		}
		k.Color = Colors.White;
		var mi = k.CommitTo(this, "Stonework");
		mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
	}

	/// <summary>Flat alpha-scissor decal quad (moss/leaves) in a basis' XZ plane.</summary>
	private static void Flat(MeshKit k, Vector3 c, Basis b, float sx, float sz, bool flip)
	{
		Vector3 ex = b.X * sx * 0.5f, ez = b.Z * sz * 0.5f;
		Vector2 u0 = flip ? new Vector2(1, 0) : new Vector2(0, 0), u1 = flip ? new Vector2(0, 1) : new Vector2(1, 1);
		k.Quad(c - ex - ez, c + ex - ez, c + ex + ez, c - ex + ez, b.Y,
			new Vector2(u0.X, u0.Y), new Vector2(u1.X, u0.Y), new Vector2(u1.X, u1.Y), new Vector2(u0.X, u1.Y));
	}

	// ------------------------------------------------------------------ the friend's things

	private void BuildBelongings()
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(Seed * 7 + 1) };
		for (int i = 0; i < ItemAt.Length; i++)
		{
			var c = At(ItemAt[i], out Vector2 dir);
			// a little off the centre line, alternating sides, as if dropped while walking
			Vector2 p = c + Left(dir) * (i % 2 == 0 ? 0.45f : -0.5f) * (i == 0 ? 2.2f : 1f);
			var node = new Node3D { Name = $"Belonging{i}" };
			AddChild(node);
			node.GlobalTransform = new Transform3D(OnSlope(p, rng.RandfRange(0f, Mathf.Tau)), Ground(p));
			var k = new MeshKit();
			switch (i)
			{
				case 0: HatOnSnag(k, node, p); break;
				case 1: WaterBottle(k); break;
				case 2: MapCornerAndReceipt(k, rng); break;
				case 3: Glove(k); break;
				case 4: SnappedPole(k); break;
				default: BootPrint(k); break;
			}
			k.Color = Colors.White;
			k.CommitTo(node, "Mesh");
		}
	}

	private static StandardMaterial3D Plain(string key, Color c, float rough = 0.9f, float spec = 0.25f)
	{
		var m = BuildingTextures.Plain(key, c, rough);
		m.MetallicSpecular = spec;
		return m;
	}

	/// <summary>A dead sapling beside the line with a faded red fleece hat snagged on a broken twig at chest height.</summary>
	private void HatOnSnag(MeshKit k, Node3D node, Vector2 p)
	{
		// the snag stands upright regardless of the slope basis
		k.Xf = new Transform3D(node.GlobalBasis.Inverse(), Vector3.Zero);
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.55f, 0.5f, 0.45f);
		Vector3 top = new(0.12f, 1.95f, -0.05f);
		k.Cylinder(new Vector3(0, -0.1f, 0), top, 0.045f, 0.018f, 5, true, 2f);
		Vector3 twigBase = new Vector3(0, -0.1f, 0).Lerp(top, 0.66f);
		Vector3 twigEnd = twigBase + new Vector3(0.34f, 0.12f, 0.1f);
		k.Cylinder(twigBase, twigEnd, 0.014f, 0.006f, 4, true, 3f);
		k.Cylinder(new Vector3(0, -0.1f, 0).Lerp(top, 0.4f), new Vector3(0, -0.1f, 0).Lerp(top, 0.4f) + new Vector3(-0.22f, 0.2f, -0.12f), 0.012f, 0.005f, 4, true, 3f);
		// the hat: a squashed fleece dome hanging off the twig, rolled brim, slumped
		k.Mat(Plain("ft_fleece", new Color(0.62f, 0.2f, 0.15f), 1f, 0.05f));
		k.Color = Colors.White;
		Vector3 hc = twigBase.Lerp(twigEnd, 0.75f) + new Vector3(0, -0.07f, 0);
		k.Blob(hc, new Vector3(0.1f, 0.085f, 0.1f), 311, 0.12f, false, 3f, 0.35f);
		k.Color = new Color(0.8f, 0.8f, 0.8f);
		k.Cylinder(hc + new Vector3(0, -0.07f, 0), hc + new Vector3(0.01f, -0.035f, 0.005f), 0.105f, 0.1f, 8, false, 3f);
		k.Xf = Transform3D.Identity;
	}

	/// <summary>A 1 L metal water bottle on its side, dented, faded blue paint worn to bare metal in places.</summary>
	private static void WaterBottle(MeshKit k)
	{
		var paint = Plain("ft_bottle", new Color(0.36f, 0.5f, 0.58f), 0.55f, 0.4f);
		var metal = ProcTextures.MetalMat;
		float r = 0.046f, y = r * 0.92f;
		var yaw = new Basis(Vector3.Up, 0.6f);
		k.Xf = new Transform3D(yaw, Vector3.Zero);
		k.Mat(paint);
		k.Color = Colors.White;
		k.Cylinder(new Vector3(-0.14f, y, 0), new Vector3(0.0f, y, 0), r, r, 8, false, 4f);
		k.Cylinder(new Vector3(0.0f, y - 0.004f, 0), new Vector3(0.04f, y - 0.006f, 0.004f), r * 0.86f, r * 0.9f, 8, false, 4f);   // the dent
		k.Cylinder(new Vector3(0.04f, y, 0), new Vector3(0.11f, y, 0), r, r * 0.95f, 8, false, 4f);
		k.Mat(metal);
		k.Color = new Color(0.9f, 0.9f, 0.88f);
		k.Cylinder(new Vector3(-0.155f, y, 0), new Vector3(-0.14f, y, 0), r * 0.9f, r, 8, true, 4f);
		k.Cylinder(new Vector3(0.11f, y, 0), new Vector3(0.15f, y, 0), r * 0.95f, r * 0.5f, 8, false, 4f);
		k.Color = new Color(0.25f, 0.25f, 0.26f);
		k.Cylinder(new Vector3(0.15f, y, 0), new Vector3(0.175f, y, 0), r * 0.52f, r * 0.52f, 8, true, 4f);
		k.Xf = Transform3D.Identity;
	}

	/// <summary>A torn corner of the park trail map and a crumpled receipt, pale against the litter.</summary>
	private static void MapCornerAndReceipt(MeshKit k, RandomNumberGenerator rng)
	{
		k.Mat(ProcTextures.MapMat);
		k.Color = new Color(0.9f, 0.88f, 0.8f);
		Vector3 a = new(-0.14f, 0.012f, -0.1f), b = new(0.17f, 0.018f, -0.12f), c = new(-0.12f, 0.05f, 0.19f), d = new(0.05f, 0.015f, 0.04f);
		k.Tri(a, b, d, Vector3.Up, new Vector2(0.7f, 0.1f), new Vector2(0.98f, 0.08f), new Vector2(0.82f, 0.35f));
		k.Tri(a, d, c, Vector3.Up, new Vector2(0.7f, 0.1f), new Vector2(0.82f, 0.35f), new Vector2(0.72f, 0.42f));
		k.Mat(ProcTextures.PaperMat);
		k.Color = new Color(0.95f, 0.94f, 0.9f);
		var rot = new Basis(Vector3.Up, 1.1f);
		Vector3 o = new(0.34f, 0.01f, 0.08f);
		Vector3 P(float x, float y, float z) => o + rot * new Vector3(x, y, z);
		k.Quad(P(-0.035f, 0.004f, -0.09f), P(0.035f, 0.02f, -0.09f), P(0.035f, 0.005f, 0.0f), P(-0.035f, 0.012f, 0.0f), Vector3.Up);
		k.Quad(P(-0.035f, 0.012f, 0.0f), P(0.035f, 0.005f, 0.0f), P(0.035f, 0.03f, 0.08f), P(-0.035f, 0.018f, 0.08f), Vector3.Up);
	}

	/// <summary>One tan leather work glove, lying palm-down, fingers curled a little.</summary>
	private static void Glove(MeshKit k)
	{
		var leather = Plain("ft_glove", new Color(0.62f, 0.47f, 0.3f), 0.9f, 0.15f);
		k.Mat(leather);
		k.Color = Colors.White;
		k.Xf = new Transform3D(new Basis(Vector3.Up, -0.4f), Vector3.Zero);
		BuildKit.Box(k, new Vector3(0, 0.022f, 0), new Vector3(0.1f, 0.035f, 0.11f), 6f, BuildKit.Face.NY);
		BuildKit.Box(k, new Vector3(0, 0.02f, 0.085f), new Vector3(0.11f, 0.03f, 0.06f), 6f, BuildKit.Face.NY);   // cuff
		for (int f = 0; f < 4; f++)
		{
			float x = -0.036f + f * 0.024f;
			float len = f == 0 || f == 3 ? 0.06f : 0.075f;
			k.Beam(new Vector3(x, 0.02f, -0.055f), new Vector3(x + (f - 1.5f) * 0.006f, 0.012f + (f == 2 ? 0.012f : 0f), -0.055f - len), 0.021f, 0.02f, 6f);
		}
		k.Beam(new Vector3(0.05f, 0.02f, -0.01f), new Vector3(0.085f, 0.013f, -0.05f), 0.022f, 0.02f, 6f);
		k.Xf = Transform3D.Identity;
	}

	/// <summary>A trekking pole snapped in two: grip half and the bent lower section a little apart.</summary>
	private static void SnappedPole(MeshKit k)
	{
		var alu = ProcTextures.MetalMat;
		var grip = Plain("ft_grip", new Color(0.12f, 0.12f, 0.12f), 0.95f, 0.1f);
		var band = Plain("ft_band", new Color(0.6f, 0.34f, 0.12f), 0.8f, 0.2f);
		float r = 0.011f;
		k.Mat(alu);
		k.Color = new Color(0.95f, 0.95f, 0.95f);
		Vector3 a0 = new(-0.62f, r, 0.02f), a1 = new(0.02f, r, -0.03f);
		k.Cylinder(a0, a1, r, r * 0.9f, 6, false, 5f);
		k.Mat(grip);
		k.Color = Colors.White;
		k.Cylinder(a0 + new Vector3(-0.14f, 0.004f, 0), a0, 0.018f, 0.016f, 6, true, 5f);
		k.Mat(band);
		k.Cylinder(a0.Lerp(a1, 0.35f), a0.Lerp(a1, 0.4f), r * 1.15f, r * 1.15f, 6, false, 5f);
		// lower half, bent at the break, basket and tip
		k.Mat(alu);
		k.Color = new Color(0.85f, 0.85f, 0.85f);
		Vector3 b0 = new(0.1f, r, 0.06f), bk = new(0.2f, r + 0.02f, 0.1f), b1 = new(0.62f, r * 0.8f, 0.3f);
		k.Cylinder(b0, bk, r * 0.8f, r * 0.8f, 6, true, 5f);
		k.Cylinder(bk, b1, r * 0.8f, r * 0.7f, 6, false, 5f);
		k.Mat(grip);
		k.Color = Colors.White;
		k.Cylinder(b1.Lerp(bk, 0.12f) + new Vector3(0, 0.02f, 0), b1.Lerp(bk, 0.12f) + new Vector3(0, -0.005f, 0), 0.03f, 0.03f, 6, true, 5f);
	}

	/// <summary>A boot print pressed into a small patch of wet mud: dark, glossy, the lug pattern darker still.</summary>
	private static void BootPrint(MeshKit k)
	{
		var mud = Plain("ft_mud", new Color(0.12f, 0.095f, 0.075f), 0.35f, 0.45f);
		var print = Plain("ft_print", new Color(0.06f, 0.045f, 0.035f), 0.25f, 0.5f);
		k.Mat(mud);
		k.Color = Colors.White;
		// irregular mud patch (fan), lifted a hair
		const int n = 11;
		var rng = new RandomNumberGenerator { Seed = 9 };
		var ring = new Vector3[n];
		for (int i = 0; i < n; i++)
		{
			float a = Mathf.Tau * i / n;
			float rr = rng.RandfRange(0.38f, 0.55f);
			ring[i] = new Vector3(Mathf.Cos(a) * rr * 1.3f, 0.012f, Mathf.Sin(a) * rr);
		}
		for (int i = 0; i < n; i++)
			k.Tri(new Vector3(0, 0.014f, 0), ring[i], ring[(i + 1) % n], Vector3.Up, new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(1, 0));
		// the print: sole and heel, heading toward the stairs (local -Z is arbitrary here; the node's yaw is random)
		k.Mat(print);
		Vector3[] sole = { new(-0.05f, 0.017f, -0.14f), new(0.045f, 0.017f, -0.15f), new(0.055f, 0.017f, -0.02f), new(-0.045f, 0.017f, -0.01f) };
		k.Quad(sole[0], sole[1], sole[2], sole[3], Vector3.Up);
		k.Quad(new Vector3(-0.04f, 0.017f, 0.04f), new Vector3(0.04f, 0.017f, 0.04f), new Vector3(0.037f, 0.017f, 0.13f), new Vector3(-0.037f, 0.017f, 0.13f), Vector3.Up);
		// a lighter scuffed smear where it slid (reads at distance as a mark, not a blob)
		k.Mat(mud);
		k.Color = new Color(1.6f, 1.5f, 1.4f);
		k.Quad(new Vector3(-0.05f, 0.015f, -0.2f), new Vector3(0.05f, 0.015f, -0.21f), new Vector3(0.045f, 0.015f, -0.15f), new Vector3(-0.05f, 0.015f, -0.15f), Vector3.Up);
	}
}
