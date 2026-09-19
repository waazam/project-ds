using Godot;
using ProjectDS.World;

namespace ProjectDS.Player;

/// <summary>
/// Act 1's optional bird-photography minigame. While the camera is equipped,
/// [Left Click] snaps whatever's dead ahead within range and a narrow cone.
/// Three birds are harmless flavour; a fourth, black with a glowing red eye,
/// answers a photo with a distant scream and takes the whole flock with it.
/// The camera itself is taken away the moment the first stairs take over.
/// </summary>
public partial class CameraTool : Node
{
	[Export] public float Range = 14f;
	[Export] public float FovDegrees = 22f;

	private PlayerController _player;
	private PlayerInventory _inv;
	private bool _wasPressed;
	private ColorRect _flash;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_inv = _player.GetNode<PlayerInventory>("Inventory");
		var layer = new CanvasLayer { Layer = 30 };
		AddChild(layer);
		_flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
		_flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(_flash);
	}

	public override void _Process(double delta)
	{
		if (_flash.Color.A > 0f)
		{
			var c = _flash.Color;
			c.A = Mathf.MoveToward(c.A, 0f, (float)delta * 3.5f);
			_flash.Color = c;
		}
		if (!_inv.HasCamera) { _wasPressed = false; return; }

		bool pressed = Input.IsActionPressed("photo");
		bool justPressed = pressed && !_wasPressed;
		_wasPressed = pressed;
		if (justPressed) Shoot();
	}

	private void Shoot()
	{
		var cam = _player.CameraRig?.Camera;
		if (cam == null) return;
		Vector3 origin = cam.GlobalPosition;
		Vector3 fwd = -cam.GlobalBasis.Z;
		float cosLimit = Mathf.Cos(Mathf.DegToRad(FovDegrees));

		Bird best = null; float bestDot = cosLimit;
		foreach (var node in GetTree().GetNodesInGroup("photo_birds"))
		{
			if (node is not Bird bird || bird.Photographed) continue;
			Vector3 to = bird.GlobalPosition - origin;
			float dist = to.Length();
			if (dist > Range || dist < 0.01f) continue;
			float dot = fwd.Dot(to / dist);
			if (dot > bestDot) { bestDot = dot; best = bird; }
		}

		_flash.Color = new Color(1, 1, 1, 0.85f);
		PlayOneShot("res://assets/audio/sfx/camera_shutter.wav", _player, -6f);

		if (best == null) return;
		bool omen = best.IsOmen;
		best.Capture();
		if (omen) TriggerScream();
	}

	private void TriggerScream()
	{
		foreach (var node in GetTree().GetNodesInGroup("photo_birds"))
			if (node is Bird b && !b.Photographed) b.Capture();
		PlayOneShot("res://assets/audio/sfx/distant_scream.wav", _player, 8f, 60f, 500f);
		GD.Print("[story] Act 1: the fourth bird screams and the flock is gone");
	}

	private static void PlayOneShot(string path, Node3D at, float volumeDb, float unitSize = 0f, float maxDistance = 0f)
	{
		if (!ResourceLoader.Exists(path)) return;
		var stream = GD.Load<AudioStream>(path);
		if (unitSize > 0f)
		{
			var voice = new AudioStreamPlayer3D { Stream = stream, VolumeDb = volumeDb, UnitSize = unitSize, MaxDistance = maxDistance };
			at.AddChild(voice);
			voice.Finished += voice.QueueFree;
			voice.Play();
		}
		else
		{
			var voice = new AudioStreamPlayer { Stream = stream, VolumeDb = volumeDb };
			at.AddChild(voice);
			voice.Finished += voice.QueueFree;
			voice.Play();
		}
	}
}
