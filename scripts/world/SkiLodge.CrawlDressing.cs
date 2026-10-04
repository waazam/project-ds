using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// What's been left in the walls (the owner, 2026-10-04: "add missing posters and other interesting things on the walls
/// like newspapers all about missing campers and hikers in the area, it would also be cool to put christmas lights
/// overhead lining the crawlspace to the end after the player gets around 1/3 through"):
/// <list type="bullet">
/// <item>missing posters and newspaper pages pinned to the boards all along the way, years of them, every one about
/// someone who went into these woods (the hiker R.H. among them, his name torn off); each can be read;</item>
/// <item>from a third of the way in, strings of Christmas lights stapled over the ceiling in a jumble, sagging between
/// their staples, lit, all the way to the end: someone put them up in here, a long time ago, and they are still on.</item>
/// </list>
/// </summary>
public partial class SkiLodge
{
	public int CrawlPosters { get; private set; }
	public int CrawlBulbs { get; private set; }
	public int CrawlLights { get; private set; }

	private static readonly (string head, string title, string body)[] MissingPosters =
	{
		("MISSING", "MISSING", "MISSING\nDANIEL ORTEGA, 34\nLast seen at Hollow Peak Lodge, December 22, leaving on snowshoes for the ridge trail.\nBlue parka, grey wool hat.\nIF YOU HAVE SEEN DANIEL PLEASE CALL"),
		("MISSING", "MISSING", "MISSING HIKER\nR___ H___, 41\nLast seen at the Blackfern trailhead, October 24.\nGreen pack, a camera round his neck.\n(The name has been torn off. Someone has written over it in pencil: \"the steps\".)"),
		("HAVE YOU SEEN US?", "Have you seen us?", "HAVE YOU SEEN US?\nTHE MARSH FAMILY\nTom (45), Helen (43), Abby (12), Sam (9)\nCamping at Overlook Park, site 14, August.\nTheir tent was found standing. Their car is still in the lot."),
		("MISSING", "MISSING", "MISSING\nKAREN BELL, 27\nSeasonal staff, Hollow Peak Lodge (bar).\nDid not return from her shift's end, December 23.\nShe told a friend she could hear someone calling her name outside."),
		("REWARD", "REWARD", "$5,000 REWARD\nfor information on the disappearance of\nGREG LANNISTER, 52, and his dog Bo.\nLast seen hunting the north slope, November.\nBo came home alone. He will not go back in the woods."),
		("MISSING", "MISSING", "MISSING\nTHE WINTER STAFF\nHollow Peak Lodge\n11 employees not seen since the lodge closed for the storm.\nThe sheriff's department asks anyone with information to come forward."),
		("MISSING", "MISSING", "MISSING\nELI PRICE, 19\nScout leader, Troop 212.\nWent back for a camper's lost boot at dusk. The camper came back. Eli did not."),
		("MISSING", "MISSING", "MISSING\nMARGARET 'PEG' OWENS, 70\nWalks the Blackfern loop every morning.\nHer walking stick was found at the foot of some old stone steps no ranger can find on a map."),
	};

	private static readonly (string head, string body)[] Headlines =
	{
		("HIKERS STILL MISSING", "THIRD HIKER MISSING IN BLACKFERN WOODS\nSearch teams returned empty-handed for the third day. \"The dogs won't follow the scent past the old steps,\" said the county's search coordinator. \"They lie down and they won't move.\""),
		("SEARCH CALLED OFF", "SEARCH FOR LODGE GUESTS CALLED OFF\nThe blizzard that closed Hollow Peak Lodge has ended the search for the missing guests and staff. Rescuers who reached the lodge describe every light burning, the tables laid, and nobody inside."),
		("CAMPERS VANISH", "CAMPERS VANISH NEAR OVERLOOK PARK\nA family of four is missing from Overlook Park. Their campfire was still smouldering when rangers arrived. Their shoes were lined up outside the tent."),
		("'IT WAS CALLING MY NAME'", "'IT WAS CALLING MY NAME' - SURVIVOR\nThe only member of last winter's ski party to be found alive told police something followed them for two days in a voice like her brother's. Her brother has not been found."),
		("LODGE TO CLOSE", "HOLLOW PEAK LODGE TO CLOSE \"FOR THE SEASON\"\nThe owners deny that the closure has anything to do with the disappearances. The lodge, they say, will reopen. \"We miss our guests,\" a statement reads. \"We want them back.\""),
		("STARVING, FREEZING", "OLD STORIES RESURFACE\nLocal elders tell of a thing that lives in these woods in the hungry months: never full, always cold, wearing the voices of the people it has eaten. \"Don't answer it,\" one said. \"Not even if it's your mother.\""),
	};

