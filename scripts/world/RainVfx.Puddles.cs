using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-10) After the rain, the woods stay wet a while:
/// - <b>puddles</b> in the hollows of the ground, still, black and glossy, the lantern's light lying on them; they fill as
///   it soaks in and shrink away as it dries. Fixed in the world (each 6 m cell of ground has its own, or none), laid as you
///   walk and taken up behind you;
/// - <b>drips off the eaves</b>: water running off the edges of the roofs (every registered shelter's) in a slow line of
///   drops, while it's raining and for a while after.
/// (The bark darkening and going glossy is tree_solid.gdshader's, from the global "wet" set each frame.)
/// </summary>
public partial class RainVfx
{
	private const float PuddleCell = 6f, PuddleRadius = 26f;
	private readonly Dictionary<Vector2I, MeshInstance3D> _puddles = new();
	private readonly HashSet<Vector2I> _dry = new();
	private StandardMaterial3D _puddleMat;
	private Vector2I _lastCell = new(int.MinValue, 0);
	private readonly List<GpuParticles3D> _eaves = new();
	private int _eavesFor = -1;

	private void Puddles(Vector3 cp, float dt)
	{
		if (_terrain == null) return;
		if (_puddleMat == null)
		{
			_puddleMat = new StandardMaterial3D
			{
				ResourceName = "puddle", AlbedoColor = new Color(0.02f, 0.025f, 0.03f, 0f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				Roughness = 0.04f, Metallic = 0.1f, MetallicSpecular = 0.9f, AlbedoTexture = PuddleTexture(),
				CullMode = BaseMaterial3D.CullModeEnum.Disabled, RenderPriority = -1,
			};
		}
		float fill = Mathf.Clamp((Wetness - 0.25f) / 0.5f, 0f, 1f);
		_puddleMat.AlbedoColor = _puddleMat.AlbedoColor with { A = 0.9f * fill };
		bool any = fill > 0.01f;
		if (!any)
		{
			if (_puddles.Count > 0) { foreach (var p in _puddles.Values) p.QueueFree(); _puddles.Clear(); _dry.Clear(); _lastCell = new(int.MinValue, 0); }
			return;
		}
		foreach (var p in _puddles.Values) p.Scale = Vector3.One * (0.55f + 0.45f * fill);
		var cell = new Vector2I(Mathf.FloorToInt(cp.X / PuddleCell), Mathf.FloorToInt(cp.Z / PuddleCell));
		if (cell == _lastCell) return;
		_lastCell = cell;
		int reach = Mathf.CeilToInt(PuddleRadius / PuddleCell);
		// lay the cells now in reach (each its own puddle or none, the same every time), take up the ones left behind
		for (int x = -reach; x <= reach; x++)
			for (int z = -reach; z <= reach; z++)
			{
				var c = new Vector2I(cell.X + x, cell.Y + z);
				if (_puddles.ContainsKey(c) || _dry.Contains(c)) continue;
				if (new Vector2(x, z).Length() * PuddleCell > PuddleRadius) continue;
				if (MakePuddle(c) is { } mi) _puddles[c] = mi; else _dry.Add(c);
			}
		var gone = new List<Vector2I>();
		foreach (var (c, mi) in _puddles)
			if (Mathf.Abs(c.X - cell.X) > reach + 1 || Mathf.Abs(c.Y - cell.Y) > reach + 1) { mi.QueueFree(); gone.Add(c); }
		foreach (var c in gone) _puddles.Remove(c);
		_dry.RemoveWhere(c => Mathf.Abs(c.X - cell.X) > reach + 1 || Mathf.Abs(c.Y - cell.Y) > reach + 1);
	}

	private MeshInstance3D MakePuddle(Vector2I c)
	{
		uint h = (uint)(c.X * 73856093) ^ (uint)(c.Y * 19349663);
		h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
		if (h % 100 > 34) return null;   // (about a third of the ground has one)
		float ox = (h % 997) / 997f, oz = (h / 997 % 991) / 991f;
		float x = (c.X + 0.15f + 0.7f * ox) * PuddleCell, z = (c.Y + 0.15f + 0.7f * oz) * PuddleCell;
		float y = _terrain.HeightAt(x, z);
		// only on the level: not on a slope (water runs off it)
		float slope = Mathf.Max(Mathf.Abs(_terrain.HeightAt(x + 0.9f, z) - _terrain.HeightAt(x - 0.9f, z)), Mathf.Abs(_terrain.HeightAt(x, z + 0.9f) - _terrain.HeightAt(x, z - 0.9f)));
		if (slope > 0.16f) return null;
		float r = 0.5f + 0.8f * ((h >> 7) % 100) / 100f;
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		const int n = 14;
		var rng = new RandomNumberGenerator { Seed = h };
		var ring = new Vector3[n];
		for (int i = 0; i < n; i++)
		{
			float a = i / (float)n * Mathf.Tau, rr = r * rng.RandfRange(0.7f, 1.25f) * (1f + 0.35f * Mathf.Sin(a * 2f + h % 7));
			ring[i] = new Vector3(Mathf.Cos(a) * rr * 1.3f, 0, Mathf.Sin(a) * rr);
		}
		for (int i = 0; i < n; i++)
		{
			var a = ring[i]; var b = ring[(i + 1) % n];
			st.SetNormal(Vector3.Up);
			st.SetUV(new Vector2(0.5f, 0.5f)); st.AddVertex(Vector3.Zero);
			st.SetUV(new Vector2(0.5f + a.X / (r * 3.4f), 0.5f + a.Z / (r * 2.6f))); st.AddVertex(a);
			st.SetUV(new Vector2(0.5f + b.X / (r * 3.4f), 0.5f + b.Z / (r * 2.6f))); st.AddVertex(b);
		}
		// (each vertex sat on the ground, a hair above it, so it follows the dip it lies in)
		var mesh = st.Commit();
		var mi = new MeshInstance3D { Name = "Puddle", Mesh = mesh, MaterialOverride = _puddleMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild(mi);
		mi.GlobalPosition = new Vector3(x, y + 0.025f, z);
		mi.GlobalRotation = new Vector3(0, (h % 628) / 100f, 0);
		return mi;
	}

	private static ImageTexture _puddleTex;
	/// <summary>The water's edge: solid in the middle, feathering out unevenly.</summary>
	private static ImageTexture PuddleTexture()
	{
		if (_puddleTex != null) return _puddleTex;
		const int s = 64;
		var noise = new FastNoiseLite { Seed = 77, Frequency = 0.09f };
		var img = Image.CreateEmpty(s, s, true, Image.Format.Rgba8);
		for (int y = 0; y < s; y++)
			for (int x = 0; x < s; x++)
			{
				float u = (x + 0.5f) / s * 2f - 1f, v = (y + 0.5f) / s * 2f - 1f;
				float r = Mathf.Sqrt(u * u + v * v) + noise.GetNoise2D(x, y) * 0.18f;
				img.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp(1f - Mathf.SmoothStep(0.55f, 0.95f, r), 0f, 1f)));
			}
		img.GenerateMipmaps();
		return _puddleTex = ImageTexture.CreateFromImage(img);
	}

