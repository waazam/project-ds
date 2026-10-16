using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
using ProjectDS.World.StationParts;

namespace ProjectDS.World;

/// <summary>
/// Act 13, Room 1 (left of the lobby's desk; the clock's key opens it). Three walls of hurried,
/// scrawled red writing, huge, over the whole of each wall, one sentence to a wall — DO NOT LOOK AT THEM / DO NOT TOUCH THEM / NEVER GO UP
/// THEM — running with drips, and a duct-taped cigar box on the floor. The knife cuts its three sides
/// (left, right, then the front: <see cref="TapeCutOverlay"/>), the lid comes up on a button, and
/// pressing it (E) melts the writing down the walls into a blood puddle at each wall's foot. That is
/// Act 13's middle checkpoint (<see cref="Checkpoint.Act13Room1Solved"/>) and it opens Room 2.
///
/// Later, for the iron door's TOUCH: one of the three puddles has something in it. The writing is
/// gone, but a ghost of each line is still just legible on its wall; the hand is in the puddle under
/// DO NOT TOUCH THEM. Reach into either of the others and something in the blood takes the player's
/// wrist, holds it a moment, and lets go.
///
/// Local space: floor y=0, the room x/z in [-Half, Half]; its doorway is the -X wall's gap at z=0,
/// which lines up with the lobby's +X wall.
/// </summary>
public partial class StationRoom1 : Node3D
{
	public const float Half = 2.8f, Height = 3.0f;

	public const string Wall1 = "DO NOT LOOK\nAT THEM";
	public const string Wall2 = "DO NOT TOUCH\nTHEM";
	public const string Wall3 = "NEVER GO\nUP THEM";

	public int BoxCutsDone { get; private set; }
	public bool BoxOpen { get; private set; }
	public bool Solved { get; private set; }
	/// <summary>For tests: the three puddles' reach points (0: LOOK wall, 1: TOUCH wall, 2: UP wall).</summary>
	public Interactable[] Puddles { get; } = new Interactable[3];
	public int WrongReaches { get; private set; }

	/// <summary>Per wall: the writing (a node holding its scrawled lines), its lines, and its faint ghost.</summary>
	private readonly List<Node3D> _writing = new();
	private readonly List<List<Label3D>> _lines = new();
	private readonly List<Node3D> _ghosts = new();
	/// <summary>Per wall: its drips, each a run of blood from under a letter and the bead at its end.</summary>
	private readonly List<List<Drip>> _drips = new();
	private readonly List<Node3D> _puddleDecals = new();

	/// <summary>
	/// A run of paint-thick blood down a wall (2026-10-15, the scan: they were straight stretched decals, dozens to a
	/// wall, that read as red neon). A strip mesh of its own: a blob where it leaves the letter, then a thin run that
	/// wanders and kinks a little and swells and thins as it goes, ending in a heavier teardrop bead. Its shape is fixed
	/// in metres down the wall, so growing it (<see cref="Set"/>) lengthens the run without stretching it. Dried: dark,
	/// browned, matte, its edges soft.
	/// </summary>
	private sealed class Drip
	{
		public float Len { get; private set; }
		public readonly float Final;
		private readonly MeshInstance3D _mi;
		private readonly ArrayMesh _mesh = new();
		private readonly float _w, _a1, _f1, _p1, _a2, _f2, _p2, _f3, _p3, _drop, _shade;
		private readonly (float at, float by)[] _kinks;
		private static StandardMaterial3D _mat;

		private static StandardMaterial3D Mat => _mat ??= new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.82f, MetallicSpecular = 0.18f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
		};

		public Drip(Node3D parent, Vector3 top, Vector3 n, float off, float width, float final, int seed, string name)
		{
			var r = new RandomNumberGenerator { Seed = (ulong)(uint)seed };
			Final = final;
			_w = width;
			_a1 = r.RandfRange(0.0015f, 0.004f); _f1 = r.RandfRange(5f, 11f); _p1 = r.RandfRange(0f, 6.3f);
			_a2 = r.RandfRange(0.0005f, 0.0015f); _f2 = r.RandfRange(23f, 41f); _p2 = r.RandfRange(0f, 6.3f);
			_f3 = r.RandfRange(9f, 19f); _p3 = r.RandfRange(0f, 6.3f);
			_drop = r.RandfRange(1.5f, 2.1f);
			_shade = r.RandfRange(0.85f, 1.1f);
			int k = r.RandiRange(0, 2);
			_kinks = new (float, float)[k];
			for (int i = 0; i < k; i++) _kinks[i] = (r.RandfRange(0.08f, Mathf.Max(0.1f, final - 0.05f)), r.RandfRange(-0.012f, 0.012f));
			Vector3 right = Vector3.Up.Cross(n).Normalized();
			_mi = new MeshInstance3D
			{
				Name = name, Mesh = _mesh, MaterialOverride = Mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Transform = new Transform3D(new Basis(right, Vector3.Up, n), top + n * off),
			};
			parent.AddChild(_mi);
		}

		private float Offset(float t)
		{
			float x = _a1 * Mathf.Sin(t * _f1 + _p1) + _a2 * Mathf.Sin(t * _f2 + _p2);
			foreach (var (at, by) in _kinks) x += by * Mathf.SmoothStep(at, at + 0.035f, t);
			return x;
		}

