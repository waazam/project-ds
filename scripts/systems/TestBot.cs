using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.UI;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// The scripted player the tests drive (the playtest; the same moves StoryTest makes): only through
/// <see cref="PlayerInput"/>'s Scripted fields (move, run, crouch, interact, lantern, photo...) plus
/// <see cref="PlayerController.Teleport"/> for the jumps between beats. Every wait is a frame at a time and
/// gives up when the run's cancellation fires, so a stuck beat can always be abandoned.
/// </summary>
public sealed class TestBot
{
	private readonly Node _host;
	public PlayerController Player { get; private set; }
	public PlayerInput Input => Player?.PlayerInput;
	public PlayerInventory Inv => Player?.Inventory;
	/// <summary>Metres walked (frame to frame, under 3 m a frame) and jumped (teleports).</summary>
	public float Walked, Jumped;
	private Vector3 _last;
	/// <summary>The latest thing the bot did, for the log.</summary>
	public string LastAction = "";

	public TestBot(Node host) { _host = host; }

	public bool Attach()
	{
		Player = StoryBeat.Player(_host);
		if (Player == null) return false;
		Player.PlayerInput.Scripted = true;
		_last = Player.GlobalPosition;
		return true;
	}

	/// <summary>Call every frame: counts the metres walked.</summary>
	public void Tick()
	{
		if (Player == null || !GodotObject.IsInstanceValid(Player)) return;
		float moved = Flat(Player.GlobalPosition).DistanceTo(Flat(_last));
		if (moved < 3f) Walked += moved;
		_last = Player.GlobalPosition;
	}

	/// <summary>Lets go of every scripted key.</summary>
	public void Release()
	{
		var i = Input;
		if (i == null || !GodotObject.IsInstanceValid(i)) return;
		i.ScriptedMove = Vector2.Zero;
		i.ScriptedRun = i.ScriptedFocus = i.ScriptedInteract = i.ScriptedLight = i.ScriptedPhoto = false;
		i.ScriptedItemNext = i.ScriptedItemPrev = i.ScriptedPhotoLog = i.ScriptedLanternMode = i.ScriptedCrouch = i.ScriptedThrow = i.ScriptedBreath = false;
	}

	// ------------------------------------------------------------------ time

	public SceneTree Tree => _host.GetTree();

	public async Task WaitUntil(Func<bool> cond, double seconds, CancellationToken ct)
	{
		ulong end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
		while (!cond() && Time.GetTicksMsec() < end)
		{
			ct.ThrowIfCancellationRequested();
			await _host.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
		}
		ct.ThrowIfCancellationRequested();
	}

	/// <summary>Waits in game time (it shrinks with <see cref="Engine.TimeScale"/>); <paramref name="maxReal"/> real seconds at most.</summary>
	public async Task WaitUntilGame(Func<bool> cond, double gameSeconds, CancellationToken ct)
	{
		double t = 0;
		while (!cond() && t < gameSeconds)
		{
			ct.ThrowIfCancellationRequested();
			await _host.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
			t += _host.GetProcessDeltaTime();
		}
		ct.ThrowIfCancellationRequested();
	}

	/// <summary>Game-time seconds (they shrink with the time scale).</summary>
	public async Task Seconds(double s, CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		await _host.ToSignal(Tree.CreateTimer(s, true, false, false), SceneTreeTimer.SignalName.Timeout);
		ct.ThrowIfCancellationRequested();
	}

	public async Task Frames(int n, CancellationToken ct)
	{
		for (int i = 0; i < n; i++)
		{
			ct.ThrowIfCancellationRequested();
			await _host.ToSignal(Tree, SceneTree.SignalName.PhysicsFrame);
		}
		ct.ThrowIfCancellationRequested();
	}

	// ------------------------------------------------------------------ finding things

	public List<T> AllOf<T>() where T : Node
	{
		var list = new List<T>();
		void Walk(Node n) { if (n is T t) list.Add(t); foreach (var c in n.GetChildren()) Walk(c); }
		if (Tree.CurrentScene is { } root) Walk(root);
		return list;
	}

