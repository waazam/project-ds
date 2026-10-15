using Godot;

namespace ProjectDS.World;

/// <summary>
/// Sweeps the micro-surface detail onto every opaque standard material in the world as its mesh enters the
/// tree: classed by the mesh's own name, then its parents' (a "Bookcase" node's "Shelves" and "Books"),
/// falling back to plain grime. Materials the factories have already given a kind keep it.
/// </summary>
public partial class DetailSweep : Node
{
	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		GetTree().NodeAdded += OnNodeAdded;
	}

	private void OnNodeAdded(Node n)
	{
		if (n is not GeometryInstance3D g || n is Label3D || n is SpriteBase3D) return;
		Callable.From(() => { if (IsInstanceValid(g)) Sweep(g); }).CallDeferred();
	}

	private static void Sweep(GeometryInstance3D g)
	{
		// (2026-10-15) a pickup, a loose thing that moves, a piece of furniture marked so: out of the stains' reach
		// (DecalDresser.NoDecalLayer; the floor and wall stains stained what stood on them)
		if (g.Layers == 1u)
		{
			Node a = g;
			for (int i = 0; i < 6 && a != null; i++, a = a.GetParent())
				if (a is Pickup or RigidBody3D || a.HasMeta("furniture")) { g.Layers = DecalDresser.NoDecalLayer; break; }
		}
		DetailKit.Kind? kind = null;
		Node p = g;
		for (int i = 0; i < 4 && p != null && kind == null; i++, p = p.GetParent()) kind = DetailKit.Classify(p.Name);
		var k = kind ?? DetailKit.Kind.Grime;
		if (g.MaterialOverride is StandardMaterial3D o) DetailKit.Apply(o, k);
		if (g is MeshInstance3D mi && mi.Mesh != null)
		{
			for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
			{
				if (mi.GetSurfaceOverrideMaterial(s) is StandardMaterial3D so) DetailKit.Apply(so, k);
				if (mi.Mesh.SurfaceGetMaterial(s) is StandardMaterial3D sm) DetailKit.Apply(sm, k);
			}
		}
		else if (g is MultiMeshInstance3D mm && mm.Multimesh?.Mesh is { } mesh)
		{
			for (int s = 0; s < mesh.GetSurfaceCount(); s++)
				if (mesh.SurfaceGetMaterial(s) is StandardMaterial3D sm) DetailKit.Apply(sm, k);
		}
	}
}
