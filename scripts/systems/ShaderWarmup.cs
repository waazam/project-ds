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
///
/// <para><b>Things an event makes on the spot</b> (the lag pass, 2026-10-13: "laggy when certain events are about to
/// happen"; the 120-150 ms the old logger saw were the engine's capped delta, the real frames ran to seconds): a
/// creature or effect built at its moment brings shaders nothing in the level had drawn yet, compiled as it first shows
/// (Act 7's giant: a 3.2 s freeze as it walked out of the murk). <see cref="Prewarm"/> takes a throwaway copy built at
/// the level's load: drawn with the rest in the black, then freed. <see cref="WarmSubtree"/> warms a thing built ahead of
/// its moment, during play, the same way (tiny, in front of the camera, a few frames).</para>
/// </summary>
public partial class ShaderWarmup : Node
{
	public static bool Ready { get; private set; }
	/// <summary>For tests and the log: what was warmed.</summary>
	public static int Warmed { get; private set; }

	private Node3D _held;

	public override void _Ready()
	{
		Ready = false;
		_ = Run();
	}

	public override void _ExitTree() { if (_held != null && IsInstanceValid(_held)) _held.QueueFree(); }

	private async Task Run()
	{
		var tree = GetTree();
		// wait for the level to have built: the underground's render budget runs once it all has
		// (the lag pass, 2026-10-13: this gave up after 6 s, and a save from Act 14 on, which builds the whole underground
		// and the winter at load, faded in while it was still building: the lodge's dressing, five seconds on the main
		// thread, froze the first view, and the warm-up had missed everything built after it. Now it waits, in the black,
		// for as long as the building takes, and a moment more for what's hung on a short timer after its build.)
		ulong t0 = Time.GetTicksMsec();
		bool built = false;
		while (Time.GetTicksMsec() - t0 < 50000)
		{
			await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			if (tree.CurrentScene?.GetNodeOrNull<RenderBudget>("RenderBudget") is { Done: true }) { built = true; break; }
			if (Time.GetTicksMsec() - t0 > 1500 && tree.CurrentScene?.GetNodeOrNull("RenderBudget") == null) break;   // (no underground in this level)
		}
		if (built)
			for (ulong t1 = Time.GetTicksMsec(); Time.GetTicksMsec() - t1 < 600;) await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		StoryManager.LoadStage("shader warm-up starting");
		var cam = GetViewport()?.GetCamera3D();
		var root = tree.CurrentScene;
		if (cam == null || root == null) { Ready = true; return; }
		_held = new Node3D { Name = "ShaderWarmUp" };
		cam.AddChild(_held);
		_held.Position = new Vector3(0, 0, -0.6f);
		var seen = new HashSet<(ulong, ulong)>();
		int n = 0;
		Walk(root, _held, seen, ref n, false);
		// the pickups' white outline on the items' own vertex layout (MeshKit's), for one revealed later in the level
		var kit = new World.MeshKit();
		kit.Mat(new StandardMaterial3D());
		kit.Box(Vector3.Zero, Vector3.One * 0.1f);
		var outlined = new MeshInstance3D { Name = "OutlineWarm", Mesh = kit.Commit() };
		outlined.SetMeta(World.ItemOutline.Meta, true);
		outlined.MaterialOverlay = World.ItemOutline.WarmOverlay(outlined);
		Place(_held, outlined, ref n);
		// the skinned ones drawn as themselves (their skinning set up too), each for those few frames
		var placed = new List<IWarmUp>();
		foreach (var w in tree.GetNodesInGroup("warm_up_self"))
			if (w is IWarmUp wu) { wu.WarmUp(cam, true); placed.Add(wu); n++; }
		// the shadow-casting lights kept off until their moment (a closet's bulb, a lamp that comes on): on for these frames,
		// in the black, so the shadow maps of what's round them are drawn once now and not on the moment
		var lit = new List<Light3D>();
		foreach (var l in root.FindChildren("*", "Light3D", true, false))
			if (l is Light3D light && light.ShadowEnabled && !light.Visible && light.GetParent() is Node3D p && p.IsVisibleInTree() && light.LightEnergy > 0f)
			{ light.Visible = true; lit.Add(light); }
		Warmed = n;
		GD.Print($"[perf] shader warm-up: {n} things drawn once, tiny, before the fade-in; {lit.Count} hidden shadowed lights lit for it");
		// a few frames before the eyes (the pipelines are built on the first; the shadows' on the next)
		for (int f = 0; f < 4; f++) await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		_held.QueueFree();
		_held = null;
		foreach (var l in lit) if (IsInstanceValid(l)) l.Visible = false;
		foreach (var wu in placed) if (wu is Node node && IsInstanceValid(node)) wu.WarmUp(cam, false);
		// the throwaway copies of things events make later (Prewarm): drawn above, done with
		foreach (var t in tree.GetNodesInGroup(PrewarmGroup)) t.QueueFree();
		Ready = true;
	}

