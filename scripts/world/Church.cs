using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.ChurchParts;

namespace ProjectDS.World;

/// <summary>
/// Act 21's church (the owner's "Real Act 21", after their cathedral and crypt references): the biggest
/// house yet, a Gothic cathedral standing alone in the winter woods. The player comes up into its crypt
/// through a hatch at the top of the long stair and is shut in.
/// <list type="bullet">
/// <item><b>The nave</b>: seven bays, 16 m wide between clustered piers, pointed arcades into side aisles,
/// a band of blind arcading, and tall stained-glass clerestory lancets under a ribbed vault 36 m up (the
/// tallest ceiling in the game). Oak pews either side of a red-marble aisle over cream marble, a
/// medallion let into the floor before the crossing. The aisles' lower windows are clear leaded glass:
/// the snow and the frosted trees show through them.</item>
/// <item><b>The crossing and transepts</b>: the arms reach out to either side; the left one ends at the
/// vestry's door (locked until the four candles burn), the right at the chapel's (Act 22).</item>
/// <item><b>The chancel and apse</b>: up five steps, the altar under a gilded canopy, the apse ringed with
/// tall windows.</item>
/// <item><b>The west end</b>: the great door, locked, with four niches in it for the four pieces that
/// open it; the rose window above; the font by the door, its lid locked; the tower's small door.</item>
/// <item><b>The crypt</b> (Church.Crypt.cs): brick vaults on stone columns over a chequered floor, twin
/// stairs up into the nave.</item>
/// </list>
/// Local space: y 0 is the nave floor; x = 0 the middle of the nave; the great door is at z = 0 and the
/// church runs along +Z to the apse. The beats are in Church.Story.cs.
/// </summary>
public partial class Church : Node3D
{
	// ---- the plan
	public const float NaveHalf = 8f, AisleOuter = 15f, WallT = 1.2f, BayLen = 7.5f;
	public const int NaveBays = 7;
	public const float NaveEnd = NaveBays * BayLen, CrossEnd = NaveEnd + 16f, ChancelEnd = CrossEnd + 12f, ApseR = 8f;
	public const float TransHalf = 24f;
	// ---- the heights
	public const float PierTop = 10f, ArcadeTop = 14.5f, TriforiumTop = 17.5f, Spring = 24f, NaveR = 13.6f;
	public const float AisleSpring = 10f, AisleR = 6f;
	public const float ChancelY = 0.9f;
	// ---- the crypt
	public const float CryptFloor = -5.4f, CryptTop = -0.6f, CryptZ0 = 40f, CryptZ1 = 68f, CryptHalf = 8f;
	/// <summary>The hatch in the crypt's floor, where the long stair comes up.</summary>
	public static readonly Vector3 HatchLocal = new(2f, CryptFloor, 46f);

	public Vector3 HatchExitWorld => ToGlobal(HatchLocal + new Vector3(0, 0.08f, -1.3f));
	public Vector3 NaveCentreWorld => ToGlobal(new Vector3(0, 0.05f, 26f));
	public Vector3 DoorWorld => ToGlobal(new Vector3(0, 0.05f, 1.6f));

	private StaticBody3D _stone, _wood, _marbleBody;
	private readonly RandomNumberGenerator _rng = new() { Seed = 2121 };

	/// <summary>Act 22's woods, road and lodge.</summary>
	public WinterWoods Woods { get; private set; }

