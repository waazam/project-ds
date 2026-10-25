using System;
using System.Linq;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;
using ProjectDS.World.LakeParts;

namespace ProjectDS.Entities;

/// <summary>
/// Act 18's boss: the thing from the lake (Act 12), at home. A vast mound of hide at the bottom of a
/// blood-filled pit, crusted with eyes, eight tentacles thick as trees rising out of the blood round
/// it. It cannot be fought. It can be drained, and it rots as the blood goes.
///
/// The tentacles are posed on curves from their roots to their tips, so a slam can be aimed: a limb
/// rears up over a spot, holds there (the telegraph), strikes down onto it, lies there a moment, and
/// draws back. <see cref="Impact"/> fires when it lands.
///
/// <see cref="Rot"/> (0..1) is how far gone it is. The hide goes from glossy purple-grey to mottled,
/// sore-covered, sloughing zombie flesh (<c>octopus_flesh.gdshader</c>), the eyes cloud over, and it
/// thrashes harder and faster: hurt, and rabid.
///
/// Local space: y=0 is the pit floor; the body sits in the middle.
/// </summary>
public partial class Leviathan : Node3D
{
	public const int Segments = 18;
	/// <summary>Limbs round the body (the owner: "multiply them tentacles").</summary>
	public const int LimbCount = 14;
	/// <summary>How long the strike itself takes, from the top of the rear to the catwalk.</summary>
	public const float StrikeSeconds = 0.3f;
	public const float Length = 30f;
	/// <summary>The tube's rings (and bones) along a limb: two to a segment.</summary>
	/// <summary>(2026-10-11, the owner: the tentacles "all segmented and not a concise designed model": four rings to a
	/// segment now, not two, so a tight bend curves instead of hinging at every ring)</summary>
	private const int RingsPer = 4, Rings = Segments * RingsPer + 1, Sides = 18;

	public sealed class Tentacle
	{
		public Node3D Root;
		public readonly List<Node3D> Segs = new();
		/// <summary>Its flesh is the Blender limb (its cups shaped into its skin: no separate suckers).</summary>
		public bool Model;
		public readonly List<Node3D> Eyes = new();
		public float Angle, Phase, BaseR;
		/// <summary>(2026-10-10) its build: a thick one, a middling one, or a skinny whip of a thing with no mouth;
		/// how far it reaches, and how much it writhes.</summary>
		public bool Skinny;
		public float Reach = 1f, Whip = 1f;
		/// <summary>The one smooth tube of its flesh, on a chain of bones (a ring of the tube to each).</summary>
		public Skeleton3D Skel;
		public readonly Vector3[] Pts = new Vector3[Rings];
		public readonly Basis[] Frames = new Basis[Rings];
		public Vector3 RootLocal;
		public enum S { Idle, Raise, Hold, Strike, Down, Recover, Limp }
		public S State = S.Idle;
		public float T, RaiseTime, HoldTime;
		public Vector3 Target;        // local
		public Vector3 Tip, Ctrl;     // local, current
		public Vector3 FromTip, FromCtrl;
		public bool Hit;
		/// <summary>(2026-10-10c) How it comes down (<see cref="Style"/>); a double's second blow still to come.</summary>
		public Style How;
		public bool Second;
		public float Side = 1f;
	}

	/// <summary>(2026-10-10c, the owner: "there should be different attack styles for the lovecraftian monster boss fight
	/// for the tentacles slamming so it all doesn't look the same") Overhead: reared straight up over the spot and
	/// smashed down. Sweep: coiled low off to one side and lashed across the catwalk in an arc onto the spot. Corkscrew:
	/// reared high, its tip coiling round and round, then down in a spiral. Double: a short rear and two blows hammered
	/// onto the spot one after the other. Whip: drawn far back and up, then cracked down, the tip trailing the body so it
	/// snaps. Every one lands on the spot it was aimed at, when the telegraph says.</summary>
	public enum Style { Overhead, Sweep, Corkscrew, Double, Whip }
	private readonly RandomNumberGenerator _styleRng = new() { Seed = 1818 };

	/// <summary>A style for the next slam: the plain ones while it's fresh, all of them as it rots and rages.</summary>
	private Style PickStyle(Tentacle t)
	{
		float r = _styleRng.Randf();
		if (t.Skinny) return r < 0.6f ? Style.Whip : Style.Sweep;
		if (_rot < 0.25f) return r < 0.55f ? Style.Overhead : r < 0.8f ? Style.Sweep : Style.Whip;
		return r < 0.24f ? Style.Overhead : r < 0.44f ? Style.Sweep : r < 0.64f ? Style.Corkscrew : r < 0.84f ? Style.Double : Style.Whip;
	}

	/// <summary>Fires when a slam lands (world position on the catwalk).</summary>
	public event Action<Vector3, Tentacle> Impact;

	public float Rot { get => _rot; set { _rot = Mathf.Clamp(value, 0f, 1f); ApplyRot(); } }
	/// <summary>The blood's surface (local y): the limbs rise out of it.</summary>
	public float Level { get; set; } = 22.5f;
	public bool Dead { get; private set; }
	public int EyeCount => _bodyEyes.Count + _tentacles.Sum(t => t.Eyes.Count);
	public int EyesBurst { get; private set; }
	public IReadOnlyList<Tentacle> Tentacles => _tentacles;
	public bool AnySlamming => _tentacles.Exists(t => t.State is Tentacle.S.Raise or Tentacle.S.Hold or Tentacle.S.Strike);

	private readonly List<Tentacle> _tentacles = new();
	private readonly List<Node3D> _bodyEyes = new();
	private readonly List<float> _eyeSize = new();
	private Node3D _body;
	private ShaderMaterial _skin, _bodySkin, _tubeSkin;
	private StandardMaterial3D _sclera, _iris;
	private float _rot, _time, _convulse;
	private readonly RandomNumberGenerator _rng = new() { Seed = 1818 };
	private Camera3D _cam;

