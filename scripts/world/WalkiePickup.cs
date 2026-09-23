using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The walkie-talkie. Since 2026-09-22 it lies dead on the CRT room's console (Act 9):
/// no static, no LED. Taking it (E) puts it in the inventory and sets
/// <see cref="StoryManager.Flag.WalkieTaken"/>; nothing else happens until the player is
/// out of the bunker, where it crackles to life (checkpoint 8, <see cref="Act11Ending"/>).
/// The rooms' way out will not open without it (see BunkerFlow).
///
/// <see cref="Dead"/> = false is the old behaviour (hissing, LED pulsing, the checkpoint on
/// pickup), kept for previews. It settles down onto whatever is under it and is used through
/// an <see cref="Interactable"/> child: highlight and prompt only while it's under the crosshair.
/// </summary>
public partial class WalkiePickup : Area3D
{
	public const string PromptText = "Pick up the walkie-talkie";
	/// <summary>Silent and dark: it only wakes outside the bunker (the story's walkie). Off = the old hissing pickup.</summary>
	[Export] public bool Dead = true;
	/// <summary>For tests: its static is running.</summary>
	public bool Hissing => _staticPlayer != null && IsInstanceValid(_staticPlayer);

	private AudioStreamPlayer3D _staticPlayer;
	private Interactable _use;
	private Node3D _item;
	private ItemMeshes.Built _built;
	private int _settleFrame;
	private bool _settled, _taken;
	private double _t;

	public override void _Ready()
	{
		CollisionLayer = 0;
		CollisionMask = 0;
		// Deferred: the walkie is spawned from the maze-exit trigger's signal, when area flags are locked.
		SetDeferred(Area3D.PropertyName.Monitoring, false);
		SetDeferred(Area3D.PropertyName.Monitorable, false);
		// Restore: already found on a previous run.
		if (StoryManager.Instance != null && (StoryManager.Instance.Current >= Checkpoint.Act10WalkieFound || StoryManager.Instance.HasFlag(StoryManager.Flag.WalkieTaken)))
		{
			QueueFree();
			return;
		}
		_item = new Node3D { Name = "Item" };
		AddChild(_item);
		_built = ItemMeshes.Build(ToolKind.Radio, _item);

		_use = new Interactable
		{
			Name = "Interactable",
			Prompt = PromptText,
			PickRadius = _built.PickRadius,
			PickOffset = _built.PickCenter,
			HighlightRoot = new NodePath("../Item"),
		};
		_use.Interacted += OnInteract;
		AddChild(_use);

		AddToGroup("walkie_marker");   // the compass finds it after the screens
		string path = "res://assets/audio/ambient/radio_static_loop.wav";
		if (!Dead && ResourceLoader.Exists(path))
		{
			// The same object as Act 11's radio: its hiss runs on the band-limited Radio bus too.
			_staticPlayer = new AudioStreamPlayer3D { UnitSize = 3f, MaxDistance = 28f, Bus = "Radio" };
			AddChild(_staticPlayer);
			_staticPlayer.AddChild(new AmbienceLoop { StreamPath = path, BaseVolumeDb = -16f });   // a quiet open squelch, not a storm
		}
	}

	private void OnInteract(PlayerController player)
	{
		if (_taken) return;
		_taken = true;
		// The radio goes into the inventory first, so the checkpoint's save already carries it.
		player.GetNodeOrNull<PlayerInventory>("Inventory")?.TryPickup(ToolKind.Radio);
		if (Dead)
		{
			// Dead in the hand. It wakes outside (BunkerFlow.OnMazeExit reaches the checkpoint there).
			StoryManager.Instance?.SetFlag(StoryManager.Flag.WalkieTaken);
			GD.Print("[story] Act 9: the walkie-talkie, dead, off the console");
			QueueFree();
			return;
		}
		StoryManager.Instance?.ReachCheckpoint(Checkpoint.Act10WalkieFound, player.GlobalPosition, player.CameraRig.Yaw);
		_staticPlayer?.QueueFree();
		QueueFree();
	}

	public override void _Process(double delta)
	{
		if (_built.Led == null) return;
		if (Dead) { _built.Led.EmissionEnergyMultiplier = 0f; if (_built.Glow != null) _built.Glow.LightEnergy = 0f; return; }
		// Slow heartbeat blink: mostly dim, a soft rise and fall every ~1.6 s.
		_t += delta;
		float ph = (float)(_t % 1.6) / 1.6f;
		float pulse = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(ph * Mathf.Pi)), 3f);
		_built.Led.EmissionEnergyMultiplier = 0.4f + 3.2f * pulse;
		if (_built.Glow != null) _built.Glow.LightEnergy = 0.05f + 0.4f * pulse;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_settled || _item == null) return;
		if (++_settleFrame < 3) return;
		var space = GetWorld3D()?.DirectSpaceState;
		if (space != null)
		{
			Vector3 p = GlobalPosition;
			var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * 0.5f, p + Vector3.Down * 3f, 1u);
			var hit = space.IntersectRay(q);
			if (hit.Count > 0)
			{
				GlobalPosition = new Vector3(p.X, ((Vector3)hit["position"]).Y + 0.003f, p.Z);
				_settled = true;
			}
		}
		if (_settleFrame > 40) _settled = true;
	}
}
