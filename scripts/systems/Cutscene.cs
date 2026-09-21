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
/// </summary>
public static class Cutscene
{
	/// <param name="lockInput">Disable player input for the duration (restored in finally).</param>
	/// <param name="freezeBody">Also stop the player's physics (for scripted glides/teleports).</param>
	public static async void Run(Node owner, Func<CancellationToken, Task> body, bool lockInput = false, bool freezeBody = false)
	{
		if (owner == null || !owner.IsInsideTree()) return;
		using var cts = new CancellationTokenSource();
		void Cancel() { if (!cts.IsCancellationRequested) cts.Cancel(); }
		owner.TreeExiting += Cancel;

		var player = (lockInput || freezeBody) ? owner.GetTree().GetFirstNodeInGroup("player") as PlayerController : null;
		if (player != null) Lock(player, lockInput, freezeBody);
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
			if (GodotObject.IsInstanceValid(owner)) owner.TreeExiting -= Cancel;
			if (player != null && GodotObject.IsInstanceValid(player) && player.IsInsideTree())
				Unlock(player, lockInput, freezeBody);
		}
	}

	public static void Lock(PlayerController p, bool input = true, bool body = false)
	{
		if (input) p.PlayerInput.SetEnabled(false);
		if (body) p.SetPhysicsProcess(false);
	}

	public static void Unlock(PlayerController p, bool input = true, bool body = false)
	{
		if (input) p.PlayerInput.SetEnabled(true);
		if (body) p.SetPhysicsProcess(true);
	}

	/// <summary>Pausable wait. Throws OperationCanceledException if the sequence was cancelled meanwhile.</summary>
	public static async Task Wait(Node owner, double seconds, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var timer = owner.GetTree().CreateTimer(seconds, processAlways: false);
		await owner.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
		ct.ThrowIfCancellationRequested();
	}

	/// <summary>Waits one process frame. Pausable: frames while the owner can't process (the tree is
	/// paused) don't count, so per-frame loops freeze with the pause menu.</summary>
	public static async Task Frame(Node owner, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		do await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
		while (GodotObject.IsInstanceValid(owner) && owner.IsInsideTree() && !owner.CanProcess() && !ct.IsCancellationRequested);
		ct.ThrowIfCancellationRequested();
	}

	/// <summary>Awaits a tween (kill it yourself on cancel if it matters).</summary>
	public static async Task Tween(Node owner, Godot.Tween tween, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		await owner.ToSignal(tween, Godot.Tween.SignalName.Finished);
		ct.ThrowIfCancellationRequested();
	}

	/// <summary>Where a sequence should parent temporary nodes: the current level, so a scene change frees them.</summary>
	public static Node SceneRoot(Node owner) => owner.GetTree().CurrentScene ?? owner;
}
