using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.SnowMaze;

/// <summary>
/// Act 25 in the trailhead level (the owner: "there is a gradiant right in front of them where the snow turns back into
/// the forest from the first acts of the game. The player will see a trail and follow it back past their cabin and to
/// their vehicle"). Out of the hole in the snow at the trail's far end: the snow thinning away into the autumn woods; the
/// trail back, an old cabin beside it (theirs, if they'd stayed); the lot, and the car with its trunk open. Then the
/// ending they earned (<see cref="Act25Ending.Choose"/>):
/// <list type="number">
/// <item>The camera back in the trunk. Black. The credits.</item>
/// <item>The camera back in the trunk. Black, held five seconds; far off, it howls. The credits.</item>
/// <item>Nearly at the car, it comes down on it from the trees and crushes it flat; burning, it shambles at them; they
/// turn from it, and the stalker is there, lunging. Black.</item>
/// <item>The fog closes in as they near the car, and where it should be: the stairs from the first act. Blood runs down
/// them, step by step to the bottom; and they go up, slowly, then faster and faster, nearly at the top. Black.</item>
/// <item>The fog closes in; but the car really is there. The camera in the trunk; black, five seconds; then behind the
/// wheel, the engine catching, and away.</item>
/// </list>
/// Exists only in the trailhead level once the story's there (Act24Finished, or after the end).
/// </summary>
public partial class EndingDirector : Node3D
{
	public static EndingDirector Instance { get; private set; }
	public int Ending { get; private set; }
	public bool Reached { get; private set; }
	public bool Done { get; private set; }
	/// <summary>For tests: where the walk starts, and the car.</summary>
	public Vector3 Start { get; private set; }
	public Vector3 CarAt { get; private set; }
	public PickupInteractable TrunkUse { get; private set; }