		/// <summary>Its half-width <paramref name="t"/> metres down a run <paramref name="len"/> long.</summary>
		private float Half(float t, float len)
		{
			float hw = _w * 0.5f * (0.8f + 0.28f * Mathf.Sin(t * _f3 + _p3));
			hw *= 1f + 0.9f * Mathf.Exp(-t / 0.018f);                       // the blob where it leaves the letter
			float dl = Mathf.Min(len * 0.55f, _w * 4.2f);                    // the bead: swelling, then a round foot
			float u = (t - (len - dl)) / Mathf.Max(dl, 1e-4f);
			if (u > 0f)
			{
				float R = _w * 0.5f * _drop;
				if (u < 0.62f) hw = Mathf.Lerp(hw * 0.85f, R, Mathf.SmoothStep(0f, 1f, u / 0.62f));
				else { float v = (u - 0.62f) / 0.38f; hw = R * Mathf.Sqrt(Mathf.Max(0f, 1f - v * v)); }
			}
			return hw;
		}

		public void Set(float len)
		{
			Len = len = Mathf.Max(len, 0.012f);
			var verts = new List<Vector3>();
			var cols = new List<Color>();
			var idx = new List<int>();
			float dl = Mathf.Min(len * 0.55f, _w * 4.2f);
			var ts = new List<float>();
			for (float t = 0f; t < len - dl; t += 0.012f) ts.Add(t);
			for (int i = 0; i <= 8; i++) ts.Add(len - dl + dl * i / 8f);
			float[] across = { -1f, -0.45f, 0.45f, 1f };
			float[] alpha = { 0f, 0.95f, 0.95f, 0f };
			foreach (float t in ts)
			{
				float hw = Half(t, len), c = Offset(t);
				float wet = Mathf.SmoothStep(len - dl * 1.2f, len - dl * 0.3f, t);   // the bead holds more, darker
				var col = new Color(0.25f, 0.03f, 0.022f).Lerp(new Color(0.15f, 0.012f, 0.01f), wet * 0.9f) * (_shade * (0.92f + 0.12f * Mathf.Sin(t * 31f + _p2)));
				for (int j = 0; j < 4; j++)
				{
					verts.Add(new Vector3(c + across[j] * hw, -t, wet * 0.0012f * (j is 1 or 2 ? 1f : 0f)));
					cols.Add(new Color(col.R, col.G, col.B, alpha[j]));
				}
			}
			for (int row = 0; row + 1 < ts.Count; row++)
				for (int j = 0; j < 3; j++)
				{
					int a = row * 4 + j, b = a + 1, c = a + 4, d = a + 5;
					idx.AddRange(new[] { a, b, c, b, d, c });   // (clockwise from the room: Godot's front)
				}
			var norms = new Vector3[verts.Count];
			System.Array.Fill(norms, Vector3.Back);
			var arr = new Godot.Collections.Array();
			arr.Resize((int)Mesh.ArrayType.Max);
			arr[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
			arr[(int)Mesh.ArrayType.Normal] = norms;
			arr[(int)Mesh.ArrayType.Color] = cols.ToArray();
			arr[(int)Mesh.ArrayType.Index] = idx.ToArray();
			_mesh.ClearSurfaces();
			_mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
		}
	}

	/// <summary>How each wall's words are broken over it: three lines, filling the wall.</summary>
	private static readonly string[][] Scrawls =
	{
		new[] { "DO NOT", "LOOK AT", "THEM" }, new[] { "DO NOT", "TOUCH", "THEM" }, new[] { "NEVER", "GO UP", "THEM" },
	};
	private Node3D _box, _lid, _button;
	private Interactable _boxUse, _buttonUse;
	private Transform3D _leftCam, _rightCam, _frontCam;
	private MeshInstance3D[] _tapes;
	private static Font _scrawl;

	/// <summary>The three walls' feet (local) and their inward normals: 0 = +Z (LOOK), 1 = +X (TOUCH), 2 = -Z (UP).</summary>
	private static readonly (Vector3 foot, Vector3 n)[] Walls =
	{
		(new Vector3(0, 0, Half), Vector3.Forward), (new Vector3(Half, 0, 0), Vector3.Left), (new Vector3(0, 0, -Half), Vector3.Back),
	};

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private static Font Scrawl => _scrawl ??= new SystemFont
	{
		FontNames = new[] { "Chiller", "Ink Free", "Segoe Print", "Comic Sans MS", "serif" },
		FontWeight = 700,
	};