	public override void _Ready()
	{
		CreatureRim.Apply(this, far: true);
		World.PhotoSubject.Attach(this, "pit_beast", new Vector3(0, 3f, 0), 4f, 90f, 18f, false, new Vector3(0, 6f, 0));
		// gooey, grimy octopus flesh that rots as the fight goes on (octopus_flesh.gdshader); the body's
		// back is its top, the limbs' is away from their suckers
		_skin = TentacleKit.Flesh(null, 0.8f);
		_bodySkin = TentacleKit.Flesh(Vector3.Up, 0.35f, 0.4f);
		_tubeSkin = TentacleKit.Flesh(null, 0.8f, 0.55f);
		_tubeSkin.SetShaderParameter("rest_space", true);
		_tubeSkin.SetShaderParameter("vc_suckers", TentacleModels.Available);   // (the Blender limb paints its cups in)
		_body = new Node3D { Name = "Body", Position = new Vector3(0, 5f, 0) };
		AddChild(_body);
		for (int i = 0; i < LimbCount; i++) BuildTentacle(i);
		BuildBody();
		ApplyRot();
	}

	// ------------------------------------------------------------------ building

	private void BuildBody()
	{
		// (2026-10-11, the owner: "chunked" pieces, "we need to bridge the meshes") the hide one continuous surface: the
		// same lobes as before (the mound, its nine lumps, the folds round the waterline, a swollen collar where each limb
		// leaves it), not drawn as overlapping balls any more but fused, a sphere pushed out to their soft union, so one
		// lump flows into the next with no crease where they met
		var lobes = new List<(Vector3 c, Vector3 r)> { (Vector3.Zero, new Vector3(10.5f, 8f, 10.5f)) };
		for (int i = 0; i < 9; i++)
		{
			float a = i / 9f * Mathf.Tau + 0.3f;
			float lr = _rng.RandfRange(3.8f, 5.4f);
			lobes.Add((new Vector3(Mathf.Cos(a) * 7f, _rng.RandfRange(-2f, 2.5f), Mathf.Sin(a) * 7f), new Vector3(lr * 1.25f, lr * 0.72f, lr * 1.25f)));
		}
		for (int i = 0; i < 14; i++)
		{
			float a = (i + 0.5f) / 14f * Mathf.Tau;
			lobes.Add((new Vector3(Mathf.Cos(a) * 9.2f, _rng.RandfRange(-3.5f, -1f), Mathf.Sin(a) * 9.2f), new Vector3(2.6f, 1.6f, 2.6f) * _rng.RandfRange(0.8f, 1.2f)));
		}
		for (int ti = 0; ti < _tentacles.Count; ti++)
		{
			var t = _tentacles[ti];
			Vector3 rel = t.RootLocal - _body.Position;
			Vector3 e = new(rel.X / 10.5f, rel.Y / 8f, rel.Z / 10.5f);
			Vector3 dir = e.LengthSquared() > 0.001f ? e.Normalized() : Vector3.Up;
			Vector3 surf = new(dir.X * 10.3f, dir.Y * 7.9f, dir.Z * 10.3f);
			Vector3 toward = (rel + Vector3.Up * 3f - surf) * 0.25f;
			float cr = t.BaseR * 1.9f + 0.4f;
			lobes.Add((surf, new Vector3(cr, cr * 0.8f, cr)));
			lobes.Add((surf + toward + dir * cr * 0.35f, Vector3.One * cr * 0.7f));
		}
		_lobes = lobes;
		_hideNoise = new FastNoiseLite { Seed = 1818, Frequency = 0.35f, FractalOctaves = 3 };
		// (2026-10-15, "everything made in blender") the hide sculpted in Blender on this same layout (tools/Blender/tentacle.py:
		// its folds, wrinkles and sores, a socket and a swollen lid for each eye below); the sphere pushed out to the lobes
		// below only if the model is missing. (The lobes still place the eyes: the same rng, the same sockets.)
		if (TentacleModels.Get("LeviathanHide") is { } hide)
		{
			var hmi = new MeshInstance3D { Name = "Hide", Mesh = hide };
			for (int s = 0; s < hide.GetSurfaceCount(); s++) hmi.SetSurfaceOverrideMaterial(s, _bodySkin);
			_body.AddChild(hmi);
		}
		else BuildHideSphere();
		// eyes all over the upper half, big ones near the crown
		for (int e = 0; e < 46; e++)
		{
			float a = _rng.RandfRange(0, Mathf.Tau), up = Mathf.Lerp(0.15f, 1f, Mathf.Pow(_rng.Randf(), 0.7f));
			Vector3 dir = new Vector3(Mathf.Cos(a) * Mathf.Sqrt(1 - up * up), up, Mathf.Sin(a) * Mathf.Sqrt(1 - up * up)).Normalized();
			Vector3 ed = new Vector3(dir.X * 10.2f, dir.Y * 7.8f, dir.Z * 10.2f).Normalized();
			Vector3 at = ed * (HideRadius(ed) - 0.15f);   // (sat in the fused hide's own surface)
			var socket = new Node3D { Name = $"Eye{e}", Position = at };
			_body.AddChild(socket);
			BuildEye(socket);
			_bodyEyes.Add(socket);
			_eyeSize.Add(Mathf.Lerp(0.6f, 2.3f, Mathf.Pow(up, 2f)) * _rng.RandfRange(0.7f, 1.1f));
		}
		if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--dump-leviathan") >= 0) DumpHide();
	}

	/// <summary>The code-built hide (the fallback): a sphere pushed out to the lobes' soft union.</summary>
	private void BuildHideSphere()
	{
		var sphere = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 160, Rings = 80 }.GetMeshArrays();
		var src = (Vector3[])sphere[(int)Mesh.ArrayType.Vertex];
		var v = new Vector3[src.Length];
		var nrm = new Vector3[src.Length];
		for (int i = 0; i < src.Length; i++)
		{
			var d = src[i].Normalized();
			v[i] = d * HideRadius(d);
			// the normal from the surface itself (two nearby rays): no seam where the sphere's own ends meet
			var tu = d.Cross(Mathf.Abs(d.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();
			var tv = d.Cross(tu);
			const float h = 0.004f;
			var du = (d + tu * h).Normalized();
			var dv = (d + tv * h).Normalized();
			var n = (du * HideRadius(du) - v[i]).Cross(dv * HideRadius(dv) - v[i]).Normalized();
			nrm[i] = n.Dot(d) < 0 ? -n : n;
		}
		sphere[(int)Mesh.ArrayType.Vertex] = v;
		sphere[(int)Mesh.ArrayType.Normal] = nrm;
		sphere[(int)Mesh.ArrayType.Tangent] = default;
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, sphere);
		mesh.SurfaceSetMaterial(0, _bodySkin);
		_body.AddChild(new MeshInstance3D { Name = "Hide", Mesh = mesh });
	}

