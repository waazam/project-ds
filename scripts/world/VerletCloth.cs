using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// A tablecloth that can be drawn off its table (the owner, 2026-10-01: the cloths "spazzing and end up across the
/// room"; they should come off slowly and smoothly and fall naturally to the floor beside the table). Jolt's soft
/// body shivered against the table and could be flung; this is our own small cloth, made to be calm:
/// <list type="bullet">
/// <item>a grid of points joined to their neighbours (along, across, the diagonals, and a soft bend to the next but
/// one), stepped several times a frame with plenty of damping;</item>
/// <item>it touches only what it needs to: the floor and the table's block, each with friction (linen on wood
/// slides, linen on the floor stops);</item>
/// <item>no point may move more than a few centimetres a step, so nothing can ever be thrown;</item>
/// <item>some points can be held by a "hand" node and let go; once it has lain still a moment it stops for good and
/// stays as it fell (a still mesh).</item>
/// </list>
/// At rest it's simply the drape (nothing runs until <see cref="Wake"/>). All in the parent's space.
/// </summary>
public partial class VerletCloth : MeshInstance3D
{
	private int _nx, _nz;
	private Vector3[] _p, _prev;
	private Node3D[] _pinTo;
	private Vector3[] _pinOffset;
	private readonly List<(int a, int b, float rest, float k)> _links = new();
	private Vector2[] _uv;
	private int[] _idx;
	private ArrayMesh _mesh;
	private Material _mat;
	private double _still, _awake;

	/// <summary>Solid blocks (in the parent's space) the cloth lies on and slides off: the table.</summary>
	public readonly List<Aabb> Blocks = new();
	/// <summary>The floor's height (parent's space).</summary>
	public float FloorY;
	/// <summary>How much of a point's sliding is kept, touching the block (wood) and the floor.</summary>
	public float BlockSlide = 0.9f, FloorSlide = 0.35f;
	public bool Settled { get; private set; }
	public int PointCount => _p.Length;

	/// <summary>A cloth from its rest shape (<paramref name="grid"/>, [nx+1, nz+1] points in the parent's space) and
	/// its material; its UVs in metres across the flat cloth (<paramref name="size"/>).</summary>
	public static VerletCloth Create(Vector3[,] grid, Vector2 size, Material mat, string name = "Cloth")
	{
		// (no shadow of its own: a thin two-sided sheet shadowing itself streaked it with dark lines)
		var c = new VerletCloth { Name = name, _mat = mat, CastShadow = ShadowCastingSetting.Off };
		c._nx = grid.GetLength(0) - 1;
		c._nz = grid.GetLength(1) - 1;
		int n = (c._nx + 1) * (c._nz + 1);
		c._p = new Vector3[n]; c._prev = new Vector3[n]; c._uv = new Vector2[n];
		c._pinTo = new Node3D[n]; c._pinOffset = new Vector3[n];
		for (int i = 0; i <= c._nx; i++)
			for (int j = 0; j <= c._nz; j++)
			{
				int k = c.I(i, j);
				c._p[k] = c._prev[k] = grid[i, j];
				c._uv[k] = new Vector2(i / (float)c._nx * size.X, j / (float)c._nz * size.Y);
			}
		// its links: their rest lengths from the flat cloth (not the drape, whose hang is shortened by the folds)
		float dx = size.X / c._nx, dz = size.Y / c._nz;
		for (int i = 0; i <= c._nx; i++)
			for (int j = 0; j <= c._nz; j++)
			{
				if (i < c._nx) c._links.Add((c.I(i, j), c.I(i + 1, j), dx, 1f));
				if (j < c._nz) c._links.Add((c.I(i, j), c.I(i, j + 1), dz, 1f));
				if (i < c._nx && j < c._nz)
				{
					float d = Mathf.Sqrt(dx * dx + dz * dz);
					c._links.Add((c.I(i, j), c.I(i + 1, j + 1), d, 0.6f));
					c._links.Add((c.I(i + 1, j), c.I(i, j + 1), d, 0.6f));
				}
				if (i + 2 <= c._nx) c._links.Add((c.I(i, j), c.I(i + 2, j), dx * 2f, 0.08f));
				if (j + 2 <= c._nz) c._links.Add((c.I(i, j), c.I(i, j + 2), dz * 2f, 0.08f));
			}
		var idx = new List<int>();
		for (int i = 0; i < c._nx; i++)
			for (int j = 0; j < c._nz; j++)
			{
				int a = c.I(i, j), b = c.I(i + 1, j), cc = c.I(i + 1, j + 1), d = c.I(i, j + 1);
				idx.Add(a); idx.Add(b); idx.Add(cc);
				idx.Add(a); idx.Add(cc); idx.Add(d);
			}
		c._idx = idx.ToArray();
		c._mesh = new ArrayMesh();
		c.Mesh = c._mesh;
		c.Rebuild();
		return c;
	}

