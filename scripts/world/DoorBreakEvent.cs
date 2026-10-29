using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 5's break-in: at the boarded cabin door, an axe chops through quickly;
/// a hammer takes a slower, hold-to-pry mini-game. Either way the door opens,
/// the tool is used up, and the player can finally step inside.
///
/// An <see cref="Interactable"/> on the door: look at the boards and press E
/// (axe) or hold E (hammer, <see cref="HammerSeconds"/>). Also owns restoring
/// the door on Continue: boarded from Act 2 until it was broken open.
///
/// (2026-10-29, the playtest: the hammer is a sledge now, and "when used on the boarded door play an animation of the sledge
/// slamming the boards off in time with the hold's progress") The hold is a run of blows, one a board
/// (<see cref="Cabin.DoorBoards"/>: the two crossed over the front, then top to bottom): the sledge heaved back as the ring
/// nears each mark (<see cref="HeldUse"/>'s slam), and as it lands the board cracks, jumps and flies off onto the porch.
/// Let go and the ring stays at the last board off (the boards don't nail themselves back on); the last blow opens the door.
///
/// Prompts never carry the key hint; the HUD adds it. (The giant no longer gates the door: it is seen from the Act 7 lookout now.)
/// </summary>
public partial class DoorBreakEvent : Interactable
{
	[Export] public NodePath CabinPath = "..";
	[Export] public float AxeSeconds = 2.2f;
	[Export] public float HammerSeconds = 9f;

	private Cabin _cabin;
	private PlayerInventory _inventory;
	private bool _busy;
	/// <summary>Sledge blows swung, and boards knocked off (the ring's floor is the last board's mark).</summary>
	private int _swung, _knocked;
	private readonly RandomNumberGenerator _rng = new() { Seed = 5150 };

	/// <summary>For tests: boards the sledge has knocked off so far.</summary>
	public int BoardsKnocked => _knocked;

	public override void _Ready()
	{
		_cabin = GetNode<Cabin>(CabinPath);
		// The pick volume sits over the door itself, a little proud of its collider.
		PickOffset = _cabin.ToLocal(_cabin.DoorCenter) - Position;
		PickRadius = 0.9f;
		MaxDistance = 3f;
		// Highlight the planks nailed over the door (resolved when focused, since they are rebuilt).
		// No highlight (Dan, 2026-09-22: the whole wall lit up): the prompt alone marks the door.
		HighlightRoot = new NodePath("NoHighlight");
		base._Ready();
		Callable.From(Restore).CallDeferred();
		if (StoryManager.Instance is { } s) s.CheckpointReached += OnCheckpoint;
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		if (StoryManager.Instance is { } s) s.CheckpointReached -= OnCheckpoint;
		if (_inventory != null && IsInstanceValid(_inventory)) _inventory.ToolChanged -= UpdateHold;
	}

	/// <summary>Continue: put the door back the way the story left it.</summary>
	private void Restore()
	{
		var s = StoryManager.Instance;
		if (StoryBeat.CabinDoorOpen(s)) _cabin.SetOpen(true);   // no sound or animation
		else if (s is { StairsClimbed: true }) _cabin.SetBoarded(true);
		UpdateEnabled();
		// The hold time depends only on what is carried, so it follows the inventory, not the prompt.
		_inventory = StoryBeat.Player(this)?.Inventory;
		if (_inventory != null) _inventory.ToolChanged += UpdateHold;
		UpdateHold();
	}

	private void OnCheckpoint(Checkpoint _) => UpdateEnabled();

	private void UpdateEnabled() => Enabled = _cabin.DoorBoarded && !_cabin.IsOpen;

	private void UpdateHold() => HoldSeconds = ToolFor(_inventory) == ToolKind.Hammer ? HammerSeconds : 0f;

	public override bool CanInteract(PlayerController player)
	{
		if (!base.CanInteract(player) || _busy) return false;
		return ToolFor(player.Inventory) != ToolKind.None;
	}

	public override string GetPrompt(PlayerController player)
	{
		if (_busy) return "Chopping through the boards...";
		var tool = ToolFor(player.Inventory);
		if (tool == ToolKind.None) return "The door is boarded shut.";
		return tool == ToolKind.Axe
			? "Chop the boards with the axe"
			: HoldProgress > 0f ? $"Smashing the boards off... {HoldProgress * 100f:0}%" : "Smash the boards off with the sledge";
	}

	// ------------------------------------------------------------------ the sledge, blow by blow

	/// <summary>Where on the ring board <paramref name="i"/> of <paramref name="n"/> comes off (the last a little short of
	/// full: the door goes with the ring's end).</summary>
	private static float BoardMark(int i, int n) => (i + 0.85f) / (n + 0.1f);

	public override void _Process(double delta)
	{
		if (!Enabled || _busy || HoldSeconds <= 0f || !_cabin.DoorBoarded) return;
		int n = _cabin.DoorBoards.Count;
		if (n == 0) return;
		// let go: the ring keeps the boards already off
		float floor = _knocked > 0 ? BoardMark(_knocked - 1, n) : 0f;
		if (HoldProgress < floor) HoldProgress = floor;
		var player = StoryBeat.Player(this);
		bool holding = player?.Interaction is { Holding: true } pi && pi.Focused == this;
		float lead = HeldUse.SlamImpact / HammerSeconds;
		// the next blow heaved back so its head lands on the board's mark
		if (holding && _swung == _knocked && _swung < n && HoldProgress >= BoardMark(_swung, n) - lead)
		{
			_swung++;
			HeldUse.Do(player, ToolKind.Hammer, _cabin.DoorCenter, HammerSeconds / (n + 0.1f) + 0.05f);
		}
		// landed: that board off
		if (_knocked < _swung && HoldProgress >= BoardMark(_knocked, n)) KnockBoard(_knocked++);
		// let go before it landed: that swing spent, the next hold heaves again
		if (!holding && _swung > _knocked && !HeldUse.Busy) _swung = _knocked;
	}