	/// <summary>One tiny copy, in front of the camera, of each kind of thing under <paramref name="node"/> not yet in
	/// <paramref name="seen"/> (hidden things too when <paramref name="force"/>, or in the group "warm_up").</summary>
	private static void Walk(Node node, Node3D held, HashSet<(ulong, ulong)> seen, ref int n, bool force)
	{
		if (node == held) return;
		force |= node.IsInGroup("warm_up");
		switch (node)
		{
			case MeshInstance3D mi when mi.Mesh != null && (mi.IsVisibleInTree() || force):
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
				// a pickup's white outline (World.ItemOutline): its overlay is on only near it, so it's drawn here with the
				// outline's shared pair, once for each vertex layout an item has
				var overlay = mi.MaterialOverlay;
				if (overlay == null && World.ItemOutline.WarmOverlay(mi) is { } outline)
				{
					overlay = outline;
					for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
						if (seen.Add((outline.GetRid().Id, 3ul << 58 ^ (ulong)(mi.Mesh is ArrayMesh om ? (long)om.SurfaceGetFormat(s) : 0)))) fresh = true;
				}
				if (fresh)
				{
					var copy = new MeshInstance3D { Mesh = mi.Mesh, MaterialOverride = mi.MaterialOverride, MaterialOverlay = overlay, CastShadow = mi.CastShadow };
					// (each surface's own override too: without it the copy drew the mesh's imported materials, and the
					// ones the thing is really drawn with, a creature's skin set on its model, were never warmed)
					for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
						if (mi.GetSurfaceOverrideMaterial(s) is { } so) copy.SetSurfaceOverrideMaterial(s, so);
					Place(held, copy, ref n);
				}
				break;
			}
			case MultiMeshInstance3D mm when mm.Multimesh?.Mesh != null && (mm.IsVisibleInTree() || force):
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
					Place(held, new MultiMeshInstance3D { Multimesh = one, MaterialOverride = mm.MaterialOverride }, ref n);
				}
				break;
			}
			case GpuParticles3D gp when gp.ProcessMaterial != null && (gp.IsVisibleInTree() || force):
				if (seen.Add((gp.ProcessMaterial.GetRid().Id, 1ul << 60)))
				{
					var d = (GpuParticles3D)gp.Duplicate();
					d.Emitting = true;
					d.OneShot = false;
					d.Visible = true;
					Place(held, d, ref n);
				}
				break;
		}
		foreach (var c in node.GetChildren()) Walk(c, held, seen, ref n, force);
	}

	private static void Place(Node3D held, GeometryInstance3D g, ref int n)
	{
		held.AddChild(g);
		g.Position = new Vector3((n % 24 - 12) * 0.002f, (n / 24 % 24 - 12) * 0.002f, 0);
		g.Scale = Vector3.One * 0.0004f;
		n++;
	}

	private const string PrewarmGroup = "warm_up_throwaway";

	/// <summary>A throwaway copy of something an event will make on the spot (its materials as the event's will be):
	/// kept hidden in the level just long enough for the load's warm-up to draw it, then freed. Called from a _Ready (the
	/// level's load, in the black); called later, it's warmed there and then.</summary>
	public static void Prewarm(Node from, Node3D thing, System.Action<Node3D> ready = null)
	{
		var root = from.GetTree()?.CurrentScene;
		if (root == null) { thing.Free(); return; }
		thing.Visible = false;
		thing.AddToGroup("warm_up");
		thing.AddToGroup(PrewarmGroup);
		thing.ProcessMode = ProcessModeEnum.Disabled;
		thing.Position = new Vector3(0, -500f, 0);   // (far under the level, should anything about it show)
		// (deferred: from a _Ready the level is still being built; by the end of the frame its warm-up is under way, or not)
		Callable.From(() => { _ = AddThrowaway(); }).CallDeferred();

		async Task AddThrowaway()
		{
			if (!IsInstanceValid(thing) || !IsInstanceValid(root) || !root.IsInsideTree()) return;
			root.AddChild(thing);
			ready?.Invoke(thing);   // (in the tree, built: what the event does to it first, an eye lit, a part shown)
			if (!Ready) return;   // a warm-up is under way: it draws this with the rest, and frees it
			await WarmSubtree(thing);
			if (IsInstanceValid(thing)) thing.QueueFree();
		}
	}

	/// <summary>Draws one tiny copy of each kind of thing under <paramref name="root"/> (hidden or not) in front of the
	/// camera for a few frames, so their pipelines are built now and not the first time they're seen. For a thing built
	/// ahead of its moment in play (a few frames' cost each, nothing seen).</summary>
	public static async Task WarmSubtree(Node root, int frames = 3)
	{
		if (root == null || !IsInstanceValid(root) || !root.IsInsideTree()) return;
		var tree = root.GetTree();
		var cam = root.GetViewport()?.GetCamera3D();
		if (cam == null) return;
		var held = new Node3D { Name = "WarmUpNow", Position = new Vector3(0, 0, -0.6f) };
		cam.AddChild(held);
		int n = 0;
		Walk(root, held, new HashSet<(ulong, ulong)>(), ref n, true);
		for (int f = 0; f < frames; f++) await root.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		if (IsInstanceValid(held)) held.QueueFree();
	}

	/// <summary>Something hidden at the start whose first sight must not stutter: put in front of the camera (unseen,
	/// e.g. fully dissolved) for the warm-up's frames, then put back.</summary>
	public interface IWarmUp
	{
		void WarmUp(Camera3D cam, bool on);
	}

	/// <summary>Waits (at most <paramref name="maxSeconds"/>) for the level's warm-up to finish (which waits for the level
	/// to have built: up to about 50 s on a slow machine loading a late save, all of it in the black).</summary>
	public static async Task WaitReady(Node from, CancellationToken ct, double maxSeconds = 60)
	{
		ulong t0 = Time.GetTicksMsec();
		while (!Ready && Time.GetTicksMsec() - t0 < maxSeconds * 1000)
		{
			ct.ThrowIfCancellationRequested();
			await from.ToSignal(from.GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}
}
