using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.UI;

namespace ProjectDS.Systems;

/// <summary>
/// `-- --playtest`: the fast functionality test of the whole game. Every act (1-25), the endings and the core systems,
/// in independent beats: each beat is seeded from its own save (the checkpoint, flags and gear a real run has there,
/// written to the test slots and Continued into, so every act's start is a real Continue too), jumps the player right
/// next to each trigger and interactable and walks only the last step or two into it, so the trigger volumes, the
/// pick volumes and the story's gating are exercised for real. Long sequences run time-compressed.
///
/// <para><b>Isolation.</b> A beat that fails (a check, an exception, its time budget) is recorded and the next beat
/// starts from its own save, so nothing cascades. A beat that has to leave the level (a death and its reload, the
/// climb's travel, the credits) says so first, and the next level's Playtest carries on with it.</para>
///
/// <para><b>Also watched, every beat:</b> engine errors (a logger on the engine's own log), the audio director's audit
/// (sounds on unknown buses, too loud, leaked players), and the frame rate.</para>
///
/// <para><b>Output:</b> test-output/playtest/report.md, one screenshot per beat, exit code 0 (all passed) or 1.
/// `--playtest-only=3,5,13-15` runs the beats of those acts only (0 = the systems beat; ids like 13b work too).</para>
/// </summary>
public partial class Playtest : Node
{
	// ------------------------------------------------------------------ the plan

	/// <summary>Where a beat starts: a new game, a save Continued into, or straight on from the beat before.</summary>
	private sealed record Seed(Checkpoint Cp, string[] Flags, string Inventory, bool NewGame = false, bool Chain = false, int Deaths = 0)
	{
		public static readonly Seed Fresh = new(Checkpoint.None, Array.Empty<string>(), "", NewGame: true);
		public static readonly Seed Follow = new(Checkpoint.None, Array.Empty<string>(), "", Chain: true);
	}

	private sealed record Beat(string Id, int Act, string Name, Seed Seed, double Budget, Func<CancellationToken, Task> Run);

	// ------------------------------------------------------------------ results (survive the level changes)

	internal sealed class BeatResult
	{
		public string Id, Name;
		public int Act;
		public string Status = "pass";   // pass, FAIL, skipped
		public readonly List<(string name, bool ok, string detail)> Checks = new();
		public double Seconds, LoadSeconds;
		public float FpsSum, FpsMin = 9999f; public int FpsN;
		public readonly List<string> Errors = new();
		public int ErrorCount, WarningCount;
		public string Shot;
		public int Fails => Checks.Count(c => !c.ok);
	}

	private sealed class Session
	{
		public List<string> Plan = new();   // beat ids
		public int Index;
		public int Seeded = -1, Active = -1;
		public bool ExpectReload;
		public int Phase;
		public readonly Dictionary<string, BeatResult> Results = new();
		public readonly ulong StartMs = Time.GetTicksMsec();
		public ulong BeatStartMs, SeedAskedMs;
		/// <summary>When this phase of the beat began (a reload begins a new one): the fps meter waits 1.5 s after it, so the
		/// first frames after a reload's build (2026-10-14c: they read as a "3 fps minimum" in Act 18) aren't counted.</summary>
		public ulong PhaseStartMs;
		public double LoadTotal;
		public int MenusSeen;
		public bool FirstBootUsed;
		public float Walked, Jumped;
		public int Chained;
		/// <summary>A beat's own notes across its reloads.</summary>
		public readonly Dictionary<string, object> Memo = new();
	}

	private static Session _s;
	private const string OutDir = "res://test-output/playtest";

	private TestBot _bot;
	private CancellationTokenSource _cts;
	private bool _leaving;
	private List<Beat> _beats;
	private double _fpsTimer;

