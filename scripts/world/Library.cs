using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
using ProjectDS.World.LibraryParts;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// Act 19: the library, through the door from the pit (its save is <see cref="Checkpoint.Act18Finished"/>,
/// on walking in). After everything, the most ordinary room in the game: a quaint, properly packed
/// library, every wall shelved to the ceiling with hardbacks, a fire in the grate, lamps lit, a clock
/// ticking, armchairs, a rug. Nothing happens here that isn't gentle.
///
/// A sheet covers a side table by the west shelves, something square under it. Pulling it off shows a
/// bamboo puzzle box and five loose pieces (<see cref="PuzzleBox"/>, played in <see cref="PuzzleOverlay"/>).
/// Solved, the box glows once, softly, and dissolves out of the world, leaving a silk bookmark. On the
/// back wall one hardback sticks out a little: the bookmark slides into it, the book goes in, and the
/// bookcase swings open on a stone passage (to Act 20's round room, <see cref="RoundRoom"/>).
///
/// Local space: y=0 is the floor; the door from the pit is at z=0, x=0; the room runs +Z to the back
/// wall at <see cref="Depth"/>.
/// </summary>
public partial class Library : Node3D
{
	public const float HalfW = 5.5f, Depth = 13f, Height = 3.8f;
	public static readonly Vector3 TableAt = new(-4.72f, 0, 6.5f);
	/// <summary>Where the passage behind the bookcase leads (this node's space): the round room's middle.</summary>
	public const float PassageLen = 3.2f;
	public static readonly Vector3 RoundRoomAt = new(0, 0, Depth + PassageLen + RoundRoom.Radius);

	public bool SheetOff { get; private set; }
	public bool PuzzleSolved { get; private set; }
	public bool BookcaseOpen { get; private set; }
	public PuzzleBox Puzzle { get; private set; }
	public Interactable SheetUse { get; private set; }
	public Interactable BoxUse { get; private set; }
	public PickupInteractable BookUse { get; private set; }
	public Pickup Bookmark { get; private set; }
	public RoundRoom Round { get; private set; }
	public Vector3 TableWorld => ToGlobal(TableAt + new Vector3(0.75f, 0.05f, 0));
	public Vector3 BookcaseFrontWorld => ToGlobal(new Vector3(0, 0.05f, Depth - 1.3f));
	public Vector3 PassageWorld => ToGlobal(new Vector3(0, 0.05f, Depth + PassageLen * 0.5f));

