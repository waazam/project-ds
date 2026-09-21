using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Act 10's ending beat: a walkie-talkie hissing with static, dropped just
/// past the maze's exit. The player follows the sound to find it; picking it
/// up with [E] is checkpoint 8 — the end of the story so far.
/// </summary>
public partial class WalkiePickup : Area3D
{
	private bool _playerInRange;
	private bool _wasPressed;
	private PlayerController _player;
	private AudioStreamPlayer3D _staticPlayer;

	public override void _Ready()
	{
		CollisionLayer = 0;
		CollisionMask = 2;
		Monitorable = false;
		Monitoring = true;
		AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 2.2f } });
		BuildVisual();
		BodyEntered += OnEntered;
		BodyExited += OnExited;

		string path = "res://assets/audio/ambient/radio_static_loop.wav";
		if (ResourceLoader.Exists(path))
		{
			_staticPlayer = new AudioStreamPlayer3D { UnitSize = 3f, MaxDistance = 28f };
			AddChild(_staticPlayer);
			_staticPlayer.AddChild(new AmbienceLoop { StreamPath = path, BaseVolumeDb = -2f });
		}
	}

	private void BuildVisual()
	{
		var k = new MeshKit();
		var body = ProcTextures.Flat("walkie_body", new Color(0.14f, 0.15f, 0.14f), 0.75f);
		var metal = ProcTextures.MetalMat;
		k.Color = Colors.White;
		k.Mat(body).Box(new Vector3(0, 0.09f, 0), new Vector3(0.08f, 0.17f, 0.045f));
		k.Mat(metal).Cylinder(new Vector3(0, 0.17f, 0), new Vector3(0, 0.32f, 0), 0.007f, 0.007f, 5);
		k.CommitTo(this, "Mesh");
	}

	public override void _Process(double delta)
	{
		bool pressed = Input.IsActionPressed("interact");
		bool justPressed = pressed && !_wasPressed;
		_wasPressed = pressed;
		if (_playerInRange) InteractPrompt.Instance?.ShowPrompt("[E] Pick up the walkie-talkie");
		if (!_playerInRange || _player == null || !justPressed) return;
		StoryManager.Instance?.ReachCheckpoint(Checkpoint.Act10WalkieFound, _player.GlobalPosition, _player.CameraRig.Yaw);
		_player.GetNodeOrNull<PlayerInventory>("Inventory")?.TryPickup(ToolKind.Radio);
		InteractPrompt.Instance?.HidePrompt();
		_staticPlayer?.QueueFree();
		QueueFree();
	}

	private void OnEntered(Node3D body) { if (body is PlayerController p) { _player = p; _playerInRange = true; } }

	private void OnExited(Node3D body)
	{
		if (body != (Node3D)_player) return;
		_playerInRange = false;
		InteractPrompt.Instance?.HidePrompt();
	}
}