	private Node3D _car, _opening, _stairs, _trunkCam;
	private ForestTerrain _terrain;
	private float _startS;
	private bool _running;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		_terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		_opening = GetTree().GetFirstNodeInGroup("opening") as Node3D;
		_car = _opening?.GetParent() as Node3D;
		_trunkCam = _opening?.GetNodeOrNull<Node3D>("TrunkCamera");
		_stairs = GetTree().CurrentScene?.FindChild("Stairs", true, false) as Node3D;
		Ending = Act25Ending.Chosen(StoryManager.Instance);
		CarAt = _car?.GlobalPosition ?? Vector3.Zero;
		// the walk starts a good way back up the trail (the trail's last third), out of a hole in a snowbank
		float len = _terrain?.TrailLength ?? 400f;
		_startS = len * 0.62f;
		if (_terrain != null)
		{
			var p = _terrain.TrailPoint(_startS, out var tan);
			Start = p with { Y = _terrain.HeightAt(p.X, p.Z) };
			BuildSnowEdge(Start, tan);
			Start = Start with { Y = Mathf.Max(Start.Y, Start.Y - BankSink + Apron(0f, 0f)) };   // (on the old snow's skin, if it's there)
		}
		if (_stairs != null) _stairs.Visible = Ending == 4 && false;   // (the stairs aren't there: only in the fourth, at the car)
		if (_trunkCam != null) _trunkCam.Visible = false;
		BuildCabin();
		BuildTrunkUse();
		GD.Print($"[story] Act 25: the trailhead - ending {Ending}, walking from {_startS:0} m up the trail");
		UI.ChapterCard.Show(this, 8, 3.0f);
	}

	/// <summary>Where the story puts the player here: at the snow's edge (Act24Finished), or by the car after the end.</summary>
	public (Vector3 pos, float yaw) SpawnFor(Checkpoint cp)
	{
		if (cp == Checkpoint.GameFinished && _car != null)
		{
			var stand = _opening?.GetNodeOrNull<Node3D>("Stand") ?? _car;
			var f = -stand.GlobalBasis.Z;
			return (stand.GlobalPosition + Vector3.Up * 0.1f, Mathf.Atan2(-f.X, -f.Z));
		}
		if (_terrain == null) return (Start + Vector3.Up * 0.2f, 0f);
		var ahead = _terrain.TrailPoint(_startS - 6f, out _);
		var d = ahead - Start;
		return (Start + Vector3.Up * 0.2f, Mathf.Atan2(-d.X, -d.Z));
	}

	// ------------------------------------------------------------------ the snow's edge, the cabin

	/// <summary>Behind them the snowbank and its hole, the cave's light in it; round them, snow thinning to nothing over
	/// twenty metres into the autumn leaves.</summary>
	/// <summary>(2026-10-13g, the texture-mood pass: the winter's soft snow, out here in the autumn woods' dusk, glared as
	/// clean white shapes under the lantern beside the dark, damp floor) Old snow, going: greyer, wetter, its crust in
	/// deeper relief. Its own copy, so the lodge's and the maze's snow are as they were.</summary>
	private static Material EdgeSnow
	{
		get
		{
			if (_edgeSnow != null) return _edgeSnow;
			_edgeSnow = (Material)WinterWoods.SoftSnow.Duplicate();
			_edgeSnow.ResourceName = "end_old_snow";
			if (_edgeSnow is ShaderMaterial sm)
			{
				sm.SetShaderParameter("top_color", new Color(0.53f, 0.54f, 0.56f));
				sm.SetShaderParameter("side_color", new Color(0.38f, 0.4f, 0.45f));
				sm.SetShaderParameter("glisten", 0.25f);
			}
			return _edgeSnow;
		}
	}
	private static Material _edgeSnow;

	private static Texture2D _patchAlb, _patchNor;
	/// <summary>(2026-10-14d) A patch of old snow, going: crusted, with ragged, broken edges and holes melted through it where
	/// the ground shows (alpha), drawn once in code.</summary>
	private static Texture2D PatchAlbedo()
	{
		if (_patchAlb != null) return _patchAlb;
		const int S = 256;
		var n = new FastNoiseLite { Frequency = 0.018f, FractalOctaves = 5, Seed = 2531 };
		var g = new FastNoiseLite { Frequency = 0.12f, FractalOctaves = 2, Seed = 2532 };
		var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
		var nor = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
		float H(int x, int y)
		{
			float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
			float r = Mathf.Sqrt(u * u + v * v);
			return Mathf.Clamp((1f - r * 1.15f) + (n.GetNoise2D(x, y)) * 0.75f, 0f, 1f);
		}
		for (int y = 0; y < S; y++)
			for (int x = 0; x < S; x++)
			{
				float h = H(x, y);
				float a = Mathf.SmoothStep(0.12f, 0.3f, h);
				float grain = 0.85f + 0.15f * g.GetNoise2D(x, y);
				// thinner toward its edge: the wet ground greying it from under
				float body = Mathf.Lerp(0.72f, 1f, Mathf.SmoothStep(0.15f, 0.6f, h)) * grain;
				img.SetPixel(x, y, new Color(body, body, body * 1.03f, a));
				float hx = H(Mathf.Min(x + 1, S - 1), y) - H(Mathf.Max(x - 1, 0), y), hy = H(x, Mathf.Min(y + 1, S - 1)) - H(x, Mathf.Max(y - 1, 0));
				var nn = new Vector3(-hx * 6f, -hy * 6f, 1f).Normalized();
				nor.SetPixel(x, y, new Color(nn.X * 0.5f + 0.5f, nn.Y * 0.5f + 0.5f, nn.Z * 0.5f + 0.5f, 1f));
			}
		img.GenerateMipmaps();
		nor.GenerateMipmaps();
		_patchAlb = ImageTexture.CreateFromImage(img);
		_patchNor = ImageTexture.CreateFromImage(nor);
		return _patchAlb;
	}
	private static Texture2D PatchNormal() { PatchAlbedo(); return _patchNor; }

	// (2026-10-14, the owner: "the whole snow part ... just floating there") The bank was three jittered spheres set at the
	// spawn's height whatever the slope did under them, the hole a dark lump stuck on its side; the cabin a ring of logs set
	// on the ground at its centre point, its roof two loose slabs over open gables. Now the bank is one surface laid over the
	// ground itself (every vertex the terrain's height plus the drift's), sunk a hand's depth at its edges so it can't stand
	// off the slope anywhere, with a real tunnel going into it; and the cabin is the cabin, grounding itself.

	/// <summary>Along the bank frame's u axis (up the trail, behind them): where the wall of snow stands.</summary>
	private const float BankFace = 1.7f;
	/// <summary>The tunnel's mouth: half its width, half its height, and its centre above the ground at the wall's foot.</summary>
	private const float MouthW = 0.8f, MouthH = 1.0f, MouthY = 0.9f;
	/// <summary>How far the bank's edges go down into the ground (so they never show a cut edge or a gap).</summary>
	private const float BankSink = 0.14f;
	private Vector3 _bankO, _bankU, _bankV;
	private float _mouthGround;
	private FastNoiseLite _bankNoise, _apronNoise;

	private float GroundAt(float u, float v)
	{
		var p = _bankO + _bankU * u + _bankV * v;
		return _terrain?.HeightAt(p.X, p.Z) ?? _bankO.Y;
	}

	private Vector3 At(float u, float v, float y) => (_bankO + _bankU * u + _bankV * v) with { Y = y };

	// The bank in three sheets that share their edges: its face (a grid over (v, height), so the hole is cut out of it
	// cleanly, the face as sheer as it likes), its top (a grid back from the face's top edge, falling away into the woods
	// behind) and, in front, an apron of old snow round its foot breaking up into patches. Every point is the terrain's
	// height plus the snow's, less BankSink: the edges all go down into the ground.

	private float Wv(float v) => _bankNoise.GetNoise2D(37f, v * 1.1f);   // (along the bank only: its line and its top wander)
	private static float AtMouth(float v) => 1f - Mathf.SmoothStep(1.5f, 3.6f, Mathf.Abs(v));

	/// <summary>Where the face's foot stands (u) along the bank.</summary>
	private float FaceU(float v) => BankFace + (0.15f + 0.45f * (1f - AtMouth(v))) * Wv(v) + 1.3f * Mathf.SmoothStep(2.5f, 7f, Mathf.Abs(v));
	/// <summary>How far back the face leans from its foot to its top: near sheer round the mouth, slumped toward its ends.</summary>
	private float FaceRun(float v) => Mathf.Lerp(0.55f, 2.6f + 0.8f * Wv(v + 11f), Mathf.SmoothStep(1.8f, 6f, Mathf.Abs(v)));
	/// <summary>How high the bank stands over the ground along it (to nothing past its ends).</summary>
	private float Crest(float v)
	{
		float av = Mathf.Abs(v), wv = Wv(v);
		float c = 2.55f + 0.55f * wv - 0.7f * Mathf.SmoothStep(1f, 7f, av);
		c = Mathf.Max(c, AtMouth(v) * (MouthY + MouthH + 0.6f));   // (always a good lip of snow over the hole)
		return c * (1f - Mathf.SmoothStep(4f + 0.7f * wv, 10f + 0.7f * wv, av));   // (its ends tapering away over metres, not cut off)
	}
	/// <summary>The face's lean: sheer low down, rounding over at the top.</summary>
	private static float Lean(float s, float v) => Mathf.Pow(s, Mathf.Lerp(2.4f, 0.85f, Mathf.SmoothStep(1.8f, 6f, Mathf.Abs(v))));
	/// <summary>Lumps and hollows over the snow (none round the mouth, so the tunnel meets the face exactly).</summary>
	private float Relief(float u, float v) => 0.08f * _apronNoise.GetNoise2D(u * 2.2f + 50f, v * 2.2f) * (1f - AtMouth(v));
	/// <summary>The face at (v, s): s = 0 its foot (in the ground), 1 its top edge.</summary>
	private Vector3 FacePoint(float v, float s)
	{
		float u = FaceU(v) + FaceRun(v) * Lean(s, v);
		float y = GroundAt(u, v) - BankSink + (Crest(v) + BankSink) * s;
		return At(u + Relief(u, v + y) * Mathf.SmoothStep(0f, 0.15f, s) * (1f - Mathf.SmoothStep(0.8f, 1f, s)), v, y);   // (none at the top edge, shared with the top)
	}
	/// <summary>The face's surface u at (v, world y) (for the tunnel and the lip to meet it).</summary>
	private float FaceSurfaceU(float v, float y)
	{
		float c = Crest(v) + BankSink, s = 0f;
		for (int i = 0; i < 4; i++)   // (the ground under it barely changes over the lean: settles at once)
		{
			float u = FaceU(v) + FaceRun(v) * Lean(s, v);
			s = Mathf.Clamp((y - (GroundAt(u, v) - BankSink)) / Mathf.Max(c, 0.01f), 0f, 1f);
		}
		return FaceU(v) + FaceRun(v) * Lean(s, v);
	}
	/// <summary>The top at (v, t): t = 0 the face's top edge, 1 the bank's back, gone into the ground.</summary>
	private Vector3 TopPoint(float v, float t)
	{
		float u0 = FaceU(v) + FaceRun(v);
		float u = Mathf.Lerp(u0, 14f, t);
		float w = _bankNoise.GetNoise2D(u, v);
		float prof = (1f + (0.2f * Mathf.Sin(u * 0.8f + v * 0.45f) + 0.15f) * Mathf.SmoothStep(0f, 0.3f, t))
			* (1f - Mathf.SmoothStep(0.35f, 1f, t + 0.08f * w)) * (1f - Mathf.SmoothStep(0.9f, 1f, t));   // (and always down into the ground at its back)
		return At(u, v, GroundAt(u, v) - BankSink + (Crest(v) + BankSink) * prof + Relief(u, v) * Mathf.SmoothStep(0f, 0.1f, t) * prof);
	}
	/// <summary>The apron's snow depth at (u, v): a skin round the bank's foot, breaking up over a few metres, with low
	/// drifts heaped at the bank's two ends; trodden thin in front of the hole.</summary>
	private float Apron(float u, float v)
	{
		float av = Mathf.Abs(v), fu = FaceU(v);
		float d = Mathf.Max(fu - u, av - 7f);
		float a = 0.34f * (1f - Mathf.SmoothStep(-0.5f, 4.2f, d)) + 0.17f * _apronNoise.GetNoise2D(u, v);
		a *= Mathf.Lerp(0.45f, 1f, Mathf.SmoothStep(0.7f, 1.6f, av));   // (the way out of the hole, trodden)
		float w = _bankNoise.GetNoise2D(u, v);
		foreach (float sv in new[] { -1f, 1f })
		{
			float dv = (v - sv * 4.6f) / 2.4f, du = (u - (FaceU(sv * 4.6f) - 0.3f)) / 2.3f;
			float r2 = dv * dv + du * du;
			if (r2 < 1f) a = Mathf.Max(a, 1.15f * (1f - r2) * (1f - r2) * (1f + 0.25f * w));
		}
		return a * (1f - Mathf.SmoothStep(fu + 0.2f, fu + 0.9f, u));   // (in under the face, gone)
	}

	/// <summary>Whether (v, height over the mouth's ground) is inside the tunnel's mouth.</summary>
	private static bool InMouth(float v, float h, float grow = 0f)
	{
		float ev = v / (MouthW + grow), ey = (h - MouthY) / (MouthH + grow);
		return ev * ev + ey * ey < 1f;
	}

	/// <summary>Behind them the snowbank and the tunnel they came out of; round them, old snow thinning to nothing over
	/// twenty metres into the autumn leaves.</summary>
	private void BuildSnowEdge(Vector3 at, Vector3 tan)
	{
		var back = new Vector3(tan.X, 0, tan.Z).Normalized();   // (behind them: up the trail, the way they came; the walk down goes the other way)
		var side = new Vector3(back.Z, 0, -back.X);
		_bankO = at; _bankU = back; _bankV = side;
		_bankNoise = new FastNoiseLite { Frequency = 0.16f, FractalOctaves = 2, Seed = 2501 };
		_apronNoise = new FastNoiseLite { Frequency = 0.55f, FractalOctaves = 3, Seed = 2502 };
		_mouthGround = GroundAt(FaceU(0f), 0f);
		BuildBank();
		BuildTunnel();
		BuildMouthRim();

		// the snow on the ground, thinning and breaking up into patches as it goes (2026-10-14d: laid on the ground as decals,
		// soft-edged and following every bump of it)
		var rng = new RandomNumberGenerator { Seed = 2504 };
		for (int i = 0; i < 46; i++)
		{
			float r = Mathf.Pow(rng.Randf(), 0.8f) * 18f;
			float a = rng.Randf() * Mathf.Tau;
			var p = at + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
			if (_terrain != null) p.Y = _terrain.HeightAt(p.X, p.Z);
			float s = Mathf.Lerp(2.2f, 0.5f, r / 18f) * rng.RandfRange(0.6f, 1.2f);
			float sz = s * rng.RandfRange(0.6f, 1f);
			var d = new Decal
			{
				Name = $"SnowPatch{i}", TextureAlbedo = PatchAlbedo(), TextureNormal = PatchNormal(), Size = new Vector3(s * 2.6f, 1.6f, sz * 2.6f),
				Modulate = new Color(0.66f, 0.68f, 0.72f, Mathf.Lerp(1f, 0.75f, r / 18f)), AlbedoMix = 1f, UpperFade = 0.3f, LowerFade = 0.3f, NormalFade = 0.1f,
				CullMask = 1, DistanceFadeEnabled = true, DistanceFadeBegin = 45f, DistanceFadeLength = 10f,
			};
			AddChild(d);
			d.GlobalPosition = p;
			d.GlobalBasis = new Basis(Vector3.Up, rng.Randf() * Mathf.Tau);
		}
	}

	/// <summary>One sheet of the bank: a grid of points (rows by columns) with smooth normals, those facing the wrong way
	/// turned out (<paramref name="outward"/>); a quad is left out where <paramref name="skip"/> says (the hole) or where it's
	/// all underground.</summary>
	private void Sheet(MeshKit k, Vector3[,] p, bool[,] under, System.Func<int, int, bool> skip, System.Func<Vector3, Vector3> outward)
	{
		int ni = p.GetLength(0), nj = p.GetLength(1);
		var n = new Vector3[ni, nj];
		for (int i = 0; i < ni; i++)
			for (int j = 0; j < nj; j++)
			{
				var a = p[Mathf.Min(i + 1, ni - 1), j] - p[Mathf.Max(i - 1, 0), j];
				var b = p[i, Mathf.Min(j + 1, nj - 1)] - p[i, Mathf.Max(j - 1, 0)];
				var c = a.Cross(b);
				if (c.LengthSquared() < 1e-12f) c = Vector3.Up;
				c = c.Normalized();
				n[i, j] = c.Dot(outward(p[i, j])) < 0 ? -c : c;
			}
		static Vector2 Uv(Vector3 q) => new(q.X * 0.5f, q.Z * 0.5f);
		for (int i = 0; i < ni - 1; i++)
			for (int j = 0; j < nj - 1; j++)
			{
				if (skip != null && skip(i, j)) continue;
				if (under[i, j] && under[i + 1, j] && under[i, j + 1] && under[i + 1, j + 1]) continue;
				Vector3 a = p[i, j], b = p[i + 1, j], c = p[i + 1, j + 1], d = p[i, j + 1];
				if ((c - a).LengthSquared() < 1e-8f) continue;
				k.Tri(a, b, c, n[i, j], n[i + 1, j], n[i + 1, j + 1], Uv(a), Uv(b), Uv(c));
				k.Tri(a, c, d, n[i, j], n[i + 1, j + 1], n[i, j + 1], Uv(a), Uv(c), Uv(d));
			}
	}

	/// <summary>The bank (face, top, apron), its mouth cut out of the face. Its own trimesh collider (the drifts' low feet
	/// walkable, the wall not), and a stop a step into the tunnel.</summary>
	private void BuildBank()
	{
		var k = new MeshKit();
		k.Mat(EdgeSnow);
		k.Color = Colors.White;
		const float vMin = -12f, vMax = 12f, dv = 0.09f;
		int nv = Mathf.CeilToInt((vMax - vMin) / dv) + 1;
		float V(int j) => vMin + j * dv;

		// the face: rows up it (fine enough that the hole's edge steps by a few cm)
		const int ns = 40;
		var face = new Vector3[ns + 1, nv];
		var faceUnder = new bool[ns + 1, nv];
		var hole = new bool[ns + 1, nv];
		for (int i = 0; i <= ns; i++)
			for (int j = 0; j < nv; j++)
			{
				float v = V(j), s = i / (float)ns;
				var q = FacePoint(v, s);
				face[i, j] = q;
				faceUnder[i, j] = q.Y < GroundAt(FaceU(v), v) - 0.02f || Crest(v) < 0.02f;
				hole[i, j] = InMouth(v, q.Y - _mouthGround);
			}
		Sheet(k, face, faceUnder, (i, j) => hole[i, j] || hole[i + 1, j] || hole[i, j + 1] || hole[i + 1, j + 1], q => -_bankU + Vector3.Up * 0.2f);

		// the top
		const int nt = 60;
		var top = new Vector3[nt + 1, nv];
		var topUnder = new bool[nt + 1, nv];
		for (int i = 0; i <= nt; i++)
			for (int j = 0; j < nv; j++)
			{
				var q = TopPoint(V(j), i / (float)nt);
				top[i, j] = q;
				topUnder[i, j] = Crest(V(j)) < 0.02f || i == nt;
			}
		Sheet(k, top, topUnder, null, q => Vector3.Up);

		// the apron, in front of the face and round its ends
		const float u0 = -6f, u1 = 9f, du = 0.14f;
		int nu = Mathf.CeilToInt((u1 - u0) / du) + 1;
		var apron = new Vector3[nu, nv];
		var apronUnder = new bool[nu, nv];
		for (int i = 0; i < nu; i++)
			for (int j = 0; j < nv; j++)
			{
				float u = u0 + i * du, v = V(j);
				float a = Apron(u, v), g = GroundAt(u, v);
				apron[i, j] = At(u, v, g - BankSink + a);
				apronUnder[i, j] = a < BankSink - 0.02f;
			}
		Sheet(k, apron, apronUnder, null, q => Vector3.Up);

		var mi = k.CommitTo(this, "SnowBank", true);
		var body = new StaticBody3D { Name = "BankBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "snow");
		body.AddChild(new CollisionShape3D { Name = "Bank", Shape = mi.Mesh.CreateTrimeshShape() });
		// a step into the tunnel and no further (it's the way back down)
		body.AddChild(new CollisionShape3D
		{
			Name = "TunnelStop", Shape = new BoxShape3D { Size = new Vector3(MouthW * 2.6f, MouthH * 2.6f, 0.4f) },
			Transform = new Transform3D(new Basis(_bankV, Vector3.Up, _bankU), At(FaceSurfaceU(0f, _mouthGround + MouthY) + 1.0f, 0f, _mouthGround + MouthY)),
		});
		AddChild(body);
	}

	/// <summary>The tunnel they came up: a bore into the bank from the hole in its face, its walls grey and glazed at the mouth
	/// (the burning thing came through here), going to black a metre or two in, running down a little.</summary>
	private void BuildTunnel()
	{
		var mat = new StandardMaterial3D
		{
			ResourceName = "end_tunnel", VertexColorUseAsAlbedo = true, Roughness = 1f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
			NormalEnabled = true, NormalTexture = GD.Load<Texture2D>("res://assets/textures/snow/snow_normal.png"), NormalScale = 0.6f,
			Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 0.5f,
		};
		var k = new MeshKit();
		k.Mat(mat);
		const int seg = 28, rings = 26;
		const float depth = 3.4f, grow = 0.07f;   // (a little wider than the hole: its stepped edge always shows the bore behind)
		var p = new Vector3[rings, seg + 1];
		var n = new Vector3[rings, seg + 1];
		var col = new Color[rings];
		var wobble = new FastNoiseLite { Frequency = 1.3f, Seed = 2510 };
		for (int r = 0; r < rings; r++)
		{
			float d = depth * r / (rings - 1f);
			float g = Mathf.Lerp(0.36f, 0f, Mathf.SmoothStep(0f, 1.6f, d));
			col[r] = new Color(g * 0.94f, g * 0.95f, g, 1f);   // (the mouth a little sooted, a little wet; then dark)
			for (int s = 0; s <= seg; s++)
			{
				float th = Mathf.Tau * s / seg;
				float wob = r == 0 ? 1f : 1f + 0.05f * wobble.GetNoise2D(d * 3f, s % seg) * Mathf.SmoothStep(0f, 0.5f, d);
				float v = Mathf.Cos(th) * (MouthW + grow) * wob;
				float y = _mouthGround + MouthY + Mathf.Sin(th) * (MouthH + grow) * wob - d * 0.12f;
				p[r, s] = At(FaceSurfaceU(v, y) + 0.015f + d, v, y);
				n[r, s] = -(_bankV * (Mathf.Cos(th) / MouthW) + Vector3.Up * (Mathf.Sin(th) / MouthH)).Normalized();
			}
		}
		for (int r = 0; r < rings - 1; r++)
			for (int s = 0; s < seg; s++)
			{
				k.TriC(p[r, s], p[r + 1, s], p[r + 1, s + 1], n[r, s], n[r + 1, s], n[r + 1, s + 1], Vector2.Zero, Vector2.Zero, Vector2.Zero, col[r], col[r + 1], col[r + 1]);
				k.TriC(p[r, s], p[r + 1, s + 1], p[r, s + 1], n[r, s], n[r + 1, s + 1], n[r, s + 1], Vector2.Zero, Vector2.Zero, Vector2.Zero, col[r], col[r + 1], col[r]);
			}
		// the dark at the end of it
		k.Color = Colors.Black;
		Vector3 c = Vector3.Zero;
		for (int s = 0; s < seg; s++) c += p[rings - 1, s];
		c /= seg;
		for (int s = 0; s < seg; s++)
			k.Tri(c, p[rings - 1, s], p[rings - 1, s + 1], -_bankU, Vector2.Zero, Vector2.Zero, Vector2.Zero);
		k.CommitTo(this, "Tunnel", false);
	}

	/// <summary>The mouth's lip: the crust broken over the top of the hole in lumps of all sizes, and a few lumps of it fallen
	/// at the foot.</summary>
	private void BuildMouthRim()
	{
		var k = new MeshKit();
		k.Mat(EdgeSnow);
		k.Color = Colors.White;
		var rng = new RandomNumberGenerator { Seed = 2511 };
		var frame = new Basis(_bankV, Vector3.Up, _bankU);
		const int count = 9;
		for (int i = 0; i < count; i++)
		{
			float th = Mathf.Lerp(0.35f, Mathf.Pi - 0.35f, (i + rng.RandfRange(-0.3f, 0.3f)) / (count - 1f));
			float v = Mathf.Cos(th) * (MouthW + 0.06f);
			float y = _mouthGround + MouthY + Mathf.Sin(th) * (MouthH + 0.04f);
			float top = Mathf.Sin(th), size = rng.RandfRange(0.5f, 1.2f);
			var r = new Vector3(0.18f + 0.12f * top, 0.09f + 0.05f * top, 0.2f) * size;
			var at = At(FaceSurfaceU(v, y) - 0.03f, v, y + r.Y * 0.4f);
			var keep = k.Xf;
			k.Xf = new Transform3D(frame.Rotated(_bankU, -th + Mathf.Pi * 0.5f), at);
			k.Blob(Vector3.Zero, r, 2520 + i, 0.14f, false, 1f, 0f, 2);
			k.Xf = keep;
		}
		for (int i = 0; i < 5; i++)
		{
			float v = rng.RandfRange(-1.4f, 1.4f) * (i % 2 == 0 ? 1f : 0.6f), u = FaceU(v) - rng.RandfRange(0.3f, 1.3f);
			var at = At(u, v, GroundAt(u, v) + 0.01f);
			k.Blob(at, new Vector3(rng.RandfRange(0.12f, 0.24f), rng.RandfRange(0.07f, 0.12f), rng.RandfRange(0.12f, 0.22f)), 2540 + i, 0.18f, false, 1f, 0f, 2);
		}
		k.CommitTo(this, "MouthRim", true);
	}

	/// <summary>Whether a mesh sits under another mesh (below <paramref name="root"/>): squashed already with its parent.</summary>
	private static bool UnderGeometry(Node n, Node root)
	{
		for (var p = n.GetParent(); p != null && p != root; p = p.GetParent())
			if (p is GeometryInstance3D) return true;
		return false;
	}

	/// <summary>An old cabin beside the trail on the way down, its door hanging open (theirs, if they'd stayed): the Hollow's
	/// cabin itself (<see cref="Cabin.Derelict"/>), so it grounds itself the same way, a fieldstone footing down to the lowest
	/// ground under it and as many porch steps as the slope wants, its porch to the trail. The woods' scatter is kept off it.</summary>
	private void BuildCabin()
	{
		if (_terrain == null) return;
		// where along the walk down: the flattest ground beside the trail (on the first spot's slope its porch stood a man's
		// height off the ground on eight steps), clear of the trail's other bends and the creek, near halfway
		Vector3 at = Vector3.Zero, side = Vector3.Zero;
		float best = float.MaxValue;
		Basis facing = Basis.Identity;
		for (float f = 0.2f; f <= 0.75f; f += 0.01f)
			foreach (float sd in new[] { 1f, -1f })
				foreach (float off in new[] { 10.5f, 12.5f, 15f })
					foreach (float turn in new[] { -0.4f, 0f, 0.4f })
					{
						var p = _terrain.TrailPoint(_startS * f, out var tan);
						var sv = new Vector3(tan.Z, 0, -tan.X).Normalized() * sd;
						var c = p + sv * off;
						var b = Basis.LookingAt(sv, Vector3.Up).Rotated(Vector3.Up, turn);   // (its porch to the trail, near enough)
						var xf = new Transform3D(b, c);
						_terrain.SampleFields(c.X, c.Z, out float dT, out _, out float dS, out _);
						var porch = xf * new Vector3(0, 0, 5.5f);
						_terrain.SampleFields(porch.X, porch.Z, out float dTp, out _, out float dSp, out _);
						if (dT < 9.5f || dTp < 4f || dS < 9f || dSp < 6f) continue;
						if (!GroundSnap.SampleRect(_terrain, xf, new Vector2(-2.8f, -3.2f), new Vector2(2.8f, 6f), 7, out var g)) continue;
						float score = (g.Max - g.Min) + 0.4f * Mathf.Abs(f - 0.45f) + 0.05f * (off - 10.5f);
						if (score < best) { best = score; at = c; side = sv; facing = b; }
					}
		if (best == float.MaxValue) return;
		at.Y = _terrain.HeightAt(at.X, at.Z);
		GD.Print($"[story] Act 25: the old cabin at {at} (ground score {best:0.00})");
		AddChild(new ClearZone { Name = "EndingCabinClear", Radius = 6.2f, Position = at });
		AddChild(new ClearZone { Name = "EndingCabinFrontClear", Radius = 3.2f, Position = at + facing.Z * 5.5f });
		// its front (+Z: the door, the porch) to the trail
		AddChild(new Cabin { Name = "EndingCabin", Derelict = true, Seed = 11, Transform = new Transform3D(facing, at) });
	}

	private void BuildTrunkUse()
	{
		if (_opening == null) return;
		TrunkUse = new PickupInteractable
		{
			Name = "TrunkUse", PickRadius = 0.7f, MaxDistance = 2.8f, Position = new Vector3(0.1f, 0.65f, -0.3f),   // (where the camera lay)
			PromptFor = _ => _running ? "" : "Put the camera back",
			CanUse = _ => !_running && Ending is 1 or 2 or 5 && Reached,
		};
		TrunkUse.Interacted += p => { if (!_running) _ = Cutscene.Run(this, ct => CameraInTrunk(p, ct), lockInput: true); };
		_opening.AddChild(TrunkUse);
	}

	// ------------------------------------------------------------------ the walk down, and the endings

	public override void _Process(double delta)
	{
		var player = StoryBeat.Player(this);
		if (player == null || _car == null || Done) return;
		float d = player.GlobalPosition.DistanceTo(CarAt);
		// the fog (the fourth and the fifth): closing in the nearer they come, until the car's lost in it
		if (Ending is 4 or 5 && StoryBeat.Atmosphere(this) is { } atmo)
			atmo.Act1FogOverride = Mathf.Clamp(1f - (d - 12f) / 80f, 0.4f, 1f);
		if (!Reached && d < 30f)
		{
			Reached = true;
			GD.Print($"[story] Act 25: the lot in sight - ending {Ending}");
			if (Ending == 3 && !_running) _ = Cutscene.Run(this, ct => Crushed(player, ct), lockInput: true);
			if (Ending == 4 && !_running) _ = Cutscene.Run(this, ct => Stairs(player, ct), lockInput: true);
		}
	}

	private async Task Credits(CancellationToken ct, bool fade = true)
	{
		Done = true;
		var player = StoryBeat.Player(this);
		await GameEnding.Play(this, player, ct);
	}

	/// <summary>The first, second and fifth: the camera lifted back into the trunk; black (and the rest).</summary>
	private async Task CameraInTrunk(PlayerController player, CancellationToken ct)
	{
		_running = true;
		GD.Print("[story] Act 25: the camera back in the trunk");
		await StoryBeat.PanTowards(this, player, _opening.GlobalPosition + Vector3.Down * 0.3f, 0.8f, ct, pitch: true);
		player.Inventory?.TakeAwayCamera();
		if (_trunkCam != null)
		{
			_trunkCam.Visible = true;
			var home = _trunkCam.Position;
			var cam = player.CameraRig.Camera;
			_trunkCam.GlobalPosition = cam.GlobalPosition + -cam.GlobalBasis.Z * 0.4f + Vector3.Down * 0.2f;
			var tw = CreateTween();
			tw.TweenProperty(_trunkCam, "position", home, 1.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			await Cutscene.Tween(this, tw, ct);
		}
		AudioDirector.OneShot(this, "wood_place", 3, _opening.GlobalPosition, -4f, "Events", 3f, 0.05f);
		await Cutscene.Wait(this, 1.0, ct);
		var fader = StoryBeat.Fader(this);
		if (fader != null) await fader.Fade(1f, 2.5f, ct);
		if (Ending == 2)
		{
			await Cutscene.Wait(this, 5.0, ct);
			AudioDirector.OneShot(this, "wendigo_howl_far", 3, null, -6f, "Unnatural");
			await Cutscene.Wait(this, 3.5, ct);
		}
		else if (Ending == 5)
		{
			await Cutscene.Wait(this, 5.0, ct);
			// behind the wheel: the view from the driver's seat, the trail's trees through the windscreen, the engine
			var seat = _car.GlobalPosition + _car.GlobalBasis * new Vector3(-0.38f, 1.15f, 0.25f);
			player.GlobalPosition = seat + Vector3.Down * 1.0f;
			// (2026-10-15, the car's rebuild: the eye at the seat's head height, inside the cabin behind the wheel and the
			// dash; standing eye height put it 0.6 m over the roof)
			player.CameraRig.EyeHeight = 1.0f;
			var fwd = -_car.GlobalBasis.Z;
			player.CameraRig.SnapBehind(Mathf.Atan2(-fwd.X, -fwd.Z));
			player.CameraRig.SetPitch(-0.05f);
			if (StoryBeat.Atmosphere(this) is { } a2) a2.Act1FogOverride = 0.3f;
			if (fader != null) await fader.Fade(0f, 2.0f, ct);
			AudioDirector.OneShot(this, "car_start", 1, null, -2f, "Events");
			GD.Print("[story] Act 25: behind the wheel - the engine catches, and away");
			await Cutscene.Wait(this, 4.0, ct);
			if (fader != null) await fader.Fade(1f, 2.5f, ct);
		}
		await Credits(ct);
	}

	/// <summary>The third: it comes down on the car and crushes it; burning, it shambles at them; they turn, and the
	/// stalker is there.</summary>
	private async Task Crushed(PlayerController player, CancellationToken ct)
	{
		_running = true;
		GD.Print("[story] Act 25: it comes down on the car");
		var w = new Wendigo { Name = "EndingWendigo" };
		AddChild(w);
		var above = CarAt + Vector3.Up * 14f + (player.GlobalPosition - CarAt).Normalized() * -6f;
		w.StandAt(above, player.GlobalPosition);
		await StoryBeat.PanTowards(this, player, CarAt + Vector3.Up * 1.2f, 0.6f, ct, pitch: true);
		bool down = false;
		w.Leap(CarAt + Vector3.Up * 0.9f, 0.7f, () => down = true, stay: true, arc: 0.5f, ballistic: true, landClip: "land", landSeconds: 0.9f);
		for (int i = 0; i < 40 && !down; i++) await Cutscene.Wait(this, 0.05, ct);
		AudioDirector.OneShot(this, "door_ram_burst", 1, CarAt, 7f, "Events", 12f, 0.02f);
		AudioDirector.OneShot(this, "window_shatter", 2, CarAt, 6f, "Events", 10f, 0.05f);
		// the car crushed flat under it
		// crushed: only its meshes squashed about the car's own origin, never the car node (its trunk's pick area, a physics
		// body, can't take a non-uniform scale: Jolt logged an error every time)
		var squash = new Transform3D(Basis.FromScale(new Vector3(1.05f, 0.55f, 1f)), Vector3.Zero);
		var carXf = _car.GlobalTransform;
		foreach (var g in _car.FindChildren("*", "", true, false))
			if (g is GeometryInstance3D gi && !(_opening?.IsAncestorOf(gi) ?? false) && !UnderGeometry(gi, _car))
				gi.GlobalTransform = carXf * (squash * (carXf.AffineInverse() * gi.GlobalTransform));
		var fire = new NapalmBurn { Name = "Burning", LightRange = 7f, LightEnergy = 2.2f };   // (2026-10-14d: the fire on its bones)
		w.AddChild(fire);
		await Cutscene.Wait(this, 1.2, ct);
		// down off the car and at them, slow, burning, dragging itself
		var to = player.GlobalPosition + (CarAt - player.GlobalPosition).Normalized() * 3.5f;
		// (its walk cycle at the pace it really covers the ground: a cycle is 2.55 m of its full stride)
		float pace = Mathf.Max(w.GlobalPosition.DistanceTo(to) / 3.6f, 0.3f);
		w.Play("walk", Mathf.Clamp(2.55f / pace, 0.9f, 3.5f), 0.3);
		var tw = CreateTween();
		tw.TweenProperty(w, "global_position", to with { Y = player.GlobalPosition.Y }, 3.6f);
		await StoryBeat.PanTowards(this, player, to + Vector3.Up * 2.5f, 2.5f, ct, pitch: true);
		// they turn away from it, and the stalker is right there
		var behind = player.GlobalPosition + (player.GlobalPosition - CarAt).Normalized() * 2.2f;
		var st = new StalkerBody { Name = "EndingStalker", Size = 1.1f };
		AddChild(st);
		st.GlobalPosition = behind with { Y = player.GlobalPosition.Y };
		st.LookAt(player.GlobalPosition with { Y = st.GlobalPosition.Y }, Vector3.Up);
		await StoryBeat.PanTowards(this, player, behind + Vector3.Up * 1.7f, 0.6f, ct, pitch: true);
		AudioDirector.OneShot(this, "stalker_seen_01", 1, behind, 6f, "Unnatural", 6f, 0.02f);
		var lunge = CreateTween();
		lunge.TweenProperty(st, "global_position", player.GlobalPosition + (behind - player.GlobalPosition).Normalized() * 0.5f, 0.22f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		await Cutscene.Tween(this, lunge, ct);
		StoryBeat.Fader(this)?.SetBlack(true);
		await Cutscene.Wait(this, 2.5, ct);
		await Credits(ct);
	}

	/// <summary>The fourth: where the car should be, the stairs. Blood down them, step by step; then up them, faster and
	/// faster, nearly to the top. Black.</summary>
	private async Task Stairs(PlayerController player, CancellationToken ct)
	{
		_running = true;
		GD.Print("[story] Act 25: the stairs, where the car should be");
		_car.Visible = false;
		if (_stairs == null) { await Credits(ct); return; }
		_stairs.Visible = true;
		_stairs.GlobalPosition = CarAt;
		var face = (player.GlobalPosition - CarAt) with { Y = 0 };
		_stairs.GlobalBasis = Basis.LookingAt(-face.Normalized(), Vector3.Up);
		await StoryBeat.PanTowards(this, player, CarAt + Vector3.Up * 2f, 1.5f, ct, pitch: true);
		await Cutscene.Wait(this, 1.5, ct);
		// the blood: down from the top step, a step at a time, to the bottom
		var blood = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.02f, 0.02f), Roughness = 0.1f, MetallicSpecular = 0.8f };
		for (int i = 11; i >= 0; i--)
		{
			var step = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.7f, 0.01f, 0.32f) }, MaterialOverride = blood, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			_stairs.AddChild(step);
			step.Position = new Vector3(0, 0.2f + i * 0.2f, -0.3f - i * 0.3f);
			AudioDirector.OneShot(this, "haunt_drip", 3, step.GlobalPosition, -10f, "Events", 2f, 0.1f);
			await Cutscene.Wait(this, 0.45, ct);
		}
		await Cutscene.Wait(this, 1.0, ct);
		// up them: slowly at first, faster and faster
		var top = _stairs.GlobalPosition + _stairs.GlobalBasis * new Vector3(0, 2.6f, -3.6f);
		var from = player.GlobalPosition;
		double t = 0, total = 6.0;
		while (t < total)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			float u = (float)(t / total);
			player.GlobalPosition = from.Lerp(top, u * u * u * 0.96f);
		}
		StoryBeat.Fader(this)?.SetBlack(true);
		await Cutscene.Wait(this, 2.0, ct);
		await Credits(ct);
	}
}
