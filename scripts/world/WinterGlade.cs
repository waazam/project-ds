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
	}

	/// <summary>The trailer's church shots want snow whatever the save says.</summary>
	public static bool ForceSnow;
	private double _check;
	/// <summary>How heavily it snows round the church (Weather's 0..1).</summary>
	public const float GladeSnow = 0.85f;

	/// <summary>The snow round the church (the weather pass: Weather's, wrapping round the camera; its roof and the trees
	/// keep it off, so from inside it falls past the windows), while the camera's near enough to see it.</summary>
	public override void _Process(double delta)
	{
		_check -= delta;
		if (_check > 0) return;
		_check = 0.25;
		var cam = GetViewport()?.GetCamera3D();
		bool near = cam != null && cam.GlobalPosition.DistanceTo(ToGlobal(new Vector3(Centre.X, 0, Centre.Y))) < Radius + 120f
			&& (ForceSnow || (Systems.StoryManager.Instance?.Current ?? Systems.Checkpoint.None) >= Systems.Checkpoint.Act20Finished);
		if (near != _snowing || near)
		{
			_snowing = near;
			Weather.Get(this).RequestSnow(this, near ? GladeSnow : 0f, 3f);
		}
	}
	private bool _snowing;

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

}
