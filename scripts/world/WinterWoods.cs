using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 22: the winter woods (the owner's "Real Act 22"). Out of the church's great door the player is back in a
/// forest like the whole first part of the game, but it is winter and softly snowing, and someone has plowed a
/// snaking road through the trees. The compass works again and points down it to the ski lodge, about 1.3 km
/// away. The further they go the colder it gets: the snowfall thins and stops, the ground glazes over, and the
/// trees are caked in ice and hung with icicles. All the way, something is keeping pace in the trees
/// (<see cref="Wendigo"/>, stalking from the director in WinterWoods.Stalking.cs). The lodge's front door is locked;
/// the back door is iced in (<see cref="SkiLodge"/>).
///
/// Built in the church's local space (a child of it, like <see cref="WinterGlade"/>, whose clearing it continues:
/// all the snow ground round the church is this one's). The ground is a coarse 4 m grid of chunks (each with its
/// own trimesh collision), and the road a finer ribbon laid along it (the grid ducks under the ribbon so the
/// road's ruts and windrows are exact). Styled PS2: small cold textures (Poly Haven's snow, tools/Textures/make_snow.py),
/// low-poly trees, and a dusk that never lets the snow glare.
/// </summary>
public partial class WinterWoods : Node3D
{
	public static WinterWoods Instance { get; private set; }
	public const float Cell = 4f, ChunkCells = 16;
	public SkiLodge Lodge { get; private set; }
	public Church Church { get; set; }
	public int Chunks { get; private set; }
	public int TreeCount { get; private set; }

	private ShaderMaterial _groundMat;
	private readonly RandomNumberGenerator _rng = new() { Seed = 2222 };

	/// <summary>Built (the woods, the road, the lodge). A new game, or a save before the drop into the pit (Act 17), leaves
	/// them unbuilt at load, to be built in the black of the drop (it had been the stairwell's fall; moved later, 2026-10-09,
	/// so a death or a Continue in Acts 14 to 17 doesn't build them either): they were eight of the 25 seconds the load from Act 1
	/// to the Hollow took (the owner, 2026-10-07: "the loading time between act 1 and 2 took a very long time").</summary>
	public bool Built { get; private set; }
	public static bool DeferAtLoad => Systems.StoryManager.Instance is { } st && st.Current < Systems.Checkpoint.Act17Finished
		&& System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-defer") < 0 && !NeverDefer;
	/// <summary>Everything built at load, always (the audits: they look at the whole level).</summary>
	public static bool NeverDefer;

	public override void _Ready()
	{
		Instance = this;
		if (DeferAtLoad)
		{
			SetProcess(false);
			// (a safety: reached some other way than the fall, it's built then)
			if (Systems.StoryManager.Instance is { } st) st.CheckpointReached += OnCheckpointBuild;
			GD.Print("[story] Act 22: the winter woods and the lodge wait to be built (in the drop into the pit)");
			return;
		}
		Build();
	}

	private void OnCheckpointBuild(Systems.Checkpoint cp) { if (IsInstanceValid(this) && IsInsideTree() && cp >= Systems.Checkpoint.Act17Finished && !Built) EnsureBuilt(); }

	/// <summary>Builds them now if they wait (in a blackout: it takes seconds), and brings the render budget and the shader
	/// warm-up up to date with them. Returns at once if they're built.</summary>
	public void EnsureBuilt()
	{
		if (Built) return;
		if (Systems.StoryManager.Instance is { } st) st.CheckpointReached -= OnCheckpointBuild;
		Build();
		// the render budget's ranges over the new rooms (it walked the level once, before they were there), and their
		// shaders built while it's still black
		var root = Systems.Cutscene.SceneRoot(this);
		if (root.GetNodeOrNull("RenderBudget") is { } old) { root.RemoveChild(old); old.QueueFree(); }
		root.AddChild(new Systems.RenderBudget { Name = "RenderBudget" });
		root.AddChild(new Systems.ShaderWarmup { Name = "ShaderWarmupWinter" });
	}

	/// <summary>The winter woods and the lodge (left unbuilt by the load from Act 1) built in a blackout the story already has (the drop into the pit, Act 17 to 18),
	/// their shaders warmed: seconds of it, so it's filled: a slow heart, loud in the ears, and a shuddering breath now and
	/// then, as if they lie out cold (2026-10-07). The heartbeat plays on through the build (the sound runs on its own).</summary>
	public static async System.Threading.Tasks.Task BuildInTheBlack(Node host, System.Threading.CancellationToken ct)
	{
		if (Instance is not { Built: false } woods) return;
		AudioStreamPlayer heart = null;
		if (ResourceLoader.Exists("res://assets/audio/ambient/heartbeat_loop.wav"))
		{
			var wav = (AudioStreamWav)GD.Load<AudioStreamWav>("res://assets/audio/ambient/heartbeat_loop.wav").Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			heart = new AudioStreamPlayer { Name = "OutColdHeart", Stream = wav, Bus = "Player", VolumeDb = -9f, PitchScale = 0.82f };
			host.AddChild(heart);
			heart.Play();
		}
		Audio.AudioDirector.OneShot(host, "breath_out", 3, null, -8f, "Player", 3f, 0.05f);
		await Systems.Cutscene.Wait(host, 0.6, ct);
		woods.EnsureBuilt();
		ulong t0 = Time.GetTicksMsec();
		while (!Systems.ShaderWarmup.Ready && Time.GetTicksMsec() - t0 < 8000) await Systems.Cutscene.Frame(host, ct);
		Audio.AudioDirector.OneShot(host, "breath_in", 5, null, -7f, "Player", 3f, 0.05f);
		await Systems.Cutscene.Wait(host, 1.2, ct);
		if (heart != null)
		{
			var tw = host.CreateTween();
			tw.TweenProperty(heart, "volume_db", -40f, 2.0f);
			tw.TweenCallback(Callable.From(heart.QueueFree));
		}
	}

	private void Build()
	{
		using var __timer = Systems.BuildTimer.Time("WinterWoods");
		Built = true;
		EnsurePath();
		Lodge = new SkiLodge { Name = "SkiLodge", Position = SkiLodge.OriginLocal };
		{ using var __t = Systems.BuildTimer.Time("WinterWoods.Lodge"); AddChild(Lodge); }
		_groundMat = GroundMat();
		{ using var __t = Systems.BuildTimer.Time("WinterWoods.BuildGround"); BuildGround(); }
		{ using var __t = Systems.BuildTimer.Time("WinterWoods.BuildRibbon"); BuildRibbon(); }
		{ using var __t = Systems.BuildTimer.Time("WinterWoods.BuildTrees"); BuildTrees(); }
		{ using var __t = Systems.BuildTimer.Time("WinterWoods.BuildProps"); BuildProps(); }
		{ using var __t = Systems.BuildTimer.Time("WinterWoods.BuildWeather"); BuildWeather(); }
		{ using var __t = Systems.BuildTimer.Time("WinterWoods.BuildStalker"); BuildStalker(); }
		{ using var __t = Systems.BuildTimer.Time("WinterWoods.BuildPrints"); BuildPrints(); }
		{ using var __t = Systems.BuildTimer.Time("WinterWoods.BuildWallWind"); BuildWallWind(); }
		SetProcess(true);
		GD.Print($"[story] Act 22: the winter woods - {Length:0} m of plowed road to the lodge, {Chunks} ground chunks, {TreeCount} trees");
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		if (Systems.StoryManager.Instance is { } sm) sm.CheckpointReached -= OnCheckpointBuild;   // (the story outlives the level)
		if (_silenced) ForestAmbienceManager.Instance?.ReleaseSilence(this);
	}

	// ------------------------------------------------------------------ materials

	public static ShaderMaterial GroundMat()
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/winter_ground.gdshader") };
		m.SetShaderParameter("tex_snow", GD.Load<Texture2D>("res://assets/textures/snow/snow_albedo.png"));
		m.SetShaderParameter("tex_plowed", GD.Load<Texture2D>("res://assets/textures/snow/plowed_albedo.png"));
		m.SetShaderParameter("tex_packed", GD.Load<Texture2D>("res://assets/textures/snow/packed_albedo.png"));
		m.SetShaderParameter("nor_snow", GD.Load<Texture2D>("res://assets/textures/snow/snow_normal.png"));
		m.SetShaderParameter("nor_plowed", GD.Load<Texture2D>("res://assets/textures/snow/plowed_normal.png"));
		m.SetShaderParameter("detail_tex", DetailKit.Albedo(DetailKit.Kind.Snow));
		return m;
	}

	/// <summary>Vertex colour for the ground shader at a church-local point: R the plowed road, G shade, A frozen.</summary>
	private static Color GroundColor(float x, float z)
	{
		float d = Nearest(x, z, out float s, out _);
		float ice = FrozenAt(s);
		if (d > RegionHalf * 0.5f) ice *= 1f - Mathf.SmoothStep(RegionHalf * 0.5f, RegionHalf, d) * 0.5f;
		return new Color(PlowedWeight(x, z), 0f, 0f, ice);
	}

	// ------------------------------------------------------------------ the ground

	/// <summary>Whether the ground grid covers a church-local point: the church's clearing, or near the road, or round the lodge.</summary>
	private static bool Covered(float x, float z)
	{
		if (new Vector2(x - WinterGlade.Centre.X, z - WinterGlade.Centre.Y).Length() < WinterGlade.Radius) return true;
		if (SkiLodge.NearLodge(x, z, 90f)) return true;
		return Nearest(x, z, out _, out _) < RegionHalf;
	}

	/// <summary>The grid's height: the ground, ducked under the road's ribbon (which draws the road itself).</summary>
	private static float GridHeight(float x, float z)
	{
		float d = Nearest(x, z, out float s, out _);
		// only alongside the road (the ribbon only covers that): past its ends, at the church's step and the
		// lodge's turning circle, the grid is the ground itself
		if (s <= 0.01f || s >= Length - 0.01f) return Height(x, z);
		// under the road and its walls, well down (its 4 m cells, spanning a wall's foot and its top, came up through
		// the road); further out, just under the ribbon's edge
		if (d < 10.8f) return RoadY(s) - 1.0f;
		float duck = 0.7f * (1f - Mathf.SmoothStep(BlendOut, BlendOut + 4f, d));
		return Height(x, z) - duck;
	}

	private void BuildGround()
	{
		// bounds of everything the grid might cover
		float minX = WinterGlade.Centre.X - WinterGlade.Radius, maxX = WinterGlade.Centre.X + WinterGlade.Radius;
		float minZ = WinterGlade.Centre.Y - WinterGlade.Radius, maxZ = WinterGlade.Centre.Y + WinterGlade.Radius;
		for (int i = 0; i < Points; i++)
		{
			var p = RoadPoint(i);
			minX = Mathf.Min(minX, p.X - RegionHalf); maxX = Mathf.Max(maxX, p.X + RegionHalf);
			minZ = Mathf.Min(minZ, p.Y - RegionHalf); maxZ = Mathf.Max(maxZ, p.Y + RegionHalf);
		}
		var lodge = SkiLodge.OriginLocal;
		minZ = Mathf.Min(minZ, lodge.Z - 100f);
		// aligned to the clearing's old 5 m grid? no: a fresh 4 m grid from here
		minX = Mathf.Floor(minX / Cell) * Cell; minZ = Mathf.Floor(minZ / Cell) * Cell;
		float chunk = Cell * ChunkCells;
		int nx = Mathf.CeilToInt((maxX - minX) / chunk), nz = Mathf.CeilToInt((maxZ - minZ) / chunk);
		// (the heights, colours and normals at every chunk's corners worked out on all the cores at once, each corner once:
		// they're pure arithmetic over the road, and were three of the eight seconds the winter took to build; the meshes
		// and their collision made after, here)
		EnsurePath();
		var data = new ChunkData[nx * nz];
		System.Threading.Tasks.Parallel.For(0, nx * nz, idx => data[idx] = Corners(minX + (idx / nz) * chunk, minZ + (idx % nz) * chunk));
		for (int idx = 0; idx < data.Length; idx++)
			if (data[idx] != null) BuildChunk(minX + (idx / nz) * chunk, minZ + (idx % nz) * chunk, data[idx]);
	}

	private sealed class ChunkData { public float[,] H; public Color[,] Col; public bool[,] Inside; public Vector3[,] N; }

	private static ChunkData Corners(float x0, float z0)
	{
		int n = (int)ChunkCells;
		var d = new ChunkData { H = new float[n + 1, n + 1], Col = new Color[n + 1, n + 1], Inside = new bool[n + 1, n + 1], N = new Vector3[n + 1, n + 1] };
		bool any = false;
		for (int i = 0; i <= n; i++)
			for (int j = 0; j <= n; j++)
			{
				float x = x0 + i * Cell, z = z0 + j * Cell;
				d.Inside[i, j] = Covered(x, z);
				if (!d.Inside[i, j]) continue;
				any = true;
				d.H[i, j] = GridHeight(x, z);
				d.Col[i, j] = GroundColor(x, z);
				d.N[i, j] = GridNormal(x, z);
			}
		return any ? d : null;
	}

	private void BuildChunk(float x0, float z0, ChunkData data)
	{
		int n = (int)ChunkCells;
		var h = data.H; var col = data.Col; var inside = data.Inside; var nrm = data.N;
		var k = new MeshKit();
		k.Mat(_groundMat);
		var faces = new List<Vector3>();
		for (int i = 0; i < n; i++)
			for (int j = 0; j < n; j++)
			{
				if (!inside[i, j] || !inside[i + 1, j] || !inside[i + 1, j + 1] || !inside[i, j + 1]) continue;
				float x = x0 + i * Cell, z = z0 + j * Cell;
				// under the church (its nave floor and its crypt) there is no snow
				if (WinterGlade.OutsideChurch(x, z) <= 0f && WinterGlade.OutsideChurch(x + Cell, z + Cell) <= 0f
					&& WinterGlade.OutsideChurch(x + Cell, z) <= 0f && WinterGlade.OutsideChurch(x, z + Cell) <= 0f) continue;
				Vector3 a = new(x, h[i, j], z), b = new(x + Cell, h[i + 1, j], z), c = new(x + Cell, h[i + 1, j + 1], z + Cell), d = new(x, h[i, j + 1], z + Cell);
				Vector3 na = nrm[i, j], nb = nrm[i + 1, j], nc = nrm[i + 1, j + 1], nd = nrm[i, j + 1];
				k.Color = col[i, j];
				// per-corner colours: two triangles, each corner its own
				TriC(k, a, b, c, na, nb, nc, col[i, j], col[i + 1, j], col[i + 1, j + 1]);
				TriC(k, a, c, d, na, nc, nd, col[i, j], col[i + 1, j + 1], col[i, j + 1]);
				faces.Add(a); faces.Add(b); faces.Add(c);
				faces.Add(a); faces.Add(c); faces.Add(d);
			}
		if (faces.Count == 0) return;
		var mi = k.CommitTo(this, $"Ground_{Chunks}", false);
		mi.VisibilityRangeEnd = 170f;   // (the fog is solid by 60 m: nothing further shows)
		mi.VisibilityRangeEndMargin = 10f;
		var body = new StaticBody3D { Name = $"GroundBody_{Chunks}", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "snow");
		var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
		shape.SetFaces(faces.ToArray());
		body.AddChild(new CollisionShape3D { Shape = shape });
		AddChild(body);
		Chunks++;
	}

	private static Vector3 GridNormal(float x, float z)
	{
		const float e = 1.5f;
		float hx = GridHeight(x + e, z) - GridHeight(x - e, z), hz = GridHeight(x, z + e) - GridHeight(x, z - e);
		return new Vector3(-hx, 2f * e, -hz).Normalized();
	}

	private static void TriC(MeshKit k, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Color ca, Color cb, Color cc)
		=> k.TriC(a, b, c, na, nb, nc, new Vector2(a.X, a.Z), new Vector2(b.X, b.Z), new Vector2(c.X, c.Z), ca, cb, cc);

	// ------------------------------------------------------------------ the road

	private static readonly float[] Across =
	{
		-RibbonHalf, -12.5f, -10.5f, -8.5f, -7.4f, -6.6f, -6.0f, -5.5f, -5.1f, -4.85f, -4.75f, -4.66f, -4.56f, -4.45f, -4.32f, -4.17f, -4.0f, -3.8f, -3.58f, -3.35f, -2.4f, -1.5f, -1.0f, -0.5f, 0f,
		0.5f, 1.0f, 1.5f, 2.4f, 3.35f, 3.58f, 3.8f, 4.0f, 4.17f, 4.32f, 4.45f, 4.56f, 4.66f, 4.75f, 4.85f, 5.1f, 5.5f, 6.0f, 6.6f, 7.4f, 8.5f, 10.5f, 12.5f, RibbonHalf,
	};

	/// <summary>The road itself: a ribbon along the centreline, every Step metres a cross-section out to the ribbon's
	/// edges (which dip under the grid), with its own collision. The plowed walls are part of it, roughed up (the owner:
	/// lumpy, not straight and smooth; and plowed a while ago): a slumped toe off the road, their faces softly in and
	/// out, their tops broken and lumpy, with snow clumps heaped along them.</summary>
	private void BuildRibbon()
	{
		int segs = 60;   // cross-sections per piece (90 m), so each piece is culled on its own
		int n = Across.Length;
		for (int start = 0; start < Points - 1; start += segs)
		{
			int end = Mathf.Min(start + segs, Points - 1);
			var k = new MeshKit();
			k.Mat(_groundMat);
			var faces = new List<Vector3>();
			// the rows (one either side beyond the piece, for the normals), then the normals from the ribbon itself (the
			// ground's finite differences had smeared the sheer faces into slopes, and knew nothing of their lumps)
			int r0 = Mathf.Max(start - 1, 0), r1 = Mathf.Min(end + 1, Points - 1);
			var rows = new Vector3[r1 - r0 + 1][];
			var cols = new Color[r1 - r0 + 1][];
			for (int i = r0; i <= r1; i++)
			{
				var p = RoadPoint(i);
				var t = RoadDir(i);
				var right = new Vector2(-t.Y, t.X);   // (x, z): the right-hand side walking along the road
				var row = new Vector3[n];
				var rc = new Color[n];
				for (int a = 0; a < n; a++)
				{
					var q = p + right * Across[a];
					float y = Height(q.X, q.Y);
					if (Mathf.Abs(Across[a]) >= RibbonHalf - 0.01f) y -= 0.8f;   // the edge tucks under the grid
					var v = new Vector3(q.X, y, q.Y);
					float ad = Mathf.Abs(Across[a]), sgn = Mathf.Sign(Across[a]);
					float roadY = RoadAt(i * Step, out _).Y;
					float rise = y - roadY;
					if (ad > WallFoot + 0.01f && ad < WallTop + 0.2f && rise > 0.35f)
					{
						// the face: softly in and out (weathered: the plow's sharp ledges long since slumped), not at the toe
						float bulge = 0.05f * Mathf.Sin(rise * 2.6f + 1.6f * Lump(q.X * 0.4f, 0f, q.Y * 0.4f)) + 0.16f * Lump(q.X * 0.8f, rise * 0.7f, q.Y * 0.8f);
						float fade = Mathf.SmoothStep(0.6f, 1.4f, rise);
						v += new Vector3(right.X, 0, right.Y) * sgn * bulge * fade;
					}
					else if (ad >= WallTop + 0.2f && ad < DeepSnow + 0.5f)
					{
						// the top: broken and lumpy, most at its lip
						float lip = 1f - Mathf.SmoothStep(WallTop + 0.3f, DeepSnow, ad);
						v.Y += (0.2f * Lump(q.X * 1.1f, 0.5f, q.Y * 1.1f) + 0.12f * Lump(q.X * 2.9f, 1.7f, q.Y * 2.9f)) * (0.35f + 0.65f * lip) * Mathf.SmoothStep(0.4f, 1.2f, rise);
					}
					row[a] = v;
					rc[a] = GroundColor(q.X, q.Y);
				}
				rows[i - r0] = row; cols[i - r0] = rc;
			}
			var nrm = new Vector3[rows.Length][];
			for (int ri = 0; ri < rows.Length; ri++)
			{
				nrm[ri] = new Vector3[n];
				var prevRow = rows[Mathf.Max(ri - 1, 0)]; var nextRow = rows[Mathf.Min(ri + 1, rows.Length - 1)];
				for (int a = 0; a < n; a++)
				{
					var across = rows[ri][Mathf.Min(a + 1, n - 1)] - rows[ri][Mathf.Max(a - 1, 0)];
					var along = nextRow[a] - prevRow[a];
					nrm[ri][a] = across.Cross(along).Normalized();   // (up on the flat, toward the road on either wall)
				}
			}
			for (int i = start + 1; i <= end; i++)
			{
				int ri = i - r0;
				Vector3[] prev = rows[ri - 1], row = rows[ri], prevN = nrm[ri - 1], rn = nrm[ri];
				Color[] prevC = cols[ri - 1], rc = cols[ri];
				for (int a = 0; a < n - 1; a++)
				{
					Vector3 v0 = prev[a], v1 = prev[a + 1], v2 = row[a + 1], v3 = row[a];
					TriC(k, v0, v1, v2, prevN[a], prevN[a + 1], rn[a + 1], prevC[a], prevC[a + 1], rc[a + 1]);
					TriC(k, v0, v2, v3, prevN[a], rn[a + 1], rn[a], prevC[a], rc[a + 1], rc[a]);
					if (Mathf.Abs(Across[a]) < RibbonHalf - 1f || Mathf.Abs(Across[a + 1]) < RibbonHalf - 1f)
					{
						faces.Add(v0); faces.Add(v1); faces.Add(v2);
						faces.Add(v0); faces.Add(v2); faces.Add(v3);
					}
				}
			}
			var mi = k.CommitTo(this, $"Road_{start / segs}", false);
			mi.VisibilityRangeEnd = 170f;
			var body = new StaticBody3D { Name = $"RoadBody_{start / segs}", CollisionLayer = 1, CollisionMask = 0 };
			body.SetMeta("surface", "snow");
			var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
			shape.SetFaces(faces.ToArray());
			body.AddChild(new CollisionShape3D { Shape = shape });
			AddChild(body);
			BuildWallClumps(start, end, $"WallClumps_{start / segs}");
		}
	}

	/// <summary>A smooth-ish lumpy noise, about -1..1 (a few crossed sines: cheap, and the same every run).</summary>
	public static float Lump(float x, float y, float z)
	{
		float v = Mathf.Sin(x * 1.31f + z * 0.47f + y * 0.9f) * 0.45f + Mathf.Sin(z * 1.73f - x * 0.61f + y * 1.7f + 1.3f) * 0.35f
			+ Mathf.Sin((x + z) * 2.9f + y * 2.3f + 2.1f) * 0.2f + Mathf.Sin(x * 4.7f - z * 3.1f + y * 3.9f) * 0.12f;
		return v / 1.12f;
	}

	private static Mesh[] _clumps;

	/// <summary>Snow clumps heaped along the plowed walls' tops (on their faces and at their feet they read as stones).</summary>
	private void BuildWallClumps(int start, int end, string name)
	{
		_clumps ??= new[] { ClumpMesh(7101), ClumpMesh(7102), ClumpMesh(7103) };
		var lists = new[] { new List<Transform3D>(), new List<Transform3D>(), new List<Transform3D>() };
		var rng = new RandomNumberGenerator { Seed = (ulong)(start * 131 + 7) };
		for (int i = start; i < end; i++)
		{
			var p = RoadPoint(i);
			var t = RoadDir(i);
			var right = new Vector2(-t.Y, t.X);
			foreach (float sd in new[] { -1f, 1f })
			{
				float H = WallHeight(i * Step, -sd);
				if (H < 0.8f) continue;
				// heaped along the top: a clump or two every step
				int m = rng.RandiRange(1, 2);
				for (int j = 0; j < m; j++)
				{
					float d = WallTop + rng.RandfRange(0.15f, 1.3f);
					var q = p + right * sd * d + t * rng.RandfRange(-0.75f, 0.75f);
					float sc = rng.RandfRange(0.35f, 0.8f);
					var at = new Vector3(q.X, Height(q.X, q.Y) - sc * 0.35f, q.Y);
					lists[rng.RandiRange(0, 2)].Add(new Transform3D(new Basis(Vector3.Up, rng.RandfRange(0, Mathf.Tau)).Scaled(new Vector3(sc * rng.RandfRange(0.9f, 1.4f), sc * rng.RandfRange(0.5f, 0.8f), sc)), at));
				}
			}
		}
		var holder = new Node3D { Name = name };
		AddChild(holder);
		for (int c = 0; c < 3; c++)
		{
			if (lists[c].Count == 0) continue;
			var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = _clumps[c], InstanceCount = lists[c].Count };
			for (int j = 0; j < lists[c].Count; j++) mm.SetInstanceTransform(j, lists[c][j]);
			holder.AddChild(new MultiMeshInstance3D { Name = $"Clumps{c}", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = 70f, VisibilityRangeEndMargin = 6f });
		}
	}

	/// <summary>A clump of packed snow about a metre across (scaled per instance): a lumpy, broken ball.</summary>
	private static Mesh ClumpMesh(int seed)
	{
		var k = new MeshKit();
		k.Mat(WallSnow);
		k.Color = Colors.White;
		k.Blob(Vector3.Zero, new Vector3(0.5f, 0.42f, 0.46f), seed, 0.28f, false, 1f);
		var rng = new RandomNumberGenerator { Seed = (ulong)seed };
		for (int i = 0; i < 2; i++)
			k.Blob(new Vector3(rng.RandfRange(-0.3f, 0.3f), rng.RandfRange(0.05f, 0.25f), rng.RandfRange(-0.3f, 0.3f)), new Vector3(0.26f, 0.2f, 0.24f), seed * 3 + i, 0.3f, false, 1f);
		return k.Commit();
	}

	private static StandardMaterial3D _wallSnow;
	/// <summary>The plowed walls' clumps: the ground's snow, world-triplanar, a hair darker (packed, in the wall's shade).</summary>
	public static StandardMaterial3D WallSnow => _wallSnow ??= SnowStd("winter_wall_snow", 0.28f, 0.8f);

	private static float Flats(float x, float z, float h) => SkiLodge.Flatten(x, z, h);

	// ------------------------------------------------------------------ the weather, the light, the sound

	private GpuParticles3D _diamondDust;
	private bool _roadSnowing;
	/// <summary>How heavily it snows on the road out of the church (Weather's 0..1), thinning to nothing by the lodge.</summary>
	public const float RoadSnow = 0.85f;
	private AudioStreamPlayer _wind;
	private bool _silenced;
	private double _clock, _nextTinkle = 8;
	/// <summary>For tests: where the player is along the road (0 at the church, 1 at the lodge), and whether they're out in the woods.</summary>
	public float PlayerProgress { get; private set; }
	public bool PlayerOutside { get; private set; }
	public float SnowRatio => Mathf.Clamp((Weather.Instance?.Snow ?? 0f) / RoadSnow, 0f, 1f);

	private void BuildWeather()
	{
		// (the snowfall itself is Weather's now: RequestSnow below)
		// near the lodge the snow stops and the air freezes: a few ice crystals hang and drift, glinting faintly (slow, never a flicker)
		// (tiny crystals out of the owner's snowflake image, lit: they glint only where the light is)
		var glint = new QuadMesh { Size = new Vector2(0.03f, 0.03f), Material = Weather.FlakeMaterial(0.6f) };
		_diamondDust = new GpuParticles3D
		{
			Name = "DiamondDust", Amount = 500, Lifetime = 18f, Preprocess = 18f, LocalCoords = false, DrawPass1 = glint, Emitting = false,
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(20f, 5f, 20f),
				Direction = Vector3.Down, Spread = 80f, InitialVelocityMin = 0.02f, InitialVelocityMax = 0.12f, Gravity = new Vector3(0, -0.02f, 0),
				TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.2f, ScaleMin = 0.5f, ScaleMax = 1.2f,
			},
			VisibilityAabb = new Aabb(new Vector3(-25f, -10f, -25f), new Vector3(50f, 20f, 50f)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		Weather.FlakeFrames((ParticleProcessMaterial)_diamondDust.ProcessMaterial, 0, 8);
		AddChild(_diamondDust);
		_wind = new AudioStreamPlayer { Name = "WinterWind", Stream = GD.Load<AudioStream>("res://assets/audio/ambient/winter_wind_loop.wav"), Bus = "Weather", VolumeDb = -60f };
		if (_wind.Stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
		AddChild(_wind);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_clock += delta;
		var s = StoryManager.Instance;
		var player = StoryBeat.Player(this);
		var cam = GetViewport()?.GetCamera3D();
		bool act = s != null && s.Current >= Checkpoint.Act21Finished;
		bool outside = act && player != null && Church != null && !Church.Inside(player.GlobalPosition) && !(Lodge?.Inside(player.GlobalPosition) ?? false);
		if (outside && player != null)
		{
			var l = ToLocal(player.GlobalPosition);
			float d = Nearest(l.X, l.Z, out float sAlong, out _);
			outside = d < RegionHalf + 40f || new Vector2(l.X - WinterGlade.Centre.X, l.Z - WinterGlade.Centre.Y).Length() < WinterGlade.Radius + 20f;
			if (outside) { PlayerProgress = Mathf.Clamp(sAlong / Length, 0f, 1f); KeepInBounds(player, l, d); }
		}
		PlayerOutside = outside;
		// halfway down the road: a save (Continue comes back here, not to the church door)
		if (outside && s.Current == Checkpoint.Act21Finished && PlayerProgress * Length > MidwayS && !s.HasFlag(StoryManager.Flag.Act22Midway))
			s.SetFlag(StoryManager.Flag.Act22Midway);
		UpdateWading(player, outside, dt);
		UpdateWallWind(player, outside, dt);
		float frozen = FrozenAt(PlayerProgress * Length);
		// the light: dusk out here, colder and clearer toward the lodge
		var atmo = StoryBeat.Atmosphere(this);
		if (atmo != null)
		{
			atmo.WinterDusk = Mathf.MoveToward(atmo.WinterDusk, outside ? 1f : (act ? 0.35f : 0f), dt * 0.25f);
			atmo.Frost = Mathf.MoveToward(atmo.Frost, outside ? frozen : 0f, dt * 0.2f);
		}
		// the snow: heavy round the church, thinning down the road, gone by the lodge (Weather's: from inside the church
		// the glade asks for it; out here the road does)
		if (outside != _roadSnowing || outside)
		{
			_roadSnowing = outside;
			Weather.Get(this).RequestSnow(this, outside ? RoadSnow * Mathf.Clamp(1f - frozen * 1.05f, 0f, 1f) : 0f, 2.5f);
		}
		if (_diamondDust != null)
		{
			bool dust = outside && frozen > 0.4f;
			if (_diamondDust.Emitting != dust) _diamondDust.Emitting = dust;
			_diamondDust.AmountRatio = Mathf.Clamp(frozen, 0.05f, 1f);
			if (cam != null) _diamondDust.GlobalPosition = cam.GlobalPosition;
		}
		// the sound: the summer forest has no place here: a thin wind that drops away to a frozen hush by the lodge
		var amb = ForestAmbienceManager.Instance;
		if (amb != null && outside != _silenced)
		{
			_silenced = outside;
			if (outside) amb.RequestSilence(this, 1f, 4); else amb.ReleaseSilence(this);
		}
		if (_wind != null)
		{
			if (outside && !_wind.Playing) _wind.Play();
			float want = outside ? Mathf.Lerp(-11f, -24f, frozen) : -60f;
			_wind.VolumeDb = Mathf.MoveToward(_wind.VolumeDb, want, dt * 8f);
			if (!outside && _wind.Playing && _wind.VolumeDb <= -59f) _wind.Stop();
		}
		if (outside && frozen > 0.35f && _clock >= _nextTinkle && player != null)
		{
			_nextTinkle = _clock + _rng.RandfRange(6f, 14f);
			var at = player.GlobalPosition + new Vector3(_rng.RandfRange(-14f, 14f), 4f, _rng.RandfRange(-14f, 14f));
			AudioDirector.OneShot(this, "ice_tinkle", 3, at, Mathf.Lerp(-22f, -14f, frozen), "Events", 5f, 0.08f);
		}
		UpdateStalker(dt, player, cam, outside);
	}

	// ------------------------------------------------------------------ footprints

	/// <summary>The prints in the snow (<see cref="SnowPrints"/>): the player's as they walk, the wendigo's where it's been.</summary>
	public SnowPrints Prints { get; private set; }
	private PlayerFootsteps _feet;
	private bool _leftFoot;

	private void BuildPrints()
	{
		Prints = new SnowPrints { Name = "Prints" };
		AddChild(Prints);
		// its tracks across the road ahead: out of the trees over one wall, across, and up over the other (it was here first)
		foreach (float s in new[] { 270f, 640f, 1010f, 1290f })
		{
			var c = RoadAt(s, out var dir);
			var right = new Vector2(-dir.Y, dir.X);
			var across = (right + dir * 0.35f).Normalized();
			for (float d = -WallTop - 1.5f; d <= WallTop + 1.5f; d += 1.25f)
			{
				var xz = new Vector2(c.X, c.Z) + across * d + new Vector2(-across.Y, across.X) * (Mathf.Sin(d * 1.7f) * 0.12f);
				LayLocal(xz, new Vector3(across.X, 0, across.Y), ((int)((d + 10f) / 1.25f)) % 2 == 0, true, 0.6f);
			}
		}
	}

	/// <summary>A print at a church-local point on the snow.</summary>
	private void LayLocal(Vector2 xz, Vector3 facingLocal, bool left, bool wendigo, float depth)
	{
		if (Prints == null) return;
		const float e = 0.25f;
		float h = Height(xz.X, xz.Y);
		var n = new Vector3(Height(xz.X - e, xz.Y) - Height(xz.X + e, xz.Y), 2f * e, Height(xz.X, xz.Y - e) - Height(xz.X, xz.Y + e)).Normalized();
		Prints.Lay(ToGlobal(new Vector3(xz.X, h, xz.Y)), GlobalBasis * n, GlobalBasis * facingLocal, left, wendigo, depth);
	}

	/// <summary>The wendigo's feet at a world point (it stood there, or sprang from there).</summary>
	public void LayClaws(Vector3 world, Vector3 facingWorld, float depth)
	{
		var l = ToLocal(world);
		var f = GlobalBasis.Inverse() * facingWorld;
		f.Y = 0;
		if (f.LengthSquared() < 1e-4f) f = Vector3.Forward;
		f = f.Normalized();
		var side = f.Cross(Vector3.Up) * 0.28f;
		LayLocal(new Vector2(l.X - side.X, l.Z - side.Z), f, true, true, depth);
		LayLocal(new Vector2(l.X + side.X, l.Z + side.Z), f, false, true, depth);
	}

	/// <summary>The player's step: a boot print at the foot that came down (left, right, left), deeper in deep snow.</summary>
	private void OnStep()
	{
		var player = StoryBeat.Player(this);
		if (player == null || !PlayerOutside || _feet == null || _feet.LastSurface != "snow") return;
		var v = player.Velocity with { Y = 0 };
		var f = v.LengthSquared() > 0.01f ? v.Normalized() : -player.CameraRig.Camera.GlobalBasis.Z with { Y = 0 };
		var fl = (GlobalBasis.Inverse() * f).Normalized();
		var l = ToLocal(player.GlobalPosition);
		var side = fl.Cross(Vector3.Up) * (_leftFoot ? -0.11f : 0.11f);
		LayLocal(new Vector2(l.X + side.X, l.Z + side.Z), fl, _leftFoot, false, Mathf.Lerp(0.35f, 1f, PlayerDeepSnow));
		_leftFoot = !_leftFoot;
	}

	/// <summary>Off the plowed road the snow is deep (the owner: they should want to stick to the road): walking in
	/// it drags them down to well under half their pace; the church's yard is trodden, only a little slower; round the
	/// lodge it's plowed, full pace.</summary>
	public const float DeepSnowPace = 0.42f, TroddenPace = 0.78f;
	/// <summary>For tests: how deep the snow under them is (0 the road, 1 deep).</summary>
	public float PlayerDeepSnow { get; private set; }
	private bool _wading;

	private void UpdateWading(PlayerController player, bool outside, float dt)
	{
		if (player == null) return;
		// (the prints follow the footfalls)
		if (_feet == null && player.Footsteps is { } feet) { _feet = feet; feet.Stepped += OnStep; }
		float want = 1f;
		if (outside)
		{
			var l = ToLocal(player.GlobalPosition);
			float deep = 1f - PlowedWeight(l.X, l.Z);
			float pace = DeepSnowPace;
			// trodden round the church; and round the lodge it's plowed (the owner: no drag there at all)
			if (WinterGlade.OutsideChurch(l.X, l.Z) < 14f) pace = TroddenPace;
			if (SkiLodge.NearLodge(l.X, l.Z, 48f)) pace = 1f;
			PlayerDeepSnow = deep;
			want = Mathf.Lerp(1f, pace, deep);
		}
		else PlayerDeepSnow = 0f;
		if (player.Footsteps != null) player.Footsteps.SnowDepth = PlayerDeepSnow;
		// (only while it's ours to set: the sewer and the crawlspace have their own)
		if (!outside && !_wading) return;
		player.WadeScale = Mathf.MoveToward(player.WadeScale, want, dt * 2.5f);
		_wading = outside || player.WadeScale < 0.999f;
		if (!_wading) player.WadeScale = 1f;
	}

	// ------------------------------------------------------------------ the wind over the walls

	private AudioStreamPlayer3D _windL, _windR;
	private float _windLevel;
	/// <summary>For tests: how loud the wind over the walls is (0..1).</summary>
	public float WallWind => _windLevel;

	/// <summary>Wind whistling over the plowed walls' crests (the detail pass): a source on each wall's top beside
	/// the player, following them down the road, as loud as the walls are high.</summary>
	private void BuildWallWind()
	{
		const string path = "res://assets/audio/sfx/wind_wall_loop.wav";
		if (!ResourceLoader.Exists(path)) return;
		AudioStreamPlayer3D One(string name, float from)
		{
			var wav = (AudioStreamWav)GD.Load<AudioStreamWav>(path).Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			var p = new AudioStreamPlayer3D { Name = name, Stream = wav, Bus = "Weather", VolumeDb = -80f, UnitSize = 6f, MaxDistance = 40f };
			AddChild(p);
			p.Play(from);
			p.StreamPaused = true;
			return p;
		}
		_windL = One("WallWindL", 0f);
		_windR = One("WallWindR", 5.3f);   // (the two sides out of step)
	}

	private void UpdateWallWind(PlayerController player, bool outside, float dt)
	{
		if (_windL == null) return;
		float want = 0f;
		if (outside && player != null)
		{
			var l = ToLocal(player.GlobalPosition);
			Nearest(l.X, l.Z, out float s, out _);
			var c = RoadAt(s, out var dir);
			var right = new Vector3(-dir.Y, 0, dir.X);
			foreach (var (p, side) in new[] { (_windL, -1f), (_windR, 1f) })
			{
				var crest = c + right * side * WallTop;
				crest.Y = Height(crest.X, crest.Z) + 0.4f;
				p.Position = crest;
				float h = WallHeight(s, side);
				p.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, _windLevel * Mathf.Clamp(h / 2f, 0f, 1f))) - 13f;
			}
			// (the frozen stretch by the lodge is still: no wind)
			want = 1f - Mathf.SmoothStep(0.86f, 0.95f, PlayerProgress);
		}
		_windLevel = Mathf.MoveToward(_windLevel, want, dt * 0.4f);
		bool on = _windLevel > 0.01f;
		_windL.StreamPaused = _windR.StreamPaused = !on;
	}

	/// <summary>The woods go on for ever into the dark, but the ground doesn't: past ~95 m from the road the
	/// snow gets deeper and deeper (a gentle push back, never a wall).</summary>
	private void KeepInBounds(PlayerController player, Vector3 local, float d)
	{
		if (SkiLodge.NearLodge(local.X, local.Z, 80f)) return;
		if (new Vector2(local.X - WinterGlade.Centre.X, local.Z - WinterGlade.Centre.Y).Length() < WinterGlade.Radius - 15f) return;
		if (d < 95f) return;
		Nearest(local.X, local.Z, out float s, out _);
		var road = RoadAt(s, out _);
		var back = new Vector3(road.X - local.X, 0, road.Z - local.Z).Normalized();
		float push = Mathf.Clamp((d - 95f) / 10f, 0f, 1.5f);
		var g = ToGlobal(local + back * push * 0.06f) - ToGlobal(local);
		player.GlobalPosition += g;
	}
}