	public T FirstOf<T>() where T : Node => AllOf<T>().FirstOrDefault();
	public T InGroup<T>(string group) where T : class => Tree.GetFirstNodeInGroup(group) as T;
	public IEnumerable<Pickup> Pickups(ToolKind kind) => AllOf<Pickup>().Where(p => p.Kind == kind && !p.Taken && p.IsVisibleInTree());

	public static T FindUnder<T>(Node root, string name = null) where T : Node
	{
		if (root == null) return null;
		foreach (var c in root.GetChildren())
		{
			if (c is T t && (name == null || c.Name == name)) return t;
			var deeper = FindUnder<T>(c, name);
			if (deeper != null) return deeper;
		}
		return null;
	}

	public string Prompt => Player?.Interaction?.PromptText ?? "";

	// ------------------------------------------------------------------ moving

	/// <summary>Puts the player at a point (onto the forest's ground if that's under it), facing another.</summary>
	public async Task Teleport(Vector3 at, Vector3 faceToward, CancellationToken ct, bool snapToTerrain = true)
	{
		var terrain = GroundSnap.FindTerrain(_host);
		// (onto the ground if it's under it; never when far under it, in a cave or the station)
		if (snapToTerrain && terrain != null && at.Y > terrain.HeightAt(at.X, at.Z) - 20f) at.Y = Mathf.Max(at.Y, terrain.HeightAt(at.X, at.Z));
		Jumped += Flat(at).DistanceTo(Flat(Player.GlobalPosition));
		var d = Flat(faceToward - at);
		Player.Teleport(at + Vector3.Up * (snapToTerrain ? 0.2f : 0.08f), d.LengthSquared() > 0.01f ? Mathf.Atan2(-d.X, -d.Z) : Player.CameraRig.Yaw);
		_last = Player.GlobalPosition;
		LastAction = $"teleport {at.Round()}";
		await Frames(4, ct);
	}

	/// <summary>A teleport indoors / underground (no terrain snap: the forest's ground means nothing there).</summary>
	public Task Inside(Vector3 at, Vector3 faceToward, CancellationToken ct) => Teleport(at, faceToward, ct, snapToTerrain: false);

	/// <summary>Jumps to <paramref name="lastMetres"/> short of <paramref name="target"/> (along the line from it to
	/// <paramref name="from"/>, or from where the player stands) and walks the rest: the trigger itself is walked into.</summary>
	public async Task<bool> ApproachAndWalk(Vector3 target, float lastMetres, float radius, CancellationToken ct, Func<bool> stopWhen = null, Vector3? from = null, bool inside = false, float giveUp = 8f)
	{
		Vector3 origin = from ?? Player.GlobalPosition;
		Vector3 dir = Flat(origin - target);
		if (dir.LengthSquared() < 0.01f) dir = Vector3.Back;
		Vector3 start = target + dir.Normalized() * lastMetres;
		start.Y = inside ? target.Y : start.Y;
		if (Flat(start).DistanceTo(Flat(Player.GlobalPosition)) > 1f)
		{
			if (inside) await Inside(start, target, ct);
			else await Teleport(start, target, ct);
		}
		return await WalkTo(target, radius, ct, stopWhen, giveUp);
	}