	/// <summary>Drops off the shelters' eaves while it's wet (made once per set of shelters).</summary>
	private void Eaves(float dt)
	{
		float rate = Mathf.Max(_intensity, Mathf.Clamp((Wetness - 0.15f) * 1.5f, 0f, 0.8f));
		if (_eavesFor != _shelters.Count)
		{
			foreach (var e in _eaves) if (IsInstanceValid(e)) e.QueueFree();
			_eaves.Clear();
			_eavesFor = _shelters.Count;
			foreach (var (owner, box) in _shelters)
			{
				if (!IsInstanceValid(owner)) continue;
				// the four edges of the box's top, a little out from the walls (the roof's overhang)
				float y = box.End.Y + 0.1f, pad = 0.35f;
				Vector3 lo = box.Position - new Vector3(pad, 0, pad), hi = box.End + new Vector3(pad, 0, pad);
				foreach (var (a, b) in new[]
				{
					(new Vector3(lo.X, y, lo.Z), new Vector3(hi.X, y, lo.Z)), (new Vector3(lo.X, y, hi.Z), new Vector3(hi.X, y, hi.Z)),
					(new Vector3(lo.X, y, lo.Z), new Vector3(lo.X, y, hi.Z)), (new Vector3(hi.X, y, lo.Z), new Vector3(hi.X, y, hi.Z)),
				})
				{
					var p = DripLine((a - b).Length());
					owner.AddChild(p);
					p.Position = (a + b) * 0.5f;
					p.Rotation = new Vector3(0, Mathf.Atan2(-(b - a).Z, (b - a).X), 0);
					_eaves.Add(p);
				}
			}
		}
		foreach (var e in _eaves)
		{
			if (!IsInstanceValid(e)) continue;
			e.Emitting = rate > 0.05f;
			e.AmountRatio = Mathf.Clamp(rate, 0.05f, 1f);
		}
	}

	private static GpuParticles3D DripLine(float len)
	{
		var pm = new ParticleProcessMaterial
		{
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(len * 0.5f, 0.02f, 0.03f),
			Direction = Vector3.Down, Spread = 2f, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.6f, Gravity = new Vector3(0, -9.8f, 0),
			ScaleMin = 0.7f, ScaleMax = 1.2f,
		};
		return new GpuParticles3D
		{
			Name = "EaveDrips", Amount = Mathf.Clamp((int)(len * 3f), 6, 40), Lifetime = 1.1, ProcessMaterial = pm, Emitting = false,
			DrawPass1 = new QuadMesh
			{
				Size = new Vector2(0.012f, 0.07f),
				Material = new StandardMaterial3D
				{
					AlbedoColor = new Color(0.75f, 0.8f, 0.85f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.1f,
					BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
				},
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityAabb = new Aabb(new Vector3(-len, -8, -2), new Vector3(len * 2, 9, 4)),
		};
	}
}
