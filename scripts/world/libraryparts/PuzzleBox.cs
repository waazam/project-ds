using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.LibraryParts;

/// <summary>
/// Act 19's puzzle box: a shallow two-tone bamboo box, four cells by four inside, and five blocky
/// pieces of different sizes (a square, an S, a straight three, a corner of three and a straight two),
/// like the owner's bamboo puzzle references. They fill the box exactly, but only two ways (and
/// their turns), so it takes some thought. Pieces turn in quarter turns only.
///
/// Local space: y=0 is the table top; the box's middle is x=z=0; the pieces rest on the table to the
/// box's +X side. Grid cell (gx, gy): gx along +X, gy along +Z.
/// </summary>
public partial class PuzzleBox : Node3D
{
	public const int N = 4;
	public const float Cell = 0.07f, Wall = 0.035f, Floor = 0.012f, Depth = 0.09f;
	public event Action Solved;

	public sealed class Piece
	{
		public string Name;
		public Vector2I[] Shape;
		public int Rot;
		public Vector2I? At;
		public Node3D Node;
		public Vector3 Rest;
		public readonly List<MeshInstance3D> Cubes = new();
	}

	public List<Piece> Pieces { get; } = new();
	public bool IsSolved { get; private set; }
	private readonly Piece[,] _grid = new Piece[N, N];
	private readonly List<ShaderMaterial> _mats = new();
	private MeshInstance3D _boxMesh;

	private static readonly (string name, Vector2I[] cells)[] Shapes =
	{
		("square", new[] { new Vector2I(0, 0), new Vector2I(1, 0), new Vector2I(0, 1), new Vector2I(1, 1) }),
		("S", new[] { new Vector2I(1, 0), new Vector2I(2, 0), new Vector2I(0, 1), new Vector2I(1, 1) }),
		("long", new[] { new Vector2I(0, 0), new Vector2I(1, 0), new Vector2I(2, 0) }),
		("corner", new[] { new Vector2I(0, 0), new Vector2I(1, 0), new Vector2I(0, 1) }),
		("short", new[] { new Vector2I(0, 0), new Vector2I(1, 0) }),
	};

