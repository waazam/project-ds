using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// Builds the stalker's body procedurally (low-poly, PS2-era) and gives it a
/// barely-there idle: a slow sway, a drifting head tilt and the occasional
/// twitch of the head.
///
/// The figure: ~2.4 m, gaunt, hunched, with heavy high shoulders and a small
/// elongated head slung forward and low between them. Long arms hang past the
/// knees and end in long thin fingers; the knees are bent slightly wrong.
/// Tattered strips hang from the shoulders, arms and hips, the longest trailing
/// to the shins, and a few bark-like shards jut from the shoulders and spine so
/// the silhouette is ragged in the fog.
///
/// Contract with Stalker.cs: the origin is at the feet and the figure faces +Z.
/// Every mesh uses the single <see cref="Skin"/> material (shader with a
/// `visibility` uniform); nothing casts shadows; there is no collision. This
/// node's own transform belongs to Stalker (it leans it when peeking), so the
/// idle only moves the child pivots.
/// </summary>
[Tool]
public partial class StalkerBody : Node3D
{
	[Export] public ShaderMaterial Skin;
	[Export] public int Seed = 1931;
	/// <summary>Uniform scale of the design (design height ≈ 2.2 m at 1.0).</summary>
	[Export] public float Size = 1.1f;

	[ExportGroup("Idle")]
	[Export] public bool Idle = true;
	[Export] public float SwayDegrees = 1.3f;
	[Export] public float SwaySeconds = 11f;
	[Export] public float HeadDriftDegrees = 4f;
	[Export] public Vector2 TwitchInterval = new(3.5f, 11f);
	[Export] public float TwitchDegrees = 5f;   // subtle: a peek, not a performance

	/// <summary>Walking (the giant): a continuous gait phase in radians, one footfall every pi (feet land at
	/// 0, pi, 2pi ...). Negative = standing. The arms swing against each other, the torso leans into the
	/// walk and rolls onto the planted foot; the body's own bob and lean are the walker's to add.</summary>
	public float WalkPhase { get; set; } = -1f;
	/// <summary>How big the gait reads (1 = a long deliberate stride).</summary>
	public float WalkAmount { get; set; } = 1f;

	public int TriangleCount { get; private set; }

	private MeshInstance3D _lower, _upper, _armL, _armR, _head;
	private RandomNumberGenerator _rng;
	private readonly RandomNumberGenerator _idleRng = new();
	private float _t;
	private Vector3 _twitch, _twitchTarget;
	private float _twitchTimer = 3f, _twitchHold;
	private bool _twitchOut;

	// Pivots (design space, before Size)
	private static readonly Vector3 Waist = new(0, 1.26f, 0);
	private static readonly Vector3 ShoulderL = new(-0.39f, 1.97f, 0.08f);
	private static readonly Vector3 ShoulderR = new(0.4f, 2.0f, 0.08f);
	private static readonly Vector3 NeckBase = new(0, 1.98f, 0.14f);

	// Base pose offsets (the idle drifts around these)
	private static readonly Vector3 UpperBase = new(0, 0, -0.035f);
	private static readonly Vector3 HeadBase = new(0.05f, 0, 0);

	/// <summary>Visibility sample points (Body-local) spread over the figure; mirror these into Stalker.SamplePoints.</summary>
	public Vector3[] SuggestedSamplePoints() => new[]
	{
		S(new Vector3(0.02f, 1.92f, 0.44f)),   // head
		S(new Vector3(0, 2.07f, 0.02f)),       // hump between the shoulders
		S(new Vector3(-0.4f, 2.12f, 0.06f)),   // shoulder L
		S(new Vector3(0.41f, 2.15f, 0.06f)),   // shoulder R
		S(new Vector3(0, 1.72f, 0.06f)),       // chest
		S(new Vector3(0, 1.32f, 0.01f)),       // waist
		S(new Vector3(0, 1.1f, 0)),            // hips
		S(new Vector3(-0.15f, 0.64f, 0.08f)),  // knee L
		S(new Vector3(0.16f, 0.61f, 0.12f)),   // knee R
		S(new Vector3(-0.44f, 0.78f, 0.17f)),  // hand L
	};

	private Vector3 S(Vector3 v) => v * Size;

	public override void _Ready()
	{
		Build();
		_idleRng.Randomize();
		_t = _idleRng.RandfRange(0f, 100f);
		ApplyPose();
	}

	// ─────────────────────────────── building ───────────────────────────────

	private void Build()
	{
		foreach (var c in GetChildren())
			if (c.HasMeta("stalker_generated")) { RemoveChild(c); c.QueueFree(); }
		if (Skin == null)
		{
			Skin = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/stalker_skin.gdshader") };
			Skin.SetShaderParameter("visibility", 1f);
		}
		_rng = new RandomNumberGenerator { Seed = (ulong)Seed };
		TriangleCount = 0;

		_lower = Part(this, Vector3.Zero, Vector3.Zero, "Lower", BuildLower);
		_upper = Part(this, Waist, Vector3.Zero, "Upper", BuildTorso);
		_armL = Part(_upper, ShoulderL, Waist, "ArmL", k => BuildArm(k, -1));
		_armR = Part(_upper, ShoulderR, Waist, "ArmR", k => BuildArm(k, 1));
		_head = Part(_upper, NeckBase, Waist, "Head", BuildHead);
	}

