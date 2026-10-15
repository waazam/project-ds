using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;

namespace ProjectDS.World.SnowMaze;

/// <summary>
/// (the gushy-water pass, 2026-10-15: "by water I mean the blood too") Ending 4's blood running down the stairs a step at
/// a time, as blood and not a red box on each tread: from the top step down, it wells up and spreads over the tread in a
/// lobed, glossy pool (dark in the middle, a raised rim at its edge), reaches the nosing, and pours thick over it down the
/// riser in a sheet with a couple of rivulets, landing on the step below, which fills in turn; drops hang and fall off the
/// nosings; at the bottom it spreads out on the ground. Each step takes the same time the old one did (0.45 s), and each
/// still has its drip one-shot (the existing set, no-repeat picker).
/// </summary>
public partial class EndingDirector
{
	internal async Task BleedStairs(CancellationToken ct)
	{
		// the steps (the stairs' own space): step i+1's tread top at (i + 1) rise, its nose at -i run, run deep
		float rise = (_stairs as World.StaircaseBuilder)?.Rise ?? 0.17f, run = (_stairs as World.StaircaseBuilder)?.Run ?? 0.28f;
		var root = new Node3D { Name = "Blood" };
		_stairs.AddChild(root);
		var pourMat = World.FlowFx.PourMat(World.FlowFx.Liquid.Blood, speed: 1.1f, strands: 5f, wobble: 0.012f, aerateLen: 0.3f);
		var ropeMat = World.FlowFx.PourMat(World.FlowFx.Liquid.Blood, speed: 1.3f, strands: 2f, tube: true, wobble: 0.006f);
		var rng = new RandomNumberGenerator { Seed = 4404 };
		for (int i = 11; i >= 0; i--)
		{
			float y = (i + 1) * rise, z = -i * run - run * 0.5f;
			// the pool on the tread, welling up from the back (where the step above pours onto it) and spreading
			var pool = new MeshInstance3D
			{
				Name = $"Tread{i}", Mesh = new PlaneMesh { Size = new Vector2(0.7f, run + 0.01f), SubdivideWidth = 4, SubdivideDepth = 2 },
				Position = new Vector3(rng.RandfRange(-0.03f, 0.03f), y + 0.012f, z), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			var pm = World.FlowFx.BloodMat(spread: 0f, speed: 0.05f, scale: 0.5f);
			pm.SetShaderParameter("source", new Vector2(0.5f, 0.15f));
			pool.MaterialOverride = pm;
			root.AddChild(pool);
			var tw = CreateTween().SetParallel();
			tw.TweenMethod(Callable.From<float>(v => pm.SetShaderParameter("spread", v)), 0f, 1.08f, 0.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			if (i < 11) tw.TweenMethod(Callable.From<float>(v => pm.SetShaderParameter("ripple", v)), 1f, 0.15f, 1.6f);
			AudioDirector.OneShot(this, "haunt_drip", 3, _stairs.ToGlobal(new Vector3(0, y, z)), -10f, "Events", 2f, 0.1f);
			await Cutscene.Wait(this, 0.3, ct);
			// over the nosing and down the riser to the next tread (or the ground), the sheet growing down from the edge
			float noseZ = z + run * 0.5f, drop = rise + 0.004f;
			var pour = new Node3D { Name = $"Riser{i}", Position = new Vector3(0, y + 0.004f, noseZ + 0.006f), Scale = new Vector3(1f, 0.02f, 1f) };
			root.AddChild(pour);
			var path = new List<Vector3> { Vector3.Zero, new(0, -0.03f, 0.02f), new(0, -drop * 0.5f, 0.03f), new(0, -drop, 0.035f) };
			float w = rng.RandfRange(0.36f, 0.52f);
			World.FlowFx.Sheet(pour, path, Vector3.Right, w, w * 1.1f, pourMat, "Sheet", 6);
			foreach (float lx in new[] { -w * 0.5f - 0.05f, w * 0.5f + rng.RandfRange(0.02f, 0.1f) })
				World.FlowFx.Tube(pour, new List<Vector3> { new(lx, 0, 0.004f), new(lx, -drop * 0.6f, 0.012f), new(lx * 1.05f, -drop, 0.015f) }, 0.011f, 0.016f, ropeMat, "Rivulet", 5);
			var grow = CreateTween();
			grow.TweenProperty(pour, "scale", Vector3.One, 0.18f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			if (i % 3 == 1) World.FlowFx.Drips(root, new Vector3(rng.RandfRange(-0.3f, 0.3f), y - 0.005f, noseZ + 0.01f), World.FlowFx.Liquid.Blood, 0.8f, 0.25f, 0.01f, $"Drip{i}");
			await Cutscene.Wait(this, 0.15, ct);
		}
		// at the foot, out over the ground
		var foot = new MeshInstance3D
		{
			Name = "Foot", Mesh = new PlaneMesh { Size = new Vector2(1.5f, 1.3f), SubdivideWidth = 6, SubdivideDepth = 6 },
			Position = new Vector3(0, 0.012f, 0.6f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		var fm = World.FlowFx.BloodMat(spread: 0f, speed: 0.04f, scale: 1.2f);
		fm.SetShaderParameter("source", new Vector2(0.5f, 0.12f));
		fm.SetShaderParameter("ripple", 0.6f);
		foot.MaterialOverride = fm;
		root.AddChild(foot);
		var ft = CreateTween();
		ft.TweenMethod(Callable.From<float>(v => fm.SetShaderParameter("spread", v)), 0f, 0.95f, 2.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
	}
}