	/// <summary>The posters and the papers, and the lights (in the maze, below; built with it).</summary>
	private void DressCrawlspace(Node3D maze)
	{
		var rng = new RandomNumberGenerator { Seed = 2323 };
		var armCells = new HashSet<Vector2I>();
		foreach (var (cell, _) in ArmSpots) armCells.Add(cell);
		var pipes = new HashSet<Vector2I>(CrouchPipes);
		Vector2I[] sides = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
		int poster = 0, paper = 0;
		for (int k = 6; k < CrawlPath.Count - 4; k += rng.RandiRange(4, 7))
		{
			var at = CrawlPath[k];
			if (armCells.Contains(at) || pipes.Contains(at) || !_maze.TryGetValue(at, out var cell) || cell.Stair) continue;
			// a wall here (not where the way goes on)
			var walls = new List<Vector2I>();
			foreach (var sd in sides) if (!Opens(_maze, at, sd)) walls.Add(sd);
			if (walls.Count == 0) continue;
			var side = walls[rng.RandiRange(0, walls.Count - 1)];
			var c = CellCentre(at) + CrawlDown;
			var along = new Vector3(side.Y, 0, -side.X);
			var inward = new Vector3(-side.X, 0, -side.Y);
			float slide = (rng.Randf() < 0.5f ? -1f : 1f) * rng.RandfRange(0.18f, 0.28f);   // (off the stud at the panel's middle)
			var spot = c + new Vector3(side.X, 0, side.Y) * 0.455f + along * slide + Vector3.Up * (cell.Y0 + rng.RandfRange(1.35f, 1.6f));
			bool isPaper = (poster + paper) % 3 == 2;
			float tilt = rng.RandfRange(-6f, 6f);
			if (isPaper)
			{
				var (head, body) = Headlines[paper % Headlines.Length];
				var r = PaperKit.Pinned(maze, ToLocalIn(maze, spot), inward, new Vector2(0.34f, 0.46f), PaperKit.Look.Board, "The Overlook Courier", body, Readable.NoteStyle.Printed, tilt, "Read the newspaper", 900 + paper);
				Headline(maze, ToLocalIn(maze, spot) + Vector3.Up * 0.16f, inward, tilt, "THE OVERLOOK COURIER", 0.022f);
				Headline(maze, ToLocalIn(maze, spot) + Vector3.Up * 0.11f, inward, tilt, head, 0.034f);
				paper++;
			}
			else
			{
				var (head, title, body) = MissingPosters[poster % MissingPosters.Length];
				PaperKit.Pinned(maze, ToLocalIn(maze, spot), inward, new Vector2(0.3f, 0.4f), PaperKit.Look.Card, title, body, Readable.NoteStyle.Printed, tilt, "Read the poster", 950 + poster);
				Headline(maze, ToLocalIn(maze, spot) + Vector3.Up * 0.15f, inward, tilt, head, head.Length > 8 ? 0.026f : 0.05f, new Color(0.45f, 0.05f, 0.04f));
				Photo(maze, ToLocalIn(maze, spot) + Vector3.Up * 0.01f, inward, tilt, 700 + poster);
				poster++;
			}
		}
		CrawlPosters = poster + paper;
		StringLights(maze);
	}

	private static Vector3 ToLocalIn(Node3D n, Vector3 lodgeLocal) => lodgeLocal;   // (the maze node sits at the lodge's origin)

	/// <summary>Printed words across a pinned sheet (a hair in front of it).</summary>
	private static void Headline(Node3D parent, Vector3 at, Vector3 outward, float tiltDeg, string text, float em, Color? color = null)
	{
		Vector3 z = outward.Normalized(), x = Vector3.Up.Cross(z).Normalized();
		var b = new Basis(x, Vector3.Up, z).Rotated(z, Mathf.DegToRad(tiltDeg));
		SignKit.Text(parent, text, at + z * 0.009f, b, em, color ?? new Color(0.12f, 0.1f, 0.1f), shadow: false);
	}