	/// <summary>A mesh pivot at design point <paramref name="pivot"/>; geometry is authored in design (Body) space.</summary>
	private MeshInstance3D Part(Node3D parent, Vector3 pivot, Vector3 parentPivot, string name, Action<MeshKit> build)
	{
		var k = new MeshKit();
		k.Mat(Skin);
		k.Xf = new Transform3D(Basis.FromScale(Vector3.One * Size), -pivot * Size);
		build(k);
		var mesh = k.Commit();
		var mi = new MeshInstance3D
		{
			Name = name,
			Mesh = mesh,
			MaterialOverride = Skin,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Position = (pivot - parentPivot) * Size,
		};
		mi.SetMeta("stalker_generated", true);
		parent.AddChild(mi);
		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
			TriangleCount += ((int[])mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Index]).Length / 3;
		return mi;
	}

	// Tone presets (vertex colour: r paleness, g brightness, b wetness)
	private static readonly Color HideTone = new(0f, 1f, 1f);
	private static readonly Color RagTone = new(0f, 0.85f, 0.25f);
	private static readonly Color Bark = new(0f, 1.0f, 0.1f);

	private struct Ring
	{
		public Vector3 C; public float Rx, Rz;
		public Ring(Vector3 c, float rx, float rz) { C = c; Rx = rx; Rz = rz; }
		public Ring(Vector3 c, float r) { C = c; Rx = r; Rz = r; }
	}

	/// <summary>
	/// Lofted tube through ring centres. Each ring is an ellipse perpendicular to
	/// the local path tangent; Rx lies along <paramref name="side"/> (projected),
	/// Rz along side × tangent. Angle 0 = +side; for upright parts with side = +X,
	/// angle 90° faces +Z (front) and 270° faces back.
	/// </summary>
	private void Loft(MeshKit k, IList<Ring> rings, int sides, bool capStart, bool capEnd,
		Vector3? side = null, Func<int, float, float> radial = null, Func<Vector3, Color> tone = null, float jitter = 0f)
	{
		Vector3 sref = side ?? Vector3.Right;
		int n = rings.Count;
		var pts = new Vector3[n, sides];
		var nrm = new Vector3[n, sides];
		for (int r = 0; r < n; r++)
		{
			Vector3 t = (rings[Math.Min(r + 1, n - 1)].C - rings[Math.Max(r - 1, 0)].C).Normalized();
			Vector3 x = (sref - t * sref.Dot(t)).Normalized();
			Vector3 f = x.Cross(t).Normalized();
			for (int i = 0; i < sides; i++)
			{
				float a = Mathf.Tau * i / sides;
				float m = radial?.Invoke(r, a) ?? 1f;
				if (jitter > 0f) m *= 1f + _rng.RandfRange(-jitter, jitter);
				float c = Mathf.Cos(a), s = Mathf.Sin(a);
				pts[r, i] = rings[r].C + (x * c * rings[r].Rx + f * s * rings[r].Rz) * m;
				nrm[r, i] = (x * c / Mathf.Max(rings[r].Rx, 1e-3f) + f * s / Mathf.Max(rings[r].Rz, 1e-3f)).Normalized();
			}
		}
		for (int r = 0; r < n - 1; r++)
			for (int i = 0; i < sides; i++)
			{
				int j = (i + 1) % sides;
				k.Color = tone?.Invoke((nrm[r, i] + nrm[r + 1, j]).Normalized()) ?? HideTone;
				k.Tri(pts[r, i], pts[r, j], pts[r + 1, j], nrm[r, i], nrm[r, j], nrm[r + 1, j], Vector2.Zero, Vector2.Zero, Vector2.Zero);
				k.Tri(pts[r, i], pts[r + 1, j], pts[r + 1, i], nrm[r, i], nrm[r + 1, j], nrm[r + 1, i], Vector2.Zero, Vector2.Zero, Vector2.Zero);
			}
		void Cap(int r, int dir)
		{
			Vector3 cn = (rings[r].C - rings[r - dir].C).Normalized();
			k.Color = tone?.Invoke(cn) ?? HideTone;
			for (int i = 0; i < sides; i++)
			{
				int j = (i + 1) % sides;
				k.Tri(rings[r].C + cn * 0.2f * Mathf.Min(rings[r].Rx, rings[r].Rz), pts[r, i], pts[r, j], cn, nrm[r, i], nrm[r, j], Vector2.Zero, Vector2.Zero, Vector2.Zero);
			}
		}
		if (capStart) Cap(0, -1);
		if (capEnd) Cap(n - 1, 1);
	}

	/// <summary>A tapering spike (3-sided cone) — bark shards, claws.</summary>
	private void Shard(MeshKit k, Vector3 root, Vector3 tip, float r)
	{
		k.Color = Bark;
		k.Cylinder(root, tip, r, 0f, 3, true, 1f, _rng.RandfRange(0f, Mathf.Tau));
	}

	/// <summary>
	/// A hanging rag strip: from anchor <paramref name="a"/>, down <paramref name="len"/>,
	/// drifting outward along <paramref name="outDir"/>, with a torn, notched end.
	/// </summary>
	private void Rag(MeshKit k, Vector3 a, Vector3 outDir, float len, float w, float flare, int segs = 4)
	{
		outDir.Y = 0; outDir = outDir.Normalized();
		Vector3 across = Vector3.Up.Cross(outDir).Normalized();
		float twist = _rng.RandfRange(-0.35f, 0.35f);
		var L = new Vector3[segs + 1];
		var R = new Vector3[segs + 1];
		float drift = _rng.RandfRange(-0.04f, 0.04f);
		for (int i = 0; i <= segs; i++)
		{
			float t = (float)i / segs;
			Vector3 c = a + Vector3.Down * len * t
				+ outDir * (flare * t * t + 0.035f * Mathf.Sin(Mathf.Pi * t))
				+ across * drift * t
				+ (i > 0 ? new Vector3(_rng.RandfRange(-0.012f, 0.012f), 0, _rng.RandfRange(-0.012f, 0.012f)) : Vector3.Zero);
			float wi = w * (1f - 0.4f * t) * (i > 0 ? _rng.RandfRange(0.75f, 1.2f) : 1f);
			Vector3 ac = across.Rotated(Vector3.Up, twist * t);
			L[i] = c - ac * wi * 0.5f;
			R[i] = c + ac * wi * 0.5f;
		}
		k.Color = RagTone;
		for (int i = 0; i < segs - 1; i++)
			k.Quad(L[i], R[i], R[i + 1], L[i + 1], outDir);
		// Torn end: two ragged tongues either side of a notch.
		int e = segs - 1;
		Vector3 mid = (L[e] + R[e]) * 0.5f;
		Vector3 down = (mid - (L[e - 1 < 0 ? 0 : e - 1] + R[e - 1 < 0 ? 0 : e - 1]) * 0.5f).Normalized();
		float seg = len / segs;
		Vector3 notch = mid + down * seg * _rng.RandfRange(0.1f, 0.45f);
		Vector3 tipL = (L[e] * 0.7f + mid * 0.3f) + down * seg * _rng.RandfRange(0.8f, 1.5f);
		Vector3 tipR = (R[e] * 0.7f + mid * 0.3f) + down * seg * _rng.RandfRange(0.6f, 1.3f);
		k.Tri(L[e], mid, notch, outDir, Vector2.Zero, Vector2.Zero, Vector2.Zero);
		k.Tri(mid, R[e], notch, outDir, Vector2.Zero, Vector2.Zero, Vector2.Zero);
		k.Tri(L[e], notch, tipL, outDir, Vector2.Zero, Vector2.Zero, Vector2.Zero);
		k.Tri(notch, R[e], tipR, outDir, Vector2.Zero, Vector2.Zero, Vector2.Zero);
	}

	// Torso rings (design space). Hunched: the upper spine curls forward.
	private static readonly Ring[] TorsoRings =
	{
		new(new Vector3(0, 1.06f, -0.01f), 0.13f, 0.09f),
		new(new Vector3(0, 1.14f, -0.01f), 0.15f, 0.10f),   // pelvis
		new(new Vector3(0, 1.25f, 0.0f), 0.13f, 0.09f),
		new(new Vector3(0, 1.36f, 0.01f), 0.10f, 0.08f),    // narrow waist
		new(new Vector3(0, 1.50f, 0.03f), 0.18f, 0.13f),    // bottom of the ribcage
		new(new Vector3(0, 1.66f, 0.05f), 0.25f, 0.165f),
		new(new Vector3(0, 1.82f, 0.07f), 0.30f, 0.185f),   // chest
		new(new Vector3(0, 1.96f, 0.08f), 0.33f, 0.19f),    // shoulder girdle
		new(new Vector3(0, 2.04f, 0.04f), 0.2f, 0.15f),     // hump (low in the middle: the head hangs in the dip)
		new(new Vector3(0, 2.08f, 0.02f), 0.09f, 0.08f),
	};

	/// <summary>Point on the torso surface at design height y and angle (0 = +X, 90° = front, 270° = back).</summary>
	private static Vector3 TorsoPoint(float y, float angleDeg, float push = 1.04f)
	{
		int i = 0;
		while (i < TorsoRings.Length - 2 && TorsoRings[i + 1].C.Y < y) i++;
		var r0 = TorsoRings[i]; var r1 = TorsoRings[i + 1];
		float t = Mathf.Clamp((y - r0.C.Y) / (r1.C.Y - r0.C.Y), 0f, 1f);
		Vector3 c = r0.C.Lerp(r1.C, t);
		float rx = Mathf.Lerp(r0.Rx, r1.Rx, t), rz = Mathf.Lerp(r0.Rz, r1.Rz, t);
		float a = Mathf.DegToRad(angleDeg);
		return c + new Vector3(Mathf.Cos(a) * rx, 0, Mathf.Sin(a) * rz) * push;
	}

	private static Vector3 Radial(float angleDeg) { float a = Mathf.DegToRad(angleDeg); return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }

	private void BuildTorso(MeshKit k)
	{
		// Ribcage taper to a narrow waist, a spine ridge down the hunched back
		// and shoulder-blade knobs.
		Loft(k, TorsoRings, 12, true, true, radial: (r, a) =>
		{
			float deg = Mathf.RadToDeg(a);
			float m = 1f;
			if (r >= 3 && r <= 8 && Mathf.Abs(deg - 270f) < 1f) m *= 1.14f;
			if (r >= 6 && r <= 7 && (Mathf.Abs(deg - 240f) < 1f || Mathf.Abs(deg - 300f) < 1f)) m *= 1.12f;
			if (r >= 6 && r <= 8 && deg > 200f && deg < 340f) m *= 1.16f;   // rounded hunch of the upper back
			if (r >= 4 && r <= 5 && (Mathf.Abs(deg - 60f) < 1f || Mathf.Abs(deg - 120f) < 1f)) m *= 1.05f;   // rib edges
			return m;
		}, jitter: 0.03f);

		// Heavy shoulders that sit high, and a trapezius mass either side of the neck.
		k.Color = HideTone;
		k.Blob(new Vector3(-0.36f, 2.07f, 0.05f), new Vector3(0.15f, 0.16f, 0.16f), Seed + 1, 0.12f);
		k.Blob(new Vector3(0.37f, 2.1f, 0.05f), new Vector3(0.15f, 0.17f, 0.16f), Seed + 2, 0.12f);
		k.Blob(new Vector3(-0.25f, 2.05f, 0.0f), new Vector3(0.09f, 0.07f, 0.1f), Seed + 3, 0.1f);
		k.Blob(new Vector3(0.26f, 2.07f, 0.0f), new Vector3(0.09f, 0.07f, 0.1f), Seed + 4, 0.1f);

		// Bark-like shards along the shoulders and the top of the spine.
		(Vector3 root, Vector3 dir, float len)[] shards =
		{
			(new(-0.44f, 2.14f, -0.02f), new(-0.8f, 0.55f, -0.6f), 0.19f),
			(new(-0.3f, 2.16f, -0.06f), new(-0.2f, 0.5f, -1f), 0.15f),
			(new(0.45f, 2.17f, -0.01f), new(0.8f, 0.6f, -0.5f), 0.21f),
			(new(0.32f, 2.18f, -0.07f), new(0.3f, 0.45f, -1f), 0.14f),
			(new(0.0f, 2.06f, -0.13f), new(0.1f, 0.6f, -1f), 0.17f),
			(new(0.02f, 1.94f, -0.18f), new(-0.1f, 0.4f, -1f), 0.13f),
			(new(-0.03f, 1.78f, -0.18f), new(0.1f, 0.2f, -1f), 0.1f),
			(new(-0.48f, 1.98f, 0.0f), new(-1f, 0.4f, -0.3f), 0.14f),
		};
		foreach (var (root, dir, len) in shards)
			Shard(k, root, root + dir.Normalized() * len, 0.035f);

		// Mantle: rags from around the shoulders, longest at the back.
		for (int i = 0; i < 16; i++)
		{
			float ang = -10f + i * (200f / 15f) + 170f + _rng.RandfRange(-6f, 6f);   // 160..370: sides and back
			float backness = Mathf.Max(0f, -Mathf.Sin(Mathf.DegToRad(ang)));
			float y = 2.02f + _rng.RandfRange(-0.04f, 0.06f);
			float len = Mathf.Lerp(0.22f, 1.0f, backness * backness) * _rng.RandfRange(0.75f, 1.2f);
			Rag(k, TorsoPoint(y, ang, 1.12f), Radial(ang), len, _rng.RandfRange(0.1f, 0.17f), _rng.RandfRange(0.04f, 0.14f));
		}
		// A few over the front of the chest, short.
		for (int i = 0; i < 4; i++)
		{
			float ang = 55f + i * 23f + _rng.RandfRange(-5f, 5f);
			Rag(k, TorsoPoint(1.97f, ang, 1.08f), Radial(ang), _rng.RandfRange(0.3f, 0.55f), _rng.RandfRange(0.08f, 0.13f), 0.05f, 3);
		}
		// Long trailing strips down the back to the shins.
		for (int i = 0; i < 6; i++)
		{
			float ang = 225f + i * 18f + _rng.RandfRange(-5f, 5f);
			float y = 1.9f + _rng.RandfRange(0f, 0.12f);
			Rag(k, TorsoPoint(y, ang, 1.1f), Radial(ang), _rng.RandfRange(1.25f, 1.55f), _rng.RandfRange(0.1f, 0.16f), _rng.RandfRange(0.1f, 0.2f), 5);
		}
	}

	private void BuildLower(MeshKit k)
	{
		// Legs: long, knees bent forward a little and not quite together — standing wrong.
		foreach (int s in new[] { -1, 1 })
		{
			bool r = s > 0;
			Vector3 hip = new(0.11f * s, 1.12f, -0.01f);
			Vector3 knee = r ? new Vector3(0.16f, 0.61f, 0.12f) : new Vector3(-0.15f, 0.64f, 0.08f);
			Vector3 ankle = r ? new Vector3(0.15f, 0.1f, -0.03f) : new Vector3(-0.13f, 0.1f, 0.0f);
			Loft(k, new Ring[]
			{
				new(hip + new Vector3(0, 0.05f, 0), 0.095f),
				new(hip.Lerp(knee, 0.3f), 0.085f, 0.09f),
				new(hip.Lerp(knee, 0.8f), 0.055f),
				new(knee, 0.06f),
				new(knee.Lerp(ankle, 0.3f) + new Vector3(0, 0, -0.02f), 0.05f, 0.055f),
				new(knee.Lerp(ankle, 0.8f), 0.035f),
				new(ankle, 0.032f),
			}, 6, false, false, jitter: 0.06f);
			// Long narrow foot, toes turned slightly in.
			Vector3 toe = ankle + new Vector3(-0.03f * s, -0.08f, 0.26f);
			Loft(k, new Ring[]
			{
				new(ankle + new Vector3(0, 0.01f, -0.06f), 0.03f),
				new(ankle + new Vector3(0, -0.04f, 0.03f), 0.04f, 0.035f),
				new(toe, 0.012f),
			}, 5, true, true);
		}

		// Hip rags: a ragged skirt at the sides and back, trailing to the shins.
		for (int i = 0; i < 11; i++)
		{
			float ang = 150f + i * (240f / 10f) + _rng.RandfRange(-7f, 7f);
			float backness = Mathf.Max(0f, -Mathf.Sin(Mathf.DegToRad(ang)));
			if (Mathf.Sin(Mathf.DegToRad(ang)) > 0.6f) continue;   // keep the front open so the legs show
			float y = 1.24f + _rng.RandfRange(-0.04f, 0.04f);
			float len = Mathf.Lerp(0.5f, 0.85f, backness) * _rng.RandfRange(0.8f, 1.15f);
			Rag(k, TorsoPoint(y, ang, 1.2f), Radial(ang), len, _rng.RandfRange(0.1f, 0.16f), _rng.RandfRange(0.05f, 0.12f), 4);
		}
	}

	private void BuildArm(MeshKit k, int s)
	{
		// Long arms hanging a touch forward, hands past the knees.
		Vector3 sh = s < 0 ? ShoulderL : ShoulderR;
		Vector3 elbow = sh + new Vector3(0.08f * s, -0.52f, -0.05f);
		Vector3 wrist = elbow + new Vector3(-0.01f * s, -0.55f, 0.1f);
		Loft(k, new Ring[]
		{
			new(sh + new Vector3(0, 0.05f, 0), 0.07f),
			new(sh.Lerp(elbow, 0.25f), 0.075f),
			new(sh.Lerp(elbow, 0.85f), 0.048f),
			new(elbow, 0.052f),
			new(elbow.Lerp(wrist, 0.3f), 0.056f, 0.05f),
			new(elbow.Lerp(wrist, 0.85f), 0.032f),
			new(wrist, 0.03f),
		}, 6, false, false, jitter: 0.07f);

		// Hand: a narrow flat palm, then long thin tapered fingers curling in.
		Vector3 palmEnd = wrist + new Vector3(0.0f, -0.13f, 0.03f);
		Loft(k, new Ring[]
		{
			new(wrist, 0.03f),
			new(wrist.Lerp(palmEnd, 0.5f), 0.022f, 0.052f),
			new(palmEnd, 0.018f, 0.05f),
		}, 5, false, false, side: new Vector3(0, 0, 1));
		// fingers fan along +Z (palm faces the thigh)
		for (int f = 0; f < 4; f++)
		{
			float spread = (f - 1.5f) * 0.028f;
			float len = f is 1 or 2 ? 0.3f : 0.25f;
			Vector3 root = palmEnd + new Vector3(0, 0.01f, spread);
			Vector3 mid = root + new Vector3(0.012f * -s, -len * 0.5f, spread * 0.4f);
			Vector3 tip = mid + new Vector3(0.05f * -s, -len * 0.45f, spread * 0.3f + 0.02f);
			k.Color = HideTone;
			k.Cylinder(root, mid, 0.011f, 0.008f, 3, false, 1f, 0.3f);
			k.Cylinder(mid, tip, 0.008f, 0f, 3, false, 1f, 0.3f);
		}
		// thumb
		Vector3 th = wrist.Lerp(palmEnd, 0.4f) + new Vector3(0.015f * -s, 0, 0.04f);
		k.Cylinder(th, th + new Vector3(0.03f * -s, -0.14f, 0.04f), 0.01f, 0f, 3, false);

		// Tattered sleeves hanging off the back/outside of the upper arm and forearm.
		Vector3 outward = new(s, 0, -0.6f);
		for (int i = 0; i < 4; i++)
		{
			float t = i < 2 ? _rng.RandfRange(0.1f, 0.5f) : _rng.RandfRange(0.9f, 1.3f);
			Vector3 a = t <= 1f ? sh.Lerp(elbow, t) : elbow.Lerp(wrist, t - 1f);
			a += outward.Normalized() * 0.06f;
			Rag(k, a, outward.Rotated(Vector3.Up, _rng.RandfRange(-0.5f, 0.5f)), _rng.RandfRange(0.35f, 0.7f), _rng.RandfRange(0.07f, 0.12f), _rng.RandfRange(0.02f, 0.08f), 3);
		}
		// a shard off the elbow
		Shard(k, elbow + new Vector3(0.02f * s, 0, -0.04f), elbow + new Vector3(0.06f * s, 0.03f, -0.16f), 0.025f);
	}

	private void BuildHead(MeshKit k)
	{
		// Neck: long and thin, out of the front of the hump, forward and down in a
		// shallow S, so the head reads as hung out on it rather than sat on the shoulders.
		Vector3 hc = new(0.02f, 1.9f, 0.44f);   // head centre
		Loft(k, new Ring[]
		{
			new(NeckBase + new Vector3(0, 0.02f, -0.05f), 0.07f, 0.075f),
			new(new Vector3(0, 1.99f, 0.21f), 0.05f, 0.055f),
			new(new Vector3(0.01f, 1.975f, 0.29f), 0.041f, 0.048f),
			new(new Vector3(0.02f, 1.95f, 0.36f), 0.038f, 0.046f),
			new(hc + new Vector3(0, -0.035f, -0.07f), 0.042f, 0.05f),
		}, 6, false, false, side: Vector3.Right);

		// Head: small for the body, an elongated skull, tilted, chin tucked. Built along its
		// own axis (y up the skull, +z the face) then posed. Front to back it is half again
		// its width: a sloped forehead, a brow that overhangs the sockets, hollow cheeks under
		// the cheekbones, a jaw narrowing to a chin that juts a little, and the cranium swept
		// back into an occipital bulge. The front is a faint pale, featureless plane.
		var pose = new Transform3D(
			new Basis(Vector3.Forward, Mathf.DegToRad(16f)) * new Basis(Vector3.Right, Mathf.DegToRad(10f)),
			hc);
		var outer = k.Xf;
		k.Xf = outer * pose;
		Func<Vector3, Color> faceTone = n =>
		{
			// Pale only on the forward-facing plane of the face, fading at its edges.
			float front = Mathf.Clamp((n.Z - 0.55f) / 0.4f, 0f, 1f);
			return new Color(0.75f * front, 1f, 0.6f);
		};
		Loft(k, new Ring[]
		{
			new(new Vector3(0, -0.175f, 0.045f), 0.022f, 0.028f),   // chin, jutting
			new(new Vector3(0, -0.135f, 0.03f), 0.046f, 0.052f),    // jaw
			new(new Vector3(0, -0.075f, 0.005f), 0.05f, 0.082f),    // hollow cheeks: narrower than above and below
			new(new Vector3(0, -0.02f, -0.005f), 0.078f, 0.1f),     // cheekbones
			new(new Vector3(0, 0.035f, -0.002f), 0.082f, 0.118f),   // brow: the widest, and reaching furthest forward (over the sockets)
			new(new Vector3(0, 0.09f, -0.03f), 0.074f, 0.108f),     // forehead, sloping back
			new(new Vector3(0, 0.14f, -0.065f), 0.066f, 0.102f),    // cranium, swept back
			new(new Vector3(0, 0.18f, -0.095f), 0.05f, 0.08f),      // occipital bulge behind
			new(new Vector3(0, 0.205f, -0.11f), 0.022f, 0.035f),
		}, 10, true, true, tone: faceTone);
		// The lower jaw: a thin bar from the hinge below the ear round to the chin, so the
		// profile has a real jaw line and a shadow under the cheekbones.
		foreach (float sx in new[] { -1f, 1f })
			Loft(k, new Ring[]
			{
				new(new Vector3(sx * 0.052f, -0.05f, -0.02f), 0.012f, 0.014f),   // hinge
				new(new Vector3(sx * 0.05f, -0.11f, 0.01f), 0.013f, 0.016f),
				new(new Vector3(sx * 0.03f, -0.155f, 0.04f), 0.012f, 0.014f),
				new(new Vector3(0, -0.172f, 0.055f), 0.011f, 0.012f),           // chin
			}, 6, true, true, side: Vector3.Up, tone: faceTone);
		// A nasal ridge down the face plane, between the sockets.
		Loft(k, new Ring[]
		{
			new(new Vector3(0, 0.03f, 0.1f), 0.007f, 0.008f),
			new(new Vector3(0, -0.02f, 0.108f), 0.01f, 0.012f),
			new(new Vector3(0, -0.06f, 0.1f), 0.013f, 0.012f),
		}, 5, false, true, side: Vector3.Right, tone: faceTone);
		// Two sockets on the face plane, under the brow. Alpha 0 marks them as eyes: the skin
		// shader gives them a faint yellow glow (every other vertex has alpha 1).
		k.Color = new Color(0f, 0.3f, 0f, 0f);
		foreach (float ex in new[] { -0.03f, 0.03f })
		{
			Vector3 c = new(ex, 0.008f, 0.092f);
			Vector3 a = c + new Vector3(-0.018f, 0.004f, -0.004f), b = c + new Vector3(0, 0.014f, 0.001f),
				d = c + new Vector3(0.018f, 0.002f, -0.004f), e = c + new Vector3(0.002f, -0.016f, 0.0f);
			k.Tri(a, b, d, Vector3.Back, Vector2.Zero, Vector2.Zero, Vector2.Zero);
			k.Tri(a, d, e, Vector3.Back, Vector2.Zero, Vector2.Zero, Vector2.Zero);
		}
		k.Xf = outer;
	}


	// ─────────────────────────────── eye glow ───────────────────────────────

	/// <summary>Design-space centres of the two eye sockets (see BuildHead), before the head's pose.</summary>
	private static readonly Vector3[] EyeSockets = { new(-0.03f, 0.008f, 0.092f), new(0.03f, 0.008f, 0.092f) };

	/// <summary>The head's pose in design space (BuildHead): the sockets go through this before the part's pivot.</summary>
	private static readonly Transform3D HeadPose = new(
		new Basis(Vector3.Forward, Mathf.DegToRad(16f)) * new Basis(Vector3.Right, Mathf.DegToRad(10f)),
		new Vector3(0.02f, 1.9f, 0.44f));

	/// <summary>World position between the eyes right now (rides the head's pose and twitch): where a
	/// cutscene camera looks to meet its gaze.</summary>
	/// <summary>A world point the head turns toward (pitch only: the body is yawed to face it by its owner),
	/// with a slight bow of the upper body. Null = the idle drift alone. Smoothed per frame (Act 11: the giant
	/// looking down at the player).</summary>
	public Vector3? LookTarget { get; set; }
	/// <summary>How much of the head's look pitch the upper body follows (a bow).</summary>
	[Export] public float LookBow = 0.3f;
	private float _lookPitch;

	public Vector3 EyesWorld
	{
		get
		{
			if (_head == null) return GlobalPosition + Vector3.Up * 1.9f * Size;
			Vector3 mid = (HeadPose * new Vector3(0f, 0.008f, 0.092f) - NeckBase) * Size;
			return _head.ToGlobal(mid);
		}
	}

	/// <summary>
	/// Lights the eyes: two small unshaded beads in the sockets and a faint light between them,
	/// so that with every lamp out they are all that can be seen of it (the bunker's rooms).
	/// Parented to the head, so they ride its twitch. Safe to call once per body.
	/// </summary>
	public void GlowEyes(Color colour, float energy = 6f)
	{
		if (_head == null || _head.GetNodeOrNull("EyeGlow") != null) return;
		var pose = new Transform3D(
			new Basis(Vector3.Forward, Mathf.DegToRad(16f)) * new Basis(Vector3.Right, Mathf.DegToRad(10f)),
			new Vector3(0.02f, 1.9f, 0.44f));
		var root = new Node3D { Name = "EyeGlow" };
		root.SetMeta("stalker_generated", true);
		_head.AddChild(root);
		var mat = new StandardMaterial3D
		{
			AlbedoColor = colour, EmissionEnabled = true, Emission = colour, EmissionEnergyMultiplier = energy,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		Vector3 mid = Vector3.Zero;
		foreach (var s in EyeSockets)
		{
			// Head-part local = (design point - the part's pivot) * Size; the socket sits a hair proud of the face.
			Vector3 p = (pose * (s + new Vector3(0, 0, 0.004f)) - NeckBase) * Size;
			mid += p * 0.5f;
			var k = new MeshKit();
			k.Mat(mat);
			k.Blob(p, new Vector3(0.011f, 0.008f, 0.006f) * Size, 6, 0f, false);
			k.CommitTo(root, "Eye", false);
		}
		root.AddChild(new OmniLight3D
		{
			Name = "Light", LightColor = colour, LightEnergy = 0.9f, OmniRange = 2.2f * Size, OmniAttenuation = 1.6f,
			Position = mid + new Vector3(0, 0, 0.12f * Size), ShadowEnabled = false,
		});
	}
	// ─────────────────────────────── idle ───────────────────────────────

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint() || !Idle || !IsVisibleInTree()) return;
		float dt = (float)delta;
		_t += dt;

		// Head twitch: a snap to one side, a hold, then a slow return.
		_twitchTimer -= dt;
		if (!_twitchOut && _twitchTimer <= 0f)
		{
			_twitchOut = true;
			float d = Mathf.DegToRad(TwitchDegrees);
			_twitchTarget = new Vector3(_idleRng.RandfRange(-0.3f, 0.3f) * d, _idleRng.RandfRange(-0.7f, 0.7f) * d,
				(_idleRng.Randf() < 0.5f ? -1f : 1f) * _idleRng.RandfRange(0.6f, 1f) * d);
			_twitchHold = _idleRng.RandfRange(0.25f, 1.1f);
		}
		if (_twitchOut)
		{
			_twitch = _twitch.Lerp(_twitchTarget, 1f - Mathf.Exp(-35f * dt));
			if (_twitch.DistanceTo(_twitchTarget) < 0.01f && (_twitchHold -= dt) <= 0f)
			{
				_twitchOut = false;
				_twitchTimer = _idleRng.RandfRange(TwitchInterval.X, TwitchInterval.Y);
			}
		}
		else _twitch = _twitch.Lerp(Vector3.Zero, 1f - Mathf.Exp(-1.3f * dt));

		ApplyPose();
	}

	private void ApplyPose()
	{
		if (_upper == null) return;
		float w = Mathf.Tau / Mathf.Max(SwaySeconds, 0.1f);
		float sway = Mathf.DegToRad(SwayDegrees);
		float drift = Mathf.DegToRad(HeadDriftDegrees);
		bool live = !Engine.IsEditorHint() && Idle;
		float t = live ? _t : 0f;

		_upper.Rotation = UpperBase + (live ? new Vector3(
			sway * 0.5f * Mathf.Sin(t * 0.63f),          // slow breath-like hunch
			sway * 0.6f * Mathf.Sin(t * w * 0.7f + 1f),
			sway * Mathf.Sin(t * w)) : Vector3.Zero);
		_head.Rotation = HeadBase + (live ? new Vector3(
			drift * 0.4f * Mathf.Sin(t * 0.21f + 2f),
			drift * 0.5f * Mathf.Sin(t * 0.17f),
			drift * Mathf.Sin(t * 0.13f + 0.5f)) : Vector3.Zero) + _twitch;
		// The look: pitch from the eyes to the target (negative = below), eased in, the upper body bowing a
		// share of it. Rotation about X tilts the +Z face DOWN for positive angles, hence the sign flip.
		float wantLook = 0f;
		if (LookTarget is { } target && IsInsideTree())
		{
			Vector3 d = GlobalTransform.Basis.Inverse() * (target - EyesWorld);
			wantLook = Mathf.Clamp(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), Mathf.DegToRad(-75f), Mathf.DegToRad(25f));
		}
		_lookPitch = live ? Mathf.Lerp(_lookPitch, wantLook, 1f - Mathf.Exp(-2.5f * (float)GetProcessDeltaTime())) : wantLook;
		_head.Rotation += new Vector3(-_lookPitch * (1f - LookBow), 0, 0);
		_upper.Rotation += new Vector3(-_lookPitch * LookBow, 0, 0);
		float arm = live ? sway * 0.8f : 0f;
		_armL.Rotation = new Vector3(arm * Mathf.Sin(t * w * 1.3f), 0, arm * 0.4f * Mathf.Sin(t * w * 0.9f + 2f));
		_armR.Rotation = new Vector3(arm * Mathf.Sin(t * w * 1.3f + 2.2f), 0, arm * 0.4f * Mathf.Sin(t * w * 0.9f));
		if (WalkPhase >= 0f)
		{
			// The gait: arms swing against each other (the left forward as the right foot lands), the
			// torso leans into the walk, rolls onto the planted foot and dips a touch at each footfall.
			float g = WalkAmount;
			float swing = Mathf.Sin(WalkPhase);
			float land = Mathf.Abs(Mathf.Cos(WalkPhase));   // 1 at each footfall, 0 mid-stride
			_armL.Rotation += new Vector3(0.42f * g * swing, 0, 0.06f * g);
			_armR.Rotation += new Vector3(-0.42f * g * swing, 0, -0.06f * g);
			_upper.Rotation += new Vector3(0.14f * g + 0.03f * g * land, 0.05f * g * swing, 0.05f * g * swing);
			_head.Rotation += new Vector3(-0.06f * g, 0, -0.03f * g * swing);
		}
	}
}
