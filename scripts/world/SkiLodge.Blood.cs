using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// (the gushy-water pass, 2026-10-15: "by water I mean the blood too") The dining hall's running blood, as blood: thick,
/// dark and wet, moving, not red beads or a flat red plane. Only the blood: the dishes, the tables and the head are
/// <c>SkiLodge.Act23.cs</c>'s.
/// <list type="bullet">
/// <item>The head on the platter (<see cref="BleedingMouth"/>): a thick, slow rope of blood out of its jaw down onto the
/// silver, swelling and narrowing as it comes, drops stretching off the teeth, and the pool it lands in spreading out
/// over the platter (lobed, glossy, a raised rim), rings rolling out from where it lands.</item>
/// <item>The table run (<see cref="BloodRunMat"/>, <see cref="BloodRunOff"/>): the blood dragged down the table creeps
/// along it to the end, glossy, clotting; at the end it pours over the edge in a heavy sheet with a rivulet or two and
/// drops off the lip to the floor.</item>
/// <item>The bowl (<see cref="BowlBlood"/>): the snow melts into blood that wells up from under it, spreading to the
/// rim of the bowl and rising, rippling as it comes.</item>
/// </list>
/// </summary>
public partial class SkiLodge
{
	private static ShaderMaterial _bloodRun;
	/// <summary>The table's dragged blood (UV.x across the run, UV.y metres along it, toward the table's end).</summary>
	private static ShaderMaterial BloodRunMat => _bloodRun ??= FlowFx.BloodMat(run: true, speed: 0.035f, scale: 1.6f);