	/// <summary>`--dump-leviathan` (dev): the hide's radius on a 2-degree grid and the eye sockets, for tools/Blender/tentacle.py
	/// to sculpt the Blender hide on the game's own layout (tools/Blender/data/leviathan_hide.json).</summary>
	private void DumpHide()
	{
		var sb = new System.Text.StringBuilder("{\"theta_step\":2,\"phi_step\":2,\"radius\":[");
		for (int ti = 0; ti <= 90; ti++)
		{
			if (ti > 0) sb.Append(',');
			sb.Append('[');
			float th = Mathf.DegToRad(ti * 2f);
			for (int pi = 0; pi <= 180; pi++)
			{
				float ph = Mathf.DegToRad(pi * 2f);
				var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
				if (pi > 0) sb.Append(',');
				sb.Append(HideRadius(d).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
			}
			sb.Append(']');
		}
		sb.Append("],\"eyes\":[");
		for (int e = 0; e < _bodyEyes.Count; e++)
		{
			var p = _bodyEyes[e].Position;
			if (e > 0) sb.Append(',');
			sb.Append(System.FormattableString.Invariant($"[{p.X:0.####},{p.Y:0.####},{p.Z:0.####},{_eyeSize[e]:0.####}]"));
		}
		sb.Append("]}");
		var path = ProjectSettings.GlobalizePath("res://tools/Blender/data/leviathan_hide.json");
		System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
		System.IO.File.WriteAllText(path, sb.ToString());
		GD.Print($"[leviathan] hide dumped to {path}");
	}

	private List<(Vector3 c, Vector3 r)> _lobes;
	private FastNoiseLite _hideNoise;

	/// <summary>How far out from the body's middle its hide is along <paramref name="d"/>: the far side of each lobe the
	/// ray passes through, joined in a smooth maximum (so where two lobes meet they blend over a metre or so instead of
	/// creasing), and a slow ripple of folds over it.</summary>
	private float HideRadius(Vector3 d)
	{
		float r = 0f;
		bool any = false;
		const float k = 1.6f;
		foreach (var (c, rad) in _lobes)
		{
			var D = new Vector3(d.X / rad.X, d.Y / rad.Y, d.Z / rad.Z);
			var C = new Vector3(c.X / rad.X, c.Y / rad.Y, c.Z / rad.Z);
			float a = D.Dot(D), b = D.Dot(C), cc = C.Dot(C) - 1f;
			float disc = b * b - a * cc;
			if (disc < 0f) continue;
			float t = (b + Mathf.Sqrt(disc)) / a;
			if (t <= 0f) continue;
			if (!any) { r = t; any = true; continue; }
			// the polynomial smooth max
			float hh = Mathf.Max(k - Mathf.Abs(r - t), 0f) / k;
			r = Mathf.Max(r, t) + hh * hh * k * 0.25f;
		}
		if (!any) r = 8f;
		return r * (1f + 0.025f * _hideNoise.GetNoise3Dv(d * 6f));
	}

	private void BuildEye(Node3D socket)
	{
		LakeCreature.BuildEye(socket);
		// this creature's own copies of the eye's materials, so they can cloud over as it rots
		var parts = socket.GetChildren();
		if (parts.Count >= 2 && parts[0] is MeshInstance3D white && parts[1] is MeshInstance3D iris)
		{
			_sclera ??= (StandardMaterial3D)((StandardMaterial3D)white.MaterialOverride).Duplicate();
			_iris ??= (StandardMaterial3D)((StandardMaterial3D)iris.MaterialOverride).Duplicate();
			white.MaterialOverride = _sclera;
			iris.MaterialOverride = _iris;
		}
	}

	/// <summary>The limbs' builds round the body (the owner: "The monster's tentacles should vary in sizes and thickness
	/// having some skinny tentacles"): T thick, M middling, S skinny.</summary>
	private const string Builds = "TSMTSMSTMSMTSM";

	private void BuildTentacle(int i)
	{
		char build = Builds[i % Builds.Length];
		var t = new Tentacle
		{
			Angle = i / (float)LimbCount * Mathf.Tau + 0.2f + _rng.RandfRange(-0.12f, 0.12f), Phase = _rng.RandfRange(0, Mathf.Tau),
			// (2026-10-24, the owner: "make the octopus tentacles skinnier they look way too fat and chody ... still vary them in
			// sizes": about two thirds of what they were, the spread between thick and whip kept)
			BaseR = build switch { 'T' => _rng.RandfRange(0.85f, 1.12f), 'M' => _rng.RandfRange(0.5f, 0.7f), _ => _rng.RandfRange(0.18f, 0.3f) },
			Skinny = build == 'S',
			Reach = build switch { 'T' => _rng.RandfRange(0.95f, 1.05f), 'M' => _rng.RandfRange(0.9f, 1.15f), _ => _rng.RandfRange(0.8f, 1.3f) },
			Whip = build switch { 'T' => 0.8f, 'M' => 1.1f, _ => 1.9f },
		};
		// the thick ones from low round the body, the skinny ones from up near the crown, between the others
		float rootR = build switch { 'T' => 8.6f, 'M' => 7.8f, _ => 6.2f };
		float rootY = build switch { 'T' => 3.5f, 'M' => 6.5f, _ => 9.5f };
		t.RootLocal = new Vector3(Mathf.Cos(t.Angle) * rootR, rootY, Mathf.Sin(t.Angle) * rootR);
		t.Root = new Node3D { Name = $"Tentacle{i}" };
		AddChild(t.Root);
		BuildTube(t, i);
		BuildSuckers(t);
		float segLen = Length / Segments;
		for (int s = 0; s < Segments; s++)
		{
			float f0 = (float)s / Segments, f1 = (float)(s + 1) / Segments;
			float r0 = Radius(t, f0), r1 = Radius(t, f1);
			var seg = new Node3D { Name = $"S{s}" };
			t.Root.AddChild(seg);
			t.Segs.Add(seg);
			// (the suckers are on the tube itself now: BuildSuckers)
			// eyes the whole way up: big (and two to a segment) where it meets the body, shrinking to the tip; on a
			// skinny one, a few small ones
			int perSeg = t.Skinny ? (s % 3 == 1 && s < 13 ? 1 : 0) : s < 4 ? 2 : s % 2 == 0 ? 1 : 0;
			for (int e = 0; e < perSeg && s < Segments - 1; e++)
			{
				float f = (s + 0.5f) / Segments;
				float ang = Mathf.Pi * 0.5f + _rng.RandfRange(0.8f, Mathf.Tau - 0.8f), rr = Mathf.Lerp(r0, r1, 0.5f);
				var socket = new Node3D { Name = $"Eye{s}_{e}", Position = new Vector3(Mathf.Cos(ang) * rr * 0.92f, segLen * (0.3f + 0.4f * e), Mathf.Sin(ang) * rr * 0.92f) };
				seg.AddChild(socket);
				BuildEye(socket);
				socket.SetMeta("size", Mathf.Min(TentacleKit.EyeSize(t.BaseR * 1.05f, 0.22f, f) * _rng.RandfRange(0.85f, 1.15f), rr * 1.0f));
				t.Eyes.Add(socket);
			}
		}
		// the mouth at the tip: jaws round a toothed throat (the skinny ones just taper to a curling point)
		if (!t.Skinny)
			(TentacleModels.Maw(t.Segs[Segments - 1], t.BaseR * 1.5f, _skin)
				?? TentacleKit.Maw(t.Segs[Segments - 1], Radius(t, 1f), t.BaseR * 1.5f, _skin, i * 7 + 3)).Position = Vector3.Up * segLen;
		_tentacles.Add(t);
		t.Tip = IdleTip(t);
		t.Ctrl = IdleCtrl(t, t.Tip);
	}

	/// <summary>The limb's flesh as one smooth tube (it was a stack of separate cylinders, blocky at every joint):
	/// built straight up +Y in its rest pose, a ring of <see cref="Sides"/> to each bone of a chain, and posed each
	/// frame by putting the bones along the limb's curve. The suckers' side is +Z.</summary>
	private void BuildTube(Tentacle t, int seed)
	{
		var skel = new Skeleton3D { Name = "Skel" };
		t.Root.AddChild(skel);
		for (int r = 0; r < Rings; r++)
		{
			skel.AddBone($"R{r}");
			skel.SetBoneRest(r, new Transform3D(Basis.Identity, new Vector3(0, r / (float)(Rings - 1) * Length, 0)));
		}
		skel.ResetBonePoses();
		// (2026-10-15, "everything made in blender") the Blender limb (tools/Blender/tentacle.py: its cups shaped into its
		// skin, painted in its vertex colour), laid along this limb's length and radius and skinned to the rings; the
		// code-built tube below only if the model is missing
		var model = TentacleModels.Limb(t.Skinny ? "LimbWhip" : "LimbLong", Length, f => Radius(t, f), _tubeSkin, TentacleKit.SuckerRim,
			TentacleKit.SuckerHole, rest: true, skin: p =>
			{
				float fr = Mathf.Clamp(p.Y / Length, 0f, 1f) * (Rings - 1);
				int i0 = Mathf.Min((int)fr, Rings - 2);
				float w = fr - i0;
				return (new[] { i0, i0 + 1 }, new[] { 1f - w, w });
			});
		if (model != null)
		{
			var mmi = new MeshInstance3D
			{
				Name = "Flesh", Mesh = model,
				CustomAabb = new Aabb(new Vector3(-45, -10, -45), new Vector3(90, 60, 90)),
			};
			skel.AddChild(mmi);
			mmi.Skeleton = mmi.GetPathTo(skel);
			mmi.Skin = skel.CreateSkinFromRestTransforms();
			t.Skel = skel;
			t.Model = true;
			return;
		}
		var noise = new FastNoiseLite { Seed = seed * 31 + 7, Frequency = 0.9f };
		int ringVerts = Sides + 1;
		int n = Rings * ringVerts + 1;
		var v = new Vector3[n];
		var nrm = new Vector3[n];
		var uv = new Vector2[n];
		var col = new Color[n];
		var bones = new int[n * 4];
		var wts = new float[n * 4];
		var c0 = new float[n * 4];
		var c1 = new float[n * 4];
		float circ = Mathf.Tau * t.BaseR;
		for (int r = 0; r < Rings; r++)
		{
			float f = r / (float)(Rings - 1), y = f * Length, rad = Radius(t, f);
			float slope = (Radius(t, Mathf.Max(0f, f - 0.01f)) - Radius(t, Mathf.Min(1f, f + 0.01f))) / (0.02f * Length);
			for (int j = 0; j <= Sides; j++)
			{
				float a = j / (float)Sides * Mathf.Tau;
				Vector3 d = new(Mathf.Sin(a), 0, Mathf.Cos(a));   // j=0 faces +Z, the suckers
				// the flesh a little uneven round it (bulges and creases running along it), never a perfect pipe; the
				// seam (j = 0 and j = Sides) sampled the same so it closes
				float bulge = 1f + 0.07f * noise.GetNoise2D(Mathf.Cos(a) * 1.5f + y * 0.25f, Mathf.Sin(a) * 1.5f);
				int idx = r * ringVerts + j;
				v[idx] = new Vector3(0, y, 0) + d * rad * bulge;
				nrm[idx] = (d + Vector3.Up * slope).Normalized();
				uv[idx] = new Vector2(j / (float)Sides * circ, y);
				col[idx] = Colors.White;
				bones[idx * 4] = r;
				wts[idx * 4] = 1f;
			}
		}
		// the point of the tip
		int tipI = n - 1;
		v[tipI] = new Vector3(0, Length + Radius(t, 1f) * 0.8f, 0);
		nrm[tipI] = Vector3.Up;
		uv[tipI] = new Vector2(0, Length);
		col[tipI] = Colors.White;
		bones[tipI * 4] = Rings - 1;
		wts[tipI * 4] = 1f;
		for (int i = 0; i < n; i++)
		{
			c0[i * 4] = v[i].X; c0[i * 4 + 1] = v[i].Y; c0[i * 4 + 2] = v[i].Z; c0[i * 4 + 3] = 1f;
			c1[i * 4] = nrm[i].X; c1[i * 4 + 1] = nrm[i].Y; c1[i * 4 + 2] = nrm[i].Z; c1[i * 4 + 3] = 0f;
		}
		var idxs = new List<int>(Rings * Sides * 6 + Sides * 3);
		for (int r = 0; r < Rings - 1; r++)
			for (int j = 0; j < Sides; j++)
			{
				int a = r * ringVerts + j, b = a + 1, c = a + ringVerts, d = c + 1;
				idxs.Add(a); idxs.Add(c); idxs.Add(b);
				idxs.Add(b); idxs.Add(c); idxs.Add(d);
			}
		int last = (Rings - 1) * ringVerts;
		for (int j = 0; j < Sides; j++) { idxs.Add(last + j); idxs.Add(tipI); idxs.Add(last + j + 1); }
		var arr = new Godot.Collections.Array();
		arr.Resize((int)Mesh.ArrayType.Max);
		arr[(int)Mesh.ArrayType.Vertex] = v;
		arr[(int)Mesh.ArrayType.Normal] = nrm;
		arr[(int)Mesh.ArrayType.TexUV] = uv;
		arr[(int)Mesh.ArrayType.Color] = col;
		arr[(int)Mesh.ArrayType.Bones] = bones;
		arr[(int)Mesh.ArrayType.Weights] = wts;
		arr[(int)Mesh.ArrayType.Custom0] = c0;
		arr[(int)Mesh.ArrayType.Custom1] = c1;
		arr[(int)Mesh.ArrayType.Index] = idxs.ToArray();
		var flags = (Mesh.ArrayFormat)(((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
			| ((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift));
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr, null, null, flags);
		mesh.SurfaceSetMaterial(0, _tubeSkin);
		var mi = new MeshInstance3D
		{
			Name = "Flesh", Mesh = mesh,
			// (posed by its bones anywhere in the pit: never culled by its rest pose's box)
			CustomAabb = new Aabb(new Vector3(-45, -10, -45), new Vector3(90, 60, 90)),
		};
		skel.AddChild(mi);
		mi.Skeleton = mi.GetPathTo(skel);
		mi.Skin = skel.CreateSkinFromRestTransforms();
		t.Skel = skel;
	}

	/// <summary>The suckers down the limb's underside, skinned to the tube's own bones (each vertex blended between the
	/// two rings either side of it), so they bend with the flesh as one thing. (They were rigid blocks, one to each
	/// segment, and at every bend the rows broke apart: the limb read as a stack of pieces.)</summary>
	private void BuildSuckers(Tentacle t)
	{
		if (t.Model) return;   // (the Blender limb has them in its skin)
		float segLen = Length / Segments;
		var k = new MeshKit();
		for (int s = 0; s < Segments - 1; s++)
		{
			float r0 = Radius(t, (float)s / Segments), r1 = Radius(t, (float)(s + 1) / Segments);
			if (r1 <= 0.12f) continue;
			k.Xf = new Transform3D(Basis.Identity, new Vector3(0, s * segLen, 0));
			TentacleKit.Suckers(k, segLen, r0, r1, t.Skinny ? 3 : 2);
		}
		if (k.IsEmpty) return;
		var src = k.Commit();
		var mesh = new ArrayMesh();
		for (int si = 0; si < src.GetSurfaceCount(); si++)
		{
			var arr = src.SurfaceGetArrays(si);
			var v = (Vector3[])arr[(int)Mesh.ArrayType.Vertex];
			var bones = new int[v.Length * 4];
			var wts = new float[v.Length * 4];
			for (int i = 0; i < v.Length; i++)
			{
				float f = Mathf.Clamp(v[i].Y / Length, 0f, 1f) * (Rings - 1);
				int i0 = Mathf.Min((int)f, Rings - 2);
				float w = f - i0;
				bones[i * 4] = i0; bones[i * 4 + 1] = i0 + 1;
				wts[i * 4] = 1f - w; wts[i * 4 + 1] = w;
			}
			arr[(int)Mesh.ArrayType.Bones] = bones;
			arr[(int)Mesh.ArrayType.Weights] = wts;
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
			mesh.SurfaceSetMaterial(si, src.SurfaceGetMaterial(si));
		}
		var mi = new MeshInstance3D
		{
			Name = "Suckers", Mesh = mesh, CustomAabb = new Aabb(new Vector3(-45, -10, -45), new Vector3(90, 60, 90)),
			CastShadow = t.Skinny ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
		};
		t.Skel.AddChild(mi);
		mi.Skeleton = mi.GetPathTo(t.Skel);
		mi.Skin = t.Skel.CreateSkinFromRestTransforms();
	}

	/// <summary>The limb's radius at f (0 root .. 1 tip): swelling out into the body at its root; tapering to its
	/// mouth, thick enough to carry it, or for a skinny one, to a fine point.</summary>
	private static float Radius(Tentacle t, float f)
	{
		float tipR = t.Skinny ? 0.04f : Mathf.Max(0.16f, t.BaseR * 0.24f);
		float r = Mathf.Lerp(t.BaseR, tipR, Mathf.Pow(f, t.Skinny ? 0.85f : 1.25f));
		return r * (1f + 0.55f * (1f - Mathf.SmoothStep(0f, 0.12f, f)));
	}

	// ------------------------------------------------------------------ the fight's API

	/// <summary>How hurt and rabid it is, for its movement: 1 at no rot, up to about 2.5 at full.</summary>
	private float Rage => 1f + 1.5f * _rot;

	/// <summary>Rear a free limb (the one best placed) up over <paramref name="world"/> and bring it down
	/// after <paramref name="telegraph"/> seconds. Null if every limb is busy.</summary>
	public Tentacle Slam(Vector3 world, float telegraph)
	{
		Vector3 target = ToLocal(world);
		float want = Mathf.Atan2(target.Z, target.X);
		Tentacle best = null;
		float bestD = float.MaxValue;
		foreach (var t in _tentacles)
		{
			if (t.State != Tentacle.S.Idle) continue;
			// (the thick ones do the slamming; a skinny one only when nothing else is near)
			float d = Mathf.Abs(Mathf.AngleDifference(t.Angle, want)) + (t.Skinny ? 0.7f : 0f);
			if (d < bestD) { bestD = d; best = t; }
		}
		if (best == null || bestD - (best.Skinny ? 0.7f : 0f) > Mathf.Pi * 0.6f) return null;
		best.Target = target;
		best.State = Tentacle.S.Raise;
		best.T = 0f;
		best.RaiseTime = telegraph * 0.6f;
		best.HoldTime = telegraph * 0.4f;
		best.FromTip = best.Tip;
		best.FromCtrl = best.Ctrl;
		best.Hit = false;
		best.Second = false;
		best.How = PickStyle(best);
		best.Side = _styleRng.Randf() < 0.5f ? -1f : 1f;
		return best;
	}

	/// <summary>Distance from a world point to the lower, catwalk-height stretch of a slamming limb.</summary>
	public float LimbDistance(Tentacle t, Vector3 world)
	{
		Vector3 p = ToLocal(world);
		float best = float.MaxValue;
		for (int i = Segments * 2 / 3; i <= Segments; i++)
		{
			Vector3 c = Curve(t, i / (float)Segments);
			best = Mathf.Min(best, c.DistanceTo(p));
		}
		return best;
	}

	/// <summary>A jolt of pain (a valve turned): every limb thrashes for a moment.</summary>
	public void Convulse(float seconds) => _convulse = Mathf.Max(_convulse, seconds);

	// ------------------------------------------------------------------ posing

	private Vector3 IdleTip(Tentacle t)
	{
		float rage = Rage, tt = _time * 0.35f * rage * (t.Skinny ? 1.5f : 1f) + t.Phase;
		float r = (11.5f + 2.2f * Mathf.Sin(tt * 0.7f)) * t.Reach;
		float a = t.Angle + 0.25f * t.Whip * Mathf.Sin(tt * 0.5f);
		float y = Mathf.Max(Level, 0f) + 7f * Mathf.Lerp(1f, t.Reach, 0.6f) + 4f * Mathf.Sin(tt);
		return new Vector3(Mathf.Cos(a) * r, Mathf.Min(y, 30f), Mathf.Sin(a) * r);
	}

	private Vector3 IdleCtrl(Tentacle t, Vector3 tip) => (t.RootLocal).Lerp(tip, 0.55f) + Vector3.Up * 9f + new Vector3(Mathf.Cos(t.Angle), 0, Mathf.Sin(t.Angle)) * -2f;

	/// <summary>The limb's centre line at f (0 root .. 1 tip): a quadratic curve root -> ctrl -> tip, with
	/// a writhe along it.</summary>
	private Vector3 Curve(Tentacle t, float f)
	{
		Vector3 a = t.RootLocal, b = t.Ctrl, c = t.Tip;
		Vector3 p = (1 - f) * (1 - f) * a + 2 * (1 - f) * f * b + f * f * c;
		float amp = (0.5f + 0.9f * (Rage - 1f) + (_convulse > 0 ? 1.6f : 0f)) * f * (1 - f) * 4f * t.Whip;
		if (t.State is Tentacle.S.Strike or Tentacle.S.Down) amp *= 0.3f;
		if (t.State == Tentacle.S.Limp) amp *= 0.1f;
		float w = _time * (1.1f * Rage) * (t.Skinny ? 1.6f : 1f) + t.Phase - f * (t.Skinny ? 8f : 5f);
		Vector3 side = new(-Mathf.Sin(t.Angle), 0, Mathf.Cos(t.Angle));
		return p + side * Mathf.Sin(w) * amp + Vector3.Up * Mathf.Cos(w * 0.8f) * amp * 0.4f;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_time += dt;
		_convulse = Mathf.Max(0f, _convulse - dt);
		_cam ??= GetViewport().GetCamera3D();
		// (out of sight across the world, nothing to move: its tentacles were 2 ms of every frame wherever the player was)
		if (_cam != null && _cam.GlobalPosition.DistanceSquaredTo(GlobalPosition) > 300f * 300f) return;
		foreach (var t in _tentacles) Step(t, dt);
		foreach (var t in _tentacles) Pose(t);
		LookAtCamera();
	}

	private void Step(Tentacle t, float dt)
	{
		t.T += dt;
		Vector3 radial = new Vector3(t.Target.X, 0, t.Target.Z).Normalized();   // out from its body to the spot
		Vector3 across = new Vector3(-radial.Z, 0, radial.X) * t.Side;          // along the catwalk, its chosen side
		// where it rears to before it comes down, by its style
		Vector3 above, aboveCtrl;
		switch (t.How)
		{
			case Style.Sweep:
				above = t.Target + across * 8f + Vector3.Up * 2.6f - radial * 1.5f;
				aboveCtrl = t.RootLocal.Lerp(above, 0.5f) + Vector3.Up * 7f;
				break;
			case Style.Corkscrew:
				above = t.Target + Vector3.Up * 13f - radial * 1.5f;
				aboveCtrl = t.RootLocal.Lerp(above, 0.5f) + Vector3.Up * 16f;
				break;
			case Style.Double:
				above = t.Target + Vector3.Up * 6.5f - radial * 2f;
				aboveCtrl = t.RootLocal.Lerp(above, 0.5f) + Vector3.Up * 10f;
				break;
			case Style.Whip:
				above = t.Target + Vector3.Up * 12f - radial * 7f;
				aboveCtrl = t.RootLocal.Lerp(above, 0.4f) + Vector3.Up * 18f;
				break;
			default:
				above = t.Target + Vector3.Up * 10f - radial * 2.5f;
				aboveCtrl = t.RootLocal.Lerp(above, 0.5f) + Vector3.Up * 14f;
				break;
		}
		switch (t.State)
		{
			case Tentacle.S.Idle:
			{
				Vector3 tip = IdleTip(t);
				t.Tip = t.Tip.Lerp(tip, Mathf.Min(1f, dt * 1.5f));
				t.Ctrl = t.Ctrl.Lerp(IdleCtrl(t, t.Tip), Mathf.Min(1f, dt * 1.5f));
				break;
			}
			case Tentacle.S.Raise:
			{
				float u = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t.T / Mathf.Max(0.05f, t.RaiseTime)));
				// (an arc up to it, not a straight line: the tip swings round over its own body's side)
				var mid = t.FromTip.Lerp(above, 0.5f) + Vector3.Up * 2.5f + across * (t.How == Style.Sweep ? 2f : 0.6f);
				var a1 = t.FromTip.Lerp(mid, u);
				t.Tip = a1.Lerp(mid.Lerp(above, u), u);
				t.Ctrl = t.FromCtrl.Lerp(aboveCtrl, u);
				if (t.T >= t.RaiseTime) { t.State = Tentacle.S.Hold; t.T = 0f; }
				break;
			}
			case Tentacle.S.Hold:
			{
				float h = Mathf.SmoothStep(0f, 1f, t.T / Mathf.Max(0.05f, t.HoldTime));
				switch (t.How)
				{
					case Style.Corkscrew:
						// the tip coiling round faster and tighter as it winds up
						float ang = t.T * (6f + 10f * h);
						t.Tip = above + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * (1.6f - 0.7f * h) + Vector3.Up * 0.8f * h;
						break;
					case Style.Sweep:
						// drawn back further along the catwalk, low, the tip curling
						t.Tip = above + across * 1.6f * h + Vector3.Up * 0.5f * Mathf.Sin(t.T * 5f);
						break;
					case Style.Whip:
						// cocked back, quivering
						t.Tip = above - radial * 1.5f * h + Vector3.Up * 1.2f * h + new Vector3(Mathf.Sin(_time * 22f), 0, Mathf.Cos(_time * 19f)) * 0.08f;
						break;
					default:
						t.Tip = above + Vector3.Up * (0.6f * h) + new Vector3(Mathf.Sin(_time * 30f), 0, Mathf.Cos(_time * 27f)) * 0.05f;
						break;
				}
				t.Ctrl = aboveCtrl;
				if (t.T >= t.HoldTime) { t.State = Tentacle.S.Strike; t.T = 0f; t.FromTip = t.Tip; t.FromCtrl = t.Ctrl; }
				break;
			}
			case Tentacle.S.Strike:
			{
				float strike = t.How == Style.Double && t.Second ? StrikeSeconds * 0.8f : StrikeSeconds;
				float u = Mathf.Min(1f, t.T / strike);
				Vector3 land = t.Target + Vector3.Up * 0.4f;
				Vector3 downCtrl = t.RootLocal.Lerp(t.Target, 0.55f) + Vector3.Up * 16f;
				switch (t.How)
				{
					case Style.Sweep:
					{
						// lashed across in an arc round its root, low, onto the spot
						float e = u * u * (3f - 2f * u);
						Vector3 from = t.FromTip - t.RootLocal, to = land - t.RootLocal;
						float a0 = Mathf.Atan2(from.Z, from.X), a1 = Mathf.Atan2(to.Z, to.X);
						float ang = Mathf.LerpAngle(a0, a1, e);
						float rad = Mathf.Lerp(new Vector2(from.X, from.Z).Length(), new Vector2(to.X, to.Z).Length(), e);
						t.Tip = t.RootLocal + new Vector3(Mathf.Cos(ang) * rad, Mathf.Lerp(from.Y, to.Y, e), Mathf.Sin(ang) * rad);
						t.Ctrl = t.RootLocal.Lerp(t.Tip, 0.5f) + Vector3.Up * Mathf.Lerp(7f, 5f, e);
						break;
					}
					case Style.Corkscrew:
					{
						float e = u * u;
						float ang = e * 14f;
						t.Tip = t.FromTip.Lerp(land, e) + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * 1.4f * (1f - e);
						t.Ctrl = t.FromCtrl.Lerp(downCtrl, e);
						break;
					}
					case Style.Whip:
					{
						// the body comes first and the tip trails it, then snaps down past it
						float body = Mathf.Sqrt(u), tip = u * u * u;
						t.Ctrl = t.FromCtrl.Lerp(downCtrl + radial * 3f, body);
						t.Tip = t.FromTip.Lerp(land, tip) + Vector3.Up * 2f * Mathf.Sin(u * Mathf.Pi) * (1f - u);
						break;
					}
					default:
					{
						float e = u * u;
						t.Tip = t.FromTip.Lerp(land, e);
						t.Ctrl = t.FromCtrl.Lerp(downCtrl, e);
						break;
					}
				}
				if (t.T >= strike && !t.Hit)
				{
					t.Hit = true;
					t.State = Tentacle.S.Down;
					t.T = 0f;
					Impact?.Invoke(ToGlobal(t.Target), t);
				}
				break;
			}
			case Tentacle.S.Down:
				// a double: up off the boards and down again at once, onto the same spot
				if (t.How == Style.Double && !t.Second && t.T >= 0.18f)
				{
					t.Second = true;
					t.Hit = false;
					t.State = Tentacle.S.Hold;
					t.HoldTime = 0.32f;
					t.T = 0f;
					break;
				}
				if (t.T >= 1.0f / Mathf.Sqrt(Rage)) { t.State = Tentacle.S.Recover; t.T = 0f; t.FromTip = t.Tip; t.FromCtrl = t.Ctrl; }
				break;
			case Tentacle.S.Recover:
			{
				// dragged back off the catwalk, slow, then up into its writhing again
				float u = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t.T / 1.2f));
				Vector3 tip = IdleTip(t);
				var drag = t.FromTip - radial * 3f * Mathf.Sin(Mathf.Min(u * 2f, 1f) * Mathf.Pi * 0.5f);
				t.Tip = drag.Lerp(tip, u);
				t.Ctrl = t.FromCtrl.Lerp(IdleCtrl(t, tip), u);
				if (t.T >= 1.2f) t.State = Tentacle.S.Idle;
				break;
			}
			case Tentacle.S.Limp:
			{
				float u = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t.T / 3f));
				Vector3 floorTip = new(Mathf.Cos(t.Angle) * 16f, 0.8f, Mathf.Sin(t.Angle) * 16f);
				t.Tip = t.FromTip.Lerp(floorTip, u);
				t.Ctrl = t.FromCtrl.Lerp((t.RootLocal).Lerp(floorTip, 0.5f) + Vector3.Up * 3f, u);
				break;
			}
		}
	}

	private void Pose(Tentacle t)
	{
		// the curve's points, and a frame at each carried along it (parallel transport: no sudden twist anywhere),
		// starting with the suckers' side facing in toward the body, so they're underneath as the limb arches out
		for (int r = 0; r < Rings; r++) t.Pts[r] = Curve(t, r / (float)(Rings - 1));
		Vector3 zPrev = new(-Mathf.Cos(t.Angle), 0, -Mathf.Sin(t.Angle));
		for (int r = 0; r < Rings; r++)
		{
			Vector3 tan = r == 0 ? t.Pts[1] - t.Pts[0] : r == Rings - 1 ? t.Pts[r] - t.Pts[r - 1] : t.Pts[r + 1] - t.Pts[r - 1];
			tan = tan.LengthSquared() > 1e-6f ? tan.Normalized() : Vector3.Up;
			Vector3 z = zPrev - tan * zPrev.Dot(tan);
			if (z.LengthSquared() < 1e-4f) z = tan.Cross(Vector3.Right);
			z = z.Normalized();
			Vector3 x = tan.Cross(z).Normalized();
			t.Frames[r] = new Basis(x, tan, z);
			zPrev = z;
			t.Skel.SetBonePosePosition(r, t.Pts[r]);
			t.Skel.SetBonePoseRotation(r, t.Frames[r].GetRotationQuaternion());
		}
		// the segments (the suckers, the eyes, the mouth) ride along it
		float nominal = Length / Segments;
		for (int i = 0; i < Segments; i++)
		{
			int r = i * RingsPer;
			Vector3 a = t.Pts[r], b = t.Pts[r + RingsPer];
			float len = (b - a).Length();
			var seg = t.Segs[i];
			seg.Position = a;
			Basis f = t.Frames[r];
			Vector3 y = len > 0.001f ? (b - a) / len : f.Y;
			Vector3 z = f.Z - y * f.Z.Dot(y);
			z = z.LengthSquared() > 1e-4f ? z.Normalized() : f.Z;
			Vector3 x = y.Cross(z).Normalized();
			seg.Basis = new Basis(x, y * (len / nominal), z);
		}
	}

	private void LookAtCamera()
	{
		if (_cam == null || !IsInstanceValid(_cam)) return;
		Vector3 target = _cam.GlobalPosition;
		for (int e = 0; e < _bodyEyes.Count; e++)
		{
			var eye = _bodyEyes[e];
			if (!eye.Visible) continue;
			float s = _eyeSize[e] * eye.GetMeta("swell", 1f).AsSingle();
			Vector3 at = eye.GlobalPosition;
			if (at.DistanceSquaredTo(target) > 0.01f) eye.GlobalBasis = Basis.LookingAt(target - at, Vector3.Up) * Basis.FromScale(Vector3.One * s);
		}
		foreach (var t in _tentacles)
			foreach (var eye in t.Eyes)
			{
				if (!eye.Visible) continue;
				Vector3 at = eye.GlobalPosition;
				float s = eye.GetMeta("size", 0.55f).AsSingle() * eye.GetMeta("swell", 1f).AsSingle();
				if (at.DistanceSquaredTo(target) > 0.01f) eye.GlobalBasis = Basis.LookingAt(target - at, Vector3.Up) * Basis.FromScale(Vector3.One * s);
			}
	}

	private void ApplyRot()
	{
		_skin?.SetShaderParameter("rot", _rot);
		_tubeSkin?.SetShaderParameter("rot", _rot);
		_bodySkin?.SetShaderParameter("rot", _rot);
		if (_sclera != null)
		{
			// bloodshot white -> milky, clouded, yellow-grey
			_sclera.AlbedoColor = new Color(0.9f, 0.84f, 0.72f).Lerp(new Color(0.62f, 0.64f, 0.5f), _rot);
			_sclera.EmissionEnergyMultiplier = Mathf.Lerp(1f, 0.3f, _rot);
			_sclera.Roughness = Mathf.Lerp(0.12f, 0.5f, _rot);
		}
		if (_iris != null)
		{
			// the red glow burns hotter as it goes rabid, then clouds white over it
			_iris.EmissionEnergyMultiplier = Mathf.Lerp(1.1f, 2.4f, Mathf.Min(1f, _rot * 1.6f)) * Mathf.Lerp(1f, 0.5f, Mathf.SmoothStep(0.7f, 1f, _rot));
			_iris.AlbedoColor = new Color(0.45f, 0.03f, 0.02f).Lerp(new Color(0.55f, 0.5f, 0.45f), Mathf.SmoothStep(0.6f, 1f, _rot) * 0.6f);
		}
	}

	// ------------------------------------------------------------------ the end

	/// <summary>Every eye, in the order they go: from the crown of the body outward and down, then the limbs.</summary>
	public List<Node3D> EyesInBurstOrder()
	{
		var list = new List<Node3D>(_bodyEyes);
		list.Sort((a, b) => b.Position.Y.CompareTo(a.Position.Y));
		foreach (var t in _tentacles) list.AddRange(t.Eyes);
		return list;
	}

	/// <summary>One eye bursts: it swells, and goes, in a spray of blood.</summary>
	public void Burst(Node3D eye)
	{
		if (!eye.Visible) return;
		var tw = eye.CreateTween();
		tw.TweenMethod(Callable.From<float>(s => eye.SetMeta("swell", s)), 1f, 1.5f, 0.12f);
		tw.TweenCallback(Callable.From(() =>
		{
			eye.Visible = false;
			EyesBurst++;
			Spray(eye.GlobalPosition, (eye.GlobalPosition - GlobalPosition - Vector3.Up * 5f).Normalized());
		}));
	}

	private void Spray(Vector3 at, Vector3 dir)
	{
		var pm = new ParticleProcessMaterial
		{
			Direction = dir, Spread = 35f, InitialVelocityMin = 3f, InitialVelocityMax = 8f, Gravity = new Vector3(0, -9.8f, 0),
			ScaleMin = 0.8f, ScaleMax = 2f,
		};
		var p = new GpuParticles3D
		{
			Amount = 36, Lifetime = 1.6, OneShot = true, Explosiveness = 0.9f, ProcessMaterial = pm,
			DrawPass1 = new QuadMesh
			{
				Size = Vector2.One * 0.35f,
				Material = new StandardMaterial3D
				{
					AlbedoTexture = LakeFx.SoftDot(), AlbedoColor = new Color(0.45f, 0.02f, 0.02f, 0.95f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				},
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityAabb = new Aabb(new Vector3(-10, -15, -10), new Vector3(20, 25, 20)),
		};
		GetParent().AddChild(p);
		p.GlobalPosition = at;
		p.Emitting = true;
		GetTree().CreateTimer(2.5).Timeout += p.QueueFree;
	}

	/// <summary>Dead: every limb thrashes once more and then drops, and the body sags.</summary>
	public void Die()
	{
		Dead = true;
		_convulse = 1.6f;
		GetTree().CreateTimer(1.6).Timeout += () =>
		{
			foreach (var t in _tentacles) { t.State = Tentacle.S.Limp; t.T = 0f; t.FromTip = t.Tip; t.FromCtrl = t.Ctrl; }
			var tw = CreateTween();
			tw.TweenProperty(_body, "scale", new Vector3(1.05f, 0.78f, 1.05f), 3.5f).SetTrans(Tween.TransitionType.Sine);
		};
	}
}
