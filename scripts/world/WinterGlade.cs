using System.Collections.Generic;
using Godot;
using ProjectDS.World.ChurchParts;

namespace ProjectDS.World;

/// <summary>
/// The winter woods round the church (the owner: back on the surface, and it's winter now: the passage of
/// time; the trees and the ground frosted; dead trees, and trees with no leaves, only branches, sprinkled
/// through). Seen through the church's clear aisle windows: a rolling snowfield, a ring of snow-laden
/// firs (the forest's own firs: every tree in the world frosts over from here on, through the "winter"
/// shader value), grey dead snags, bare black-branched trees with snow along their limbs, and snow
/// falling, never inside the church.
///
/// Local space: the church's (this is its child); the ground sits a little below the nave's floor.
/// </summary>
public partial class WinterGlade : Node3D
{
	public const float GroundY = -0.35f, Radius = 190f;
	public static readonly Vector2 Centre = new(-4f, 44f);
	public int Trees { get; private set; }

	private readonly RandomNumberGenerator _rng = new() { Seed = 2112 };

	/// <summary>The snow's height at local (x, z): flat round the church, then slow drifts and low rises
	/// climbing gently toward the edge so the horizon is trees, not a rim.</summary>
	public static float HeightAt(float x, float z)
	{
		float d = OutsideChurch(x, z);
		float swell = Mathf.SmoothStep(8f, 40f, d);
		float n = Mathf.Sin(x * 0.045f + 1.3f) * Mathf.Cos(z * 0.038f - 0.7f) * 2.2f + Mathf.Sin(x * 0.11f + z * 0.07f) * 0.7f;
		float rise = Mathf.Max(0f, new Vector2(x - Centre.X, z - Centre.Y).Length() - 110f) * 0.12f;
		return GroundY + swell * (n + 1.2f + rise);
	}

	/// <summary>How far a point is outside the church's footprint (0 inside it).</summary>
	private static float OutsideChurch(float x, float z)
	{
		float dx = Mathf.Max(Mathf.Max(Church.VestryX1 - 1f - x, x - (Church.TransHalf + 1.5f)), 0f);
		float dz = Mathf.Max(Mathf.Max(-2f - z, z - (Church.ChancelEnd + Church.ApseR + 1f)), 0f);
		return new Vector2(dx, dz).Length();
	}

	public override void _Ready()
	{
		BuildGround();
		BuildTrees();
		BuildSnowfall();
	}

	private readonly System.Collections.Generic.List<GpuParticles3D> _snow = new();
	/// <summary>The trailer's church shots want snow whatever the save says.</summary>
	public static bool ForceSnow;
	private double _check;

	/// <summary>The snowfall only runs while the camera is near enough to see it (the optimization pass:
	/// fourteen thousand flakes were falling all game, underground and in the summer woods).</summary>
	public override void _Process(double delta)
	{
		_check -= delta;
		if (_check > 0) return;
		_check = 0.25;
		var cam = GetViewport()?.GetCamera3D();
		bool near = cam != null && cam.GlobalPosition.DistanceTo(ToGlobal(new Vector3(Centre.X, 0, Centre.Y))) < Radius + 120f
			&& (ForceSnow || (Systems.StoryManager.Instance?.Current ?? Systems.Checkpoint.None) >= Systems.Checkpoint.Act20Finished);
		foreach (var p in _snow)
			if (p.Emitting != near)
			{
				p.Emitting = near;
				p.Visible = near;
				if (near) p.Restart();   // already falling (its preprocess), not starting from the sky
			}
	}

