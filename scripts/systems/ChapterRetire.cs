using System.Collections.Generic;
using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// The Hollow's chapters let go of what's behind them (2026-10-22, the owner: the game "broken up in accordance to the
/// chapters ... to make the game run smoother"). The Hollow is one scene holding every place from the camp to the snow
/// maze; the story is one-way, so once a chapter has moved on, the places it left can never be seen again. Each is freed
/// at the first checkpoint from which it is out of sight for good (not always the chapter's first: the library opens off
/// the pit, the round room's shaft looks down into it), one place a frame so freeing is never a hitch. A place that
/// holds the way on (the station holds everything under it; each underground room holds the next) keeps that child and
/// lets the rest go, its own script stopped. The woods' ground is only hidden: its heights are still asked for.
///
/// What it saves: their meshes, materials and lights stop costing anything (the descriptor heap that overflowed by Act 23
/// was every place at once), and memory comes back. A Continue lets go of the same places right after its load.
/// </summary>
public partial class ChapterRetire : Node
{
	private sealed record Rule(Checkpoint At, string Path, string[] Keep = null, bool HideOnly = false);

	private const string W = "HollowWorld/", S = "HollowWorld/StationInterior/";
	private static readonly Rule[] Rules =
	{
		// the woods (chapters 2 and 3), once the lake is crossed: far behind, over the water
		new(Checkpoint.Act12LakeCrossed, W + "Camp"), new(Checkpoint.Act12LakeCrossed, W + "Cabin"), new(Checkpoint.Act12LakeCrossed, W + "Shed"),
		new(Checkpoint.Act12LakeCrossed, W + "Woodpile"), new(Checkpoint.Act12LakeCrossed, W + "AxePickup"), new(Checkpoint.Act12LakeCrossed, W + "Footbridge"),
		new(Checkpoint.Act12LakeCrossed, W + "Clearing"), new(Checkpoint.Act12LakeCrossed, W + "Lookout"), new(Checkpoint.Act12LakeCrossed, W + "Act7Cabin"),
		new(Checkpoint.Act12LakeCrossed, W + "Act7Herding"), new(Checkpoint.Act12LakeCrossed, W + "Bunker"), new(Checkpoint.Act12LakeCrossed, W + "SurveyLot"),
		new(Checkpoint.Act12LakeCrossed, W + "BunkerInterior"), new(Checkpoint.Act12LakeCrossed, W + "FinalStairs"), new(Checkpoint.Act12LakeCrossed, W + "Forest"),
		new(Checkpoint.Act12LakeCrossed, W + "Terrain", HideOnly: true),
		// the lake and the station's lobby and side rooms, once down the stairwell
		new(Checkpoint.Act14Finished, W + "LakeCrossing"), new(Checkpoint.Act14Finished, W + "Lake"),   // (the crossing first: it reads the lake)
		new(Checkpoint.Act14Finished, S + "Room1"), new(Checkpoint.Act14Finished, S + "Room2"), new(Checkpoint.Act14Finished, S + "Basement"),
		new(Checkpoint.Act14Finished, S + "Decor"), new(Checkpoint.Act14Finished, S + "Headquarters"), new(Checkpoint.Act14Finished, S + "LockedRoom"),
		// the stairwell and the long hall (and the closet at its end), once out into the sewer
		new(Checkpoint.Act16Finished, S + "Room3"),
		// the sewer, once down into the pit
		new(Checkpoint.Act17Finished, S + "Sewer"),
		// the pit, once through the library into the round room (the library opens off it)
		new(Checkpoint.Act19Finished, S + "BossRoom", new[] { "Library" }),
		// the library and the round room, once up in the church (the round room's shaft looked down into them)
		new(Checkpoint.Act21ChurchReached, S + "BossRoom/Library", new[] { "RoundRoom" }),
		new(Checkpoint.Act21ChurchReached, S + "BossRoom/Library/RoundRoom", new[] { "LongStair" }),
		// the long stair and the church, once snowed into the lodge
		new(Checkpoint.Act22Finished, S + "BossRoom/Library/RoundRoom/LongStair", new[] { "Church" }),
		new(Checkpoint.Act22Finished, S + "BossRoom/Library/RoundRoom/LongStair/Church", new[] { "WinterWoods" }),
	};

	private readonly HashSet<Rule> _done = new();
	private readonly Queue<Rule> _queue = new();