	private int I(int i, int j) => i * (_nz + 1) + j;
	public int IndexOf(int i, int j) => I(Mathf.Clamp(i, 0, _nx), Mathf.Clamp(j, 0, _nz));
	public Vector3 Point(int k) => _p[k];

	/// <summary>For tests: the farthest any point lies from <paramref name="c"/> across the floor (parent's space), and
	/// the cloth's mean height.</summary>
	public (float farthest, float meanY) Spread(Vector3 c)
	{
		float far = 0f, y = 0f;
		foreach (var p in _p) { far = Mathf.Max(far, new Vector2(p.X - c.X, p.Z - c.Z).Length()); y += p.Y; }
		return (far, y / _p.Length);
	}

	private bool _running;

	// (Godot turns physics processing on for a script that has _PhysicsProcess as it enters the tree, whatever was set
	// before: all the lodge's cloths ran from the start, a physics tick of 60 ms, the game slowed to a crawl)
	public override void _Ready() => SetPhysicsProcess(_running);

	/// <summary>Starts it moving (it lies still as the drape until then).</summary>
	public void Wake()
	{
		Settled = false;
		_still = 0; _awake = 0;
		_running = true;
		SetPhysicsProcess(true);
	}

	/// <summary>Point <paramref name="k"/> held by <paramref name="hand"/> (a node in the same parent), where it is now.</summary>
	public void Pin(int k, Node3D hand)
	{
		_pinTo[k] = hand;
		_pinOffset[k] = _p[k] - hand.Position;
	}

	public void Unpin(int k) => _pinTo[k] = null;

	public override void _PhysicsProcess(double delta)
	{
		const int steps = 4, iterations = 6;
		const float maxStep = 0.03f;   // (metres a step: about 4.8 m/s at most, so nothing is ever thrown)
		float h = (float)delta / steps;
		var g = new Vector3(0, -9.0f, 0) * h * h;
		bool held = false;
		float moved = 0f;
		for (int s = 0; s < steps; s++)
		{
			for (int k = 0; k < _p.Length; k++)
			{
				if (_pinTo[k] != null)
				{
					held = true;
					_prev[k] = _p[k];
					// (the hand eases: its points follow it a step at a time)
					_p[k] = _p[k].Lerp(_pinTo[k].Position + _pinOffset[k], 0.5f);
					continue;
				}
				var v = (_p[k] - _prev[k]) * 0.985f;
				if (v.LengthSquared() > maxStep * maxStep) v = v.Normalized() * maxStep;
				_prev[k] = _p[k];
				_p[k] += v + g;
			}
			for (int it = 0; it < iterations; it++)
			{
				foreach (var (a, b, rest, stiff) in _links)
				{
					var d = _p[b] - _p[a];
					float len = d.Length();
					if (len < 1e-5f) continue;
					var corr = d * ((len - rest) / len) * 0.5f * stiff;
					bool pa = _pinTo[a] != null, pb = _pinTo[b] != null;
					if (pa && pb) continue;
					if (pa) _p[b] -= corr * 2f;
					else if (pb) _p[a] += corr * 2f;
					else { _p[a] += corr; _p[b] -= corr; }
				}
				Collide();
			}
		}
		for (int k = 0; k < _p.Length; k++) moved = Mathf.Max(moved, (_p[k] - _prev[k]).Length() * steps);
		Rebuild();
		// at rest: once it's lain still a moment (and nothing holds it), it stops for good
		double dt = delta;
		_awake += dt;
		_still = !held && moved < 0.004f ? _still + dt : 0;
		if ((_still > 0.6 && _awake > 1.5) || _awake > 14)
		{
			Settled = true;
			_running = false;
			SetPhysicsProcess(false);
		}
	}