	/// <summary>A poster's photograph: a grey print, a face in it only just made out (a pale oval, two dark eyes).</summary>
	private static void Photo(Node3D parent, Vector3 at, Vector3 outward, float tiltDeg, int seed)
	{
		Vector3 z = outward.Normalized(), x = Vector3.Up.Cross(z).Normalized();
		var b = new Basis(x, Vector3.Up, z).Rotated(z, Mathf.DegToRad(tiltDeg));
		var k = new MeshKit();
		k.Mat(_photoMat ??= new StandardMaterial3D { ResourceName = "crawl_photo", AlbedoColor = new Color(0.32f, 0.31f, 0.3f), Roughness = 0.6f });
		k.Color = Colors.White;
		k.Box(Vector3.Zero, new Vector3(0.14f, 0.17f, 0.002f), 1f);
		k.Mat(_faceMat ??= new StandardMaterial3D { ResourceName = "crawl_photo_face", AlbedoColor = new Color(0.55f, 0.53f, 0.5f), Roughness = 0.6f });
		k.Blob(new Vector3(0, 0.015f, 0.002f), new Vector3(0.035f, 0.045f, 0.002f), seed, 0.1f, false, 1f);
		k.Mat(_photoMat);
		foreach (float s in new[] { -1f, 1f }) k.Box(new Vector3(s * 0.013f, 0.025f, 0.0035f), new Vector3(0.008f, 0.005f, 0.001f), 1f);
		var mi = k.CommitTo(parent, "Photo", false);
		mi.Transform = new Transform3D(b, at + z * 0.008f);
	}

	private static StandardMaterial3D _photoMat, _faceMat;

