using Godot;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// Act 5, inside the cabin: what the friend left at his table (he is not there; STORY.md:
/// "his chair pushed back from the table, a roll of bandage and dark stains ... his last page
/// on the table. On the table in front of his chair is a newel post bulb"). Builds, PS2 budget:
/// his chair, pushed back and turned a little as if he got up in a hurry; the table; a roll of
/// bandage and a few loose strips (one on the floor); dark dried stains on the tabletop, down
/// the table edge and on the floorboards; and his page (P4). The newel post pickup lives under
/// the "Table" node (friend.tscn) in front of the chair.
///
/// The class keeps its old name (scenes and tools refer to it); it no longer builds a body.
///
/// Frame: the chair's home spot is the origin, the table is in front of it (+Z). Geometry is
/// generated into the nodes at <see cref="ChairPath"/> / <see cref="TablePath"/> and here.
/// Collision: one box for the table, one for the chair, on layer 1.
/// </summary>
[Tool]
public partial class FriendBody : Node3D
{
	[Export] public NodePath ChairPath = "../Chair";
	[Export] public NodePath TablePath = "../Table";
	[Export] public bool BuildCollision = true;
	/// <summary>Where the chair ended up (friend space): pushed back from the table and turned.</summary>
	[Export] public Vector3 ChairPushedTo = new(0.08f, 0f, -0.34f);
	[Export] public float ChairTurnDegrees = 17f;

	public int TriangleCount { get; private set; }

	/// <summary>Table top height (friend space), for anything placed on it.</summary>
	public const float TableTop = 0.65f;

	/// <summary>The friend's last page (P4) lies on the table from Act 5's checkpoint on, like the newel post.</summary>
	[Export] public Checkpoint PageCheckpoint = Checkpoint.None;   // on the table from load (Dan, 2026-09-22: nothing pops in on entry)
	/// <summary>The page's Readable (null in the editor), for tests and previews.</summary>
	public Readable Page { get; private set; }

	private const string PageText =
		"It came away in my hand at the top like it wanted to. The cap off the post.\n" +
		"The hand hasn't been mine since. I kept the rest of me.\n" +
		"The door won't open from in here. I didn't do that.\n" +
		"Don't take it back up. Don't take it anywhere.\n" +
		"If you get in and I'm gone, I went up.\n" +
		"R.H.";

	private Node3D _page;
	private bool _pageShown;

	public override void _Ready()
	{
		Build();
		if (!Engine.IsEditorHint() && StoryManager.Instance is { } s) s.CheckpointReached += OnCheckpoint;
	}

	public override void _ExitTree()
	{
		if (StoryManager.Instance is { } s) s.CheckpointReached -= OnCheckpoint;
	}

	private void OnCheckpoint(Checkpoint _) => UpdatePage();

	/// <summary>Shows the page now regardless of checkpoint (dev previews only; the story reveals it normally).</summary>
	public void RevealPage() { _pageShown = true; UpdatePage(); }

	private void UpdatePage()
	{
		if (_page == null || !IsInstanceValid(_page)) return;
		bool on = _pageShown || PageCheckpoint == Checkpoint.None
			|| (StoryManager.Instance != null && StoryManager.Instance.Current >= PageCheckpoint);
		_page.Visible = on;
		if (Page != null) Page.Enabled = on;
	}

	/// <summary>
	/// The page lies at the table's right-hand corner (from the chair), its near edge in the dried
	/// stain. Its pick sphere is small (0.2 m, just above the sheet) and well clear of the newel
	/// post's (0.3 m at (-0.1, 0.8, 0.55)), so the post is what the crosshair finds whenever both
	/// are under it.
	/// </summary>
	private void BuildPage(Node3D table)
	{
		Page = null;
		_page = null;
		var old = table.GetNodeOrNull("Page");
		if (old != null) { table.RemoveChild(old); old.QueueFree(); }
		if (Engine.IsEditorHint()) return;
		// Built detached so the Readable reads the pick radius/offset below when it enters the tree.
		var root = new Node3D { Name = "Page" };
		root.AddToGroup(Cabin.PapersGroup);
		Page = PaperKit.Flat(root, new Vector3(0.40f, TableTop, 0.21f), 18f, new Vector2(0.14f, 0.18f), PaperKit.Look.Note,
			"", PageText, Readable.NoteStyle.Handwritten, prompt: "Read the page", seed: 9);
		Page.ReadFlag = "read_friend_page";
		Page.PickRadius = 0.2f;
		Page.PickOffset = new Vector3(0, 0, 0.05f);   // the sheet's +Z is up: the sphere sits just above the table
		table.AddChild(root);
		_page = root;
		UpdatePage();
	}

