using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace ProjectDS.Systems;

/// <summary>A dev probe (does nothing without it): prints the engine's frame costs every two seconds (`-- --perf`); with `--perf-classes`,
/// once 40 s in, switches each script class's _Process off in turn to find what costs the frame.
///
/// <para><b>The lag meter</b> (`-- --hitches`, the lag pass 2026-10-13: "the game feels very laggy when certain events
/// are about to happen"): every frame's wall time, from one frame's start to the next, and each frame over 33 ms logged
/// as `[lag]` with what it can be blamed on:
/// <list type="bullet">
/// <item>where the time went: the physics steps, the awaits resumed on the frame signal (most story beats are async),
/// the nodes' _Process, and the rest (drawing, deferred calls, a level's load);</item>
/// <item>the story event marked just before it (<see cref="Mark"/>: checkpoints, flags, the autotest's checks and
/// screenshots) and how long before;</item>
/// <item>the pipelines compiled in it (a first sight), the garbage collections, the nodes and objects made (a build, an
/// instancing burst), the timed builds that ran in it (<see cref="BuildTimer"/>);</item>
/// <item>whether the screen was black (a hidden load), sped up (the autotest's x3 stretches) or just teleported.</item>
/// </list>
/// A table per act (frames over 33, 50 and 100 ms in play; the worst and what came before it) is printed as each act
/// ends and written to `hitches.txt` beside the autotest's report (or `--hitch-out=`).</para></summary>
public partial class PerfProbe : Node
{
	private double _t, _age;
	private bool _swept;

	// ------------------------------------------------------------------ the lag meter
	public static bool Metering { get; private set; }
	private static string _mark = "", _act = "";
	private static ulong _markUsec;
	private static readonly List<(string what, double ms)> _builds = new();

	/// <summary>Something happened that a hitch just after it may be down to (a story beat, a trigger, a check).</summary>
	public static void Mark(string what)
	{
		if (!Metering) return;
		_mark = what;
		_markUsec = Time.GetTicksUsec();
		if (what.StartsWith("screenshot")) _shotUsec = _markUsec;   // (a check can follow it in the same frame)
	}
	private static ulong _shotUsec;

	/// <summary>The act (the autotest's step) frames are counted against.</summary>
	public static string Act { get => _act; set { if (Metering && value != _act) { Instance?.ActEnded(); _act = value ?? ""; } } }

	/// <summary>A timed piece of work (<see cref="BuildTimer"/>) that ran this frame.</summary>
	public static void Built(string what, double ms) { if (Metering && ms >= 2.0) _builds.Add((what, ms)); }

	private static PerfProbe Instance;
	private ulong _frameStart, _procStart, _procEnd, _physStart, _physUsec, _physTailEnd, _stepUsec, _lastCompiles, _lastObjs, _lastNodes;

	/// <summary>The physics server's step after the nodes' _PhysicsProcess: the bodies moved, and the areas' and bodies'
	/// signals sent (every trigger volume's entered/exited: story beats begin here).</summary>
	private void StepEnded(ulong now)
	{
		if (_physTailEnd != 0) _stepUsec += now - _physTailEnd;
		_physTailEnd = 0;
	}
	private long _lastAlloc;
	private readonly int[] _lastGc = new int[3];
	private Vector3 _lastPlayer;
	private ulong _teleportUsec;

	private sealed class ActStats
	{
		public int Frames, Over33, Over50, Over100, Hidden, TestOnly, Gc1, Gc2, Compile, Build, Gc, Draw;
		public long Alloc;
		public double Worst; public string WorstWhat = "";
	}
	private readonly Dictionary<string, ActStats> _stats = new();
	private readonly List<string> _order = new();
	private readonly StringBuilder _all = new();