	/// <summary>Over the end of the table and down: a slow heavy sheet, two rivulets, drops off the lip.</summary>
	private static void BloodRunOff(Node3D holder)
	{
		float x = TableLen * 0.5f;
		var mat = FlowFx.PourMat(FlowFx.Liquid.Blood, speed: 0.5f, strands: 4f, wobble: 0.008f, aerateLen: 0.3f);
		var path = new List<Vector3> { new(x - 0.02f, 0.004f, 0), new(x + 0.01f, -0.02f, 0), new(x + 0.02f, -TableH * 0.5f, 0), new(x + 0.025f, -TableH, 0) };
		FlowFx.Sheet(holder, path, Vector3.Back, 0.2f, 0.12f, mat, "RunOff", 5);
		var rope = FlowFx.PourMat(FlowFx.Liquid.Blood, speed: 0.6f, strands: 2f, tube: true, wobble: 0.004f);
		foreach (float z in new[] { -0.13f, 0.1f })
			FlowFx.Tube(holder, new List<Vector3> { new(x + 0.005f, 0f, z), new(x + 0.012f, -TableH * 0.45f, z * 0.9f), new(x + 0.014f, -TableH * 0.8f, z * 0.85f) }, 0.008f, 0.004f, rope, "Rivulet", 5);
		FlowFx.Drips(holder, new Vector3(x + 0.02f, -0.01f, 0.03f), FlowFx.Liquid.Blood, 0.7f, TableH, 0.08f, "Drips");
		// where it lands on the floor: a pool, spreading
		var pool = new MeshInstance3D
		{
			Name = "RunPool", Mesh = new PlaneMesh { Size = new Vector2(0.7f, 0.6f), SubdivideWidth = 4, SubdivideDepth = 4 },
			Position = new Vector3(x + 0.2f, -TableH + 0.006f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		var pm = FlowFx.BloodMat(spread: 0.85f, speed: 0.03f, scale: 0.7f);
		pm.SetShaderParameter("source", new Vector2(0.25f, 0.5f));
		pm.SetShaderParameter("ripple", 0.35f);
		pool.MaterialOverride = pm;
		holder.AddChild(pool);
	}

	/// <summary>The head's jaw running with blood onto the platter, and the pool spreading over it. Built once the head
	/// is in place (deferred: its mouth is known then); the rope hangs from the head, so it turns with it.</summary>
	private void BleedingMouth(Node3D holder, Vector3 plateTop, float poolFrom, float poolTo)
	{
		// the pool over the platter's own (the static one under it is what's left when it's reloaded): spreading
		var pool = new MeshInstance3D
		{
			Name = "BloodPool", Mesh = new PlaneMesh { Size = Vector2.One * poolTo * 2.2f, SubdivideWidth = 6, SubdivideDepth = 6 },
			Position = plateTop + new Vector3(0, 0.003f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		var pm = FlowFx.BloodMat(spread: poolFrom / poolTo, speed: 0.03f, scale: 0.5f);
		pm.SetShaderParameter("ripple", 0.8f);
		pool.MaterialOverride = pm;
		holder.AddChild(pool);
		var tw = pool.CreateTween();
		tw.TweenMethod(Callable.From<float>(v => pm.SetShaderParameter("spread", v)), poolFrom / poolTo, 0.8f, 9f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		Callable.From(() =>
		{
			if (!IsInstanceValid(PlatterSkull) || !IsInstanceValid(pool)) return;
			Vector3 mouth = PlatterSkull.MouthWorld;
			Vector3 land = new(mouth.X, pool.GlobalPosition.Y, mouth.Z);
			// where it lands on the pool, in the pool's UV (for its rings)
			Vector3 l = pool.ToLocal(land);
			float size = poolTo * 2.2f;
			pm.SetShaderParameter("source", new Vector2(l.X / size + 0.5f, l.Z / size + 0.5f));
			// the rope: out over the lip of the jaw, sagging, and down, thick and slow (in the head's space: it turns with it)
			var outward = (mouth - PlatterSkull.GlobalPosition) with { Y = 0 };
			outward = outward.LengthSquared() > 1e-6f ? outward.Normalized() : Vector3.Forward;
			var midW = mouth.Lerp(land, 0.45f) + outward * 0.025f;
			var a = PlatterSkull.ToLocal(mouth);
			var mid = PlatterSkull.ToLocal(midW);
			var b = PlatterSkull.ToLocal(land);
			var rope = FlowFx.PourMat(FlowFx.Liquid.Blood, speed: 0.55f, strands: 2.5f, tube: true, wobble: 0.01f);
			// (the head is scaled down: its local radii are scaled back up to the same thickness in the world)
			float inv = 1f / Mathf.Max(0.01f, PlatterSkull.Scale.X);
			FlowFx.Tube(PlatterSkull, new List<Vector3> { a, a.Lerp(mid, 0.5f), mid, mid.Lerp(b, 0.5f), b }, 0.02f * inv, 0.012f * inv, rope, "BloodRope", 6);
			FlowFx.Drips(holder, holder.ToLocal(mouth + outward * 0.02f), FlowFx.Liquid.Blood, 1.2f, mouth.Y - land.Y, 0.02f, "MouthDrips");
			// where it lands: blood rolling in blood
			FlowFx.Churn(holder, holder.ToLocal(land) + Vector3.Up * 0.002f, 0.09f, FlowFx.Liquid.Blood, 0.8f, "Landing");
		}).CallDeferred();
	}

	/// <summary>The bowl's snow melting into blood: a surface welling up from under the snow, spreading to the rim and
	/// rising up the bowl as the snow slumps (the tween is <c>MeltToBlood</c>'s, 2.6 s).</summary>
	private static MeshInstance3D BowlBlood(Node3D bowl, out Tween rise)
	{
		var mi = new MeshInstance3D
		{
			Name = "Blood", Mesh = new PlaneMesh { Size = Vector2.One * 0.31f, SubdivideWidth = 4, SubdivideDepth = 4 },
			Position = new Vector3(0, 0.09f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		var pm = FlowFx.BloodMat(spread: 0.15f, speed: 0.04f, scale: 0.35f);
		pm.SetShaderParameter("ripple", 1f);
		mi.MaterialOverride = pm;
		bowl.AddChild(mi);
		rise = mi.CreateTween().SetParallel();
		rise.TweenProperty(mi, "position:y", 0.11f, 2.6f).SetTrans(Tween.TransitionType.Sine);
		rise.TweenMethod(Callable.From<float>(v => pm.SetShaderParameter("spread", v)), 0.15f, 0.9f, 2.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		rise.TweenMethod(Callable.From<float>(v => pm.SetShaderParameter("ripple", v)), 1f, 0.1f, 4f);
		return mi;
	}
}
