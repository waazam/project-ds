using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Puts the blacklight's marks on the walls, floors and ceilings they were meant for (2026-10-13, the owner: "some of
/// the handprints are floating on nothing"). A moment after a mark is placed (its room has finished building by then),
/// it is checked against the visible geometry round it, along its normal:
/// <list type="bullet">
/// <item>on the surface (within a couple of centimetres): left be;</item>
/// <item>off it (standing out in the air, or sunk behind it): moved onto it;</item>
/// <item>no surface there (over a doorway, off the end of a board): slid along its plane to the nearest spot that has
/// one, up to half a metre;</item>
/// <item>none anywhere near: dropped (words are never dropped: they say something; they're logged instead).</item>
/// </list>
/// The decal (<see cref="UvInk"/>) itself shows only the surface inside its box, so a mark can't float; this makes sure
/// each one has the surface it needs to show at all. Every move is logged (<c>[uvink]</c>) and kept in <see cref="Log"/>.
/// </summary>
public partial class UvInkSettler : Node
{
	private static UvInkSettler _instance;
	private static readonly List<(UvInk.Mark mark, bool pinned, ulong frame)> _pending = new();
	/// <summary>What was moved or dropped, and why (for the audit).</summary>
	public static readonly List<string> Log = new();
	private const int SettleFrames = 45;
	/// <summary>How far out from the surface, and in behind it, a mark looks for it.</summary>
	private const float Out = 0.14f, In = 0.4f;

	public static void Queue(UvInk.Mark m, bool pinned)
	{
		_pending.Add((m, pinned, Engine.GetProcessFrames()));
		if (_instance != null && IsInstanceValid(_instance)) return;
		if (Engine.GetMainLoop() is not SceneTree tree) return;
		_instance = new UvInkSettler { Name = "UvInkSettler", ProcessMode = ProcessModeEnum.Always };
		tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
	}

	public override void _ExitTree() { if (_instance == this) _instance = null; }