	public override void _Ready()
	{
		BuildBox();
		Vector3[] rests = { new(0.33f, 0, -0.16f), new(0.35f, 0, 0.06f), new(0.58f, 0, -0.18f), new(0.57f, 0, 0.02f), new(0.46f, 0, 0.21f) };
		int[] rots = { 0, 1, 0, 2, 1 };
		for (int i = 0; i < Shapes.Length; i++)
		{
			var p = new Piece { Name = Shapes[i].name, Shape = Shapes[i].cells, Rot = rots[i], Rest = rests[i] };
			p.Node = new Node3D { Name = $"Piece_{p.Name}" };
			AddChild(p.Node);
			for (int c = 0; c < p.Shape.Length; c++)
			{
				var cube = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * (Cell - 0.003f) }, MaterialOverride = Mat((i + c) % 2 == 0) };
				p.Node.AddChild(cube);
				p.Cubes.Add(cube);
			}
			Pieces.Add(p);
			Layout(p);
		}
	}

	// ------------------------------------------------------------------ the pieces' shapes

	/// <summary>The piece's cells after <paramref name="rot"/> quarter turns, shifted so the smallest x and y are 0.</summary>
	public static Vector2I[] Rotated(Vector2I[] shape, int rot)
	{
		var cells = new Vector2I[shape.Length];
		for (int i = 0; i < shape.Length; i++)
		{
			Vector2I c = shape[i];
			for (int r = 0; r < ((rot % 4) + 4) % 4; r++) c = new Vector2I(c.Y, -c.X);
			cells[i] = c;
		}
		int mx = int.MaxValue, my = int.MaxValue;
		foreach (var c in cells) { mx = Math.Min(mx, c.X); my = Math.Min(my, c.Y); }
		for (int i = 0; i < cells.Length; i++) cells[i] -= new Vector2I(mx, my);
		return cells;
	}

	public bool Fits(Piece p, int rot, Vector2I at)
	{
		foreach (var c in Rotated(p.Shape, rot))
		{
			Vector2I g = at + c;
			if (g.X < 0 || g.Y < 0 || g.X >= N || g.Y >= N) return false;
			if (_grid[g.X, g.Y] != null && _grid[g.X, g.Y] != p) return false;
		}
		return true;
	}

	public bool Place(Piece p, int rot, Vector2I at)
	{
		if (IsSolved || !Fits(p, rot, at)) return false;
		Lift(p);
		p.Rot = rot;
		p.At = at;
		foreach (var c in Rotated(p.Shape, rot)) _grid[at.X + c.X, at.Y + c.Y] = p;
		Layout(p);
		if (Pieces.TrueForAll(q => q.At != null)) { IsSolved = true; Solved?.Invoke(); }
		return true;
	}

	public void Lift(Piece p)
	{
		if (p.At == null) return;
		for (int x = 0; x < N; x++)
			for (int y = 0; y < N; y++)
				if (_grid[x, y] == p) _grid[x, y] = null;
		p.At = null;
	}

	/// <summary>Grid cell (gx, gy) in this node's space, at the height a piece sits in the box.</summary>
	public static Vector3 CellLocal(float gx, float gy) => new(-N * Cell * 0.5f + (gx + 0.5f) * Cell, Floor + Cell * 0.5f, -N * Cell * 0.5f + (gy + 0.5f) * Cell);

	/// <summary>Puts a piece's cubes where it is: in the box, held over it, or resting on the table.</summary>
	public void Layout(Piece p, Vector2I? hover = null, int? hoverRot = null)
	{
		var cells = Rotated(p.Shape, hoverRot ?? p.Rot);
		Vector3 origin;
		if (hover is { } h) origin = CellLocal(h.X, h.Y) + Vector3.Up * 0.07f;
		else if (p.At is { } a) origin = CellLocal(a.X, a.Y);
		else origin = p.Rest + Vector3.Up * Cell * 0.5f;
		p.Node.Position = origin;
		for (int i = 0; i < cells.Length; i++) p.Cubes[i].Position = new Vector3(cells[i].X * Cell, 0, cells[i].Y * Cell);
	}

	/// <summary>A solution from where things stand (backtracking): tests use it, and so could a hint.</summary>
	public List<(Piece p, int rot, Vector2I at)> Solve()
	{
		var placed = new List<(Piece, int, Vector2I)>();
		var grid = new Piece[N, N];
		var order = new List<Piece>(Pieces);
		order.Sort((a, b) => b.Shape.Length.CompareTo(a.Shape.Length));
		bool Rec(int i)
		{
			if (i == order.Count) return true;
			var p = order[i];
			for (int rot = 0; rot < 4; rot++)
				for (int x = 0; x < N; x++)
					for (int y = 0; y < N; y++)
					{
						var cells = Rotated(p.Shape, rot);
						bool ok = true;
						foreach (var c in cells)
						{
							int gx = x + c.X, gy = y + c.Y;
							if (gx >= N || gy >= N || grid[gx, gy] != null) { ok = false; break; }
						}
						if (!ok) continue;
						foreach (var c in cells) grid[x + c.X, y + c.Y] = p;
						placed.Add((p, rot, new Vector2I(x, y)));
						if (Rec(i + 1)) return true;
						placed.RemoveAt(placed.Count - 1);
						foreach (var c in cells) grid[x + c.X, y + c.Y] = null;
					}
			return false;
		}
		Rec(0);
		return placed;
	}

	/// <summary>0..1: going out of the world.</summary>
	public void Dissolve(float progress)
	{
		foreach (var m in _mats) m.SetShaderParameter("progress", progress);
	}

	// ------------------------------------------------------------------ building

	private static Texture2D _light, _dark;

	/// <summary>Bamboo: pale gold with fine grain and a node line, or the darker caramel.</summary>
	private static Texture2D Bamboo(bool dark)
	{
		ref Texture2D t = ref (dark ? ref _dark : ref _light);
		if (t != null) return t;
		var img = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
		var rng = new RandomNumberGenerator { Seed = dark ? 77u : 66u };
		Color baseC = dark ? new Color(0.55f, 0.34f, 0.18f) : new Color(0.86f, 0.68f, 0.44f);
		var grain = new float[32];
		for (int x = 0; x < 32; x++) grain[x] = rng.RandfRange(-0.07f, 0.07f);
		for (int y = 0; y < 32; y++)
			for (int x = 0; x < 32; x++)
			{
				float g = 1f + grain[x] + (y == 20 || y == 21 ? -0.18f : 0f);
				img.SetPixel(x, y, baseC * g);
			}
		img.GenerateMipmaps();
		t = ImageTexture.CreateFromImage(img);
		return t;
	}

	private ShaderMaterial Mat(bool dark)
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/dissolve.gdshader") };
		m.SetShaderParameter("albedo_tex", Bamboo(dark));
		m.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		m.SetShaderParameter("progress", 0f);
		_mats.Add(m);
		return m;
	}

	private void BuildBox()
	{
		float inner = N * Cell, outer = inner + 2f * Wall;
		var parts = new List<(Vector3 c, Vector3 s, bool dark)>
		{
			(new Vector3(0, Floor * 0.5f, 0), new Vector3(outer, Floor, outer), true),
			(new Vector3(0, Depth * 0.5f, -(inner + Wall) * 0.5f), new Vector3(outer, Depth, Wall), false),
			(new Vector3(0, Depth * 0.5f, (inner + Wall) * 0.5f), new Vector3(outer, Depth, Wall), false),
			(new Vector3(-(inner + Wall) * 0.5f, Depth * 0.5f, 0), new Vector3(Wall, Depth, inner), true),
			(new Vector3((inner + Wall) * 0.5f, Depth * 0.5f, 0), new Vector3(Wall, Depth, inner), true),
		};
		foreach (var (c, s, dark) in parts)
			AddChild(new MeshInstance3D { Name = "Box", Mesh = new BoxMesh { Size = s }, Position = c, MaterialOverride = Mat(dark) });
		// inlaid strips on the rim, like the reference's checkered bamboo
		for (int i = 0; i < 4; i++)
		{
			float x = -outer * 0.5f + (i + 0.5f) * outer / 4f;
			AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(outer / 4f - 0.002f, 0.004f, Wall - 0.004f) }, Position = new Vector3(x, Depth + 0.002f, -(inner + Wall) * 0.5f), MaterialOverride = Mat(i % 2 == 0) });
			AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(outer / 4f - 0.002f, 0.004f, Wall - 0.004f) }, Position = new Vector3(x, Depth + 0.002f, (inner + Wall) * 0.5f), MaterialOverride = Mat(i % 2 == 1) });
		}
	}
}
