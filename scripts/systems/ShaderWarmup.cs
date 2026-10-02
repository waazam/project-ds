using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// The whole level's shaders, built while the screen is still black (the optimization pass, 2026-10-02: the full
/// test run's hitches of 120-150 ms, a tenth of a second each time something was first seen, were the renderer
/// building that thing's pipeline the first time it was drawn). Once the level has built (the underground with it),
/// one of every kind of thing it will draw is drawn once, tiny, just in front of the camera, for a few frames: one
/// mesh for each pair of material and vertex layout (a pipeline is built for the pair), one of each multimesh's, and
/// a copy of each particle system. Then they're cleared and the fade-in goes ahead (GameFlow waits on
/// <see cref="WaitReady"/>, at most a few seconds).
/// </summary>
public partial class ShaderWarmup : Node
{
	public static bool Ready { get; private set; }
	/// <summary>For tests and the log: what was warmed.</summary>
	public static int Warmed { get; private set; }

	private Node3D _held;
	private int _frames;

	public override void _Ready()
	{
		Ready = false;
		_ = Run();
	}

	public override void _ExitTree() { if (_held != null && IsInstanceValid(_held)) _held.QueueFree(); }

	private async Task Run()
	{
		var tree = GetTree();
		// wait for the level to have built: the underground's render budget runs once it all has (or a few seconds)
		ulong t0 = Time.GetTicksMsec();
		while (Time.GetTicksMsec() - t0 < 6000)
		{
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			if (tree.CurrentScene?.GetNodeOrNull<RenderBudget>("RenderBudget") is { Done: true }) break;
			if (Time.GetTicksMsec() - t0 > 1500 && tree.CurrentScene?.GetNodeOrNull("RenderBudget") == null) break;   // (no underground in this level)
		}
		var cam = GetViewport()?.GetCamera3D();
		var root = tree.CurrentScene;
		if (cam == null || root == null) { Ready = true; return; }
		_held = new Node3D { Name = "ShaderWarmUp" };
		cam.AddChild(_held);
		_held.Position = new Vector3(0, 0, -0.6f);
		var seen = new HashSet<(ulong, ulong)>();
		int n = 0;
		void Place(GeometryInstance3D g)
		{
			_held.AddChild(g);
			g.Position = new Vector3((n % 24 - 12) * 0.002f, (n / 24 % 24 - 12) * 0.002f, 0);
			g.Scale = Vector3.One * 0.0004f;
			n++;
		}
		void Walk(Node node)
		{
			if (node == _held) return;
			switch (node)
			{
				case MeshInstance3D mi when mi.Mesh != null && mi.IsVisibleInTree():
				{
					bool fresh = false;
					for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
					{
						var m = mi.GetSurfaceOverrideMaterial(s) ?? mi.MaterialOverride ?? mi.Mesh.SurfaceGetMaterial(s);
						if (m == null) continue;
						var key = (m.GetRid().Id, (ulong)(mi.Mesh is ArrayMesh am ? (long)am.SurfaceGetFormat(s) : 0) ^ (mi.Skin != null ? 1ul << 62 : 0));
						if (seen.Add(key)) fresh = true;
					}
					if (mi.MaterialOverlay != null && seen.Add((mi.MaterialOverlay.GetRid().Id, 1))) fresh = true;
					if (fresh)
						Place(new MeshInstance3D { Mesh = mi.Mesh, MaterialOverride = mi.MaterialOverride, MaterialOverlay = mi.MaterialOverlay, CastShadow = mi.CastShadow });
					break;
				}
				case MultiMeshInstance3D mm when mm.Multimesh?.Mesh != null && mm.IsVisibleInTree():
				{
					var mesh = mm.Multimesh.Mesh;
					bool fresh = false;
					for (int s = 0; s < mesh.GetSurfaceCount(); s++)
					{
						var m = mm.MaterialOverride ?? mesh.SurfaceGetMaterial(s);
						if (m != null && seen.Add((m.GetRid().Id, 1ul << 61))) fresh = true;
					}
					if (fresh)
					{
						var one = new MultiMesh { TransformFormat = mm.Multimesh.TransformFormat, UseColors = mm.Multimesh.UseColors, UseCustomData = mm.Multimesh.UseCustomData, Mesh = mesh, InstanceCount = 1 };
						one.SetInstanceTransform(0, Transform3D.Identity);
						Place(new MultiMeshInstance3D { Multimesh = one, MaterialOverride = mm.MaterialOverride });
					}
					break;
				}
				case GpuParticles3D gp when gp.ProcessMaterial != null && gp.IsVisibleInTree():
					if (seen.Add((gp.ProcessMaterial.GetRid().Id, 1ul << 60)))
					{
						var d = (GpuParticles3D)gp.Duplicate();
						d.Emitting = true;
						d.OneShot = false;
						Place(d);
					}
					break;
			}
			foreach (var c in node.GetChildren()) Walk(c);
		}
		Walk(root);
		Warmed = n;
		GD.Print($"[perf] shader warm-up: {n} things drawn once, tiny, before the fade-in");
		// a few frames before the eyes (the pipelines are built on the first; the shadows' on the next)
		for (int f = 0; f < 4; f++) await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		_held.QueueFree();
		_held = null;
		Ready = true;
	}

	/// <summary>Waits (at most <paramref name="maxSeconds"/>) for the level's warm-up to finish.</summary>
	public static async Task WaitReady(Node from, CancellationToken ct, double maxSeconds = 8)
	{
		ulong t0 = Time.GetTicksMsec();
		while (!Ready && Time.GetTicksMsec() - t0 < maxSeconds * 1000)
		{
			ct.ThrowIfCancellationRequested();
			await from.ToSignal(from.GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}
}
