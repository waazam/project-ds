using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Systems;

/// <summary>A dev probe (does nothing without it): prints the engine's frame costs every two seconds (`-- --perf`); with `--perf-classes`,
/// once 40 s in, switches each script class's _Process off in turn to find what costs the frame.</summary>
public partial class PerfProbe : Node
{
	private double _t, _age;
	private bool _swept;
	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		if (!OS.GetCmdlineUserArgs().Contains("--perf")) QueueFree();
	}

	public override void _Process(double delta)
	{
		_age += delta;
		if (!_swept && _age > 40 && OS.GetCmdlineUserArgs().Contains("--perf-classes")) { _swept = true; _ = Sweep(); }
		_t += delta;
		if (_t < 2) return;
		_t = 0;
		var p = StoryBeat.Player(this);
		GD.Print($"[perf] fps {Engine.GetFramesPerSecond()} process {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.0}ms physics {Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:0.0}ms draws {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} objs {Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame)} prims {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1000:0}k nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)} at {p?.GlobalPosition}");
	}

	private async Task<double> Avg(int frames)
	{
		double s = 0;
		for (int i = 0; i < frames; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			s += Performance.GetMonitor(Performance.Monitor.TimeProcess);
		}
		return s / frames * 1000;
	}

	private async Task Sweep()
	{
		var all = GetTree().Root.FindChildren("*", "", true, false).Where(n => n != this && (n.IsProcessing() || n.IsPhysicsProcessing())).ToList();
		var groups = all.GroupBy(n => n.GetScript().Obj is Script s ? System.IO.Path.GetFileNameWithoutExtension(s.ResourcePath) : n.GetClass()).ToList();
		GD.Print($"[perfclass] {all.Count} processing nodes in {groups.Count} classes");
		var results = new List<(string name, int count, double saved)>();
		foreach (var g in groups)
		{
			var nodes = g.Where(GodotObject.IsInstanceValid).ToList();
			double on = await Avg(15);
			foreach (var n in nodes) if (GodotObject.IsInstanceValid(n)) n.SetProcess(false);
			double off = await Avg(15);
			foreach (var n in nodes) if (GodotObject.IsInstanceValid(n)) n.SetProcess(true);
			results.Add((g.Key, nodes.Count, on - off));
		}
		foreach (var r in results.OrderByDescending(r => r.saved).Take(20))
			GD.Print($"[perfclass] {r.saved,6:0.00} ms  x{r.count,-4} {r.name}");
	}
}
