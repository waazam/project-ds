using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 10's ending beat: a walkie-talkie hissing with static, dropped just
/// past the maze's exit. The player follows the sound to find it; picking it
/// up with [E] is checkpoint 8, the end of the story so far.
///
/// It lies on the floor (snapped down onto whatever is under it) with a slow
/// pulsing red LED, and is used through an <see cref="Interactable"/> child:
/// highlight and prompt only while it's under the crosshair.
/// </summary>
public partial class WalkiePickup : Area3D
{
	public const string PromptText = "Pick up the walkie-talkie";

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
		Monitoring = false;
		Monitorable = false;
		// Restore: already found on a previous run.
		if (StoryManager.Instance != null && StoryManager.Instance.Current >= Checkpoint.Act10WalkieFound)
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

		string path = "res://assets/audio/ambient/radio_static_loop.wav";
		if (ResourceLoader.Exists(path))
		{
			_staticPlayer = new AudioStreamPlayer3D { UnitSize = 3f, MaxDistance = 28f, Bus = "Events" };
			AddChild(_staticPlayer);
			_staticPlayer.AddChild(new AmbienceLoop { StreamPath = path, BaseVolumeDb = -2f });
		}
	}

	private void OnInteract(PlayerController player)
	{
		if (_taken) return;
		_taken = true;
		StoryManager.Instance?.ReachCheckpoint(Checkpoint.Act10WalkieFound, player.GlobalPosition, player.CameraRig.Yaw);
		player.GetNodeOrNull<PlayerInventory>("Inventory")?.TryPickup(ToolKind.Radio);
		_staticPlayer?.QueueFree();
		QueueFree();
	}

	public override void _Process(double delta)
	{
		if (_built.Led == null) return;
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