	/// <summary>The long stair this church sits at the top of.</summary>
	public LongStair Stair { get; set; }

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		using var __timer = Systems.BuildTimer.Time("Church");
		// no snow indoors (Weather's: the vault's surfaces face in, so the roof collider can't see them from above): the
		// nave and its aisles to the apse, the transepts and the vestry
		float top = Spring + NaveR + 3f;
		Weather.RegisterShelter(this, new Aabb(new Vector3(-AisleOuter - WallT, CryptFloor - 1f, -2f), new Vector3((AisleOuter + WallT) * 2f, top - CryptFloor + 1f, ChancelEnd + ApseR + 3f)));
		Weather.RegisterShelter(this, new Aabb(new Vector3(-TransHalf - WallT, CryptFloor - 1f, NaveEnd - 1f), new Vector3((TransHalf + WallT) * 2f, top - CryptFloor + 1f, CrossEnd - NaveEnd + 2f)));
		Weather.RegisterShelter(this, new Aabb(new Vector3(VestryX1 - 0.5f, -1f, VestryZ0 - 0.5f), new Vector3(VestryX0 - VestryX1 + 1f, VestryH + 2f, VestryZ1 - VestryZ0 + 1f)));
		_stone = Body("Stone", "stone");
		_marbleBody = Body("Marble", "stone");
		_wood = Body("Wood", "wood");
		BuildFloors();
		BuildPiersAndArcades();
		BuildWalls();
		BuildVaults();
		BuildChancel();
		BuildPews();
		BuildFont();
		BuildCandles();
		BuildDoors();
		BuildVestry();
		BuildCrypt();
		BuildLights();
		BuildGothic();
		BuildShafts();
		BuildSound();
		AddChild(new WinterGlade { Name = "WinterGlade" });
		// Act 22: the winter woods beyond the great door, the plowed road, the ski lodge (it lays all the snow round here)
		Woods = new WinterWoods { Name = "WinterWoods", Church = this };
		AddChild(Woods);
		StartStory();
		GD.Print("[story] Act 21: the church stands in the snow");
	}

	private StaticBody3D Body(string name, string surface)
	{
		var b = new StaticBody3D { Name = name, CollisionLayer = 1, CollisionMask = 0 };
		b.SetMeta("surface", surface);
		AddChild(b);
		return b;
	}

	// ------------------------------------------------------------------ helpers

	/// <summary>A pointed (two-centred) arch's height over its springing at <paramref name="x"/> from its
	/// middle, for a span of 2*<paramref name="half"/> struck from radius <paramref name="r"/>.</summary>
	public static float Pointed(float x, float half, float r)
	{
		float ax = Mathf.Min(Mathf.Abs(x), half);
		float c = half - r;
		return Mathf.Sqrt(Mathf.Max(0f, r * r - (ax - c) * (ax - c)));
	}

	/// <summary>The nave vault's underside at <paramref name="x"/> across the nave.</summary>
	public static float NaveVaultY(float x) => Spring + Pointed(x, NaveHalf, NaveR);

	/// <summary>The aisle vault's underside at x (either aisle).</summary>
	public static float AisleVaultY(float x)
	{
		float c = Mathf.Sign(x) * (NaveHalf + AisleOuter) * 0.5f;
		return AisleSpring + Pointed(x - c, (AisleOuter - NaveHalf) * 0.5f, AisleR);
	}

	private static void Solid(MeshKit k, StaticBody3D body, Vector3 c, Vector3 s, float uv = 1f, BuildKit.Face skip = BuildKit.Face.None)
	{
		if (k != null) BuildKit.Box(k, c, s, uv, skip);
		body?.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
	}

	private static void Collide(StaticBody3D body, Vector3 c, Vector3 s, Basis? b = null)
		=> body.AddChild(new CollisionShape3D { Transform = new Transform3D(b ?? Basis.Identity, c), Shape = new BoxShape3D { Size = s } });

	/// <summary>A hole in a wall: across [<see cref="U0"/>, <see cref="U1"/>] along it, from <see cref="Bottom"/>
	/// up to <see cref="Top"/>(u); glazed with <see cref="Glass"/> (null for an open doorway or niche).</summary>
	public sealed class Hole
	{
		public float U0, U1, Bottom;
		public Func<float, float> Top;
		public Material Glass;
		/// <summary>Where the glass sits in the wall's thickness (0 the front face, 1 the back).</summary>
		public float GlassDepth = 0.5f;
		public float Height(float u) => Top(u);

		/// <summary>A pointed lancet: straight sides up to <paramref name="springY"/>, then a pointed head.</summary>
		public static Hole Lancet(float u0, float u1, float bottom, float springY, Material glass, float sharp = 1.1f)
		{
			float half = (u1 - u0) * 0.5f, mid = (u0 + u1) * 0.5f;
			return new Hole { U0 = u0, U1 = u1, Bottom = bottom, Glass = glass, Top = u => springY + Pointed(u - mid, half, half * 2f * sharp) };
		}

		/// <summary>A round hole (the rose), centred at (<paramref name="cu"/>, <paramref name="cy"/>).</summary>
		public static Hole Round(float cu, float cy, float r, Material glass)
			=> new() { U0 = cu - r, U1 = cu + r, Bottom = float.NaN, Glass = glass, Top = u => cy + Mathf.Sqrt(Mathf.Max(0f, r * r - (u - cu) * (u - cu))), RoundCy = cy, RoundR = r, RoundCu = cu };

		public float RoundCy = float.NaN, RoundR, RoundCu;
		public float BottomAt(float u) => float.IsNaN(RoundCy) ? Bottom : RoundCy - Mathf.Sqrt(Mathf.Max(0f, RoundR * RoundR - (u - RoundCu) * (u - RoundCu)));
	}

	/// <summary>
	/// A wall of stone <paramref name="t"/> thick, its front face on the plane through <paramref name="origin"/>
	/// facing <paramref name="normal"/>, running along <paramref name="along"/> from u = <paramref name="u0"/>
	/// to <paramref name="u1"/>, from <paramref name="y0"/> up to <paramref name="top"/>(u), with <paramref name="holes"/>
	/// cut through it: both faces, the reveals round every hole (jambs, sill, the pointed head's soffit), and
	/// the glass in each. Built in narrow vertical strips so the heads and the top can be any curve.
	/// </summary>
	private static void Wall(MeshKit k, Material stone, Vector3 origin, Vector3 along, Vector3 normal, float t, float u0, float u1, float y0, Func<float, float> top, IList<Hole> holes, float step = 0.25f, Node3D glassParent = null)
	{
		along = along.Normalized();
		normal = normal.Normalized();
		Vector3 P(float u, float y, float d) { var q = origin + along * u - normal * d; return new Vector3(q.X, y, q.Z); }
		// strip edges: regular, plus every hole's edges so the jambs are exact
		var cuts = new List<float>();
		for (float u = u0; u < u1 - 1e-4f; u += step) cuts.Add(u);
		cuts.Add(u1);
		if (holes != null) foreach (var h in holes) { cuts.Add(h.U0); cuts.Add(h.U1); }
		cuts.Sort();
		var uniq = new List<float>();
		foreach (var c in cuts) if (c >= u0 - 1e-4f && c <= u1 + 1e-4f && (uniq.Count == 0 || c - uniq[^1] > 1e-3f)) uniq.Add(Mathf.Clamp(c, u0, u1));
		k.Mat(stone);
		for (int i = 0; i < uniq.Count - 1; i++)
		{
			float ua = uniq[i], ub = uniq[i + 1], um = (ua + ub) * 0.5f;
			float ta = top(ua), tb = top(ub);
			// every hole this strip passes through, bottom to top: solid between them
			var hs = new List<Hole>();
			if (holes != null) foreach (var h in holes) if (um > h.U0 && um < h.U1) hs.Add(h);
			hs.Sort((p, q) => p.BottomAt(um).CompareTo(q.BottomAt(um)));
			var spans = new List<(float a0, float b0, float a1, float b1)>();
			float ca = y0, cb = y0;
			foreach (var hole in hs)
			{
				float ba = hole.BottomAt(ua), bb = hole.BottomAt(ub), ha = hole.Height(ua), hb = hole.Height(ub);
				if (Mathf.Max(ba - ca, bb - cb) > 1e-3f) spans.Add((ca, cb, ba, bb));
				// the reveals: the head's soffit and the sill, across the thickness
				Vector3 h0 = P(ua, ha, 0), h1 = P(ub, hb, 0), h2 = P(ub, hb, t), h3 = P(ua, ha, t);
				k.Quad(h0, h1, h2, h3, Vector3.Down);
				Vector3 s0 = P(ua, ba, 0), s1 = P(ub, bb, 0), s2 = P(ub, bb, t), s3 = P(ua, ba, t);
				if (ba > y0 + 1e-3f || bb > y0 + 1e-3f) k.Quad(s3, s2, s1, s0, Vector3.Up);
				if (hole.Glass != null) GlassStrip(k, hole, P, ua, ub, ba, bb, ha, hb, t, stone, normal);
				ca = ha; cb = hb;
			}
			spans.Add((ca, cb, ta, tb));
			foreach (var (a0, b0, a1, b1) in spans)
			{
				if (a1 - a0 < 1e-3f && b1 - b0 < 1e-3f) continue;
				// front face (towards normal) and back face
				k.Quad(P(ua, a0, 0), P(ub, b0, 0), P(ub, b1, 0), P(ua, a1, 0), normal,
					new Vector2(ua, -a0), new Vector2(ub, -b0), new Vector2(ub, -b1), new Vector2(ua, -a1));
				k.Quad(P(ub, b0, t), P(ua, a0, t), P(ua, a1, t), P(ub, b1, t), -normal,
					new Vector2(-ub, -b0), new Vector2(-ua, -a0), new Vector2(-ua, -a1), new Vector2(-ub, -b1));
			}
		}
		// the jambs: at each hole's two sides, a vertical reveal across the thickness
		if (holes != null)
			foreach (var h in holes)
			{
				foreach (var (u, sgn) in new[] { (h.U0, 1f), (h.U1, -1f) })
				{
					float b = h.BottomAt(u), tt = h.Height(u);
					if (tt - b < 1e-3f) continue;
					Vector3 a = P(u, b, 0), bq = P(u, tt, 0), c = P(u, tt, t), d = P(u, b, t);
					if (sgn > 0) k.Quad(d, c, bq, a, along * sgn);
					else k.Quad(a, bq, c, d, along * sgn);
				}
			}
	}

	private static void GlassStrip(MeshKit k, Hole h, Func<float, float, float, Vector3> P, float ua, float ub, float ba, float bb, float ha, float hb, float t, Material stone, Vector3 normal)
	{
		float d = t * h.GlassDepth;
		float w = h.U1 - h.U0;
		float gb = float.IsNaN(h.RoundCy) ? h.Bottom : h.RoundCy - h.RoundR;
		float gt = float.IsNaN(h.RoundCy) ? MaxTop(h) : h.RoundCy + h.RoundR;
		float hh = Mathf.Max(0.01f, gt - gb);
		Vector2 UV(float u, float y) => new((u - h.U0) / w, 1f - (y - gb) / hh);
		k.Mat(h.Glass);
		k.Color = Colors.White;
		k.Quad(P(ua, ba, d), P(ub, bb, d), P(ub, hb, d), P(ua, ha, d), normal, UV(ua, ba), UV(ub, bb), UV(ub, hb), UV(ua, ha));
		k.Mat(stone);
	}

	private static float MaxTop(Hole h)
	{
		float m = 0;
		for (int i = 0; i <= 16; i++) m = Mathf.Max(m, h.Height(Mathf.Lerp(h.U0, h.U1, i / 16f)));
		return m;
	}
}