	private StaticBody3D _body;
	private Node3D _sheet, _bookcase, _jutting;
	private OmniLight3D _glow;
	private readonly RandomNumberGenerator _rng = new() { Seed = 1919 };
	private StandardMaterial3D _wood;

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		_body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		_body.SetMeta("surface", "wood");
		AddChild(_body);
		_wood = new StandardMaterial3D { AlbedoTexture = PropTextures.DeckMat.AlbedoTexture, AlbedoColor = new Color(0.36f, 0.22f, 0.14f), Roughness = 0.55f, MetallicSpecular = 0.4f, VertexColorUseAsAlbedo = true, Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 1.3f };
		BuildShell();
		BuildShelves();
		BuildSecretBookcase();
		BuildFireplace();
		BuildFurniture();
		BuildSideTable();
		BuildPassage();
		Round = new RoundRoom { Name = "RoundRoom", Position = RoundRoomAt };
		AddChild(Round);
		var s = StoryManager.Instance;
		if (s != null && s.Current >= Checkpoint.Act19Finished) { SheetOff = PuzzleSolved = true; _sheet.Visible = false; Puzzle.Visible = false; SetBookcaseOpen(true, instant: true); }
		SetProcess(true);
		GD.Print("[story] Act 19: the library");
	}

	private void Slab(MeshKit k, Vector3 c, Vector3 s, bool collide = true, float tint = 1f, Color? color = null)
	{
		k.Color = color ?? Colors.White * tint;
		BuildKit.Box(k, c, s, 1f);
		if (collide) _body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
	}

	// ------------------------------------------------------------------ the room

	private void BuildShell()
	{
		var plaster = new MeshKit();
		plaster.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.8f, 0.7f), Roughness = 0.9f, VertexColorUseAsAlbedo = true });
		Slab(plaster, new Vector3(-HalfW - 0.15f, Height * 0.5f, Depth * 0.5f), new Vector3(0.3f, Height, Depth + 0.3f));
		Slab(plaster, new Vector3(HalfW + 0.15f, Height * 0.5f, Depth * 0.5f), new Vector3(0.3f, Height, Depth + 0.3f));
		// the back wall, with the gap the bookcase hides
		Slab(plaster, new Vector3(-(HalfW + 0.62f) * 0.5f, Height * 0.5f, Depth + 0.15f), new Vector3(HalfW - 0.62f, Height, 0.3f));
		Slab(plaster, new Vector3((HalfW + 0.62f) * 0.5f, Height * 0.5f, Depth + 0.15f), new Vector3(HalfW - 0.62f, Height, 0.3f));
		Slab(plaster, new Vector3(0, (2.4f + Height) * 0.5f, Depth + 0.15f), new Vector3(1.24f, Height - 2.4f, 0.3f));
		// the front wall, round the door from the pit
		Slab(plaster, new Vector3(-(HalfW + 0.65f) * 0.5f, Height * 0.5f, -0.15f), new Vector3(HalfW - 0.65f, Height, 0.3f));
		Slab(plaster, new Vector3((HalfW + 0.65f) * 0.5f, Height * 0.5f, -0.15f), new Vector3(HalfW - 0.65f, Height, 0.3f));
		Slab(plaster, new Vector3(0, (2.4f + Height) * 0.5f, -0.15f), new Vector3(1.3f, Height - 2.4f, 0.3f));
		Slab(plaster, new Vector3(0, Height + 0.1f, Depth * 0.5f), new Vector3(HalfW * 2f, 0.2f, Depth), false, 0.95f);
		plaster.CommitTo(this, "Plaster", true);
		// a parquet floor, beams across the ceiling
		var floor = new MeshKit();
		floor.Mat(new StandardMaterial3D { AlbedoTexture = PropTextures.DeckMat.AlbedoTexture, AlbedoColor = new Color(0.55f, 0.36f, 0.22f), Roughness = 0.4f, MetallicSpecular = 0.5f, VertexColorUseAsAlbedo = true, Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 1.8f });
		Slab(floor, new Vector3(0, -0.05f, (Depth - 0.8f) * 0.5f), new Vector3(HalfW * 2f, 0.1f, Depth + 0.8f));
		floor.CommitTo(this, "Floor", true);
		var beams = new MeshKit();
		beams.Mat(_wood);
		for (float z = 1.2f; z < Depth; z += 2.2f) Slab(beams, new Vector3(0, Height - 0.12f, z), new Vector3(HalfW * 2f, 0.24f, 0.22f), false);
		beams.CommitTo(this, "Beams", true);
		// a big rug
		var rug = new MeshKit();
		rug.Mat(new StandardMaterial3D { AlbedoColor = Colors.White, Roughness = 1f, VertexColorUseAsAlbedo = true });
		rug.Color = new Color(0.42f, 0.1f, 0.09f);
		BuildKit.Box(rug, new Vector3(0.6f, 0.006f, 6.5f), new Vector3(5.2f, 0.012f, 7f));
		rug.Color = new Color(0.62f, 0.48f, 0.22f);
		BuildKit.Box(rug, new Vector3(0.6f, 0.008f, 6.5f), new Vector3(4.6f, 0.012f, 6.4f));
		rug.Color = new Color(0.2f, 0.14f, 0.28f);
		BuildKit.Box(rug, new Vector3(0.6f, 0.01f, 6.5f), new Vector3(4.2f, 0.012f, 6.0f));
		rug.CommitTo(this, "Rug", false);
	}

	/// <summary>Bookcases on every wall, floor to ceiling, packed with hardbacks.</summary>
	private void BuildShelves()
	{
		var runs = new List<(Vector3 a, Vector3 b, Vector3 inward)>
		{
			(new Vector3(-HalfW, 0, 0.3f), new Vector3(-HalfW, 0, Depth - 0.3f), Vector3.Right),
			(new Vector3(HalfW, 0, 0.3f), new Vector3(HalfW, 0, 5.4f), Vector3.Left),
			(new Vector3(HalfW, 0, 7.6f), new Vector3(HalfW, 0, Depth - 0.3f), Vector3.Left),
			(new Vector3(-HalfW + 0.3f, 0, Depth), new Vector3(-0.66f, 0, Depth), Vector3.Forward),
			(new Vector3(0.66f, 0, Depth), new Vector3(HalfW - 0.3f, 0, Depth), Vector3.Forward),
			(new Vector3(-HalfW + 0.3f, 0, 0), new Vector3(-0.95f, 0, 0), Vector3.Back),
			(new Vector3(0.95f, 0, 0), new Vector3(HalfW - 0.3f, 0, 0), Vector3.Back),
		};
		var frame = new MeshKit();
		frame.Mat(_wood);
		var books = new List<Transform3D>();
		var colors = new List<Color>();
		foreach (var (a, b, inw) in runs)
		{
			Shelves(frame, books, colors, a, b, inw, this, _body);
		}
		frame.CommitTo(this, "Bookcases", true);
		AddBooks(this, books, colors, "Books");
	}

	private const float ShelfDepth = 0.34f;
	private static readonly float[] Rows = { 0.08f, 0.52f, 0.96f, 1.4f, 1.84f, 2.28f, 2.72f, 3.16f };

	/// <summary>One run of bookcase from a to b against a wall (inw points into the room), and its books.</summary>
	private void Shelves(MeshKit frame, List<Transform3D> books, List<Color> colors, Vector3 a, Vector3 b, Vector3 inw, Node3D owner, StaticBody3D body)
	{
		Vector3 along = (b - a).Normalized();
		float len = (b - a).Length();
		Vector3 mid = (a + b) * 0.5f + inw * ShelfDepth * 0.5f;
		Vector3 alongSize = along.Abs() * len, depthSize = inw.Abs() * ShelfDepth;
		frame.Color = Colors.White;
		BuildKit.Box(frame, mid + Vector3.Up * (Height * 0.5f - 0.1f) - inw * (ShelfDepth * 0.5f - 0.01f), alongSize + inw.Abs() * 0.02f + Vector3.Up * (Height - 0.2f));   // the back
		foreach (float y in Rows)
			BuildKit.Box(frame, mid + Vector3.Up * (y - 0.012f), alongSize + depthSize + Vector3.Up * 0.024f);
		BuildKit.Box(frame, mid + Vector3.Up * (Height - 0.2f), alongSize + depthSize + inw.Abs() * 0.05f + Vector3.Up * 0.12f);   // the crown
		int uprights = Mathf.Max(1, Mathf.RoundToInt(len / 1.0f));
		for (int u = 0; u <= uprights; u++)
		{
			Vector3 p = a + along * (len * u / uprights) + inw * ShelfDepth * 0.5f + Vector3.Up * ((Height - 0.2f) * 0.5f);
			BuildKit.Box(frame, p, along.Abs() * 0.045f + depthSize + Vector3.Up * (Height - 0.2f));
		}
		body?.AddChild(new CollisionShape3D { Position = mid + Vector3.Up * (Height * 0.5f), Shape = new BoxShape3D { Size = alongSize + depthSize + Vector3.Up * Height } });
		// the books, row by row, packed in, a few leaning, now and then a gap
		for (int r = 0; r < Rows.Length - 1; r++)
		{
			float s = 0.03f;
			while (s < len - 0.05f)
			{
				float t = _rng.RandfRange(0.022f, 0.06f), h = _rng.RandfRange(0.22f, 0.37f), d = _rng.RandfRange(0.16f, 0.25f);
				if (_rng.Randf() < 0.025f) { s += _rng.RandfRange(0.08f, 0.2f); continue; }
				if (s + t > len - 0.03f) break;
				// don't push through an upright
				float seg = len / uprights, inSeg = (s % seg);
				if (inSeg < 0.03f || inSeg > seg - 0.03f - t) { s += 0.035f; continue; }
				float lean = _rng.Randf() < 0.04f ? _rng.RandfRange(0.12f, 0.3f) : 0f;
				Vector3 c = a + along * (s + t * 0.5f) + inw * (0.03f + d * 0.5f) + Vector3.Up * (Rows[r] + h * 0.5f);
				var basis = Basis.LookingAt(inw, Vector3.Up) * new Basis(Vector3.Forward, lean);
				// a unit box scaled to the book: its local X is along the shelf, Z is depth
				var bb = new Basis(basis.X * t, basis.Y * h, basis.Z * d);
				books.Add(new Transform3D(bb, c));
				colors.Add(BookColor());
				s += t + (lean > 0 ? 0.02f : 0.002f);
			}
		}
	}

	private Color BookColor()
	{
		Color[] cloth = { new(0.35f, 0.07f, 0.06f), new(0.1f, 0.18f, 0.1f), new(0.08f, 0.12f, 0.25f), new(0.3f, 0.22f, 0.12f), new(0.45f, 0.36f, 0.22f), new(0.15f, 0.1f, 0.08f), new(0.5f, 0.45f, 0.35f), new(0.22f, 0.05f, 0.1f) };
		return cloth[_rng.RandiRange(0, cloth.Length - 1)] * _rng.RandfRange(0.75f, 1.2f);
	}

	private static void AddBooks(Node3D owner, List<Transform3D> xf, List<Color> colors, string name)
	{
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = Vector3.One }, InstanceCount = xf.Count };
		for (int i = 0; i < xf.Count; i++) { mm.SetInstanceTransform(i, xf[i]); mm.SetInstanceColor(i, colors[i]); }
		owner.AddChild(new MultiMeshInstance3D
		{
			Name = name, Multimesh = mm,
			MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, AlbedoColor = Colors.White, Roughness = 0.75f, MetallicSpecular = 0.3f },
		});
	}

	/// <summary>The bookcase in the middle of the back wall: hinged at its right edge, one book on the
	/// third shelf sticking out a finger's width.</summary>
	private void BuildSecretBookcase()
	{
		_bookcase = new Node3D { Name = "SecretBookcase", Position = new Vector3(0.62f, 0, Depth) };
		AddChild(_bookcase);
		var frame = new MeshKit();
		frame.Mat(_wood);
		var books = new List<Transform3D>();
		var colors = new List<Color>();
		var hinged = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		_bookcase.AddChild(hinged);
		Shelves(frame, books, colors, new Vector3(-1.24f, 0, 0), new Vector3(0f, 0, 0), Vector3.Forward, _bookcase, hinged);
		frame.CommitTo(_bookcase, "Frame", true);
		// leave a slot for the one that sticks out
		for (int i = books.Count - 1; i >= 0; i--)
			if (Mathf.Abs(books[i].Origin.X + 0.55f) < 0.07f && Mathf.Abs(books[i].Origin.Y - (Rows[3] + 0.15f)) < 0.2f) { books.RemoveAt(i); colors.RemoveAt(i); }
		AddBooks(_bookcase, books, colors, "Books");
		// the one sticking out
		_jutting = new Node3D { Name = "JuttingBook", Position = new Vector3(-0.55f, Rows[3] + 0.15f, -(0.03f + 0.11f) - 0.07f) };
		_bookcase.AddChild(_jutting);
		_jutting.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.3f, 0.22f) },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.08f, 0.07f), Roughness = 0.6f },
		});
		_jutting.AddChild(new Label3D
		{
			Text = "S", FontSize = 32, PixelSize = 0.003f, Modulate = new Color(0.85f, 0.7f, 0.3f), OutlineSize = 0, Shaded = true,
			Position = new Vector3(0, 0.05f, -0.112f), Rotation = new Vector3(0, Mathf.Pi, 0),
		});
		BookUse = new PickupInteractable { Name = "Book", PickRadius = 0.2f, MaxDistance = 2.4f, PromptFor = p => p?.Inventory is { } inv && inv.HasTool(ToolKind.Bookmark) ? "Slide the bookmark in" : "One of the books sticks out.", CanUse = _ => !BookcaseOpen, Position = _jutting.Position + new Vector3(0, 0, -0.12f) };
		BookUse.Interacted += OnBook;
		_bookcase.AddChild(BookUse);
	}

	private void BuildFireplace()
	{
		var stone = new MeshKit();
		stone.Mat(new StandardMaterial3D { AlbedoTexture = StairwellTextures.CleanConcrete, AlbedoColor = new Color(0.75f, 0.7f, 0.62f), Roughness = 0.85f, VertexColorUseAsAlbedo = true, Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 1.2f });
		float z = 6.5f, x = HalfW;
		Slab(stone, new Vector3(x - 0.3f, 0.6f, z - 0.9f), new Vector3(0.6f, 1.2f, 0.4f));
		Slab(stone, new Vector3(x - 0.3f, 0.6f, z + 0.9f), new Vector3(0.6f, 1.2f, 0.4f));
		Slab(stone, new Vector3(x - 0.3f, 1.3f, z), new Vector3(0.7f, 0.2f, 2.2f));                 // mantel
		Slab(stone, new Vector3(x - 0.2f, (1.4f + Height) * 0.5f, z), new Vector3(0.4f, Height - 1.4f, 1.9f));   // chimney breast
		Slab(stone, new Vector3(x - 0.5f, 0.04f, z), new Vector3(1.0f, 0.08f, 2.2f), false);       // hearth
		stone.Color = new Color(0.08f, 0.07f, 0.06f);
		BuildKit.Box(stone, new Vector3(x - 0.05f, 0.6f, z), new Vector3(0.1f, 1.2f, 1.4f));        // the soot-black back
		stone.CommitTo(this, "Fireplace", true);
		var fire = new FireVfx { Name = "Fire", Extent = new Vector3(0.5f, 0.45f, 0.8f), Seed = 19, LightRange = 9f, Smoke = false, SmokeAmount = 0.3f };
		AddChild(fire);
		fire.Position = new Vector3(x - 0.35f, 0.08f, z);
		fire.Intensity = 0.45f;
		Loop("res://assets/audio/ambient/fire_crackle_loop.wav", new Vector3(x - 0.4f, 0.4f, z), -10f, 4f);
		// candles and a clock on the mantel
		var brass = new MeshKit();
		brass.Mat(ItemTextures.BrassMat);
		brass.Color = new Color(0.85f, 0.7f, 0.42f);
		foreach (float dz in new[] { -0.8f, 0.8f }) brass.Cylinder(new Vector3(x - 0.35f, 1.4f, z + dz), new Vector3(x - 0.35f, 1.62f, z + dz), 0.035f, 0.02f, 8, true);
		brass.CommitTo(this, "Candlesticks", false);
	}

	private void BuildFurniture()
	{
		var k = new MeshKit();
		k.Mat(_wood);
		// the reading table in the middle, a green banker's lamp on it
		Slab(k, new Vector3(0, 0.74f, 8.2f), new Vector3(1.6f, 0.06f, 0.9f));
		foreach (var (dx, dz) in new[] { (-0.72f, -0.38f), (0.72f, -0.38f), (-0.72f, 0.38f), (0.72f, 0.38f) })
			Slab(k, new Vector3(dx, 0.36f, 8.2f + dz), new Vector3(0.07f, 0.72f, 0.07f), false);
		_body.AddChild(new CollisionShape3D { Position = new Vector3(0, 0.4f, 8.2f), Shape = new BoxShape3D { Size = new Vector3(1.6f, 0.8f, 0.9f) } });
		// two armchairs by the fire
		var chair = new MeshKit();
		chair.Mat(new StandardMaterial3D { AlbedoColor = Colors.White, Roughness = 0.8f, VertexColorUseAsAlbedo = true });
		// either side of the fire, turned in towards it and each other
		foreach (var (cz, dir) in new[] { (4.75f, 1f), (8.25f, -1f) })
		{
			var leather = new Color(0.3f, 0.1f, 0.07f);
			const float cx = 4.0f;
			Slab(chair, new Vector3(cx, 0.25f, cz), new Vector3(0.8f, 0.5f, 0.8f), color: leather);
			Slab(chair, new Vector3(cx, 0.8f, cz - dir * 0.36f), new Vector3(0.8f, 0.9f, 0.14f), false, color: leather * 0.9f);
			foreach (float s in new[] { -0.36f, 0.36f }) Slab(chair, new Vector3(cx + s, 0.6f, cz), new Vector3(0.12f, 0.2f, 0.8f), false, color: leather * 1.1f);
			Slab(chair, new Vector3(cx, 0.53f, cz + dir * 0.05f), new Vector3(0.6f, 0.07f, 0.62f), false, color: new Color(0.4f, 0.15f, 0.1f));   // the cushion
		}
		chair.CommitTo(this, "Armchairs", true);
		// a globe, a grandfather clock, a rolling ladder, a floor lamp by the door
		var brass = new MeshKit();
		brass.Mat(ItemTextures.BrassMat);
		brass.Color = new Color(0.8f, 0.62f, 0.34f);
		brass.Cylinder(new Vector3(-2.8f, 0, 10.8f), new Vector3(-2.8f, 0.9f, 10.8f), 0.03f, 0.03f, 8, true);
		brass.CommitTo(this, "GlobeStand", false);
		AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.3f, Height = 0.6f }, Position = new Vector3(-2.8f, 1.15f, 10.8f), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.48f, 0.3f), Roughness = 0.4f } });
		Slab(k, new Vector3(4.7f, 1.05f, Depth - 0.75f), new Vector3(0.5f, 2.1f, 0.4f));
		k.CommitTo(this, "Tables", true);
		AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = 0.03f }, Position = new Vector3(4.47f, 1.6f, Depth - 0.75f), Rotation = new Vector3(0, 0, Mathf.Pi * 0.5f), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.9f, 0.82f) } });
		Loop("res://assets/audio/sfx/clock_tick_loop.wav", new Vector3(4.5f, 1.5f, Depth - 0.75f), -12f, 2.5f);
		var lad = new MeshKit();
		lad.Mat(_wood);
		foreach (float dz in new[] { -0.25f, 0.25f })
			lad.Cylinder(new Vector3(-HalfW + ShelfDepth + 0.05f, Height - 0.3f, 10.2f + dz), new Vector3(-HalfW + ShelfDepth + 0.7f, 0, 10.2f + dz), 0.025f, 0.025f, 6, true);
		for (float t = 0.1f; t < 1f; t += 0.11f)
		{
			Vector3 p = new Vector3(-HalfW + ShelfDepth + 0.05f, Height - 0.3f, 10.2f).Lerp(new Vector3(-HalfW + ShelfDepth + 0.7f, 0, 10.2f), t);
			lad.Cylinder(p + Vector3.Back * -0.25f, p + Vector3.Back * 0.25f, 0.015f, 0.015f, 5, false);
		}
		lad.CommitTo(this, "Ladder", false);
		// warm light: the table lamp, the floor lamp, two sconces
		Lamp(new Vector3(0.4f, 1.1f, 8.2f), new Color(0.2f, 0.5f, 0.25f), 1.3f);
		Lamp(new Vector3(-1.6f, 1.6f, 1.0f), new Color(0.9f, 0.85f, 0.7f), 1.1f);
		Lamp(new Vector3(-HalfW + 0.5f, 2.6f, 3.5f), new Color(0.9f, 0.7f, 0.4f), 0.7f);
		Lamp(new Vector3(HalfW - 0.5f, 2.6f, 10.5f), new Color(0.9f, 0.7f, 0.4f), 0.7f);
	}

	private void Lamp(Vector3 at, Color shade, float energy)
	{
		AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.16f, Height = 0.14f }, Position = at, MaterialOverride = new StandardMaterial3D { AlbedoColor = shade, Roughness = 0.4f, EmissionEnabled = true, Emission = shade, EmissionEnergyMultiplier = 0.4f } });
		AddChild(new OmniLight3D { Position = at + Vector3.Down * 0.12f, LightColor = new Color(1f, 0.82f, 0.6f), LightEnergy = energy, OmniRange = 7f, OmniAttenuation = 1.1f, ShadowEnabled = energy > 1f });
	}

	/// <summary>The side table by the west shelves: the puzzle box and its pieces on it, a sheet over everything.</summary>
	private void BuildSideTable()
	{
		var k = new MeshKit();
		k.Mat(_wood);
		Slab(k, TableAt + new Vector3(0, 0.7f, 0), new Vector3(0.6f, 0.04f, 1.25f), false);
		foreach (var (dx, dz) in new[] { (-0.25f, -0.55f), (0.25f, -0.55f), (-0.25f, 0.55f), (0.25f, 0.55f) })
			Slab(k, TableAt + new Vector3(dx, 0.35f, dz), new Vector3(0.05f, 0.7f, 0.05f), false);
		k.CommitTo(this, "SideTable", true);
		_body.AddChild(new CollisionShape3D { Position = TableAt + new Vector3(0, 0.37f, 0), Shape = new BoxShape3D { Size = new Vector3(0.6f, 0.74f, 1.25f) } });
		// the puzzle, turned so its loose pieces lie along the table and its near side faces the room
		Puzzle = new PuzzleBox { Name = "PuzzleBox", Position = TableAt + new Vector3(0f, 0.72f, 0.3f), Rotation = new Vector3(0, Mathf.Pi * 0.5f, 0) };
		AddChild(Puzzle);
		Puzzle.Solved += OnSolved;
		_glow = new OmniLight3D { Name = "Glow", Position = Puzzle.Position + Vector3.Up * 0.15f, LightColor = new Color(1f, 0.92f, 0.75f), LightEnergy = 0f, OmniRange = 5f, ShadowEnabled = false };
		AddChild(_glow);
		// the sheet
		_sheet = new Node3D { Name = "Sheet", Position = TableAt + new Vector3(0, 0.72f, 0) };
		AddChild(_sheet);
		_sheet.AddChild(Drape());
		SheetUse = new Interactable { Name = "Sheet", Prompt = "Pull off the sheet", PickRadius = 0.5f, MaxDistance = 2.4f, Position = TableAt + new Vector3(0.1f, 0.9f, 0) };
		SheetUse.Interacted += OnSheet;
		AddChild(SheetUse);
		BoxUse = new Interactable { Name = "Box", Prompt = "Work the puzzle box", PickRadius = 0.4f, MaxDistance = 2.2f, Position = Puzzle.Position + Vector3.Up * 0.08f, Enabled = false };
		BoxUse.Interacted += OnBox;
		AddChild(BoxUse);
	}

	/// <summary>A white sheet thrown over the table: flat on the top, lifted over the box, hanging down
	/// the sides in soft folds.</summary>
	private MeshInstance3D Drape()
	{
		const int nx = 18, nz = 30;
		const float w = 1.3f, l = 1.95f;
		var h = new float[nx + 1, nz + 1];
		var pos = new Vector3[nx + 1, nz + 1];
		for (int i = 0; i <= nx; i++)
			for (int j = 0; j <= nz; j++)
			{
				float x = -w * 0.5f + w * i / nx, z = -l * 0.5f + l * j / nz;
				float over = Mathf.Max(Mathf.Abs(x) - 0.3f, 0f) + Mathf.Max(Mathf.Abs(z) - 0.62f, 0f);
				float y = 0.012f - over * 1.4f;                            // hanging down past the table's edges
				float box = Mathf.Max(0f, 1f - new Vector2(Mathf.Max(0f, Mathf.Abs(x) - 0.19f), Mathf.Max(0f, Mathf.Abs(z - 0.3f) - 0.19f)).Length() / 0.12f);
				float bits = Mathf.Max(0f, 1f - new Vector2(Mathf.Max(0f, Mathf.Abs(x) - 0.2f), Mathf.Max(0f, Mathf.Abs(z + 0.22f) - 0.28f)).Length() / 0.1f);
				y += Mathf.Max(0.11f * Mathf.SmoothStep(0f, 1f, box), 0.06f * Mathf.SmoothStep(0f, 1f, bits));   // over the box, and the pieces
				y += 0.02f * Mathf.Sin(z * 14f + x * 3f) * Mathf.Clamp(over * 5f, 0f, 1f);   // folds where it hangs
				pos[i, j] = new Vector3(x * (1f - Mathf.Clamp(over, 0f, 0.3f) * 0.2f), Mathf.Max(y, -0.66f), z);
			}
		var k = new MeshKit();
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.74f, 0.72f, 0.67f), Roughness = 0.95f, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
		k.Color = Colors.White;
		for (int i = 0; i < nx; i++)
			for (int j = 0; j < nz; j++)
			{
				Vector3 a = pos[i, j], b = pos[i + 1, j], c = pos[i + 1, j + 1], d = pos[i, j + 1];
				Vector3 n = (b - a).Cross(d - a).Normalized();
				if (n.Y < 0) k.Quad(a, d, c, b, -n);
				else k.Quad(a, b, c, d, n);
			}
		var mi = k.CommitTo(this, "Drape", true);
		RemoveChild(mi);
		return mi;
	}

	private void BuildPassage()
	{
		var stone = new MeshKit();
		stone.Mat(new StandardMaterial3D { AlbedoTexture = StairwellTextures.CleanConcrete, AlbedoColor = new Color(0.6f, 0.58f, 0.54f), Roughness = 0.9f, VertexColorUseAsAlbedo = true, Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 0.8f });
		float z0 = Depth + 0.3f, z1 = Depth + PassageLen + 0.4f, zc = (z0 + z1) * 0.5f;
		Slab(stone, new Vector3(-0.85f, 1.25f, zc), new Vector3(0.3f, 2.5f, z1 - z0));
		Slab(stone, new Vector3(0.85f, 1.25f, zc), new Vector3(0.3f, 2.5f, z1 - z0));
		Slab(stone, new Vector3(0, 2.6f, zc), new Vector3(2f, 0.2f, z1 - z0), false);
		Slab(stone, new Vector3(0, -0.05f, zc), new Vector3(1.8f, 0.1f, z1 - z0 + 0.4f));
		stone.CommitTo(this, "Passage", true);
		foreach (float dz in new[] { 0.9f, 2.4f })
		{
			AddChild(new OmniLight3D { Position = new Vector3(0.55f, 1.8f, Depth + dz), LightColor = new Color(1f, 0.75f, 0.45f), LightEnergy = 0.6f, OmniRange = 3.5f, ShadowEnabled = false });
			AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.025f, Height = 0.15f }, Position = new Vector3(0.62f, 1.62f, Depth + dz), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.9f, 0.82f) } });
		}
	}

	private void Loop(string path, Vector3 at, float db, float unit)
	{
		if (!ResourceLoader.Exists(path)) return;
		var stream = GD.Load<AudioStream>(path);
		if (stream is AudioStreamWav wav)
		{
			wav = (AudioStreamWav)wav.Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			stream = wav;
		}
		var p = new AudioStreamPlayer3D { Stream = stream, Bus = "Events", VolumeDb = db, UnitSize = unit, MaxDistance = 20f, Position = at, Autoplay = true };
		AddChild(p);
	}
}
