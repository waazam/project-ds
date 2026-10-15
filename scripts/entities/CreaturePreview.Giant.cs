using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Entities;

/// <summary>(2026-10-15) The giant's walk and look: `--giant-walk` measures the walk clip's stride and foot contacts.</summary>
public partial class CreaturePreview
{
	private async Task GiantWalkMeasure()
	{
		var body = new StalkerBody { Name = "Measure", Size = 1f };
		AddChild(body);
		await Frames(3);
		var s = body.SampleWalk(128);
		if (s.Count == 0) { GD.PrintErr("[giant-walk] not rigged"); return; }
		foreach (var (name, pick) in new (string, System.Func<StalkerBody.WalkSample, Vector3>)[] { ("R", w => w.FootR), ("L", w => w.FootL) })
		{
			float minY = s.Min(w => pick(w).Y), maxY = s.Max(w => pick(w).Y);
			float minZ = s.Min(w => pick(w).Z), maxZ = s.Max(w => pick(w).Z);
			float lowCut = minY + 0.15f * (maxY - minY);
			var stance = s.Where(w => pick(w).Y < lowCut).ToList();
			float sMin = stance.Min(w => pick(w).Z), sMax = stance.Max(w => pick(w).Z);
			int c = -1;
			for (int i = 0; i < s.Count; i++)
			{
				var a = pick(s[(i + s.Count - 1) % s.Count]); var b = pick(s[i]);
				if (a.Y >= lowCut && b.Y < lowCut) { c = i; break; }
			}
			GD.Print($"[giant-walk] foot {name}: lift {maxY - minY:0.000}, z range {minZ:0.000}..{maxZ:0.000} ({maxZ - minZ:0.000}), stance z {sMin:0.000}..{sMax:0.000} ({sMax - sMin:0.000}, {stance.Count}/{s.Count} samples), contact phase {(c >= 0 ? s[c].Phase : -1f):0.000}");
		}
		var line = string.Join(" ", s.Where((w, i) => i % 8 == 0).Select(w => $"{w.Phase:0.00}:R({w.FootR.Y:0.00},{w.FootR.Z:0.00})L({w.FootL.Y:0.00},{w.FootL.Z:0.00})"));
		GD.Print($"[giant-walk] samples {line}");
		body.QueueFree();
	}
}