	private void BuildGround()
	{
		var k = new MeshKit();
		k.Mat(ChurchTextures.SnowMat);
		const float cell = 5f;
		for (float x = Centre.X - Radius; x < Centre.X + Radius; x += cell)
			for (float z = Centre.Y - Radius; z < Centre.Y + Radius; z += cell)
			{
				if (new Vector2(x + cell * 0.5f - Centre.X, z + cell * 0.5f - Centre.Y).Length() > Radius) continue;
				// under the church (and its crypt) there's no snow to draw
				if (OutsideChurch(x + cell * 0.5f, z + cell * 0.5f) <= 0f && OutsideChurch(x, z) <= 0f && OutsideChurch(x + cell, z + cell) <= 0f) continue;
				Vector3 P(float px, float pz) => new(px, HeightAt(px, pz), pz);
				Vector3 a = P(x, z), b = P(x + cell, z), c = P(x + cell, z + cell), d = P(x, z + cell);
				Vector3 n = (d - a).Cross(b - a).Normalized();
				if (n.Y < 0) n = -n;
				k.Color = Colors.White * (0.92f + 0.08f * Mathf.Sin(x * 0.3f + z * 0.2f));
				k.Quad(a, b, c, d, n, new Vector2(x, z), new Vector2(x + cell, z), new Vector2(x + cell, z + cell), new Vector2(x, z + cell));
			}
		var mi = k.CommitTo(this, "Snowfield", false);
		// something to stand on round the walls (Act 22 may go outside): a plain collision slab
		var body = new StaticBody3D { Name = "SnowBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "snow");
		// four slabs round the church's footprint, never under it (its crypt and the crypt's stairs are down there)
		float x0 = Church.VestryX1 - 1f, x1 = Church.TransHalf + 1.5f, z0 = -2f, z1 = Church.ChancelEnd + Church.ApseR + 1f;
		float X0 = Centre.X - 60f, X1 = Centre.X + 60f, Z0 = Centre.Y - 75f, Z1 = Centre.Y + 75f;
		foreach (var (a0, a1, b0, b1) in new[] { (X0, x0, Z0, Z1), (x1, X1, Z0, Z1), (x0, x1, Z0, z0), (x0, x1, z1, Z1) })
			body.AddChild(new CollisionShape3D { Position = new Vector3((a0 + a1) * 0.5f, GroundY - 0.5f, (b0 + b1) * 0.5f), Shape = new BoxShape3D { Size = new Vector3(a1 - a0, 1f, b1 - b0) } });
		AddChild(body);
	}

	private void BuildTrees()
	{
		var kinds = new List<(Mesh mesh, float weight, float scale)>
		{
			(ForestScatter.FirMesh(71, 24f, 0.4f, 0.34f, 12, 3.6f, 0.14f), 3f, 1f),
			(ForestScatter.FirMesh(72, 17f, 0.3f, 0.26f, 10, 2.9f, 0.18f), 3f, 1f),
			(ForestScatter.FirMesh(73, 30f, 0.48f, 0.4f, 13, 4.1f, 0.12f), 1.5f, 1f),
			(ForestScatter.SnagMesh(74, 14f), 1.2f, 1f),
			(ForestScatter.SnagMesh(75, 20f), 0.8f, 1f),
			(BareTreeMesh(76, 13f), 1.8f, 1f),
			(BareTreeMesh(77, 17f), 1.4f, 1f),
			(BareTreeMesh(78, 10f), 1.2f, 1f),
		};
		float total = 0; foreach (var (_, w, _) in kinds) total += w;
		var byKind = new List<Transform3D>[kinds.Count];
		for (int i = 0; i < kinds.Count; i++) byKind[i] = new List<Transform3D>();
		var placed = new List<Vector2>();
		for (int tries = 0; tries < 3000 && placed.Count < 300; tries++)
		{
			float a = _rng.RandfRange(0, Mathf.Tau), r = Mathf.Sqrt(_rng.RandfRange(0.02f, 1f)) * (Radius - 10f);
			var p = Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
			if (OutsideChurch(p.X, p.Y) < 16f) continue;
			bool close = false;
			foreach (var q in placed) if (q.DistanceSquaredTo(p) < 36f) { close = true; break; }
			if (close) continue;
			placed.Add(p);
			float pick = _rng.RandfRange(0, total);
			int kind = 0;
			for (; kind < kinds.Count - 1; kind++) { pick -= kinds[kind].weight; if (pick <= 0) break; }
			float s = _rng.RandfRange(0.8f, 1.2f);
			var basis = new Basis(Vector3.Up, _rng.RandfRange(0, Mathf.Tau)).Scaled(Vector3.One * s);
			byKind[kind].Add(new Transform3D(basis, new Vector3(p.X, HeightAt(p.X, p.Y) - 0.1f, p.Y)));
		}
		for (int i = 0; i < kinds.Count; i++)
		{
			if (byKind[i].Count == 0) continue;
			var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = kinds[i].mesh, InstanceCount = byKind[i].Count };
			for (int j = 0; j < byKind[i].Count; j++) mm.SetInstanceTransform(j, byKind[i][j]);
			AddChild(new MultiMeshInstance3D { Name = $"Trees{i}", Multimesh = mm });
			Trees += byKind[i].Count;
		}
	}

