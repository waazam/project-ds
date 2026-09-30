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
	public static float HeightAt(float x, float z) => WinterWoods.Height(x, z);   // one ground for the clearing and Act 22's woods

	/// <summary>How far a point is outside the church's footprint (0 inside it).</summary>
	public static float OutsideChurch(float x, float z)
	{
		float dx = Mathf.Max(Mathf.Max(Church.VestryX1 - 1f - x, x - (Church.TransHalf + 1.5f)), 0f);
		float dz = Mathf.Max(Mathf.Max(-2f - z, z - (Church.ChancelEnd + Church.ApseR + 1f)), 0f);
		return new Vector2(dx, dz).Length();
	}

	public override void _Ready()
	{
		// (the snow on the ground is WinterWoods': one ground from the church's walls to the ski lodge)
		WinterWoods.TreeSpots.Clear();   // the glade is built first: it starts the list
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

	private void BuildTrees()
	{
		// (Act 22's own mix: mostly bare broadleaves, a few firs and snags)
		var kinds = WinterWoods.WinterTreeKinds();
		float total = 0; foreach (var k in kinds) total += k.weight;
		var byKind = new List<Transform3D>[kinds.Count];
		for (int i = 0; i < kinds.Count; i++) byKind[i] = new List<Transform3D>();
		var placed = new List<Vector2>();
		for (int tries = 0; tries < 3000 && placed.Count < 300; tries++)
		{
			float a = _rng.RandfRange(0, Mathf.Tau), r = Mathf.Sqrt(_rng.RandfRange(0.02f, 1f)) * (Radius - 10f);
			var p = Centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
			if (OutsideChurch(p.X, p.Y) < 16f) continue;
			if (WinterWoods.Nearest(p.X, p.Y, out _, out _) < 16f) continue;   // off Act 22's plowed road
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
			WinterWoods.TreeSpots.Add((p, kinds[kind].h * s, kinds[kind].fir));   // the wendigo leaps into these too
		}
		for (int i = 0; i < kinds.Count; i++)
		{
			if (byKind[i].Count == 0) continue;
			var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = kinds[i].mesh, InstanceCount = byKind[i].Count };
			for (int j = 0; j < byKind[i].Count; j++) { mm.SetInstanceTransform(j, byKind[i][j]); mm.SetInstanceCustomData(j, new Color(0, 0, 0, 0)); }
			AddChild(new MultiMeshInstance3D { Name = $"Trees{i}", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
			Trees += byKind[i].Count;
		}
	}

	/// <summary>A leafless broadleaf (now <see cref="WinterTreeKit"/>'s).</summary>
	public static Mesh BareTreeMesh(int seed, float height) => WinterTreeKit.BareTree(seed, height, seed % 2);

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