	/// <summary>For a Continue: waits (in the black) until the marks placed at the load have settled, at most
	/// <paramref name="maxSeconds"/> (2026-10-25: settling them walks the whole tree and reads the meshes round each, a
	/// 500 ms frame at Act 18's catwalk just after the fade-in).</summary>
	public static async System.Threading.Tasks.Task Settle(Node from, System.Threading.CancellationToken ct, double maxSeconds = 5)
	{
		ulong t0 = Time.GetTicksMsec();
		while (_pending.Count > 0 && _instance != null && IsInstanceValid(_instance) && Time.GetTicksMsec() - t0 < maxSeconds * 1000)
		{
			ct.ThrowIfCancellationRequested();
			await from.ToSignal(from.GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	public override void _Process(double delta)
	{
		if (_pending.Count == 0) return;
		ulong now = Engine.GetProcessFrames();
		List<(UvInk.Mark mark, bool pinned, ulong frame)> due = null;
		for (int i = _pending.Count - 1; i >= 0; i--)
			if (now - _pending[i].frame >= SettleFrames)
			{
				(due ??= new()).Add(_pending[i]);
				_pending.RemoveAt(i);
			}
		if (due == null) return;
		_meshes = null;
		foreach (var (m, pinned, _) in due)
			if (IsInstanceValid(m.Node) && m.Node.IsInsideTree()) Settle(m, pinned);
		_meshes = null;
		_tris.Clear();
	}

	// ------------------------------------------------------------------ the surfaces round a mark

	private List<(MeshInstance3D mi, Aabb box)> _meshes;
	private readonly Dictionary<Mesh, TriangleMesh> _tris = new();

	private void Gather()
	{
		_meshes = new();
		var stack = new Stack<Node>();
		stack.Push(GetTree().Root);
		while (stack.Count > 0)
		{
			var n = stack.Pop();
			if (n is CanvasItem) continue;
			// (hidden ones too: the render budget hides whole places while the player is far from them)
			if (n is MeshInstance3D mi && mi.Mesh != null && mi.CastShadow != GeometryInstance3D.ShadowCastingSetting.ShadowsOnly
				&& mi.Mesh != UvInkBox && !mi.IsInGroup("player"))
				_meshes.Add((mi, mi.GlobalTransform * mi.GetAabb()));
			if (n.IsInGroup("player")) continue;
			foreach (var c in n.GetChildren()) stack.Push(c);
		}
	}

	private static Mesh UvInkBox => UvInk.SharedBox;

	/// <summary>The nearest visible surface to <paramref name="c"/> along <paramref name="n"/> (the first one met coming in
	/// from outside, from <see cref="Out"/> out to <see cref="In"/> in), as the distance along n (negative: behind c) and
	/// its normal; or null.</summary>
	private (float t, Vector3 normal)? Probe(Vector3 c, Vector3 n)
	{
		Vector3 from = c + n * Out, to = c - n * In;
		var seg = new Aabb(from, Vector3.Zero).Expand(to).Grow(0.02f);
		float best = float.MaxValue;
		Vector3 bestN = Vector3.Zero;
		foreach (var (mi, box) in _meshes)
		{
			if (!box.Intersects(seg) || !IsInstanceValid(mi)) continue;
			if (!_tris.TryGetValue(mi.Mesh, out var tm)) _tris[mi.Mesh] = tm = mi.Mesh.GenerateTriangleMesh();
			if (tm == null) continue;
			var inv = mi.GlobalTransform.AffineInverse();
			var hit = tm.IntersectSegment(inv * from, inv * to);
			if (hit.Count == 0) continue;
			Vector3 p = mi.GlobalTransform * (Vector3)hit["position"];
			Vector3 hn = (mi.GlobalBasis.Inverse().Transposed() * (Vector3)hit["normal"]).Normalized();
			// only a surface turned (either winding) mostly the mark's way: not a board edge-on to it
			if (Mathf.Abs(hn.Dot(n)) < 0.6f) continue;
			float along = (from - p).Length();
			if (along < best) { best = along; bestN = hn.Dot(n) < 0 ? -hn : hn; }
		}
		if (best == float.MaxValue) return null;
		return (Out - best, bestN);
	}

	private void Settle(UvInk.Mark m, bool pinned)
	{
		if (_meshes == null) Gather();
		var (c, n) = m.World();
		var node = m.Node;
		string what = Describe(m);
		var hit = Probe(c, n);
		if (hit is { } h)
		{
			if (Mathf.Abs(h.t) <= 0.02f) return;
			node.GlobalPosition += n * h.t;
			Note($"moved {what} {h.t * 100f:+0;-0} cm onto its surface");
			return;
		}
		// no surface under it: the nearest spot along its plane that has one
		var right = node.GlobalBasis.X.Normalized();
		var down = node.GlobalBasis.Z.Normalized();
		foreach (float r in new[] { 0.12f, 0.25f, 0.4f, 0.55f })
			for (int k = 0; k < 8; k++)
			{
				float a = k * Mathf.Tau / 8f;
				Vector3 off = (right * Mathf.Cos(a) + down * Mathf.Sin(a)) * r;
				if (Probe(c + off, n) is { } h2)
				{
					node.GlobalPosition += off + n * h2.t;
					Note($"moved {what} {r * 100f:0} cm along its wall: there was nothing under it");
					return;
				}
			}
		if (pinned) { Note($"KEPT {what}: no surface found near it (words are never dropped)"); return; }
		Note($"dropped {what}: no surface near it");
		UvInk.Marks.Remove(m);
		node.QueueFree();
	}

	private static string Describe(UvInk.Mark m)
	{
		string path = m.Node.GetPath().ToString();
		int cut = path.Length > 80 ? path.Length - 80 : 0;
		var (c, _) = m.World();
		return $"{m.Kind} at ({c.X:0.0}, {c.Y:0.0}, {c.Z:0.0}) {(cut > 0 ? "..." : "")}{path[cut..]}";
	}

	private static void Note(string s) { Log.Add(s); GD.Print("[uvink] " + s); }
}