	/// <summary>A leafless broadleaf: a trunk that forks, forks again, and again, into a fine black crown of
	/// twigs (the owner: trees with no leaves, just branches). The winter frost lays snow along the tops of
	/// the limbs.</summary>
	public static Mesh BareTreeMesh(int seed, float height)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 6007) };
		var k = new MeshKit();
		k.Mat(ProcTextures.TreeBarkMat);
		k.Color = new Color(0.55f, 0.53f, 0.52f);
		float trunkR = 0.14f + height * 0.018f;
		Vector3 fork = new(rng.RandfRange(-0.4f, 0.4f), height * rng.RandfRange(0.32f, 0.42f), rng.RandfRange(-0.4f, 0.4f));
		ForestScatter.TrunkLoft(k, new List<(Vector3, float, Color)>
		{
			(new Vector3(0, -0.4f, 0), trunkR * 1.5f, k.Color), (new Vector3(0, 0.1f, 0), trunkR * 1.2f, k.Color),
			(fork * 0.5f, trunkR * 1.02f, k.Color), (fork, trunkR * 0.9f, k.Color),
		}, 8, 1f);
		void Branch(Vector3 from, Vector3 dir, float len, float r, int depth)
		{
			Vector3 to = from + dir * len;
			k.Cylinder(from, to, r, r * 0.68f, depth > 1 ? 6 : 4, false, 1f);
			if (depth == 0 || r < 0.012f) return;
			int n = depth > 2 ? 3 : 2 + (rng.Randf() < 0.5f ? 1 : 0);
			for (int i = 0; i < n; i++)
			{
				float spread = rng.RandfRange(0.35f, 0.75f);
				float a = rng.RandfRange(0, Mathf.Tau);
				Vector3 side = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
				Vector3 d = (dir + side * spread + Vector3.Up * rng.RandfRange(0.05f, 0.35f)).Normalized();
				Branch(to, d, len * rng.RandfRange(0.62f, 0.8f), r * 0.68f, depth - 1);
			}
		}
		int limbs = 3 + rng.RandiRange(0, 1);
		for (int i = 0; i < limbs; i++)
		{
			float a = Mathf.Tau * i / limbs + rng.RandfRange(-0.3f, 0.3f);
			Vector3 d = new Vector3(Mathf.Cos(a) * 0.55f, 1f, Mathf.Sin(a) * 0.55f).Normalized();
			Branch(fork, d, height * 0.28f, trunkR * 0.75f, 4);
		}
		return k.Commit();
	}

	/// <summary>Snow falling round the church in four great sheets, one to each side, never over its roof.</summary>
	private void BuildSnowfall()
	{
		var flake = new QuadMesh { Size = new Vector2(0.05f, 0.05f) };
		flake.Material = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.95f, 0.97f, 1f, 0.85f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
		var boxes = new[]
		{
			(new Vector3(Church.VestryX1 - 32f, 0, 44f), new Vector3(60f, 1f, 170f)),
			(new Vector3(Church.TransHalf + 32f, 0, 44f), new Vector3(60f, 1f, 170f)),
			(new Vector3(-4f, 0, -32f), new Vector3(66f, 1f, 56f)),
			(new Vector3(-4f, 0, Church.ChancelEnd + Church.ApseR + 30f), new Vector3(66f, 1f, 56f)),
		};
		int i = 0;
		foreach (var (c, size) in boxes)
		{
			var mat = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = size * 0.5f,
				Direction = new Vector3(0.15f, -1f, 0.05f), Spread = 12f, InitialVelocityMin = 1.1f, InitialVelocityMax = 1.8f,
				Gravity = new Vector3(0.1f, -0.25f, 0), TurbulenceEnabled = true, TurbulenceNoiseStrength = 0.6f, TurbulenceNoiseScale = 6f,
				ScaleMin = 0.6f, ScaleMax = 1.4f,
			};
			var p = new GpuParticles3D
			{
				Name = $"Snow{i++}", Amount = 3500, Lifetime = 26f, Preprocess = 26f, ProcessMaterial = mat, DrawPass1 = flake,
				Position = c + Vector3.Up * 34f, VisibilityAabb = new Aabb(new Vector3(-size.X * 0.5f - 4f, -40f, -size.Z * 0.5f - 4f), new Vector3(size.X + 8f, 44f, size.Z + 8f)),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			AddChild(p);
			_snow.Add(p);
		}
	}
}
