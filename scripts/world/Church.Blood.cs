using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// (the gushy-water pass, 2026-10-15: "by water I mean the blood too") The left transept altar's blood still wet and
/// moving: drops gathering on the torn cloth's edge, stretching and falling onto the altar's step, a thick run of it over
/// the step's edge to the floor, and the pool there a live, glossy surface (lobed, a raised rim, slow swells, rings where
/// the run comes in) over the old stain. The smear to the vestry stays as it was (dragged, drying). Only the blood: the
/// altar and the cloth are <c>Church.Gothic.cs</c>'s.
/// </summary>
public partial class Church
{
	/// <summary><paramref name="pool"/>: the pool's middle on the floor; <paramref name="altar"/>: the altar's foot.</summary>
	private void BleedingAltar(Vector3 pool, Vector3 altar)
	{
		var root = new Node3D { Name = "AltarBlood" };
		AddChild(root);
		// the cloth's edge on the pool's side, and the step under it
		foreach (float x in new[] { -0.55f, 0.1f, 0.7f })
			FlowFx.Drips(root, altar + new Vector3(x, 1.2f, -0.32f), FlowFx.Liquid.Blood, 0.45f, 1.0f, 0.03f, "ClothDrips");
		// over the step's edge and down to the floor, toward the pool
		Vector3 edge = altar + new Vector3(0.25f, 0.2f, -0.9f);
		var mat = FlowFx.PourMat(FlowFx.Liquid.Blood, speed: 0.35f, strands: 3f, wobble: 0.006f, aerateLen: 0.2f);
		FlowFx.Sheet(root, new List<Vector3> { edge + new Vector3(0, 0.004f, 0.25f), edge + new Vector3(0, 0.004f, 0.02f), edge + new Vector3(0, -0.06f, -0.02f), edge + new Vector3(0, -0.195f, -0.04f) },
			Vector3.Right, 0.28f, 0.2f, mat, "StepRun", 4);
		// the pool: live and glossy over the stain
		var mi = new MeshInstance3D
		{
			Name = "Pool", Mesh = new PlaneMesh { Size = Vector2.One * 2.1f, SubdivideWidth = 6, SubdivideDepth = 6 },
			Position = pool + Vector3.Up * 0.009f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		var pm = FlowFx.BloodMat(spread: 0.62f, speed: 0.02f, scale: 1.4f);   // (inside the old stain: the wet middle of it)
		Vector3 inLocal = edge - pool;
		pm.SetShaderParameter("source", new Vector2(inLocal.X / 2.1f + 0.5f, inLocal.Z / 2.1f + 0.5f));
		pm.SetShaderParameter("ripple", 0.3f);
		mi.MaterialOverride = pm;
		root.AddChild(mi);
	}
}
