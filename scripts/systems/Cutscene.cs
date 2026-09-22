using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.Systems;

/// <summary>
/// Runs story sequences safely. Every scripted beat (a forced climb, a reveal, a
/// set piece) goes through <see cref="Run"/> instead of a bare fire-and-forget Task:
/// - it is cancelled when its owner leaves the tree (quit to menu mid-scene);
/// - exceptions are logged instead of silently swallowed;
/// - if it locked the player, control is always handed back, even on error.
/// Waits use pausable timers, so opening the pause menu pauses the story too.
/// Temporary nodes a sequence spawns go under <see cref="SceneRoot"/>, never the
/// tree root, so a scene change frees them.
///
/// The input/body lock is reference-counted: several overlapping locks (two
/// runs, or a run plus a manual <see cref="Lock"/>/<see cref="Unlock"/> pair)
/// only hand control back when the last one lets go. The counts belong to one
/// player instance and reset when it leaves the tree (a scene change).
/// </summary>
public static class Cutscene
{
	/// <summary>How many <see cref="Run"/> bodies are in flight right now (tests and the HUD dot read it).</summary>
	public static int ActiveCount { get; private set; }

	/// <summary>Current depth of the input lock (0 = the player has control, as far as cutscenes are concerned).</summary>
	public static int InputLocks => _inputLocks;

	private static int _inputLocks, _bodyLocks;
	private static PlayerController _lockedPlayer;

	/// <param name="lockInput">Disable player input for the duration (restored in finally).</param>
	/// <param name="freezeBody">Also stop the player's physics (for scripted glides/teleports).</param>
	/// <returns>Completes when the body has finished, failed or been cancelled. Story code may ignore it
	/// (fire-and-forget); tests await it.</returns>
	public static Task Run(Node owner, Func<CancellationToken, Task> body, bool lockInput = false, bool freezeBody = false)
	{
		if (owner == null || !owner.IsInsideTree()) return Task.CompletedTask;
		return RunCore(owner, body, lockInput, freezeBody);
	}

	private static async Task RunCore(Node owner, Func<CancellationToken, Task> body, bool lockInput, bool freezeBody)
	{
		using var cts = new CancellationTokenSource();
		void Cancel() { if (!cts.IsCancellationRequested) cts.Cancel(); }
		owner.TreeExiting += Cancel;

		var player = (lockInput || freezeBody) ? owner.GetTree().GetFirstNodeInGroup("player") as PlayerController : null;
		if (player != null) Lock(player, lockInput, freezeBody);
		ActiveCount++;
		try
		{
			await body(cts.Token);
		}
		catch (OperationCanceledException) { }
		catch (Exception e)
		{
			GD.PushError($"Cutscene on '{owner.Name}' failed: {e}");
		}
		finally
		{
			ActiveCount--;
			if (GodotObject.IsInstanceValid(owner)) owner.TreeExiting -= Cancel;
			if (player != null && GodotObject.IsInstanceValid(player) && player.IsInsideTree())
				Unlock(player, lockInput, freezeBody);
		}
	}

	/// <summary>Takes one reference on the input (and optionally body) lock. Input is disabled at the first lock.</summary>
	public static void Lock(PlayerController p, bool input = true, bool body = false)
	{
		Bind(p);
		if (input && _inputLocks++ == 0) p.PlayerInput.SetEnabled(false);
		if (body && _bodyLocks++ == 0) p.SetPhysicsProcess(false);
	}

	/// <summary>Releases one reference. Input is re-enabled only when the last lock lets go.</summary>
	public static void Unlock(PlayerController p, bool input = true, bool body = false)
	{
		Bind(p);
		if (input && _inputLocks > 0 && --_inputLocks == 0) p.PlayerInput.SetEnabled(true);
		if (body && _bodyLocks > 0 && --_bodyLocks == 0) p.SetPhysicsProcess(true);
	}

	/// <summary>The counts belong to one player: a new player (a new level) starts from zero, and the
	/// counts also reset when the player leaves the tree, so a level quit mid-cutscene can't strand them.</summary>
	private static void Bind(PlayerController p)
	{
		if (ReferenceEquals(p, _lockedPlayer)) return;
		_inputLocks = 0;
		_bodyLocks = 0;
		_lockedPlayer = p;
		p.TreeExiting += () =>
		{
			if (!ReferenceEquals(p, _lockedPlayer)) return;
			_inputLocks = 0;
			_bodyLocks = 0;
			_lockedPlayer = null;
		};
	}

	// Every await below is bound to the SceneTree, not the owner: a signal awaiter whose target has
	// been freed never resumes (Godot drops it with "class instance is gone"), which would strand the
	// sequence and skip its finally. The tree outlives every owner, so a freed owner's sequence
	// resumes, sees its cancellation, and unwinds properly.
	private static SceneTree TreeOf(Node owner, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree()) throw new OperationCanceledException();
		return owner.GetTree();
	}

	/// <summary>Pausable wait. Throws OperationCanceledException if the sequence was cancelled meanwhile.</summary>
	public static async Task Wait(Node owner, double seconds, CancellationToken ct)
	{
		var tree = TreeOf(owner, ct);
		var timer = tree.CreateTimer(seconds, processAlways: false);
		await tree.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
		ct.ThrowIfCancellationRequested();
	}

	/// <summary>Waits one process frame. Pausable: frames while the owner can't process (the tree is
	/// paused) don't count, so per-frame loops freeze with the pause menu.</summary>
	public static async Task Frame(Node owner, CancellationToken ct)
	{
		var tree = TreeOf(owner, ct);
		do await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		while (GodotObject.IsInstanceValid(owner) && owner.IsInsideTree() && !owner.CanProcess() && !ct.IsCancellationRequested);
		ct.ThrowIfCancellationRequested();
	}

	/// <summary>
	/// Awaits a tween. Cancelled meanwhile, it kills the tween and throws; killed or finished
	/// elsewhere (anything that makes it invalid), it simply returns, so no continuation is ever
	/// left waiting on a signal that will not come.
	/// </summary>
	public static async Task Tween(Node owner, Godot.Tween tween, CancellationToken ct)
	{
		var tree = TreeOf(owner, ct);
		if (tween == null) return;
		while (GodotObject.IsInstanceValid(tween) && tween.IsValid())
		{
			await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			if (!ct.IsCancellationRequested) continue;
			if (GodotObject.IsInstanceValid(tween) && tween.IsValid()) tween.Kill();
			ct.ThrowIfCancellationRequested();
		}
		ct.ThrowIfCancellationRequested();
	}

	/// <summary>Where a sequence should parent temporary nodes: the current level, so a scene change frees them.</summary>
	public static Node SceneRoot(Node owner) => owner.GetTree().CurrentScene ?? owner;
}
