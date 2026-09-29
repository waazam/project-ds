using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Act 22's trees: the winter forest either side of the plowed road, thick right up to the windrows and on into
/// the dark. Firs heavy with snow (the world's "winter" frost on them), grey dead snags, black bare broadleaves;
/// toward the lodge they are caked in ice (a glassy glaze drawn over them) and hung with icicles. MultiMeshes in
/// 96 m chunks that fade out past the murk, and a trunk collider for every tree the player could reach.
/// </summary>
public partial class WinterWoods
{
	public const float TreeChunk = 96f;
	/// <summary>Where the trees are (church-local xz) and how big: the wendigo leaps into them.</summary>
	public static readonly List<(Vector2 at, float height, bool fir)> TreeSpots = new();

	private void BuildTrees()
	{
		var kinds = new List<(Mesh mesh, float weight, float h, float maxR, float crown, bool fir)>
		{
			(ForestScatter.FirMesh(221, 22f, 0.38f, 0.3f, 11, 3.4f, 0.16f), 3f, 22f, 3.4f, 0.3f, true),
			(ForestScatter.FirMesh(222, 16f, 0.3f, 0.26f, 9, 2.8f, 0.2f), 3f, 16f, 2.8f, 0.26f, true),
			(ForestScatter.FirMesh(223, 28f, 0.46f, 0.38f, 12, 3.9f, 0.14f), 1.6f, 28f, 3.9f, 0.38f, true),
			(ForestScatter.SnagMesh(224, 15f), 1.1f, 15f, 0.5f, 0.9f, false),
			(WinterGlade.BareTreeMesh(225, 14f), 1.6f, 14f, 3f, 0.4f, false),
			(WinterGlade.BareTreeMesh(226, 18f), 1.2f, 18f, 3.5f, 0.4f, false),
		};
		float total = 0; foreach (var k in kinds) total += k.weight;
		// per chunk: per kind, the plain trees and the iced ones; icicles; trunks
		var chunks = new Dictionary<Vector2I, (List<Transform3D>[] plain, List<Transform3D>[] iced, List<Transform3D> icicles, List<Vector3> trunks)>();
		(List<Transform3D>[] plain, List<Transform3D>[] iced, List<Transform3D> icicles, List<Vector3> trunks) ChunkAt(Vector2 p)
		{
			var key = new Vector2I(Mathf.FloorToInt(p.X / TreeChunk), Mathf.FloorToInt(p.Y / TreeChunk));
			if (!chunks.TryGetValue(key, out var c))
			{
				c = (new List<Transform3D>[kinds.Count], new List<Transform3D>[kinds.Count], new List<Transform3D>(), new List<Vector3>());
				for (int i = 0; i < kinds.Count; i++) { c.plain[i] = new List<Transform3D>(); c.iced[i] = new List<Transform3D>(); }
				chunks[key] = c;
			}
			return c;
		}
		const float cell = 6.4f;
		var lodge = SkiLodge.OriginLocal;
		// walk the road, and scatter across it on a jittered grid (each grid cell visited once: keyed)
		var seen = new HashSet<Vector2I>();
		for (int i = 0; i < Points; i += 4)
		{
			var c0 = RoadPoint(i);
			int gx0 = Mathf.FloorToInt((c0.X - RegionHalf) / cell), gx1 = Mathf.FloorToInt((c0.X + RegionHalf) / cell);
			int gz0 = Mathf.FloorToInt((c0.Y - RegionHalf) / cell), gz1 = Mathf.FloorToInt((c0.Y + RegionHalf) / cell);
			for (int gx = gx0; gx <= gx1; gx++)
				for (int gz = gz0; gz <= gz1; gz++)
				{
					var key = new Vector2I(gx, gz);
					if (!seen.Add(key)) continue;
					var rng = new RandomNumberGenerator { Seed = (ulong)(gx * 73856093 ^ gz * 19349663) + 7 };
					var p = new Vector2((gx + 0.5f) * cell + rng.RandfRange(-2.6f, 2.6f), (gz + 0.5f) * cell + rng.RandfRange(-2.6f, 2.6f));
					float d = Nearest(p.X, p.Y, out float s, out _);
					if (d > RegionHalf - 3f) continue;
					if (new Vector2(p.X - WinterGlade.Centre.X, p.Y - WinterGlade.Centre.Y).Length() < WinterGlade.Radius - 10f) continue;   // the clearing's own
					if (SkiLodge.NearLodge(p.X, p.Y, 62f) && Mathf.Abs(p.X - lodge.X) < SkiLodge.WingX1 + 14f && p.Y > lodge.Z - SkiLodge.WingHalfZ - 16f) continue;
					if (Mathf.Abs(p.X - lodge.X) < SkiLodge.WingX1 + 8f && p.Y > lodge.Z - SkiLodge.WingHalfZ - 12f && p.Y < lodge.Z + SkiLodge.Apothem + 26f) continue;
					float keep = d < 8f ? 0f : d < 12f ? 0.35f : d < 60f ? 0.85f : 0.55f;
					if (rng.Randf() > keep || NearProp(p, 5f)) continue;
					float pick = rng.RandfRange(0, total);
					int kind = 0;
					for (; kind < kinds.Count - 1; kind++) { pick -= kinds[kind].weight; if (pick <= 0) break; }
					float sc = rng.RandfRange(0.8f, 1.2f);
					float yaw = rng.RandfRange(0, Mathf.Tau);
					var basis = new Basis(Vector3.Up, yaw).Scaled(Vector3.One * sc);
					var at = new Vector3(p.X, Height(p.X, p.Y) - 0.1f, p.Y);
					var ch = ChunkAt(p);
					bool iced = rng.Randf() < FrozenAt(s) * 0.95f;
					(iced ? ch.iced : ch.plain)[kind].Add(new Transform3D(basis, at));
					if (d < 80f) ch.trunks.Add(at);
					TreeSpots.Add((p, kinds[kind].h * sc, kinds[kind].fir));
					if (iced && kinds[kind].fir)
					{
						// icicles under the tiers' edges
						float H = kinds[kind].h * sc, R = kinds[kind].maxR * sc, cs = kinds[kind].crown;
						int n = rng.RandiRange(8, 16);
						for (int j = 0; j < n; j++)
						{
							float f = rng.RandfRange(cs + 0.05f, 0.8f);
							float r = R * (1f - (f - cs) / (1f - cs)) * rng.RandfRange(0.75f, 0.98f);
							float a = rng.RandfRange(0, Mathf.Tau);
							float len = Mathf.Pow(rng.Randf(), 2f) * 0.9f + 0.15f;
							ch.icicles.Add(new Transform3D(Basis.Identity.Scaled(new Vector3(1f, len, 1f)), at + new Vector3(Mathf.Cos(a) * r, H * f - 0.1f, Mathf.Sin(a) * r)));
						}
					}
					TreeCount++;
				}
		}
		foreach (var (key, c) in chunks)
		{
			var holder = new Node3D { Name = $"Trees_{key.X}_{key.Y}" };
			AddChild(holder);
			for (int k = 0; k < kinds.Count; k++)
			{
				AddTrees(holder, kinds[k].mesh, c.plain[k], $"K{k}", null);
				AddTrees(holder, kinds[k].mesh, c.iced[k], $"K{k}Ice", IceOverlay);
			}
			if (c.icicles.Count > 0)
			{
				var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = IcicleMesh, InstanceCount = c.icicles.Count };
				for (int j = 0; j < c.icicles.Count; j++) mm.SetInstanceTransform(j, c.icicles[j]);
				holder.AddChild(new MultiMeshInstance3D { Name = "Icicles", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = 90f, VisibilityRangeEndMargin = 10f });
			}
			if (c.trunks.Count > 0)
			{
				var body = new StaticBody3D { Name = "Trunks", CollisionLayer = 1, CollisionMask = 0 };
				body.SetMeta("surface", "wood");
				foreach (var t in c.trunks)
					body.AddChild(new CollisionShape3D { Position = t + Vector3.Up * 2.2f, Shape = TrunkShape });
				holder.AddChild(body);
			}
		}
	}

	private static CylinderShape3D _trunk;
	private static CylinderShape3D TrunkShape => _trunk ??= new CylinderShape3D { Radius = 0.42f, Height = 4.6f };

	private static void AddTrees(Node3D holder, Mesh mesh, List<Transform3D> list, string name, Material overlay)
	{
		if (list.Count == 0) return;
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = list.Count };
		for (int i = 0; i < list.Count; i++) mm.SetInstanceTransform(i, list[i]);
		holder.AddChild(new MultiMeshInstance3D
		{
			Name = name, Multimesh = mm, MaterialOverlay = overlay,
			VisibilityRangeEnd = 175f, VisibilityRangeEndMargin = 15f,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	// ------------------------------------------------------------------ ice and snow

	private static StandardMaterial3D _ice, _iceOverlay, _snowStd;
	private static Mesh _icicle;

	/// <summary>Clear, cold, glassy ice (icicles, the glaze on the lodge's door): pale blue, a sharp sheen, a little see-through.</summary>
	public static StandardMaterial3D IceMat => _ice ??= new StandardMaterial3D
	{
		ResourceName = "ice", AlbedoColor = new Color(0.5f, 0.64f, 0.8f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		Roughness = 0.06f, MetallicSpecular = 0.85f, Metallic = 0.1f,
		RimEnabled = true, Rim = 0.35f, RimTint = 0.6f,
	};

	/// <summary>The ice caking the trees near the lodge: a thin glassy shell drawn over the tree (grown a hair off
	/// it), so the bark and the needles show through dulled and glazed.</summary>
	public static StandardMaterial3D IceOverlay => _iceOverlay ??= new StandardMaterial3D
	{
		ResourceName = "ice_overlay", AlbedoColor = new Color(0.62f, 0.74f, 0.88f, 0.42f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		Roughness = 0.1f, MetallicSpecular = 0.8f, Grow = true, GrowAmount = 0.035f,
	};

	/// <summary>An icicle: a thin tapering cone 1 m long (scaled per instance), a few faces.</summary>
	public static Mesh IcicleMesh
	{
		get
		{
			if (_icicle != null) return _icicle;
			var k = new MeshKit();
			k.Mat(IceMat);
			k.Color = Colors.White;
			k.Cylinder(Vector3.Zero, Vector3.Down, 0.045f, 0.003f, 5, false);
			return _icicle = k.Commit();
		}
	}

	/// <summary>A snow material for props and roofs: the fresh-snow texture, world-triplanar.</summary>
	public static StandardMaterial3D SnowStd(string name, float scale = 0.3f, float bright = 0.9f)
	{
		var m = new StandardMaterial3D
		{
			ResourceName = name, AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/snow/snow_albedo.png"), AlbedoColor = Colors.White * bright,
			NormalEnabled = true, NormalTexture = GD.Load<Texture2D>("res://assets/textures/snow/snow_normal.png"), NormalScale = 0.5f,
			Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * scale, Roughness = 0.92f,
		};
		m.SetMeta("detail_kind", (int)DetailKit.Kind.Snow);
		return m;
	}

	public static StandardMaterial3D PropSnow => _snowStd ??= SnowStd("winter_prop_snow", 0.8f, 0.92f);

	/// <summary>Soft, fresh snow blown in indoors (the lodge's drifts): the same snow at a broader grain, a touch
	/// cooler and smoother (at the props' grain, heaped on carpet under lamplight, it read as grey granite).</summary>
	public static StandardMaterial3D SoftSnow
	{
		get
		{
			if (_softSnow != null) return _softSnow;
			// near-white with only the snow's fine grain over it (the photo at any scale read as marble indoors)
			_softSnow = new StandardMaterial3D
			{
				ResourceName = "winter_soft_snow", AlbedoColor = new Color(0.74f, 0.77f, 0.84f), Roughness = 0.85f,
				NormalEnabled = true, NormalTexture = GD.Load<Texture2D>("res://assets/textures/snow/snow_normal.png"), NormalScale = 0.35f,
				Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 0.6f,
			};
			DetailKit.Apply(_softSnow, DetailKit.Kind.Snow);
			return _softSnow;
		}
	}
	private static StandardMaterial3D _softSnow;
}