	/// <summary>For tests and the log: places let go of so far.</summary>
	public static int Retired { get; private set; }
	public static readonly List<string> RetiredPaths = new();

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		if (StoryManager.Instance is { } s) s.CheckpointReached += OnCheckpoint;
		// (after the level's own builds: a Continue builds what its checkpoint needs first)
		Callable.From(() => { _ = AfterLoad(); }).CallDeferred();
	}

	public override void _ExitTree()
	{
		if (StoryManager.Instance is { } s) s.CheckpointReached -= OnCheckpoint;
	}

	private async System.Threading.Tasks.Task AfterLoad()
	{
		while (IsInsideTree() && GameFlow.Instance is not { Started: true }) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		for (int i = 0; i < 30 && IsInsideTree(); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (StoryManager.Instance is { } s) Enqueue(s.Current);
	}

	private void OnCheckpoint(Checkpoint cp) => Enqueue(cp);

	private void Enqueue(Checkpoint cp)
	{
		foreach (var r in Rules)
			if (cp >= r.At && _done.Add(r)) _queue.Enqueue(r);
	}

	private readonly Stack<Node> _dismantle = new();
	/// <summary>For tests: still being taken apart.</summary>
	public static bool Busy => _instance is { } i && IsInstanceValid(i) && (i._dismantle.Count > 0 || i._queue.Count > 0);
	private static ChapterRetire _instance;
	public static bool Present => _instance != null && IsInstanceValid(_instance) && _instance.IsInsideTree();
	public override void _EnterTree() => _instance = this;

	/// <summary>Frees about <paramref name="budget"/> nodes' worth of what's on the stack: a small node (and what's under it)
	/// at once, a big one child by child, its big children taken apart in their turn, then itself.</summary>
	private void Dismantle(int budget)
	{
		const int Small = 48;
		while (budget > 0 && _dismantle.Count > 0)
		{
			var t = _dismantle.Peek();
			if (!IsInstanceValid(t) || t.IsQueuedForDeletion()) { _dismantle.Pop(); continue; }
			int cc = t.GetChildCount();
			if (cc <= Small) { _dismantle.Pop(); t.QueueFree(); budget -= 1 + cc * 4; continue; }
			bool deeper = false;
			int left = 0, i = cc - 1;
			for (; i >= 0 && budget > 0; i--)
			{
				var c = t.GetChild(i);
				if (c.IsQueuedForDeletion()) continue;
				left++;
				if (c.GetChildCount() > Small) { _dismantle.Push(c); deeper = true; break; }
				budget -= 1 + c.GetChildCount() * 4;
				c.QueueFree();
				left--;
			}
			if (deeper) continue;
			// everything under it gone (or going this frame): it goes too, next time round
			if (i < 0 && left == 0) { _dismantle.Pop(); t.QueueFree(); budget -= 1; }
			else break;
		}
	}

	public override void _Process(double delta)
	{
		// a big place taken apart a few hundred nodes a frame (2026-10-23, the lag meter: the woods, 8000 nodes freed in one
		// frame, held it 66 ms); a new place only once the last is gone
		if (_dismantle.Count > 0) { Dismantle(450); return; }
		if (_queue.Count == 0 || GetTree().CurrentScene is not { } root) return;
		var r = _queue.Dequeue();
		if (root.GetNodeOrNull(r.Path) is not Node n) return;
		if (r.HideOnly)
		{
			if (n is Node3D n3) n3.Visible = false;
		}
		else if (r.Keep == null) { n.ProcessMode = ProcessModeEnum.Disabled; _dismantle.Push(n); }
		else
		{
			// its own script stops (the place is behind them); the way on keeps running
			n.ProcessMode = ProcessModeEnum.Disabled;
			foreach (var c in n.GetChildren())
			{
				if (System.Array.IndexOf(r.Keep, c.Name.ToString()) >= 0) { c.ProcessMode = ProcessModeEnum.Pausable; continue; }
				c.ProcessMode = ProcessModeEnum.Disabled;
				_dismantle.Push(c);
			}
			if (n is Node3D own) foreach (var c in own.GetChildren()) if (c is VisualInstance3D vi && System.Array.IndexOf(r.Keep, c.Name.ToString()) < 0) vi.Visible = false;
		}
		Retired++;
		RetiredPaths.Add(r.Path);
		GD.Print($"[chapters] let go of {r.Path}{(r.Keep != null ? $" (kept {string.Join(", ", r.Keep)})" : r.HideOnly ? " (hidden)" : "")} at {StoryManager.Instance?.Current}");
	}
}