	public async Task<bool> WalkTo(Vector3 target, float radius, CancellationToken ct, Func<bool> stopWhen = null, float giveUp = 8f, bool run = false)
	{
		double stuck = 0, sidestep = 0; float best = float.MaxValue, dir = 1f;
		LastAction = $"walk to {target.Round()}";
		try
		{
			while (true)
			{
				ct.ThrowIfCancellationRequested();
				if (stopWhen != null && stopWhen()) return true;
				float d = Flat(Player.GlobalPosition).DistanceTo(Flat(target));
				if (d < radius) return true;
				if (!Input.Enabled) { Input.ScriptedMove = Vector2.Zero; await Frames(1, ct); stuck += _host.GetProcessDeltaTime() * 0.25; if (stuck > giveUp * 4) return false; continue; }   // a beat has control
				if (d < best - 0.3f) { best = d; stuck = 0; }
				stuck += _host.GetProcessDeltaTime();
				if (stuck > giveUp) return false;
				Steer(target);
				Input.ScriptedRun = run || d > 8f;
				if (stuck > 1.2 && sidestep <= 0) { sidestep = 0.9; dir = -dir; }
				if (sidestep > 0) { sidestep -= _host.GetProcessDeltaTime(); Input.ScriptedMove = new Vector2(dir, 0.4f); }
				else Input.ScriptedMove = new Vector2(0, 1);
				await Frames(1, ct);
			}
		}
		finally { if (Input != null && GodotObject.IsInstanceValid(Input)) { Input.ScriptedMove = Vector2.Zero; Input.ScriptedRun = false; } }
	}

	/// <summary>Walks toward a point for a fixed (game) time, whatever happens.</summary>
	public async Task WalkFor(Vector3 target, double seconds, CancellationToken ct, bool run = false)
	{
		double t = 0;
		try
		{
			while (t < seconds)
			{
				ct.ThrowIfCancellationRequested();
				if (Input.Enabled) { Steer(target); Input.ScriptedMove = new Vector2(0, 1); Input.ScriptedRun = run; }
				await Frames(1, ct);
				t += _host.GetProcessDeltaTime();
			}
		}
		finally { if (Input != null && GodotObject.IsInstanceValid(Input)) { Input.ScriptedMove = Vector2.Zero; Input.ScriptedRun = false; } }
	}

	/// <summary>Holds a raw move (e.g. (0,-1): backwards) for a fixed time without steering.</summary>
	public async Task PushFor(Vector2 move, double seconds, CancellationToken ct, bool run = false)
	{
		double t = 0;
		try
		{
			while (t < seconds)
			{
				ct.ThrowIfCancellationRequested();
				if (Input.Enabled) { Input.ScriptedMove = move; Input.ScriptedRun = run; }
				await Frames(1, ct);
				t += _host.GetProcessDeltaTime();
			}
		}
		finally { if (Input != null && GodotObject.IsInstanceValid(Input)) { Input.ScriptedMove = Vector2.Zero; Input.ScriptedRun = false; } }
	}

	/// <summary>The paddle keys in alternation (A, D, A, D...) until <paramref name="done"/>, or <paramref name="giveUp"/> game seconds.</summary>
	public async Task<bool> PaddleUntil(Func<bool> done, CancellationToken ct, double giveUp)
	{
		double t = 0, sideTimer = 0; bool left = true;
		try
		{
			while (!done())
			{
				ct.ThrowIfCancellationRequested();
				if (Input.Enabled)
				{
					sideTimer -= _host.GetProcessDeltaTime();
					if (sideTimer <= 0) { left = !left; sideTimer = 0.15; }
					Input.ScriptedMove = new Vector2(left ? -1f : 1f, 0f);
				}
				await Frames(1, ct);
				t += _host.GetProcessDeltaTime();
				if (t > giveUp) return false;
			}
			return true;
		}
		finally { if (Input != null && GodotObject.IsInstanceValid(Input)) Input.ScriptedMove = Vector2.Zero; }
	}

	public void Steer(Vector3 target)
	{
		var to = Flat(target - Player.GlobalPosition);
		if (to.LengthSquared() < 0.01f) return;
		float want = Mathf.Atan2(-to.X, -to.Z);
		float diff = Mathf.AngleDifference(Player.CameraRig.Yaw, want);
		Input.AddScriptedLook(new Vector2(Mathf.Clamp(diff, -0.09f, 0.09f), 0));
	}