	/// <summary>The sledge lands: the board cracks off its nails, jumps and flies out onto the porch.</summary>
	private void KnockBoard(int i)
	{
		var boards = _cabin.DoorBoards;
		if (i >= boards.Count || boards[i] is not { } b || !IsInstanceValid(b) || b.HasMeta("off")) return;
		b.SetMeta("off", true);
		var at = b.GlobalPosition;
		Audio.AudioDirector.OneShot(this, "door_frame_thud", 4, at, 4f, unit: 5f, pitchJitter: 0.08f);
		Audio.AudioDirector.OneShot(this, i % 2 == 0 ? "stair_break" : "wall_burst", i % 2 == 0 ? 1 : 3, at, -2f, unit: 4f, pitchJitter: 0.12f);
		if (StoryBeat.Player(this)?.CameraRig is { } rig) _ = Jolt(rig);
		Splinters(at);
		// off its nails and out (the cabin's +Z, away from the door), turning over, down flat onto the porch boards
		float side = _rng.RandfRange(-0.6f, 0.6f);
		Vector3 from = b.Position, rest = from + new Vector3(side, -from.Y + 0.03f + 0.02f * i, _rng.RandfRange(0.8f, 1.5f));
		var spin = b.Rotation + new Vector3(_rng.RandfRange(-1.4f, -0.6f), _rng.RandfRange(-0.8f, 0.8f), _rng.RandfRange(-0.5f, 0.5f));
		var lie = new Vector3(-Mathf.Pi * 0.5f, _rng.RandfRange(-0.9f, 0.9f), b.Rotation.Z * 0.2f);
		var tw = b.CreateTween();
		tw.TweenProperty(b, "position", from + new Vector3(0, 0.02f, 0.04f), 0.05f);   // the crack: it jumps off its nails
		tw.TweenProperty(b, "position", (from + rest) * 0.5f + new Vector3(0, 0.35f, 0.2f), 0.18f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tw.Parallel().TweenProperty(b, "rotation", spin, 0.18f);
		tw.TweenProperty(b, "position", rest, 0.24f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tw.Parallel().TweenProperty(b, "rotation", lie, 0.24f);
		tw.TweenCallback(Callable.From(() => { if (IsInstanceValid(b)) Audio.AudioDirector.OneShot(this, "wood_bump", 3, b.GlobalPosition, -4f); }));
		GD.Print($"[story] Act 5: the sledge lands, board {i + 1}/{boards.Count} off ({HoldProgress * 100f:0}%)");
	}

	/// <summary>The blow through the arms: a short, hard shake of the view.</summary>
	private async System.Threading.Tasks.Task Jolt(PlayerCameraRig rig)
	{
		double t = 0;
		while (t < 0.22 && IsInstanceValid(rig))
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			t += GetProcessDeltaTime();
			float k = 0.012f * (1f - (float)(t / 0.22));
			rig.Shake = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1), 0) * k;
		}
		if (IsInstanceValid(rig)) rig.Shake = Vector3.Zero;
	}

	/// <summary>Splinters and grit off the struck board.</summary>
	private void Splinters(Vector3 world)
	{
		var pm = new ParticleProcessMaterial
		{
			Direction = Vector3.Up, Spread = 70f, InitialVelocityMin = 1.5f, InitialVelocityMax = 4f, Gravity = new Vector3(0, -9.8f, 0),
			AngularVelocityMin = -720f, AngularVelocityMax = 720f, ScaleMin = 0.5f, ScaleMax = 1.5f,
		};
		var p = new GpuParticles3D
		{
			Name = "Splinters", Amount = 22, Lifetime = 0.9, OneShot = true, Explosiveness = 0.95f, ProcessMaterial = pm,
			DrawPass1 = new BoxMesh { Size = new Vector3(0.05f, 0.01f, 0.012f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.52f, 0.4f), Roughness = 0.95f } },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, LocalCoords = false,
		};
		GetTree().CurrentScene.AddChild(p);
		p.GlobalPosition = world + _cabin.GlobalBasis.Z * 0.1f;
		p.Emitting = true;
		p.Finished += p.QueueFree;
	}

	public override void Interact(PlayerController player)
	{
		var inv = player.Inventory;
		if (inv == null || _busy) return;
		var tool = ToolFor(inv);
		if (tool == ToolKind.None) return;
		_busy = true;
		bool axe = tool == ToolKind.Axe;
		// (the sledge: whatever boards the blows didn't take go with the last)
		if (!axe) for (int i = _knocked; i < _cabin.DoorBoards.Count; i++) KnockBoard(i);
		_ = Cutscene.Run(this, async ct =>
		{
			if (axe) await Cutscene.Wait(this, AxeSeconds, ct);
			else await Cutscene.Wait(this, 0.5, ct);   // (the last board down on the porch before the door swings)
			inv.Consume(tool);
			_cabin.OpenDoor();
			Enabled = false;
			StoryManager.Instance?.SetFlag(StoryManager.Flag.CabinDoorOpen);
			base.Interact(player);
			await Cutscene.Wait(this, 1.2, ct);   // no line (Dan, 2026-09-22: the door itself says it)
		});
	}

	/// <summary>The axe if carried (quick), else the hammer (the slow pry), else nothing.</summary>
	private static ToolKind ToolFor(PlayerInventory inv)
		=> inv == null ? ToolKind.None : inv.HasTool(ToolKind.Axe) ? ToolKind.Axe : inv.HasTool(ToolKind.Hammer) ? ToolKind.Hammer : ToolKind.None;
}