	// the garbage made in each part of the frame (the main thread's): the awaits resumed on the frame signal, the nodes'
	// _Process, the physics (the nodes', then the server's step with the triggers' signals), and the rest (drawing,
	// deferred calls); what a part made since the last boundary is put down to the part that just ended
	private const int PhAwaits = 0, PhProcess = 1, PhPhysics = 2, PhStep = 3, PhRest = 4;
	private static readonly string[] PhaseNames = { "awaits", "process", "physics", "step+triggers", "rest" };
	private static readonly long[] _phaseAlloc = new long[5];
	private static long _phaseMark;
	private static int _phase = PhRest;
	/// <summary>The part of the frame that just ended; <paramref name="next"/> begins.</summary>
	private static void Phase(int next)
	{
		long a = System.GC.GetAllocatedBytesForCurrentThread();
		if (_phaseMark != 0) _phaseAlloc[_phase] += a - _phaseMark;
		_phaseMark = a;
		_phase = next;
	}

	/// <summary>Last in each frame's _Process and _PhysicsProcess: where the nodes' own work ends.</summary>
	private partial class Tail : Node
	{
		public PerfProbe Probe;
		public override void _Ready() { ProcessMode = ProcessModeEnum.Always; ProcessPriority = int.MaxValue; ProcessPhysicsPriority = int.MaxValue; }
		public override void _Process(double delta) { Probe._procEnd = Time.GetTicksUsec(); Phase(PhRest); }
		public override void _PhysicsProcess(double delta)
		{
			Phase(PhStep);
			ulong now = Time.GetTicksUsec();
			if (Probe._physStart != 0) Probe._physUsec += now - Probe._physStart;
			Probe._physStart = 0;
			Probe._physTailEnd = now;
		}
	}

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		// (always, probe or not: the collector's full sweeps done alongside the game, never stopping it for one; the
		// lag pass, 2026-10-13, found its pauses, 30-100 ms every few seconds, the commonest hitch in play)
		System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
		var args = OS.GetCmdlineUserArgs();
		Metering = args.Contains("--hitches");
		if (!args.Contains("--perf") && !Metering) { QueueFree(); return; }
		Instance = this;
		if (Metering)
		{
			ProcessPriority = int.MinValue;
			if (StoryManager.Instance is { } sm)
			{
				sm.CheckpointReached += cp => Mark($"checkpoint {cp}");
				sm.FlagSet += f => Mark($"flag {f}");
			}
			// (connected first, so it runs before every await resumed on the frame signal)
			GetTree().ProcessFrame += Meter;
			GetTree().PhysicsFrame += () => { Phase(PhPhysics); _physStart = Time.GetTicksUsec(); StepEnded(_physStart); };
			AddChild(new Tail { Name = "Tail", Probe = this });
			if (args.Contains("--alloc-types")) _allocTypes = new AllocTypes();
			// the renderer's own times for the window's picture (the GPU's, and the render thread's CPU): what "draw" costs
			RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
			GD.Print("[lag] metering every frame (over 33 ms logged)");
		}
	}

	public override void _ExitTree()
	{
		if (Instance != this) return;
		if (Metering) { ActEnded(); WriteReport(); }
		Instance = null;
	}

	public override void _Process(double delta)
	{
		if (Metering) { _procStart = Time.GetTicksUsec(); Phase(PhProcess); }
		if (Metering) { Soak(); ReportAllocTypes(delta); }
		if (!OS.GetCmdlineUserArgs().Contains("--perf")) return;
		_age += delta;
		if (!_swept && _age > SweepAt()) { _swept = true; _ = Sweep(); }
		_t += delta;
		if (_t < 2) return;
		_t = 0;
		var p = StoryBeat.Player(this);
		GD.Print($"[perf] fps {Engine.GetFramesPerSecond()} process {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.0}ms physics {Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:0.0}ms draws {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} objs {Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame)} prims {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1000:0}k nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)} bodies {Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects)} pairs {Performance.GetMonitor(Performance.Monitor.Physics3DCollisionPairs)} islands {Performance.GetMonitor(Performance.Monitor.Physics3DIslandCount)} at {p?.GlobalPosition}");
	}

	// ------------------------------------------------------------------ what the garbage is

	/// <summary>`--alloc-types`: the runtime's own allocation sampling (an event about every 100 KB allocated, naming the
	/// type that tipped it), counted by type and printed every ten seconds: what a heavy garbage rate is made of.</summary>
	private sealed class AllocTypes : System.Diagnostics.Tracing.EventListener
	{
		public readonly Dictionary<string, long> Counts = new();
		protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
		{
			if (source.Name == "Microsoft-Windows-DotNETRuntime")
				EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1);
		}
		protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
		{
			if (e.EventName == null || !e.EventName.StartsWith("GCAllocationTick") || e.PayloadNames == null) return;
			int i = e.PayloadNames.IndexOf("TypeName");
			if (i < 0) return;
			string type = e.Payload[i] as string ?? "?";
			lock (Counts) Counts[type] = Counts.GetValueOrDefault(type) + 1;
		}
	}
	private AllocTypes _allocTypes;
	private double _allocT;

	private void ReportAllocTypes(double delta)
	{
		if (_allocTypes == null) return;
		if ((_allocT += delta) < 10) return;
		_allocT = 0;
		List<KeyValuePair<string, long>> top;
		lock (_allocTypes.Counts) { top = _allocTypes.Counts.OrderByDescending(k => k.Value).Take(12).ToList(); _allocTypes.Counts.Clear(); }
		GD.Print($"[alloctypes] {_act}: " + string.Join(", ", top.Select(k => $"{k.Key} x{k.Value}")));
	}

	// ------------------------------------------------------------------ the soak
	private GameFlow _soakFlow;
	private int _soakLevels;
	private double _soakT = -1;
	private bool _soakDone;
	private long _soakAlloc0;
	private int _soakFrames, _soakGc0, _soakGc1, _soakGc2;

	/// <summary>`--soak=60` with `--autotest --story-from=N`: once the act's save has loaded, the autotest is taken off and
	/// the game left standing at the save for that many seconds, measured (garbage made a frame, collections, frames over
	/// 33 ms), then quits: the game's own cost, without the test's tree-wide searches. With `--perf`, the per-class sweep
	/// runs ten seconds in.</summary>
	private void Soak()
	{
		double secs = 0;
		foreach (var a in OS.GetCmdlineUserArgs()) if (a.StartsWith("--soak=")) double.TryParse(a["--soak=".Length..], out secs);
		if (secs <= 0 || _soakDone) return;
		if (GameFlow.Instance != _soakFlow && GameFlow.Instance is { Started: true } gf) { _soakFlow = gf; _soakLevels++; }
		if (_soakLevels < 2 || _soakFlow == null || !IsInstanceValid(_soakFlow)) return;
		if (_soakT < 0)
		{
			if (!ShaderWarmup.Ready) return;
			foreach (var n in _soakFlow.GetChildren()) if (n is StoryTest st) { st.QueueFree(); GD.Print("[soak] the autotest taken off: the game alone"); }
			if (StoryBeat.Player(this) is { } p) p.PlayerInput.Scripted = false;
			_soakT = 0;
			Act = $"soak at {StoryManager.Instance?.Current}";
			_soakAlloc0 = System.GC.GetAllocatedBytesForCurrentThread();
			_soakGc0 = System.GC.CollectionCount(0); _soakGc1 = System.GC.CollectionCount(1); _soakGc2 = System.GC.CollectionCount(2);
			if (OS.GetCmdlineUserArgs().Contains("--perf")) { _swept = true; GetTree().CreateTimer(10).Timeout += () => _ = Sweep(); }
			return;
		}
		_soakT += GetProcessDeltaTime();
		_soakFrames++;
		if (_soakT < secs) return;
		_soakDone = true;
		GD.Print($"[soak] {StoryManager.Instance?.Current}: {_soakFrames} frames in {secs:0} s, {(System.GC.GetAllocatedBytesForCurrentThread() - _soakAlloc0) / 1024.0 / _soakFrames:0.0} KB allocated a frame, collections gen0/1/2 {System.GC.CollectionCount(0) - _soakGc0}/{System.GC.CollectionCount(1) - _soakGc1}/{System.GC.CollectionCount(2) - _soakGc2}");
		GetTree().Quit();
	}

	/// <summary>`--perf-classes` sweeps 40 s in; `--perf-classes=90`, 90 s in (never without it).</summary>
	private static double SweepAt()
	{
		foreach (var a in OS.GetCmdlineUserArgs())
		{
			if (a == "--perf-classes") return 40;
			if (a.StartsWith("--perf-classes=") && double.TryParse(a["--perf-classes=".Length..], out double s)) return s;
		}
		return double.MaxValue;
	}

	/// <summary>At each frame's start (the frame signal): the frame just gone, from the last start to this one. It held
	/// the last frame's awaits, _Process and drawing, and this frame's physics steps (they run before the signal).</summary>
	private void Meter()
	{
		ulong now = Time.GetTicksUsec();
		StepEnded(now);
		Phase(PhAwaits);
		ulong compiles = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsDraw)
			+ RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsSpecialization)
			+ RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.PipelineCompilationsSurface);
		ulong objs = (ulong)Performance.GetMonitor(Performance.Monitor.ObjectCount);
		ulong nodes = (ulong)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
		int g0 = System.GC.CollectionCount(0), g1 = System.GC.CollectionCount(1), g2 = System.GC.CollectionCount(2);
		long alloc = System.GC.GetAllocatedBytesForCurrentThread();
		var player = StoryBeat.Player(this);
		Vector3 pp = player != null && IsInstanceValid(player) ? player.GlobalPosition : _lastPlayer;
		if (pp.DistanceTo(_lastPlayer) > 4f) _teleportUsec = now;   // (a jump, not a walk: the test's teleports, a respawn)
		if (_frameStart != 0)
		{
			double ms = (now - _frameStart) / 1000.0;
			string act = _act.Length > 0 ? _act : (StoryManager.Instance?.Current.ToString() ?? "?");
			if (!_stats.TryGetValue(act, out var st)) { st = new ActStats(); _stats[act] = st; _order.Add(act); }
			st.Frames++;
			st.Alloc += alloc - _lastAlloc;
			st.Gc1 += g1 - _lastGc[1]; st.Gc2 += g2 - _lastGc[2];
			if (ms > 33.3)
			{
				bool during = _markUsec >= _frameStart;
				double before = during ? 0 : (_frameStart - _markUsec) / 1e6;
				string when = _markUsec == 0 ? "" : during ? $"during '{_mark}'" : $"{before:0.0}s after '{_mark}'";
				bool black = StoryBeat.Fader(this) is { } f && IsInstanceValid(f) && f.IsBlack;
				bool teleported = now - _teleportUsec < 1_500_000;
				bool shot = _shotUsec != 0 && _shotUsec + 300_000 >= _frameStart;
				// (the autotest's x3 stretches count: a first sight, a build or a collection costs the same sped up)
				bool test = shot || teleported;
				double signals = _procStart > _frameStart ? (_procStart - _frameStart) / 1000.0 : 0;
				double proc = _procEnd > _procStart && _procStart > _frameStart ? (_procEnd - _procStart) / 1000.0 : 0;
				double phys = _physUsec / 1000.0, step = _stepUsec / 1000.0;
				var why = new StringBuilder();
				why.Append($"awaits {signals:0} / process {proc:0} / physics {phys:0} / step+triggers {step:0} / draw+rest {ms - signals - proc - phys - step:0} ms");
				var vp = GetViewport().GetViewportRid();
				why.Append($", {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0} draws {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1000:0}k tris"
					+ $", render cpu {RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp) + RenderingServer.GetFrameSetupTimeCpu():0} gpu {RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp):0} ms");
				if (compiles > _lastCompiles) why.Append($", {compiles - _lastCompiles} pipeline compiles");
				if (g0 != _lastGc[0] || g1 != _lastGc[1] || g2 != _lastGc[2]) why.Append($", GC gen0/1/2 +{g0 - _lastGc[0]}/{g1 - _lastGc[1]}/{g2 - _lastGc[2]}");
				long dn = (long)nodes - (long)_lastNodes, dobj = (long)objs - (long)_lastObjs;
				if (dn != 0 || System.Math.Abs(dobj) > 20) why.Append($", nodes {dn:+0;-0} objects {dobj:+0;-0}");
				foreach (var (w, bms) in _builds.OrderByDescending(b => b.ms).Take(4)) why.Append($", built {w} {bms:0} ms");
				if (black) why.Append(", screen black");
				if (Engine.TimeScale > 1.01) why.Append($", time x{Engine.TimeScale:0}");
				if (teleported) why.Append(", just after a teleport");
				if (shot) why.Append(", a test screenshot");
				string line = $"[lag] {ms:0} ms in {act} {when} at {pp.Round()} ({why})";
				GD.Print(line);
				_all.AppendLine(line);
				if (black) st.Hidden++;
				else if (test) st.TestOnly++;
				else
				{
					st.Over33++;
					if (ms > 50) st.Over50++;
					if (ms > 100) st.Over100++;
					// what it's down to: a first sight, a build or burst of nodes, a collection, or none of those (the
					// draw itself: a heavy view, or another program on the machine)
					if (compiles > _lastCompiles) st.Compile++;
					else if (_builds.Count > 0 || System.Math.Abs(dn) > 50) st.Build++;
					else if (g0 != _lastGc[0]) st.Gc++;
					else st.Draw++;
					if (ms > st.Worst) { st.Worst = ms; st.WorstWhat = $"{when} ({why})"; }
				}
			}
		}
		_builds.Clear();
		_physUsec = 0; _stepUsec = 0;
		_frameStart = now; _lastCompiles = compiles; _lastObjs = objs; _lastNodes = nodes; _lastPlayer = pp;
		_lastGc[0] = g0; _lastGc[1] = g1; _lastGc[2] = g2; _lastAlloc = alloc;
	}

	private void ActEnded()
	{
		string act = _act.Length > 0 ? _act : null;
		if (act == null || !_stats.TryGetValue(act, out var st)) return;
		GD.Print($"[lag-act] {act}: {st.Frames} frames, >33 ms {st.Over33}, >50 ms {st.Over50}, >100 ms {st.Over100}, worst {st.Worst:0} ms {st.WorstWhat}; behind black {st.Hidden}, test-only {st.TestOnly}; {st.Alloc / 1024.0 / System.Math.Max(1, st.Frames):0} KB allocated a frame, {st.Gc1} gen1 and {st.Gc2} gen2 collections");
		var split = new StringBuilder();
		for (int i = 0; i < 5; i++) { split.Append($"{PhaseNames[i]} {_phaseAlloc[i] / 1024.0 / System.Math.Max(1, st.Frames):0} "); _phaseAlloc[i] = 0; }
		GD.Print($"[lag-act] {act}: KB a frame by part of the frame: {split}");
	}

	private void WriteReport()
	{
		var sb = new StringBuilder();
		sb.AppendLine("# Frames over 33/50/100 ms in play (not behind a black screen, not the test's own screenshots or teleports)");
		sb.AppendLine("act | frames | >33 | >50 | >100 | worst ms | compile/build/GC/draw | KB/frame | gen1/gen2 GCs | black | test-only | worst");
		foreach (var act in _order)
		{
			var s = _stats[act];
			sb.AppendLine($"{act} | {s.Frames} | {s.Over33} | {s.Over50} | {s.Over100} | {s.Worst:0} | {s.Compile}/{s.Build}/{s.Gc}/{s.Draw} | {s.Alloc / 1024.0 / System.Math.Max(1, s.Frames):0} | {s.Gc1}/{s.Gc2} | {s.Hidden} | {s.TestOnly} | {s.WorstWhat}");
		}
		string table = sb.ToString();
		sb.AppendLine();
		sb.Append(_all);
		string dir = StoryTest.OutputDir();
		foreach (var a in OS.GetCmdlineUserArgs()) if (a.StartsWith("--hitch-out=")) dir = a["--hitch-out=".Length..];
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(dir));
		using var f = FileAccess.Open($"{dir}/hitches.txt", FileAccess.ModeFlags.Write);
		f?.StoreString(sb.ToString());
		GD.Print(table);
	}

	/// <summary>Over some frames: the frame's script time, the KB allocated, and the engine objects made (counted while no
	/// collection ran: the collector frees the C# side's dead ones, and the count drops).</summary>
	private async Task<(double ms, double kb, double objs)> Avg(int frames)
	{
		double s = 0, made = 0;
		int counted = 0;
		long a0 = System.GC.GetAllocatedBytesForCurrentThread();
		double o0 = Performance.GetMonitor(Performance.Monitor.ObjectCount);
		for (int i = 0; i < frames; i++)
		{
			int gc = System.GC.CollectionCount(0);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			double o = Performance.GetMonitor(Performance.Monitor.ObjectCount);
			if (gc == System.GC.CollectionCount(0) && o >= o0) { made += o - o0; counted++; }
			o0 = o;
			s += Performance.GetMonitor(Performance.Monitor.TimeProcess) + Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess);
		}
		return (s / frames * 1000, (System.GC.GetAllocatedBytesForCurrentThread() - a0) / 1024.0 / frames, made / System.Math.Max(1, counted));
	}

	/// <summary>Each script class's _Process and _PhysicsProcess switched off in turn: what it costs a frame, in time and
	/// in garbage (the KB it allocates a frame: what the collector sweeps up later, in a pause of its own).</summary>
	private async Task Sweep()
	{
		var all = GetTree().Root.FindChildren("*", "", true, false).Where(n => n != this && n.GetParent() != this && (n.IsProcessing() || n.IsPhysicsProcessing())).ToList();
		var groups = all.GroupBy(n => n.GetScript().Obj is Script s ? System.IO.Path.GetFileNameWithoutExtension(s.ResourcePath) : n.GetClass()).ToList();
		GD.Print($"[perfclass] {all.Count} processing nodes in {groups.Count} classes");
		var results = new List<(string name, int count, double saved, double kb, double objs)>();
		foreach (var g in groups)
		{
			var nodes = g.Where(GodotObject.IsInstanceValid).Select(n => (n, p: n.IsProcessing(), ph: n.IsPhysicsProcessing())).ToList();
			var on = await Avg(20);
			foreach (var (n, _, _) in nodes) if (GodotObject.IsInstanceValid(n)) { n.SetProcess(false); n.SetPhysicsProcess(false); }
			var off = await Avg(20);
			foreach (var (n, p, ph) in nodes) if (GodotObject.IsInstanceValid(n)) { n.SetProcess(p); n.SetPhysicsProcess(ph); }
			results.Add((g.Key, nodes.Count, on.ms - off.ms, on.kb - off.kb, on.objs - off.objs));
		}
		foreach (var r in results.OrderByDescending(r => r.saved).Take(20))
			GD.Print($"[perfclass] {r.saved,6:0.00} ms  x{r.count,-4} {r.name}");
		foreach (var r in results.OrderByDescending(r => r.kb).Take(20))
			GD.Print($"[perfalloc] {r.kb,7:0.0} KB a frame, {r.objs,5:0.0} engine objects a frame  x{r.count,-4} {r.name}");
		foreach (var r in results.OrderByDescending(r => r.objs).Take(12))
			GD.Print($"[perfobjs] {r.objs,5:0.0} engine objects a frame, {r.kb,7:0.0} KB  x{r.count,-4} {r.name}");
	}
}