	// ───────────────────────────── materials ─────────────────────────────

	private static Texture2D Gauze() => ItemTextures.Make("fr_gauze", 16, 16, (x, y) =>
	{
		bool thread = x % 3 == 0 || y % 3 == 0;
		float n = ItemTextures.Fbm(x, y, 16, 16, 2, 2, 2, 331);
		float v = (thread ? 0.92f : 0.78f) + (n - 0.5f) * 0.2f;
		return new Color(v, v, v);
	});

	private static StandardMaterial3D GauzeMat => ItemTextures.Std("fr_gauze", Gauze(), 0.9f, 0.2f);
	/// <summary>Old, dried blood: near black-brown, a faint sheen, never bright red.</summary>
	private static StandardMaterial3D StainMat => (StandardMaterial3D)ItemTextures.Cached("fr_stain", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(0.09f, 0.03f, 0.025f),
		Roughness = 0.6f,
		MetallicSpecular = 0.4f,
	});

	/// <summary>MeshKit vertex colours are linear; author in sRGB and convert.</summary>
	private static Color S(float r, float g, float b) => new Color(r, g, b).SrgbToLinear();

	// ───────────────────────────── build ─────────────────────────────

	public void Build()
	{
		foreach (var c in GetChildren())
			if (c.HasMeta("friend_generated")) { RemoveChild(c); c.QueueFree(); }
		TriangleCount = 0;

		var chair = GetNodeOrNull<Node3D>(ChairPath);
		if (chair != null)
		{
			chair.Position = ChairPushedTo;
			chair.Rotation = new Vector3(0, Mathf.DegToRad(ChairTurnDegrees), 0);
			TriangleCount += BuildChair(chair);
		}
		var table = GetNodeOrNull<Node3D>(TablePath);
		if (table != null)
		{
			TriangleCount += BuildTable(table);
			BuildPage(table);
		}

		// On the floor (friend space): a strip of bandage dropped by the chair and the stains under where he sat.
		var k = new MeshKit();
		BuildFloor(k);
		var floor = k.CommitTo(this, "LeftBehind", false);
		floor.SetMeta("friend_generated", true);
		TriangleCount += ItemMeshes.CountTriangles(floor.Mesh);

		if (BuildCollision && !Engine.IsEditorHint())
		{
			var body = new StaticBody3D { Name = "Collision", CollisionLayer = 1, CollisionMask = 0 };
			body.SetMeta("friend_generated", true);
			body.SetMeta("surface", "wood");
			AddChild(body);
			if (chair != null)
				body.AddChild(new CollisionShape3D { Position = ChairPushedTo + new Vector3(0, 0.48f, -0.03f), Rotation = chair.Rotation, Shape = new BoxShape3D { Size = new Vector3(0.46f, 0.96f, 0.46f) } });
			if (table != null)
			{
				Vector3 tp = table.Position;
				body.AddChild(new CollisionShape3D { Position = tp + new Vector3(0, TableTop * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(1.1f, TableTop, 0.7f) } });
			}
		}
	}

	/// <summary>An irregular flat stain (friend or table space) a hair above the surface.</summary>
	private static void Stain(MeshKit k, Vector3 c, float r, float stretch, int seed, float yaw = 0f)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)seed };
		const int n = 10;
		var ring = new Vector3[n];
		var rot = new Basis(Vector3.Up, yaw);
		for (int i = 0; i < n; i++)
		{
			float a = Mathf.Tau * i / n;
			float rr = r * rng.RandfRange(0.6f, 1.05f);
			ring[i] = c + rot * new Vector3(Mathf.Cos(a) * rr * stretch, 0, Mathf.Sin(a) * rr);
		}
		for (int i = 0; i < n; i++)
			k.Tri(c, ring[i], ring[(i + 1) % n], Vector3.Up, new Vector2(0.5f, 0.5f), Vector2.Zero, Vector2.Right);
	}

	private static void BuildFloor(MeshKit k)
	{
		k.Mat(StainMat);
		k.Color = Colors.White;
		// under the table's edge in front of the chair, where it ran off, and a few drips toward the door
		Stain(k, new Vector3(0.22f, 0.004f, 0.36f), 0.13f, 1.3f, 7101, 0.3f);
		Stain(k, new Vector3(0.1f, 0.004f, 0.12f), 0.07f, 1.1f, 7102);
		Stain(k, new Vector3(0.34f, 0.004f, 1.45f), 0.03f, 1f, 7103);
		Stain(k, new Vector3(0.28f, 0.004f, 1.8f), 0.025f, 1f, 7104);
		Stain(k, new Vector3(0.36f, 0.004f, 2.2f), 0.02f, 1f, 7105);
		// a loose strip of bandage on the floor beside the chair, dark at one end
		k.Mat(GauzeMat);
		k.Color = S(0.66f, 0.63f, 0.57f);
		ItemMeshes.Ribbon(k, new[]
		{
			new Vector3(0.38f, 0.006f, -0.1f), new Vector3(0.45f, 0.007f, 0.02f),
			new Vector3(0.43f, 0.008f, 0.16f), new Vector3(0.5f, 0.007f, 0.27f),
		}, 0.045f, Vector3.Up, true, 6f);
		k.Color = S(0.17f, 0.055f, 0.045f);
		ItemMeshes.Ribbon(k, new[] { new Vector3(0.5f, 0.0075f, 0.27f), new Vector3(0.56f, 0.007f, 0.34f) }, 0.045f, Vector3.Up, true, 6f);
	}

	// ───────────────────────────── furniture ─────────────────────────────

	private static int BuildChair(Node3D chair)
	{
		var old = chair.GetNodeOrNull("Generated");
		if (old != null) { chair.RemoveChild(old); old.QueueFree(); }
		var k = new MeshKit();
		k.Mat(ProcTextures.WoodMat);
		k.Color = new Color(0.4f, 0.31f, 0.22f);
		k.Box(new Vector3(0, 0.47f, 0), new Vector3(0.44f, 0.04f, 0.42f), 1.5f);
		foreach (float x in new[] { -1f, 1f })
			foreach (float z in new[] { -1f, 1f })
				k.Cylinder(new Vector3(x * 0.19f, 0f, z * 0.18f), new Vector3(x * 0.18f, 0.45f, z * 0.17f), 0.019f, 0.022f, 5, false, 2f);
		// back posts leaning back, top rail, three spindles
		foreach (float x in new[] { -1f, 1f })
			k.Cylinder(new Vector3(x * 0.19f, 0.45f, -0.19f), new Vector3(x * 0.2f, 0.97f, -0.25f), 0.021f, 0.018f, 5, true, 2f);
		k.Box(new Vector3(0, 0.92f, -0.243f), new Vector3(0.42f, 0.08f, 0.03f), 1.5f, new Basis(Vector3.Right, -0.11f));
		for (int i = -1; i <= 1; i++)
			k.Cylinder(new Vector3(i * 0.08f, 0.49f, -0.2f), new Vector3(i * 0.085f, 0.88f, -0.238f), 0.011f, 0.011f, 4, false, 2f);
		// stretchers
		k.Box(new Vector3(0, 0.16f, 0.175f), new Vector3(0.36f, 0.025f, 0.02f), 1.5f);
		k.Box(new Vector3(0, 0.16f, -0.18f), new Vector3(0.36f, 0.025f, 0.02f), 1.5f);
		// dark smears on the seat's front edge and down one leg
		k.Mat(StainMat);
		k.Color = Colors.White;
		Stain(k, new Vector3(0.1f, 0.492f, 0.12f), 0.06f, 1.4f, 7111, 0.2f);
		k.Box(new Vector3(0.19f, 0.3f, 0.195f), new Vector3(0.012f, 0.16f, 0.006f), 1f);
		var gen = new Node3D { Name = "Generated" };
		chair.AddChild(gen);
		var mi = k.CommitTo(gen, "ChairMesh");
		return ItemMeshes.CountTriangles(mi.Mesh);
	}

	private static int BuildTable(Node3D table)
	{
		var old = table.GetNodeOrNull("Generated");
		if (old != null) { table.RemoveChild(old); old.QueueFree(); }
		var k = new MeshKit();
		k.Mat(ProcTextures.WoodMat);
		k.Color = new Color(0.5f, 0.39f, 0.27f);
		k.Box(new Vector3(0, TableTop - 0.03f, 0), new Vector3(1.1f, 0.06f, 0.7f), 1.2f);
		k.Color = new Color(0.38f, 0.29f, 0.2f);
		k.Box(new Vector3(0, TableTop - 0.1f, 0.3f), new Vector3(0.94f, 0.08f, 0.025f), 1.2f);
		k.Box(new Vector3(0, TableTop - 0.1f, -0.3f), new Vector3(0.94f, 0.08f, 0.025f), 1.2f);
		k.Box(new Vector3(0.46f, TableTop - 0.1f, 0), new Vector3(0.025f, 0.08f, 0.56f), 1.2f);
		k.Box(new Vector3(-0.46f, TableTop - 0.1f, 0), new Vector3(0.025f, 0.08f, 0.56f), 1.2f);
		foreach (float x in new[] { -1f, 1f })
			foreach (float z in new[] { -1f, 1f })
				k.Beam(new Vector3(x * 0.48f, 0f, z * 0.28f), new Vector3(x * 0.48f, TableTop - 0.06f, z * 0.28f), 0.05f, 0.05f, 1.2f, Vector3.Forward);

		// The dried stain where his left arm lay (table space), and a run of it over the near edge.
		k.Mat(StainMat);
		k.Color = Colors.White;
		float y = TableTop + 0.0015f;
		Stain(k, new Vector3(0.265f, y, 0.02f), 0.09f, 1.25f, 4417);
		Stain(k, new Vector3(0.2f, y, -0.2f), 0.045f, 1.3f, 4418, 0.5f);
		k.Box(new Vector3(0.23f, TableTop - 0.05f, -0.352f), new Vector3(0.05f, 0.09f, 0.004f), 1f);

		// A roll of bandage, half unwound, and loose strips cut from it.
		k.Mat(GauzeMat);
		k.Color = S(0.78f, 0.76f, 0.7f);
		Vector3 roll = new(-0.36f, TableTop + 0.034f, 0.12f);
		k.Cylinder(roll + new Vector3(-0.026f, 0, 0), roll + new Vector3(0.026f, 0, 0), 0.034f, 0.034f, 8, true, 5f);
		ItemMeshes.Ribbon(k, new[]
		{
			roll + new Vector3(0, -0.03f, 0.03f), new Vector3(-0.35f, TableTop + 0.002f, 0.2f),
			new Vector3(-0.3f, TableTop + 0.002f, 0.27f), new Vector3(-0.22f, TableTop + 0.002f, 0.29f),
		}, 0.05f, Vector3.Up, true, 6f);
		k.Color = S(0.62f, 0.5f, 0.45f);
		ItemMeshes.Ribbon(k, new[]
		{
			new Vector3(0.12f, TableTop + 0.002f, -0.05f), new Vector3(0.19f, TableTop + 0.003f, 0.0f),
			new Vector3(0.25f, TableTop + 0.003f, -0.02f), new Vector3(0.31f, TableTop + 0.002f, 0.04f),
		}, 0.045f, Vector3.Up, true, 6f);
		k.Color = S(0.17f, 0.055f, 0.045f);
		ItemMeshes.Ribbon(k, new[]
		{
			new Vector3(0.3f, TableTop + 0.003f, -0.16f), new Vector3(0.36f, TableTop + 0.003f, -0.12f),
			new Vector3(0.39f, TableTop + 0.002f, -0.06f),
		}, 0.04f, Vector3.Up, true, 6f);

		var gen = new Node3D { Name = "Generated" };
		table.AddChild(gen);
		var mi = k.CommitTo(gen, "TableMesh");
		return ItemMeshes.CountTriangles(mi.Mesh);
	}
}