	/// <summary>Points the view straight at a world point (yaw and pitch), like a player lining up the crosshair.</summary>
	public async Task Aim(Vector3 point, CancellationToken ct)
	{
		Player.PlayerInput.ConsumeLook();
		var eye = Player.CameraRig.Camera.GlobalPosition;
		var d = point - eye;
		float yaw = Mathf.Atan2(-d.X, -d.Z);
		float pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
		Player.CameraRig.SnapBehind(yaw);
		Player.Visual.Rotation = new Vector3(0, yaw, 0);
		Player.CameraRig.SetPitch(pitch);
		await Frames(3, ct);
	}

	// ------------------------------------------------------------------ keys

	/// <summary>E: pressed, held a moment (or <paramref name="hold"/> game seconds), released.</summary>
	public async Task Press(CancellationToken ct, double hold = 0.08)
	{
		LastAction = $"press E '{Prompt}'";
		Input.ScriptedInteract = true;
		try
		{
			await Frames(3, ct);
			await Seconds(hold, ct);
		}
		finally { if (Input != null && GodotObject.IsInstanceValid(Input)) Input.ScriptedInteract = false; }
		await Frames(3, ct);
	}

	/// <summary>One press and release of a scripted key (set/reset through the given setter).</summary>
	public async Task Tap(Action<bool> key, CancellationToken ct)
	{
		key(true);
		try
		{
			// held for a few physics ticks and a couple of rendered frames both (readers in _Process and in
			// _PhysicsProcess each see the edge; at a low frame rate three ticks can fall inside one frame)
			await Frames(3, ct);
			for (int i = 0; i < 2; i++) await _host.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
		}
		finally { key(false); }
		await Frames(3, ct);
	}