	private void Build()
	{
		using var __timer = Systems.BuildTimer.Time("StationRoom1");
		var s = StoryManager.Instance;
		// (2026-10-14h, the station pass, with the interiors kit: drawn finely for the bake; the paper, boards and plaster
		// weathered; the room's dark in its corners and seams, its light a pool under one bare bulb)
		var k = new MeshKit { Tess = 0.6f };
		var floorK = new MeshKit { Tess = 0.6f };
		var ceilK = new MeshKit { Tess = 0.6f };
		var body = new StaticBody3D { Name = "Walls", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		AddChild(body);
		k.Mat(RoomSurface.Of(StationTextures.WallpaperStainedMat, RoomSurface.Paper with { Streaks = 0.8f, Tide = 0.75f, Grime = 1.2f }, 141));
		k.Color = new Color(0.85f, 0.8f, 0.75f);
		// the wall shared with the lobby: a thin skin just inside the lobby's own wall (never in the same
		// plane, or the two wallpapers fight), no collision of its own (the lobby's wall has it)
		StationKit.WallAlongZ(k, null, -Half + 0.085f, -Half, Half, Height, 0, (0f, 2.2f), 2.2f, 0.02f, trimSide: 1);
		StationKit.WallAlongX(k, body, Half, -Half, Half, Height, 0, null, trimSide: -1);
		StationKit.WallAlongX(k, body, -Half, -Half, Half, Height, 0, null, trimSide: 1);
		StationKit.WallAlongZ(k, body, Half, -Half, Half, Height, 0, null, trimSide: -1);
		// (2026-10-14b: the lobby's plank floor and plaster ceiling, darker in here; they were the walls' own boards)
		floorK.Mat(RoomSurface.Of(StationTextures.PlankFloorMat, RoomSurface.Wood with { FloorDirt = 0.65f }, 142));
		floorK.Color = new Color(0.7f, 0.66f, 0.62f);
		ceilK.Mat(RoomSurface.Of(StationTextures.CeilingMat, RoomSurface.Plaster with { CeilingStains = 0.9f }, 143));
		ceilK.Color = new Color(0.6f, 0.58f, 0.55f);
		StationKit.FloorAndCeiling(floorK, ceilK, body, Half, Half, Height, 0);
		k.Color = Colors.White;
		// the room's inside: the skin on the lobby's wall at -Half + 0.095, the others' faces 7 cm in from ±Half
		float ix0 = -Half + 0.095f, ix1 = Half - 0.07f, iz = Half - 0.07f;
		var lampAt = new Vector3(0.2f, Height - 0.55f, -0.1f);
		var shade = new RoomShade(ix0, ix1, -iz, iz, 0f, Height) { Dim = 0.6f, Reach = 1.3f };
		shade.Light(lampAt, 3.6f).Path(new Vector3(ix0, 0, 0), new Vector3(0.3f, 0, 0.2f), 0.9f, 0.6f)
			.Damp(new Vector3(ix1, Height, iz), 1.6f, 0.7f).Damp(new Vector3(ix0, 0.4f, -iz), 1.2f, 0.6f)
			.Soot(lampAt, 0.5f, 0.35f);
		// a few things left in the corners, clear of the box, the three pools and the door's swing
		var small = new MeshKit();
		var clutterMat = StationTextures.Flat("st_card", new Color(0.4f, 0.32f, 0.22f), 0.95f, 0.05f);
		var clutter = new ClutterKit(new Aabb(new Vector3(ix0, 0, -iz), new Vector3(ix1 - ix0, Height, iz * 2f)), 1314, shade, body);
		clutter.Keep(new Vector3(0.3f, 0, 0.2f), 0.9f).Keep(new Vector3(ix0 + 1.1f, 0, 0), 1.25f).Keep(new Vector3(ix0 + 0.6f, 0, -1.3f), 0.6f);
		foreach (var (foot, n) in Walls) clutter.Keep(new Aabb(foot + n * 0.75f - new Vector3(Mathf.Abs(n.Z) * 2f + 0.75f * Mathf.Abs(n.X), 0, Mathf.Abs(n.X) * 2f + 0.75f * Mathf.Abs(n.Z)), new Vector3(Mathf.Abs(n.Z) * 4f + 1.5f * Mathf.Abs(n.X), 2.5f, Mathf.Abs(n.X) * 4f + 1.5f * Mathf.Abs(n.Z))));
		var roles = StationParts.LobbyDecor.Roles;
		var crate = ClutterKit.Model("crate", roles, canStackOn: true);
		var chair = ClutterKit.Model("dining_chair", roles);
		var carton = ClutterKit.Carton(clutterMat, new Vector3(0.42f, 0.3f, 0.32f));
		var files = ClutterKit.Bundle(clutterMat, clutterMat, new Vector3(0.3f, 0.12f, 0.24f), new Color(0.62f, 0.58f, 0.48f));
		if (crate != null) { var c1 = clutter.InCorner(k, crate); if (c1 != null) clutter.On(small, c1, carton); }
		var c2 = clutter.InCorner(small, carton);
		if (c2 != null) clutter.On(small, c2, files);
		clutter.InCorner(small, ClutterKit.Bucket(StationTextures.CastIronMat));
		if (chair != null) clutter.Topple(k, chair);
		clutter.Lean(small, ClutterKit.Frame(StationTextures.CasingMat, clutterMat, 0.5f, 0.62f));
		GD.Print($"[station] Room 1: {clutter.All.Count} pieces of clutter");
		shade.Apply(k);
		shade.Apply(floorK);
		shade.Apply(ceilK);
		shade.Apply(small);
		k.CommitTo(this, "Walls");
		small.CommitTo(this, "Clutter", false);
		// webs up in its corners (the interiors pass, 2026-10-03)
		WebKit.DressRoom(this, new RandomNumberGenerator { Seed = 1311 }, -Half + 0.1f, Half - 0.07f, -Half + 0.07f, Half - 0.07f, 0f, Height, 0.9f, 0.75f);
		DebrisKit.Scatter(this, new RandomNumberGenerator { Seed = 1312 }, -Half + 0.1f, Half - 0.07f, -Half + 0.07f, Half - 0.07f, 0f, new Color(0.62f, 0.58f, 0.5f), 50, 3,
			new[] { (new Vector3(-Half, 0, 0), 1.1f), (Vector3.Zero, 1.3f) });
		floorK.Color = Colors.White;
		floorK.CommitTo(this, "Floor");
		ceilK.Color = Colors.White;
		ceilK.CommitTo(this, "Ceiling", false);
		// one bare bulb on a flex (the room had a light and no lamp), a dull warm pool under it; its walls fall off into
		// the dark, the writing's top lines only just reached
		var flex = new MeshKit();
		flex.Mat(StationTextures.Flat("st_flex", new Color(0.05f, 0.05f, 0.05f)));
		flex.Color = Colors.White;
		flex.Cylinder(new Vector3(lampAt.X, Height, lampAt.Z), lampAt + Vector3.Up * 0.09f, 0.0045f, 0.0045f, 5, false);
		HollowProps.Socket(flex, lampAt + Vector3.Up * 0.02f);   // (2026-10-15: a lampholder and an A-shaped bulb, not a cylinder and a sphere)
		flex.CommitTo(this, "Flex", false);
		AddChild(new MeshInstance3D
		{
			Name = "Bulb", Mesh = HollowProps.BulbMesh(), Position = lampAt + Vector3.Up * 0.02f,
			MaterialOverride = StationTextures.Glow("st_bulb_r1", new Color(1f, 0.72f, 0.5f), 1.3f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		AddChild(new OmniLight3D
		{
			Name = "RoomLamp", LightColor = new Color(1f, 0.66f, 0.48f), LightEnergy = 1.6f,
			OmniRange = 6f, OmniAttenuation = 1.45f, Position = lampAt - Vector3.Up * 0.1f,
		});
		// stains, cracks and scuffs by rule; not over the writing
		DecalDresser.Room(this, new DecalDresser.RoomSpec
		{
			Box = new Aabb(new Vector3(ix0, 0, -iz), new Vector3(ix1 - ix0, Height, iz * 2f)), Seed = 141, Painted = true, Amount = 0.9f,
			Doors = { (new Vector3(ix0, 0, 0), Vector3.Right, 2.2f, 2.2f) },
			SkipWalls = { Vector3.Forward, Vector3.Left, Vector3.Back },
			Flames = { lampAt },
			Keep = { (new Vector3(0, 0, 0), 2.6f) },
		});

		// the writing (the owner: scrawled over the whole wall): three huge, hurried lines to a wall, each a
		// little crooked, with blood running from under the letters. (2026-10-15, the scan: letter by letter now, each
		// at its own lean, size and height, the brush's second pass off it, the red browned as old blood dries; and a
		// few runs to a line, not a comb of them: see Drip)
		var red = new Color(0.44f, 0.035f, 0.025f);
		var rng = new RandomNumberGenerator { Seed = 1331 };
		var drng = new RandomNumberGenerator { Seed = 1334 };
		float[] lineY = { 2.42f, 1.52f, 0.66f };
		for (int i = 0; i < 3; i++)
		{
			var (foot, n) = Walls[i];
			Vector3 right = Vector3.Up.Cross(n).Normalized();
			var wall = new Node3D { Name = $"Writing{i}" };
			var ghost = new Node3D { Name = $"Ghost{i}", Visible = false };
			AddChild(wall);
			AddChild(ghost);
			var labels = new List<Label3D>();
			var drips = new List<Drip>();
			int reachFloor = 0;
			for (int li = 0; li < 3; li++)
			{
				string line = Scrawls[i][li];
				float target = line.Length <= 4 ? rng.RandfRange(3.0f, 3.8f) : rng.RandfRange(4.4f, 5.1f);
				float px = Mathf.Max(1f, Scrawl.GetStringSize(line, HorizontalAlignment.Left, -1, 96).X);
				float em = Mathf.Min(target * 96f / px, 1.15f);
				float shift = rng.RandfRange(-0.25f, 0.25f);
				var b = Basis.LookingAt(-n, Vector3.Up) * new Basis(Vector3.Back, rng.RandfRange(-0.07f, 0.07f));
				Vector3 at = foot + n * 0.075f + right * shift + Vector3.Up * lineY[li];
				// letter by letter: where each sits along the line (its centre, in metres) and its own jitter
				var letters = new List<(string ch, float x, float y, float spin, float size)>();
				for (int c = 0; c < line.Length; c++)
				{
					if (line[c] == ' ') continue;
					float x0 = c == 0 ? 0f : Scrawl.GetStringSize(line[..c], HorizontalAlignment.Left, -1, 96).X;
					float cw = Scrawl.GetStringSize(line[c].ToString(), HorizontalAlignment.Left, -1, 96).X;
					letters.Add((line[c].ToString(), (x0 + cw * 0.5f - px * 0.5f) * em / 96f, rng.RandfRange(-0.035f, 0.035f) * em,
						rng.RandfRange(-0.09f, 0.09f), rng.RandfRange(0.9f, 1.1f)));
				}
				foreach (var (ch, x, y, spin, size) in letters)
				{
					var lb = b * new Basis(Vector3.Back, spin);
					Vector3 la = at + b * new Vector3(x, y, 0f);
					labels.Add(Scrawled(wall, ch, la, lb, em * size, red));
					// a second pass of the brush, a hair off the first: thick, smeared strokes
					labels.Add(Scrawled(wall, ch, la + n * 0.001f + right * em * rng.RandfRange(0.01f, 0.03f) + Vector3.Down * em * rng.RandfRange(0.015f, 0.035f),
						lb * new Basis(Vector3.Back, rng.RandfRange(-0.03f, 0.03f)), em * size * rng.RandfRange(0.97f, 1.04f), new Color(0.3f, 0.02f, 0.015f, 0.6f)));
					Scrawled(ghost, ch, la - n * 0.002f, lb, em * size, new Color(0.3f, 0.07f, 0.05f, 0.14f));
				}
				// a few runs from under the letters (a line's worth of wet paint, not a comb): most short, one or two
				// a wall going on to the skirting once the writing lets go
				int count = li == 1 ? drng.RandiRange(1, 3) : drng.RandiRange(2, 4);
				var used = new List<float>();
				for (int d = 0; d < count; d++)
				{
					var (_, lx, ly, _, lsize) = letters[drng.RandiRange(0, letters.Count - 1)];
					float x = lx + drng.RandfRange(-0.18f, 0.18f) * em;
					bool near = false;
					foreach (var u in used) near |= Mathf.Abs(u - x) < 0.12f;
					if (near) continue;
					used.Add(x);
					Vector3 top = at + b * new Vector3(x, ly - em * lsize * drng.RandfRange(0.2f, 0.3f), 0f);
					top.Y = Mathf.Max(top.Y, 0.2f);
					// where it can run to: the bottom line's down the panels to the skirting; the others stop on the dado
					// rail (its nosing at 0.97 m) or short of it
					float floorAt = li == 2 ? 0.14f : 0.985f;
					float room = Mathf.Max(top.Y - floorAt, 0.02f);
					float final;
					if (li == 2 && reachFloor < 2 && drng.Randf() < 0.55f) { final = room; reachFloor++; }
					else if (li != 2 && drng.Randf() < 0.3f) final = room;
					else final = Mathf.Min(room, drng.RandfRange(0.12f, 0.75f) * (li == 0 ? 1.4f : 1f));
					float start = Mathf.Min(drng.RandfRange(0.05f, 0.26f), final);
					// the bottom line's runs stand just off the panel mouldings (12 mm proud); the rest lie on the paper
					var drip = new Drip(this, top, n, li == 2 ? 0.009f : 0.002f, drng.RandfRange(0.022f, 0.042f), final, (int)drng.Randi(), $"Drip{i}_{li}_{d}");
					drip.Set(start);
					drips.Add(drip);
				}
			}
			_writing.Add(wall);
			_lines.Add(labels);
			_ghosts.Add(ghost);
			_drips.Add(drips);
		}

		BuildBox(new Vector3(0.3f, 0, 0.2f));
		BuildPuddles();

		// Continue
		if (s != null && s.HasFlag(StoryManager.Flag.StationRoom1Solved))
		{
			Solved = true;
			BoxOpen = true;
			BoxCutsDone = 3;
			foreach (var t in _tapes) t.Visible = false;
			// (the joints pass) open on its hinge as OpenBox leaves it: it was lifted 16 cm and floated over the box
			_lid.Rotation = new Vector3(Mathf.DegToRad(-100f), 0, 0);
			_boxUse.Enabled = false;
			foreach (var w in _writing) w.Visible = false;
			// the runs stay on the walls as they stopped, dried
			foreach (var wall in _drips) foreach (var d in wall) d.Set(d.Final);
			foreach (var g in _ghosts) g.Visible = true;
			foreach (var p in _puddleDecals) { p.Visible = true; p.Scale = Vector3.One; }
		}
		SetProcess(true);
	}

	private Label3D Scrawled(Node3D parent, string text, Vector3 at, Basis b, float em, Color c)
	{
		var l = new Label3D
		{
			Text = text, Font = Scrawl, FontSize = 96, PixelSize = em / 96f, Modulate = c, OutlineSize = 0,
			Shaded = true, AlphaCut = c.A < 0.99f ? Label3D.AlphaCutMode.Disabled : Label3D.AlphaCutMode.Discard,
			Transform = new Transform3D(b, at), DoubleSided = false, LineSpacing = -10f,
		};
		parent.AddChild(l);
		return l;
	}

	public override void _Process(double delta)
	{
		var s = StoryManager.Instance;
		bool hunt = Solved && s != null && s.HasFlag(StoryManager.Flag.StationDoor3Seen) && !s.HasFlag(StoryManager.Flag.StationHandTaken);
		foreach (var p in Puddles) if (p != null) p.Enabled = hunt;
	}

	// ------------------------------------------------------------------ the cigar box

	private void BuildBox(Vector3 at)
	{
		_box = new Node3D { Name = "CigarBox", Position = at, Rotation = new Vector3(0, 0.35f, 0) };
		AddChild(_box);
		const float hw = 0.22f, hd = 0.14f, hh = 0.075f;
		var k = new MeshKit();
		_lid = new Node3D { Name = "Lid", Position = new Vector3(0, hh * 2f, -hd) };
		_box.AddChild(_lid);
		var lk = new MeshKit();
		// (2026-10-15) the box and its lid modelled in Blender (tools/Blender/fixtures.py fx_cigar_box/_lid; the lid keeps
		// its hinge node); the code below is the fallback
		var band = StationTextures.Flat("st_cigarband", new Color(0.7f, 0.5f, 0.2f), 0.6f, 0.4f);
		if (FixtureModels.Have("fx_cigar_box", "fx_cigar_lid"))
		{
			FurnitureKit.Add(k, "fx_cigar_box", Transform3D.Identity, new System.Collections.Generic.Dictionary<string, Material>() { ["wood"] = FixtureModels.Tint(BuildingTextures.BoardsMat, new Color(0.4f, 0.26f, 0.14f)), ["band"] = band });
			FurnitureKit.Add(lk, "fx_cigar_lid", Transform3D.Identity, new System.Collections.Generic.Dictionary<string, Material>() { ["wood"] = FixtureModels.Tint(BuildingTextures.BoardsMat, new Color(0.42f, 0.27f, 0.15f)) });
		}
		else
		{
			k.Mat(BuildingTextures.BoardsMat);
			k.Color = new Color(0.4f, 0.26f, 0.14f);
			BuildKit.Box(k, new Vector3(0, hh, 0), new Vector3(hw * 2f, hh * 2f, hd * 2f), 3f, BuildKit.Face.PY);
			// a paper band round it, gold and red, the brand long gone
			k.Mat(band);
			k.Color = Colors.White;
			BuildKit.Box(k, new Vector3(0, hh * 1.2f, hd + 0.002f), new Vector3(hw * 1.2f, hh * 0.5f, 0.004f));
			lk.Mat(BuildingTextures.BoardsMat);
			lk.Color = new Color(0.42f, 0.27f, 0.15f);
			BuildKit.Box(lk, new Vector3(0, 0, hd), new Vector3(hw * 2f + 0.01f, 0.015f, hd * 2f + 0.01f), 3f);
			lk.Color = Colors.White;
		}
		k.CommitTo(_box, "BoxMesh", true);
		lk.CommitTo(_lid, "LidMesh", true);

		var tapeMat = BuildingTextures.Plain("s_tape2", new Color(0.62f, 0.6f, 0.56f), 0.6f);
		_tapes = new MeshInstance3D[3];
		void Tape(int i, Vector3 pos, Vector3 size)
		{
			var tk = new MeshKit();
			tk.Mat(tapeMat);
			tk.Color = Colors.White;
			tk.Box(pos, size, 3f);
			_tapes[i] = tk.CommitTo(_box, $"Tape{i}", false);
		}
		// (their tops 4 mm over the lid's, not in its plane: they z-fought; the scan's clip audit, 2026-10-15)
		Tape(0, new Vector3(-hw - 0.004f, hh * 1.4f + 0.002f, 0), new Vector3(0.012f, hh * 1.4f + 0.004f, hd * 1.9f));
		Tape(1, new Vector3(hw + 0.004f, hh * 1.4f + 0.002f, 0), new Vector3(0.012f, hh * 1.4f + 0.004f, hd * 1.9f));
		Tape(2, new Vector3(0, hh * 1.4f + 0.002f, hd + 0.006f), new Vector3(hw * 1.9f, hh * 1.4f + 0.004f, 0.012f));

		var body = new StaticBody3D { Name = "BoxBody", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, hh, 0), Shape = new BoxShape3D { Size = new Vector3(hw * 2f, hh * 2f, hd * 2f) } });
		_box.AddChild(body);

		_boxUse = new PickupInteractable
		{
			Name = "Use", Prompt = "A cigar box, taped shut", PickRadius = 0.45f, MaxDistance = 2.2f, Position = new Vector3(0, hh, 0),
			PromptFor = p => p?.Inventory is { } inv && inv.HasTool(ToolKind.Knife) ? "Cut the tape" : "A cigar box, taped shut",
		};
		_boxUse.Interacted += OnBoxUsed;
		_box.AddChild(_boxUse);

		Transform3D Look(Vector3 focusLocal, Vector3 eyeOffsetLocal)
		{
			Vector3 focus = _box.ToGlobal(focusLocal), eye = _box.ToGlobal(focusLocal + eyeOffsetLocal);
			return new Transform3D(Basis.LookingAt(focus - eye, Vector3.Up), eye);
		}
		_leftCam = Look(new Vector3(-hw, hh, 0), new Vector3(-0.38f, 0.18f, 0.12f));
		_rightCam = Look(new Vector3(hw, hh, 0), new Vector3(0.38f, 0.18f, 0.12f));
		_frontCam = Look(new Vector3(0, hh, hd), new Vector3(0, 0.22f, 0.45f));
	}

	private void OnBoxUsed(PlayerController player)
	{
		if (BoxOpen || TapeCutOverlay.Instance == null) return;
		if (player?.Inventory is not { } inv || !inv.HasTool(ToolKind.Knife)) return;
		var cuts = new List<TapeCutOverlay.CutSpec>
		{
			new() { CameraView = _leftCam, Direction = Vector2.Down, PixelsNeeded = 220f,
				OnProgress = p => { if (IsInstanceValid(_tapes[0])) _tapes[0].Scale = new Vector3(1f, 1f, 1f - p); }, OnCut = () => CutSide(0) },
			new() { CameraView = _rightCam, Direction = Vector2.Down, PixelsNeeded = 220f,
				OnProgress = p => { if (IsInstanceValid(_tapes[1])) _tapes[1].Scale = new Vector3(1f, 1f, 1f - p); }, OnCut = () => CutSide(1) },
			new() { CameraView = _frontCam, Direction = Vector2.Right, PixelsNeeded = 220f,
				OnProgress = p => { if (IsInstanceValid(_tapes[2])) _tapes[2].Scale = new Vector3(1f - p, 1f, 1f); }, OnCut = () => CutSide(2) },
		};
		TapeCutOverlay.Instance.Open(player, cuts, OpenBox);
	}

	private void CutSide(int i)
	{
		BoxCutsDone++;
		if (IsInstanceValid(_tapes[i])) _tapes[i].Visible = false;
	}

	private void OpenBox()
	{
		BoxOpen = true;
		_boxUse.Enabled = false;
		var tween = CreateTween();
		tween.TweenProperty(_lid, "rotation:x", Mathf.DegToRad(-100f), 0.9f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		PlayOneShot("res://assets/audio/sfx/door_creak_01.wav", 0f, 1.8f);

		_button = new Node3D { Name = "Button", Position = new Vector3(0, 0.06f, 0) };
		_box.AddChild(_button);
		var bk = new MeshKit();
		bk.Mat(StationTextures.Flat("st_velvet", new Color(0.25f, 0.03f, 0.05f), 1f, 0.05f));
		bk.Color = Colors.White;
		BuildKit.Box(bk, new Vector3(0, -0.02f, 0), new Vector3(0.4f, 0.02f, 0.24f));
		bk.Mat(BuildingTextures.IronMat);
		bk.Color = new Color(0.25f, 0.24f, 0.22f);
		bk.Cylinder(new Vector3(0, -0.01f, 0), new Vector3(0, 0.012f, 0), 0.05f, 0.05f, 12, true);
		bk.Mat(StationTextures.Glow("st_button_red", new Color(0.6f, 0.04f, 0.03f), 0.4f));
		bk.Color = Colors.White;
		bk.Cylinder(new Vector3(0, 0.012f, 0), new Vector3(0, 0.03f, 0), 0.036f, 0.036f, 12, true);
		bk.CommitTo(_button, "ButtonMesh", true);
		_buttonUse = new Interactable { Name = "Press", Prompt = "Press the button", PickRadius = 0.3f, MaxDistance = 2f, Position = new Vector3(0, 0.03f, 0) };
		_buttonUse.Interacted += OnButtonPressed;
		_button.AddChild(_buttonUse);
		GD.Print("[story] Act 13, room 1: the box is open on a button");
	}

	private void OnButtonPressed(PlayerController player)
	{
		if (Solved) return;
		Solved = true;
		_buttonUse.Enabled = false;
		_button.Position = _button.Position with { Y = _button.Position.Y - 0.012f };
		PlayOneShot("res://assets/audio/sfx/radio_tick_01.wav", 2f, 0.6f);
		MeltWalls();
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationRoom1Solved);
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act13Room1Solved);
		GD.Print("[story] Act 13, room 1: solved - the writing melts");
	}

	/// <summary>The writing drips off the walls (the owner): every run of blood lets go and races down to the
	/// floor, the words sag, stretch and slide down after them and thin to nothing, and the blood pools
	/// wide at each wall's foot. The runs stay as stains; a faint ghost of the words stays on each wall.</summary>
	private void MeltWalls()
	{
		// the blood letting go and sheeting down (2026-10-15: the blood here was the radio's squelch, which is static)
		PlayOneShot(Audio.AudioDirector.Take("blood_slide", 4), 0f, (float)GD.RandRange(0.9, 1.0));
		var rng = new RandomNumberGenerator { Seed = 1333 };
		for (int i = 0; i < 3; i++)
		{
			var wall = _writing[i];
			var puddle = _puddleDecals[i];
			float delay = i * 0.5f;
			var (foot, n) = Walls[i];
			PlayOneShotAt("res://assets/audio/sfx/blood_drain_01.wav", foot + n * 0.3f + Vector3.Up * 1.2f, -3f, 0.8f + i * 0.08f, delay + 0.3f);
			var tw = CreateTween().SetParallel();
			// each run lets go and creeps on down the wall, its bead at its foot, slowing as its paint gives out
			foreach (var d in _drips[i])
			{
				var drip = d;
				float from = drip.Len, to = drip.Final;
				float start = delay + rng.RandfRange(0f, 1.8f), time = rng.RandfRange(1.6f, 3.6f) * Mathf.Sqrt(Mathf.Max(to - from, 0.1f) / 2f);
				tw.TweenMethod(Callable.From<float>(drip.Set), from, to, time).SetDelay(start).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			}
			// the words sag and slide down after them, stretching, and thin away
			tw.TweenProperty(wall, "position:y", -0.9f, 5.5f).SetDelay(delay + 0.6f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			tw.TweenProperty(wall, "scale", new Vector3(1.03f, 1.5f, 1f), 5.5f).SetDelay(delay + 0.6f);
			foreach (var l in _lines[i])
				tw.TweenProperty(l, "modulate:a", 0f, 5.5f).SetDelay(delay + 0.6f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.In);
			puddle.Visible = true;
			puddle.Scale = Vector3.One * 0.05f;
			tw.TweenProperty(puddle, "scale", Vector3.One, 4.5f).SetDelay(delay + 1.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			var ghost = _ghosts[i];
			tw.TweenCallback(Callable.From(() => { ghost.Visible = true; wall.Visible = false; })).SetDelay(delay + 6.2f);
		}
	}

	// ------------------------------------------------------------------ the puddles (the iron door's TOUCH)

	private void BuildPuddles()
	{
		var rng = new RandomNumberGenerator { Seed = 1332 };
		for (int i = 0; i < 3; i++)
		{
			var (foot, n) = Walls[i];
			// wide pools along each wall's foot (the owner: larger), a few smaller splashes round them
			var root = new Node3D { Name = $"Puddle{i}", Position = foot + n * 0.72f, Visible = false };
			AddChild(root);
			float along = i == 1 ? Mathf.Pi * 0.5f : 0f;
			StationProps.Decal(root, StationTextures.BloodPoolMat, new Vector3(0, 0.008f, 0), Vector3.Up, new Vector2(rng.RandfRange(3.2f, 3.8f), 1.45f), along + rng.RandfRange(-0.15f, 0.15f));
			Vector3 right = Vector3.Up.Cross(n).Normalized();
			for (int b = 0; b < 4; b++)
			{
				Vector3 off = right * rng.RandfRange(-2.0f, 2.0f) + n * rng.RandfRange(-0.3f, 0.9f);
				StationProps.Decal(root, StationTextures.BloodMat, off + new Vector3(0, 0.009f + b * 0.0008f, 0), Vector3.Up, Vector2.One * rng.RandfRange(0.45f, 0.95f), rng.RandfRange(0, 3f));
			}
			_puddleDecals.Add(root);
			int which = i;
			var reach = new Interactable
			{
				Name = "Reach", Prompt = "Reach into the blood", HoldSeconds = 1.2f, PickRadius = 0.45f, MaxDistance = 2.4f,
				Position = foot + n * 0.55f + Vector3.Up * 0.05f, Enabled = false,
			};
			reach.Interacted += p => OnReach(p, which);
			AddChild(reach);
			Puddles[i] = reach;
		}
	}

	private void OnReach(PlayerController player, int which)
	{
		var s = StoryManager.Instance;
		if (s == null || s.HasFlag(StoryManager.Flag.StationHandTaken)) return;
		if (which == 1)
		{
			// DO NOT TOUCH THEM: here it is
			PlayOneShot(Audio.AudioDirector.Take("blood_plunge", 6), 0f, (float)GD.RandRange(0.92, 1.02));
			player?.Inventory?.TryPickup(ToolKind.PaleHand);
			s.SetFlag(StoryManager.Flag.StationHandTaken);
			_ = StoryBeat.Caption(this, "A hand. Cold. Its fingers close round yours.", 0.4f, 2.6f, 1f);
			GD.Print("[story] Act 13: the pale hand is taken from the blood");
			return;
		}
		WrongReaches++;
		_ = Cutscene.Run(this, async ct =>
		{
			PlayOneShot(Audio.AudioDirector.Take("blood_plunge", 6), 1f, (float)GD.RandRange(0.85, 0.95));
			PlayOneShot($"res://assets/audio/sfx/whisper_voice_0{1 + WrongReaches % 3}.wav", -2f, 0.8f);
			var rig = player.CameraRig;
			float from = rig.Pitch;
			// yanked down toward it, held, let go
			double t = 0;
			while (t < 0.9)
			{
				await Cutscene.Frame(this, ct);
				t += GetProcessDeltaTime();
				float u = (float)(t / 0.9);
				rig.SetPitch(Mathf.Lerp(from, Mathf.DegToRad(-70f), Mathf.Sin(Mathf.Min(1f, u * 2.2f) * Mathf.Pi * 0.5f)));
				rig.Shake = new Vector3(Mathf.Sin(u * 40f), Mathf.Cos(u * 33f), 0) * 0.015f * (1f - u);
			}
			rig.Shake = Vector3.Zero;
			await StoryBeat.Caption(this, "Something in it takes your wrist - and lets go.", 0.3f, 1.8f, 0.8f, ct);
		}, lockInput: true);
	}

	private void PlayOneShotAt(string path, Vector3 local, float volumeDb, float pitch, float delay)
	{
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = volumeDb, PitchScale = pitch, UnitSize = 2f, MaxDistance = 15f, Position = local };
		AddChild(s);
		s.Finished += s.QueueFree;
		GetTree().CreateTimer(delay).Timeout += () => { if (IsInstanceValid(s)) s.Play(); };
	}

	private void PlayOneShot(string path, float volumeDb, float pitch)
	{
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = volumeDb, PitchScale = pitch, UnitSize = 2f, MaxDistance = 15f };
		AddChild(s);
		s.Finished += s.QueueFree;
		s.Play();
	}
}