	private PlayerController Player => _bot.Player;
	private PlayerInput Input => _bot.Input;
	private PlayerInventory Inv => _bot.Inv;
	private StoryManager Story => StoryManager.Instance;
	private Beat Current => _s != null && _s.Index < _s.Plan.Count ? _beats.FirstOrDefault(b => b.Id == _s.Plan[_s.Index]) : null;
	private BeatResult Result => Current != null && _s.Results.TryGetValue(Current.Id, out var r) ? r : null;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutDir));
		using (FileAccess.Open("res://test-output/.gdignore", FileAccess.ModeFlags.Write)) { }
		_cts = new CancellationTokenSource();
		TreeExiting += () => { _leaving = true; _cts.Cancel(); Engine.TimeScale = 1.0; };
		_bot = new TestBot(this);
		_beats = BuildBeats();
		if (_s == null)
		{
			_s = new Session();
			_s.Plan = Select(_beats).Select(b => b.Id).ToList();
			ErrorLog.Install();
			GetTree().NodeAdded += OnNodeAdded;
			GD.Print($"[playtest] {_s.Plan.Count} beats: {string.Join(" ", _s.Plan)}");
		}
		RunAll();
	}

	private static void OnNodeAdded(Node n) { if (n is MainMenu && _s != null) _s.MenusSeen++; }

	/// <summary>`--playtest-only=3,5,13-15,23c`: acts (and ranges of them) or beat ids.</summary>
	private static IEnumerable<Beat> Select(List<Beat> all)
	{
		string only = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--playtest-only="))?["--playtest-only=".Length..];
		if (string.IsNullOrWhiteSpace(only)) return all;
		var acts = new HashSet<int>();
		var ids = new HashSet<string>();
		foreach (var part in only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var range = part.Split('-');
			if (all.Any(b => b.Id == part)) ids.Add(part);   // (a beat id first: "25-3" is ending 3, not acts 25 to 3)
			else if (range.Length == 2 && int.TryParse(range[0], out int a) && int.TryParse(range[1], out int b)) for (int i = a; i <= b; i++) acts.Add(i);
			else if (int.TryParse(part, out int n)) acts.Add(n);
			else ids.Add(part);
		}
		return all.Where(b => acts.Contains(b.Act) || ids.Contains(b.Id));
	}

	// ------------------------------------------------------------------ running

	private async void RunAll()
	{
		try
		{
			await _bot.WaitUntil(() => GameFlow.Instance is { Started: true } && StoryBeat.Player(this) != null, 60, _cts.Token);
			if (!_bot.Attach()) { GD.PushError("[playtest] no player"); Finish(); return; }
			if (_s.Seeded >= 0 && _s.SeedAskedMs > 0)
			{
				double load = (Time.GetTicksMsec() - _s.SeedAskedMs) / 1000.0;
				_s.LoadTotal += load;
				if (Result != null) Result.LoadSeconds += load;
				_s.SeedAskedMs = 0;
			}
			while (true)
			{
				if (_s.Index >= _s.Plan.Count) { Finish(); return; }
				var beat = Current;
				if (beat == null) { _s.Index++; continue; }
				if (_s.Active == _s.Index)
				{
					// we were inside this beat when the level went: a reload it asked for (a death, a travel), or not
					if (!_s.ExpectReload)
					{
						Check("the level didn't reload in the middle of the beat (an unexpected death or level change)", false, $"phase {_s.Phase}");
						EndBeat(beat);
						continue;
					}
					_s.ExpectReload = false;
					_s.Phase++;
				}
				else if (_s.Seeded != _s.Index)
				{
					if (beat.Seed.Chain)
					{
						var prev = _s.Index > 0 && _s.Results.TryGetValue(_s.Plan[_s.Index - 1], out var pr) ? pr : null;
						if (prev == null || prev.Status != "pass")
						{
							Begin(beat).Status = "skipped";
							Result.Checks.Add(("runs straight on from the beat before, which " + (prev == null ? "wasn't run" : "failed"), true, "skipped"));
							_s.Index++;
							continue;
						}
					}
					else if (FastChain && CanChain(beat))
					{
						// `--playtest-fast`: the beat before passed and left the story exactly at this beat's save: straight on
						GD.Print($"[playtest] ---- {beat.Id}: chained on from the beat before (no reload)");
						_s.Seeded = _s.Index;
						Begin(beat);
						_s.Chained++;
						continue;
					}
					else
					{
						// seed it: a new game, or the beat's save Continued into (the reload brings up a new Playtest)
						_s.Seeded = _s.Index;
						_s.SeedAskedMs = Time.GetTicksMsec();
						Begin(beat);
						Engine.TimeScale = 1.0;
						GetTree().Paused = false;
						FlushDistances();
						// the first beat on the first boot: the menu has just started a new game, so it's already seeded
						if (beat.Seed.NewGame && !_s.FirstBootUsed && _s.Index == 0 && Story.Current <= Checkpoint.Act1Start
							&& GetTree().CurrentScene?.SceneFilePath == StoryManager.TrailheadScene)
						{
							_s.FirstBootUsed = true;
							_s.SeedAskedMs = 0;
							continue;
						}
						_s.FirstBootUsed = true;
						if (beat.Seed.NewGame) { GD.Print($"[playtest] ---- seeding {beat.Id}: a new game"); Story.StartNewGame(); }
						else
						{
							GD.Print($"[playtest] ---- seeding {beat.Id}: {beat.Seed.Cp}, {beat.Seed.Flags.Length} flags, '{beat.Seed.Inventory}'");
							SaveSystem.Save(new SaveData { Checkpoint = beat.Seed.Cp, Flags = beat.Seed.Flags, Inventory = beat.Seed.Inventory, Deaths = beat.Seed.Deaths, PlaySeconds = 600 });
							if (Story.ContinueGame() != true) { Check("the beat's save Continues", false, "ContinueGame returned false"); EndBeat(beat); continue; }
						}
						return;
					}
				}
				bool done = await RunBeat(beat);
				if (!done) return;   // the level is changing under us: the next level's Playtest carries on
				EndBeat(beat);
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception e)
		{
			GD.PushError($"[playtest] harness: {e}");
			if (Result != null) Check("the harness ran without an exception", false, e.ToString());
			Finish(2);
		}
	}

	/// <summary>`--playtest-fast`: chain passed beats without the reload when the story is already at the next one's save
	/// (the per-act Continue is then left to the full run and `--continue-test`).</summary>
	private static readonly bool FastChain = OS.GetCmdlineUserArgs().Contains("--playtest-fast");

	private static readonly HashSet<string> NeedsReload = new() { "8", "13a" };

	private bool CanChain(Beat beat)
	{
		var seed = beat.Seed;
		if (seed.NewGame || beat.Name.StartsWith("Continue into") || _s.Index == 0) return false;   // (the Continue beats are the Continue)
		// (places that set themselves up only as the level loads: the bunker's interior after the lookout, the station
		// after the lake. Chained, they failed: they need their reload)
		if (NeedsReload.Contains(beat.Id)) return false;
		if (!_s.Results.TryGetValue(_s.Plan[_s.Index - 1], out var prev) || prev.Status != "pass") return false;
		if (Story == null || Story.Current != seed.Cp || GetTree().CurrentScene?.SceneFilePath != StoryManager.LevelFor(seed.Cp)) return false;
		if (!seed.Flags.All(Story.HasFlag)) return false;
		static string Gear(string s) => string.Join(",", (s ?? "").Split(';')[0].Split(',', StringSplitOptions.RemoveEmptyEntries).Where(g => g != "camera").OrderBy(g => g))
			+ ";" + string.Join("+", ((s ?? "").Split(';').Length > 1 ? s.Split(';')[1].Replace("tools=", "").Replace("tool=", "") : "").Split('+', StringSplitOptions.RemoveEmptyEntries).Where(x => x != "None").OrderBy(x => x));
		return Gear(Inv?.Serialize()) == Gear(seed.Inventory);
	}

	private BeatResult Begin(Beat beat)
	{
		if (!_s.Results.TryGetValue(beat.Id, out var r))
		{
			r = new BeatResult { Id = beat.Id, Name = beat.Name, Act = beat.Act };
			_s.Results[beat.Id] = r;
		}
		return r;
	}

	/// <summary>Runs one phase of a beat under its time budget. False if the level is going away under it.</summary>
	private async Task<bool> RunBeat(Beat beat)
	{
		Begin(beat);
		if (_s.Active != _s.Index) { _s.Active = _s.Index; _s.Phase = 0; _s.BeatStartMs = Time.GetTicksMsec(); }
		_s.PhaseStartMs = Time.GetTicksMsec();
		ErrorLog.Beat = beat.Id;
		GD.Print($"[playtest] ---- {beat.Id} {beat.Name} (phase {_s.Phase})");
		var beatCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
		bool timedOut = false, over = false;
		var timer = GetTree().CreateTimer(beat.Budget, true, false, true);
		timer.Timeout += () => { if (!over && !beatCts.IsCancellationRequested) { timedOut = true; beatCts.Cancel(); } };
		try
		{
			await _bot.WaitUntil(() => GameFlow.Instance is { Started: true }, 30, beatCts.Token);
			await beat.Run(beatCts.Token);
		}
		catch (OperationCanceledException)
		{
			if (_leaving) return false;
			if (timedOut) Check($"finished within its budget ({beat.Budget:0} s)", false, $"timed out; last: {_bot.LastAction}");
		}
		catch (Abort a) { Check(a.Message, false, a.Detail); }
		catch (Exception e) { Check("ran without an exception", false, e.ToString()); }
		finally
		{
			over = true;
			Engine.TimeScale = 1.0;
			if (!_leaving)
			{
				_bot.Release();
				ClosePause();
				if (NoteOverlay.Instance is { IsOpen: true } note) note.Close();
			}
		}
		if (_leaving) return false;
		if (_s.ExpectReload)
		{
			// the beat asked for a reload that never came
			Check("the reload it was waiting for came", false, "no level change");
			_s.ExpectReload = false;
		}
		return true;
	}

	/// <summary>The audio audit, the engine's errors, the frame rate, a screenshot; then on to the next beat.</summary>
	private void EndBeat(Beat beat)
	{
		var r = Begin(beat);
		if (r.Status != "skipped")
		{
			if (AudioDirector() is { } ad)
			{
				Check("audio: every sound on a real bus, none over +8 dB, finished ones freed", ad.BadBuses.Count == 0 && ad.TooLoud.Count == 0 && ad.Leaks < 200, ad.Report());
				ad.BadBuses.Clear(); ad.TooLoud.Clear(); ad.SilentSurfaces.Clear(); ad.Unbounded.Clear();
			}
			var (errors, warnings, msgs) = ErrorLog.Take(beat.Id);
			r.ErrorCount += errors; r.WarningCount += warnings; r.Errors.AddRange(msgs);
			Check("no engine errors in the log", errors == 0, errors == 0 ? $"{warnings} warnings" : $"{errors} errors: {string.Join(" | ", msgs.Take(4))}");
			if (r.Shot == null) Shot("end");
			r.Seconds = (Time.GetTicksMsec() - _s.BeatStartMs) / 1000.0;
			if (r.Fails > 0) r.Status = "FAIL";
		}
		GD.Print($"[playtest] {beat.Id} {r.Status}: {r.Checks.Count - r.Fails}/{r.Checks.Count} in {r.Seconds:0.0} s (+{r.LoadSeconds:0.0} s loading)");
		_s.Index++;
		_s.Active = -1;
		_s.Phase = 0;
		_s.ExpectReload = false;
		ErrorLog.Beat = "";
	}

	private static Audio.AudioDirector AudioDirector() => Audio.AudioDirector.Instance;

	/// <summary>The pause menu shut and the tree running again, whatever a beat left behind.</summary>
	private void ClosePause()
	{
		if (!GetTree().Paused) return;
		if ((GetTree().GetFirstNodeInGroup("pause_menu") ?? GetTree().CurrentScene?.FindChild("PauseMenu", true, false)) is PauseMenu pm) pm.SetOpen(false);
		GetTree().Paused = false;
	}

	/// <summary>The bot's metres into the session (the bot is new with every level).</summary>
	private void FlushDistances()
	{
		if (_bot == null || _s == null) return;
		_s.Walked += _bot.Walked; _s.Jumped += _bot.Jumped;
		_bot.Walked = _bot.Jumped = 0;
	}

	public override void _Process(double delta)
	{
		if (_s == null || _bot?.Player == null || !IsInstanceValid(_bot.Player)) return;
		_bot.Tick();
		_fpsTimer += delta / Math.Max(Engine.TimeScale, 0.01);
		if (_fpsTimer >= 0.5 && _s.Active == _s.Index && Result is { } r && !GetTree().Paused && Time.GetTicksMsec() - _s.PhaseStartMs > 1500)
		{
			_fpsTimer = 0;
			float fps = (float)Engine.GetFramesPerSecond();
			r.FpsSum += fps; r.FpsN++; r.FpsMin = Mathf.Min(r.FpsMin, fps);
		}
	}

	// ------------------------------------------------------------------ checks

	private sealed class Abort : Exception
	{
		public string Detail;
		public Abort(string what, string detail) : base(what) { Detail = detail; }
	}

	private bool Check(string name, bool ok, string detail = "")
	{
		var r = Result;
		if (r == null) { GD.Print($"[playtest] (no beat) {(ok ? "PASS" : "FAIL")} {name} {detail}"); return ok; }
		r.Checks.Add((name, ok, detail ?? ""));
		GD.Print($"[playtest] {(ok ? "PASS" : "FAIL")} {name} {detail}  [{(Time.GetTicksMsec() - _s.BeatStartMs) / 1000.0:0.0}s]");
		return ok;
	}

	/// <summary>A check the rest of the beat can't go on without: on a failure the beat ends here.</summary>
	private void Require(string name, bool ok, string detail = "")
	{
		if (!ok) throw new Abort(name, detail + " (the beat stops here)");
		Check(name, true, detail);
	}

	private void CheckObjective(string name, string group)
	{
		var node = GetTree().GetFirstNodeInGroup(group) as Node3D;
		var obj = Story.ObjectivePosition;
		Check(name, node != null && obj != null && obj.Value.DistanceTo(node.GlobalPosition) < 3f, $"objective {obj}, {group} {node?.GlobalPosition}");
	}

	/// <summary>The beat is about to leave the level (a death, a travel, the credits): the next level's Playtest carries on
	/// with it (the beat runs again with <see cref="Phase"/> one higher). Waits for it.</summary>
	private async Task ExpectReload(CancellationToken ct, double seconds = 60, double scale = 4.0)
	{
		_s.ExpectReload = true;
		// (the death's fade and its line, the credits' cards: time-compressed; the level's change resets the scale)
		Engine.TimeScale = scale;
		await _bot.WaitUntil(() => false, seconds, ct);
	}

	private int Phase => _s.Phase;
	private T Memo<T>(string key, T fallback = default) => _s.Memo.TryGetValue($"{Current?.Id}.{key}", out var v) && v is T t ? t : fallback;
	private void Memo(string key, object value) => _s.Memo[$"{Current?.Id}.{key}"] = value;

	/// <summary>Time-compressed while the returned scope lives (only round waits and sequences: walking stays at 1).</summary>
	private static Scale Fast(double scale = 3.0) => new(scale);
	private readonly struct Scale : IDisposable
	{
		public Scale(double s) { Engine.TimeScale = s; }
		public void Dispose() { Engine.TimeScale = 1.0; }
	}

	/// <summary>Control back after a load or a cutscene.</summary>
	private async Task Control(CancellationToken ct, double seconds = 20)
	{
		await _bot.WaitUntil(() => Input.Enabled && !Input.Modal, seconds, ct);
		await _bot.Frames(3, ct);
	}

	// ------------------------------------------------------------------ output

	/// <summary>(2026-10-19) A fog zone: its state in the log, and the view with it and without it (label_on, label_off).</summary>
	private async Task FogProbe(World.FogZone zone, string label, CancellationToken ct)
	{
		var env = GetViewport().World3D?.Environment ?? GetTree().Root.GetCamera3D()?.Environment;
		var cam = GetViewport().GetCamera3D();
		GD.Print($"[fog] {label}: zone {(zone != null ? "there" : "MISSING")} visible {zone?.Visible} strength {zone?.Strength:0.00} camera in {(zone != null && cam != null ? zone.Contains(cam.GlobalPosition) : false)}; "
			+ $"env volumetric {env?.VolumetricFogEnabled} density {env?.VolumetricFogDensity:0.000} length {env?.VolumetricFogLength:0}");
		if (zone == null) return;
		await _bot.Seconds(0.6, ct);
		Shot(label + "_on");
		float s = zone.Strength;
		zone.FadeTo(0f, 0f);
		await _bot.Seconds(0.6, ct);
		Shot(label + "_off");
		zone.FadeTo(s, 0f);
	}

	private void Shot(string label)
	{
		var r = Result;
		if (r == null || DisplayServer.GetName() == "headless") return;
		try
		{
			string file = $"{r.Act:00}_{r.Id}_{label}.png";
			GetViewport()?.GetTexture()?.GetImage()?.SavePng(ProjectSettings.GlobalizePath($"{OutDir}/{file}"));
			r.Shot ??= file;
			if (World.GapAudit.Requested) World.GapAudit.Scan(GetTree().Root, file[..^4]);   // (the joints audit: --gap-audit)
		}
		catch (Exception e) { GD.PushWarning($"[playtest] screenshot '{label}' failed: {e.Message}"); }
	}

	private void Finish(int code = -1)
	{
		Engine.TimeScale = 1.0;
		double secs = (Time.GetTicksMsec() - _s.StartMs) / 1000.0;
		var results = _s.Plan.Select(id => _s.Results.TryGetValue(id, out var r) ? r : null).Where(r => r != null).ToList();
		int checks = results.Sum(r => r.Checks.Count), fails = results.Sum(r => r.Fails);
		int beatsFailed = results.Count(r => r.Status == "FAIL"), skipped = results.Count(r => r.Status == "skipped");
		var sb = new StringBuilder();
		sb.AppendLine("# Project DS playtest");
		sb.AppendLine();
		sb.AppendLine($"{Time.GetDatetimeStringFromSystem()} · {(fails == 0 && code < 0 ? "**PASS**" : "**FAIL**")} · {results.Count - beatsFailed - skipped}/{results.Count} beats passed ({skipped} skipped) · {checks - fails}/{checks} checks · "
			+ $"{secs / 60:0}:{secs % 60:00} total ({_s.LoadTotal:0} s of it loading) · walked {_s.Walked:0} m, jumped {_s.Jumped:0} m{(FastChain ? $" · fast: {_s.Chained} beats chained without a reload" : "")}");
		sb.AppendLine();
		sb.AppendLine("| beat | act | what | result | checks | time (s) | load (s) | fps avg / min | errors | shot |");
		sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
		foreach (var r in results)
			sb.AppendLine($"| {r.Id} | {r.Act} | {r.Name} | {(r.Status == "FAIL" ? "**FAIL**" : r.Status)} | {r.Checks.Count - r.Fails}/{r.Checks.Count} | {r.Seconds:0} | {r.LoadSeconds:0} | "
				+ $"{(r.FpsN > 0 ? $"{r.FpsSum / r.FpsN:0} / {r.FpsMin:0}" : "-")} | {r.ErrorCount}{(r.WarningCount > 0 ? $" (+{r.WarningCount}w)" : "")} | {(r.Shot != null ? $"[png]({r.Shot})" : "")} |");
		var failed = results.Where(r => r.Fails > 0).ToList();
		if (failed.Count > 0)
		{
			sb.AppendLine().AppendLine("## Failures").AppendLine();
			foreach (var r in failed)
				foreach (var c in r.Checks.Where(c => !c.ok))
					sb.AppendLine($"- **{r.Id}** {c.name} — {Md(c.detail)}");
		}
		if (results.Any(r => r.Errors.Count > 0))
		{
			sb.AppendLine().AppendLine("## Engine errors").AppendLine();
			foreach (var r in results.Where(r => r.Errors.Count > 0))
				foreach (var e in r.Errors.Take(8)) sb.AppendLine($"- **{r.Id}** {Md(e)}");
		}
		sb.AppendLine().AppendLine("## Every check").AppendLine();
		foreach (var r in results)
		{
			sb.AppendLine($"### {r.Id} · Act {r.Act}: {r.Name} — {r.Status}");
			foreach (var c in r.Checks) sb.AppendLine($"- {(c.ok ? "ok" : "**FAIL**")} {c.name}{(string.IsNullOrEmpty(c.detail) ? "" : " — " + Md(c.detail))}");
			sb.AppendLine();
		}
		using (var f = FileAccess.Open($"{OutDir}/report.md", FileAccess.ModeFlags.Write)) f?.StoreString(sb.ToString());
		GD.Print(sb.ToString());
		int exit = code >= 0 ? code : fails > 0 ? 1 : 0;
		_s = null;
		SaveSystem.DeleteSlots();
		GetTree().Quit(exit);
	}

	private static string Md(string s) => (s ?? "").Replace("\n", " ").Replace("|", "/").Length > 600 ? (s ?? "").Replace("\n", " ").Replace("|", "/")[..600] + "..." : (s ?? "").Replace("\n", " ").Replace("|", "/");

	// ------------------------------------------------------------------ the engine's own log

	/// <summary>Every engine error and warning, counted against the beat running (a logger on the engine's log).</summary>
	private static partial class ErrorLog
	{
		private static readonly object Gate = new();
		private static readonly Dictionary<string, (int e, int w, List<string> m)> ByBeat = new();
		private static bool _installed;
		public static volatile string Beat = "";

		public static void Install()
		{
			if (_installed) return;
			_installed = true;
			OS.AddLogger(new Sink());
		}

		public static void Note(bool warning, string text)
		{
			lock (Gate)
			{
				string b = Beat ?? "";
				if (!ByBeat.TryGetValue(b, out var x)) x = (0, 0, new List<string>());
				if (warning) x.w++;
				else { x.e++; if (x.m.Count < 12) x.m.Add(text); }
				ByBeat[b] = x;
			}
		}

		/// <summary>The beat's count (and anything logged between beats, while it was loading).</summary>
		public static (int errors, int warnings, List<string> msgs) Take(string beat)
		{
			lock (Gate)
			{
				int e = 0, w = 0; var m = new List<string>();
				foreach (var key in new[] { beat, "" })
					if (ByBeat.Remove(key, out var x)) { e += x.e; w += x.w; m.AddRange(x.m); }
				return (e, w, m);
			}
		}

		private sealed partial class Sink : Logger
		{
			public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
			{
				// errorType: 0 error, 1 warning, 2 script, 3 shader
				Note(errorType == 1, $"{(string.IsNullOrEmpty(rationale) ? code : rationale)} ({System.IO.Path.GetFileName(file)}:{line} {function})");
			}
		}
	}
}