	/// <summary>One press and release of an input action through the real input pipeline (what a modal overlay's _Input sees).</summary>
	public async Task Key(string action, CancellationToken ct)
	{
		Godot.Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true }); Godot.Input.FlushBufferedEvents();
		await Frames(2, ct);
		Godot.Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false }); Godot.Input.FlushBufferedEvents();
		await Frames(2, ct);
	}

	/// <summary>Looks at an object's Interactable and presses (or holds) E. Returns the prompt it was focused on.</summary>
	public async Task<string> UseIt(Node3D obj, CancellationToken ct)
	{
		if (obj == null || !GodotObject.IsInstanceValid(obj) || !obj.IsInsideTree() || obj is Pickup { Taken: true }) return null;
		var use = obj as Interactable ?? obj.FindChildren("*", "", true, false).OfType<Interactable>().FirstOrDefault()
			?? FindUnder<Interactable>(obj);
		var aim = use != null ? use.ToGlobal(use.PickOffset) : obj.GlobalPosition + Vector3.Up * 0.15f;
		if (Flat(Player.GlobalPosition).DistanceTo(Flat(aim)) > 2.2f) await WalkTo(aim, 1.6f, ct);
		await Aim(aim, ct);
		var focus = Player.Interaction?.Focused;
		if (focus == null && Flat(Player.GlobalPosition).DistanceTo(Flat(aim)) > 1.2f)
		{
			await WalkTo(aim, 1.0f, ct, giveUp: 6f);
			await Aim(aim, ct);
			focus = Player.Interaction?.Focused;
		}
		string prompt = Prompt;
		GD.Print($"[playtest] use {obj.Name}: focused '{focus?.GetParent()?.Name}' prompt '{prompt}'");
		await Press(ct, hold: use != null && use.HoldSeconds > 0 ? use.HoldSeconds + 0.3 : 0.08);
		await Seconds(0.3, ct);
		return prompt;
	}

	/// <summary>Stands <paramref name="dist"/> m from <paramref name="obj"/>'s use point on whichever side it can actually
	/// be focused from (eight tried, the one toward the player first), and uses it. Returns the prompt, or null.</summary>
	public async Task<string> UseAround(Node3D obj, CancellationToken ct, float dist = 1.4f, bool snapToTerrain = true)
	{
		if (obj == null || !GodotObject.IsInstanceValid(obj) || obj is Pickup { Taken: true }) return null;
		var use = obj as Interactable ?? obj.FindChildren("*", "", true, false).OfType<Interactable>().FirstOrDefault();
		var aim = use != null ? use.ToGlobal(use.PickOffset) : obj.GlobalPosition + Vector3.Up * 0.15f;
		var toward = Flat(Player.GlobalPosition - aim).Normalized();
		if (toward == Vector3.Zero) toward = Vector3.Back;
		for (int k = 0; k < 8; k++)
		{
			var dir = toward.Rotated(Vector3.Up, k * Mathf.Tau / 8f);
			var stand = aim + dir * dist;
			stand.Y = obj.GlobalPosition.Y - 0.1f;
			await Teleport(stand, aim, ct, snapToTerrain);
			await Aim(aim, ct);
			await Frames(2, ct);
			var f = Player.Interaction?.Focused;
			if (f != null && (f == use || f.GetParent() == obj || f == obj))
			{
				string prompt = Prompt;
				await Press(ct, hold: use != null && use.HoldSeconds > 0 ? use.HoldSeconds + 0.3 : 0.08);
				await Seconds(0.3, ct);
				return prompt;
			}
		}
		GD.Print($"[playtest] {obj.Name}: no side it could be used from");
		return null;
	}

	/// <summary>B until the lantern is in the wanted mode (a press made while control is briefly held doesn't count).</summary>
	public async Task LanternMode(bool blacklight, CancellationToken ct)
	{
		var lantern = Player.GetNodeOrNull<Lantern>("Lantern");
		for (int i = 0; i < 6 && lantern != null && lantern.Blacklight != blacklight; i++)
		{
			await WaitUntil(() => Input.Enabled && !Input.Modal, 3, ct);
			await Tap(on => Input.ScriptedLanternMode = on, ct);
		}
	}

	public async Task Crouch(bool down, CancellationToken ct)
	{
		for (int i = 0; i < 4 && Player.Crouching != down; i++) await Tap(on => Input.ScriptedCrouch = on, ct);
	}

	/// <summary>Puts a note down (E), and makes sure it's gone.</summary>
	public async Task PutDown(CancellationToken ct)
	{
		await Seconds(0.3, ct);
		if (NoteOverlay.Instance is { IsOpen: true }) await Press(ct);
		await Seconds(0.3, ct);
		if (NoteOverlay.Instance is { IsOpen: true }) NoteOverlay.Instance.Close();
		await Seconds(0.2, ct);
	}

	/// <summary>Raises the camera, zooms, aims at <paramref name="at"/>, lets the focus come in, and takes the picture.</summary>
	public async Task<PhotoLog.Photo> TakePicture(Vector3 at, int zoom, CancellationToken ct)
	{
		var cam = FirstOf<CameraTool>();
		int before = PhotoLog.Instance?.RecordedCount ?? 0;
		Input.ScriptedFocus = true;
		try
		{
			await Seconds(0.4, ct);
			cam?.SetZoom(zoom);
			await Aim(at, ct);
			await Seconds(1.2, ct);
			for (int i = 0; i < 3 && (PhotoLog.Instance?.RecordedCount ?? 0) == before; i++)
			{
				await Tap(on => Input.ScriptedPhoto = on, ct);
				await Seconds(0.6, ct);
			}
		}
		finally { if (Input != null && GodotObject.IsInstanceValid(Input)) Input.ScriptedFocus = false; }
		cam?.SetZoom(0);
		await Seconds(0.3, ct);
		return (PhotoLog.Instance?.RecordedCount ?? 0) > before ? PhotoLog.Instance.Photos.LastOrDefault() : null;
	}

	/// <summary>Whether a ray between two world points hits anything on the world layer.</summary>
	public async Task<bool> RayHits(Vector3 from, Vector3 to, CancellationToken ct)
	{
		await Frames(1, ct);
		var space = Player?.GetWorld3D()?.DirectSpaceState;
		if (space == null) return false;
		return space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, to, 1u)).Count > 0;
	}

	public static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);
}