	/// <summary>Christmas lights over the ceiling from a third of the way in to the end (the owner: "a bunch of different lines
	/// of lights on the ceiling in different jumbles ... the lights should really glow and illuminate the crawlspace with
	/// their different colors"): four strands, joining one by one, each wandering its own way across the boards from staple
	/// to staple, crossing the others, sagging (some a long droop); a bulb every few inches in the old colours, a few dead,
	/// each with a soft glow round it; and the colours thrown on the boards by lights hung all along (steady: none of them
	/// blink).</summary>
	private void StringLights(Node3D maze)
	{
		int from = CrawlPath.Count / 3;
		var wire = new MeshKit();
		wire.Mat(_wireMat ??= new StandardMaterial3D { ResourceName = "xmas_wire", AlbedoColor = new Color(0.05f, 0.1f, 0.05f), Roughness = 0.6f });
		wire.Color = Colors.White;
		Color[] colours = { new(1f, 0.12f, 0.08f), new(0.12f, 0.9f, 0.2f), new(1f, 0.55f, 0.1f), new(0.2f, 0.32f, 1f), new(0.85f, 0.2f, 0.9f) };
		var bulbs = new MeshKit[colours.Length];
		var halos = new List<Transform3D>[colours.Length];
		for (int i = 0; i < colours.Length; i++)
		{
			bulbs[i] = new MeshKit();
			bulbs[i].Mat(new StandardMaterial3D { ResourceName = $"xmas_bulb_{i}", AlbedoColor = colours[i], EmissionEnabled = true, Emission = colours[i], EmissionEnergyMultiplier = 4f, Roughness = 0.3f });
			bulbs[i].Color = Colors.White;
			halos[i] = new List<Transform3D>();
		}
		var dead = new MeshKit();
		dead.Mat(new StandardMaterial3D { ResourceName = "xmas_bulb_dead", AlbedoColor = new Color(0.25f, 0.24f, 0.22f), Roughness = 0.4f });
		dead.Color = Colors.White;
		var rng = new RandomNumberGenerator { Seed = 2412 };
		float Roof(int k) => FloorAt(_maze[CrawlPath[k]], 0f, 0f) + CrawlH;
		Vector3 Centre(int k) => CellCentre(CrawlPath[k]) + CrawlDown;
		const int strands = 4;
		int n = 0;
		for (int st = 0; st < strands; st++)
		{
			// each joins a little further in than the last (a jumble that thickens), and wanders: a staple per cell, its place
			// across the ceiling drifting (so the strands cross), now and then a long droop between staples
			int start = from + st * rng.RandiRange(1, 3);
			var off = new Vector2(rng.RandfRange(-0.3f, 0.3f), rng.RandfRange(-0.3f, 0.3f));
			Vector3 Staple(int k)
			{
				// (each strand kept to its own reach across the boards: held to one shared edge, two would meet there, bulb in bulb)
				float reach = 0.26f + 0.025f * st;
				off = (off + new Vector2(rng.RandfRange(-0.18f, 0.18f), rng.RandfRange(-0.18f, 0.18f))).Clamp(new Vector2(-reach, -reach), new Vector2(reach, reach));
				return Centre(k) + new Vector3(off.X, Roof(k) - 0.05f - rng.RandfRange(0f, 0.04f), off.Y);
			}
			Vector3 a = Staple(start);
			for (int k = start; k < CrawlPath.Count - 1; k++)
			{
				Vector3 b = Staple(k + 1);
				float sag = rng.Randf() < 0.15f ? rng.RandfRange(0.14f, 0.2f) : rng.RandfRange(0.04f, 0.09f);
				Vector3 mid = (a + b) * 0.5f + Vector3.Down * sag * 2f;   // (the curve reaches half its control point's depth)
				Vector3 Curve(float t) => (1 - t) * (1 - t) * a + 2 * (1 - t) * t * mid + t * t * b;
				Vector3 prev = a;
				for (int s = 1; s <= 5; s++)
				{
					Vector3 p = Curve(s / 5f);
					wire.Cylinder(prev, p, 0.005f, 0.005f, 4, false);
					prev = p;
				}
				for (int s = 0; s < 3; s++)
				{
					Vector3 p = Curve((s + 0.5f) / 3f);
					int ci = (n + s + st) % colours.Length;
					bool isDead = rng.Randf() < 0.06f;
					var kit = isDead ? dead : bulbs[ci];
					kit.Cylinder(p + Vector3.Down * 0.005f, p + Vector3.Down * 0.024f, 0.009f, 0.009f, 6, false);   // the socket's cap (open: its flat ends lay in one plane with others')
					kit.Blob(p + Vector3.Down * 0.044f, new Vector3(0.014f, 0.025f, 0.014f), n + s, 0f, false, 1f);
					if (!isDead) halos[ci].Add(new Transform3D(Basis.FromScale(Vector3.One * rng.RandfRange(0.85f, 1.15f)), p + Vector3.Down * 0.044f));
					CrawlBulbs++;
				}
				n += 3;
				a = b;
			}
		}
		// the glow round each bulb (soft, added on: the post's halation picks it up)
		for (int i = 0; i < colours.Length; i++)
		{
			if (halos[i].Count == 0) continue;
			var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new QuadMesh { Size = new Vector2(0.16f, 0.16f) }, InstanceCount = halos[i].Count };
			for (int h = 0; h < halos[i].Count; h++) mm.SetInstanceTransform(h, halos[i][h]);
			maze.AddChild(new MultiMeshInstance3D { Name = $"XmasHalos{i}", Multimesh = mm, MaterialOverride = HaloMat(colours[i]), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		}
		// the colours on the boards: a light every cell and a half, round the colours (steady)
		int light = 0;
		for (int k = from + 1; k < CrawlPath.Count - 1; k += (k - from) % 3 == 1 ? 1 : 2)
		{
			var lc = colours[light++ % colours.Length];
			var at = Centre(k) + new Vector3(rng.RandfRange(-0.2f, 0.2f), Roof(k) - 0.22f, rng.RandfRange(-0.2f, 0.2f));
			CrawlLights++;
			maze.AddChild(new OmniLight3D { Name = "XmasGlow", Position = at, LightColor = lc.Lerp(new Color(1f, 0.85f, 0.7f), 0.15f), LightEnergy = 0.85f, OmniRange = 2.4f, OmniAttenuation = 1.4f, ShadowEnabled = false,
				DistanceFadeEnabled = true, DistanceFadeBegin = 13f, DistanceFadeLength = 4f });   // (only those near drawn: there are dozens)
		}
		wire.CommitTo(maze, "XmasWire", false);
		for (int i = 0; i < bulbs.Length; i++) if (!bulbs[i].IsEmpty) bulbs[i].CommitTo(maze, $"XmasBulbs{i}", false);
		if (!dead.IsEmpty) dead.CommitTo(maze, "XmasBulbsDead", false);
	}

	/// <summary>A bulb's glow: a soft round sprite facing the eye, its colour added over what's behind.</summary>
	private static StandardMaterial3D HaloMat(Color c)
	{
		_haloTex ??= new GradientTexture2D
		{
			Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
			Gradient = new Gradient { Offsets = new[] { 0f, 0.25f, 1f }, Colors = new[] { new Color(1, 1, 1, 0.8f), new Color(1, 1, 1, 0.3f), new Color(1, 1, 1, 0f) } },
		};
		return new StandardMaterial3D
		{
			ResourceName = "xmas_halo", AlbedoTexture = _haloTex, AlbedoColor = new Color(c.R, c.G, c.B, 0.55f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = false, DisableReceiveShadows = true,
		};
	}

	private static GradientTexture2D _haloTex;
	private static StandardMaterial3D _wireMat;
}