	private void Collide()
	{
		const float skin = 0.012f;
		for (int k = 0; k < _p.Length; k++)
		{
			if (_pinTo[k] != null) continue;
			var p = _p[k];
			if (p.Y < FloorY + skin)
			{
				p.Y = FloorY + skin;
				// friction on the floor: most of its sliding gone
				_prev[k] = new Vector3(Mathf.Lerp(p.X, _prev[k].X, FloorSlide), p.Y, Mathf.Lerp(p.Z, _prev[k].Z, FloorSlide));
			}
			foreach (var b in Blocks)
			{
				var lo = b.Position - Vector3.One * skin; var hi = b.End + Vector3.One * skin;
				if (p.X <= lo.X || p.X >= hi.X || p.Y <= lo.Y || p.Y >= hi.Y || p.Z <= lo.Z || p.Z >= hi.Z) continue;
				// out by the nearest face (the top, nearly always)
				float up = hi.Y - p.Y, dn = p.Y - lo.Y, px = hi.X - p.X, nx = p.X - lo.X, pz = hi.Z - p.Z, nz = p.Z - lo.Z;
				float m = Mathf.Min(up, Mathf.Min(Mathf.Min(px, nx), Mathf.Min(pz, nz)));
				if (m == up)
				{
					p.Y = hi.Y;
					_prev[k] = new Vector3(Mathf.Lerp(p.X, _prev[k].X, BlockSlide), p.Y, Mathf.Lerp(p.Z, _prev[k].Z, BlockSlide));
				}
				else if (m == px) p.X = hi.X;
				else if (m == nx) p.X = lo.X;
				else if (m == pz) p.Z = hi.Z;
				else p.Z = lo.Z;
				_ = dn;
			}
			_p[k] = p;
		}
	}

	/// <summary>The mesh from the points: positions, smooth normals from the grid, the UVs.</summary>
	private void Rebuild()
	{
		var nrm = new Vector3[_p.Length];
		var tan = new float[_p.Length * 4];
		for (int i = 0; i <= _nx; i++)
			for (int j = 0; j <= _nz; j++)
			{
				var dx = _p[I(Mathf.Min(i + 1, _nx), j)] - _p[I(Mathf.Max(i - 1, 0), j)];
				var dz = _p[I(i, Mathf.Min(j + 1, _nz))] - _p[I(i, Mathf.Max(j - 1, 0))];
				var n = dz.Cross(dx);
				nrm[I(i, j)] = n.LengthSquared() > 1e-10f ? n.Normalized() : Vector3.Up;
				// (the tangent along the cloth's U, for the weave's normal map)
				var t = dx.LengthSquared() > 1e-10f ? dx.Normalized() : Vector3.Right;
				int q = I(i, j) * 4;
				tan[q] = t.X; tan[q + 1] = t.Y; tan[q + 2] = t.Z; tan[q + 3] = 1f;
			}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = _p;
		arrays[(int)Mesh.ArrayType.Normal] = nrm;
		arrays[(int)Mesh.ArrayType.Tangent] = tan;
		arrays[(int)Mesh.ArrayType.TexUV] = _uv;
		arrays[(int)Mesh.ArrayType.Index] = _idx;
		_mesh.ClearSurfaces();
		_mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		_mesh.SurfaceSetMaterial(0, _mat);
	}
}
