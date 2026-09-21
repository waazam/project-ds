using Godot;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.Player;

/// <summary>
/// Act 1's optional bird-photography minigame. With the camera in hand, holding
/// right mouse (PlayerInput.Focus) raises it to the eye: the view zooms and the
/// viewfinder overlay appears. [Left Click] (PlayerInput.PhotoPressed) while raised
/// snaps whatever's dead ahead within range and a narrow cone. Three birds are harmless flavour; a fourth, black
/// with a glowing red eye, answers a photo with a distant scream and takes the
/// whole flock with it. The camera itself is taken away the moment the first
/// stairs take over. The shutter is on the Player bus, the scream on Unnatural;
/// Reduce Flashing softens the white flash.
/// </summary>
public partial class CameraTool : Node
{
	[Export] public float Range = 14f;
	[Export] public float FovDegrees = 22f;
	[Export] public float FlashAlpha = 0.85f;
	[Export] public float ReducedFlashAlpha = 0.2f;
	[Export] public int FilmFrames = 24;
	/// <summary>Seconds to raise or lower the camera to the eye.</summary>
	[Export] public float RaiseSeconds = 0.18f;

	/// <summary>Raised for every photo taken, with the camera it was taken through (the stalker listens).</summary>
	public static event System.Action<Camera3D> PhotoTaken;

	public bool IsRaised => _raise > 0.85f;

	private PlayerController _player;
	private PlayerInventory _inv;
	private ColorRect _flash;
	private UI.CameraViewfinder _viewfinder;
	private float _raise;
	private int _framesLeft;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_inv = _player.GetNode<PlayerInventory>("Inventory");
		var layer = new CanvasLayer { Layer = 30 };
		AddChild(layer);
		_flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
		_flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(_flash);
		_viewfinder = new UI.CameraViewfinder();
		AddChild(_viewfinder);
		_framesLeft = FilmFrames;
	}

	public override void _Process(double delta)
	{
		if (_flash.Color.A > 0f)
		{
			var c = _flash.Color;
			c.A = Mathf.MoveToward(c.A, 0f, (float)delta * 3.5f);
			_flash.Color = c;
		}
		var input = _player.PlayerInput;
		bool raising = _inv.HasCamera && input.Enabled && input.Focus;
		_raise = Mathf.MoveToward(_raise, raising ? 1f : 0f, (float)delta / Mathf.Max(RaiseSeconds, 0.01f));
		_viewfinder.Raise = _raise;
		_viewfinder.FramesLeft = _framesLeft;
		if (_raise > 0.01f) _viewfinder.FocusLocked = FindSubject() != null;
		if (IsRaised && input.PhotoPressed && _framesLeft > 0) Shoot();
	}

	/// <summary>The unphotographed bird closest to the centre of the frame, within range and the cone, or null.</summary>
	private Bird FindSubject()
	{
		var cam = _player.CameraRig?.Camera;
		if (cam == null) return null;
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
		return best;
	}

	private void Shoot()
	{
		var cam = _player.CameraRig?.Camera;
		if (cam == null) return;
		var best = FindSubject();
		_framesLeft--;

		bool reduce = GameSettings.Instance?.ReduceFlashing ?? false;
		_flash.Color = new Color(1, 1, 1, reduce ? ReducedFlashAlpha : FlashAlpha);
		PlayOneShot("res://assets/audio/sfx/camera_shutter.wav", "Player", _player, -6f);
		PhotoTaken?.Invoke(cam);

		if (best == null) return;
		bool omen = best.IsOmen;
		best.Capture();
		if (omen) TriggerScream();
	}

	private void TriggerScream()
	{
		foreach (var node in GetTree().GetNodesInGroup("photo_birds"))
			if (node is Bird b && !b.Photographed) b.Capture();
		PlayOneShot("res://assets/audio/sfx/distant_scream.wav", "Unnatural", _player, 8f, 60f, 500f);
		GD.Print("[story] Act 1: the fourth bird screams and the flock is gone");
	}

	private static void PlayOneShot(string path, string bus, Node3D at, float volumeDb, float unitSize = 0f, float maxDistance = 0f)
	{
		if (!ResourceLoader.Exists(path)) return;
		var stream = GD.Load<AudioStream>(path);
		Node voice = unitSize > 0f
			? new AudioStreamPlayer3D { Stream = stream, Bus = bus, VolumeDb = volumeDb, UnitSize = unitSize, MaxDistance = maxDistance }
			: new AudioStreamPlayer { Stream = stream, Bus = bus, VolumeDb = volumeDb };
		at.AddChild(voice);
		if (voice is AudioStreamPlayer3D v3) { v3.Finished += v3.QueueFree; v3.Play(); }
		else if (voice is AudioStreamPlayer v2) { v2.Finished += v2.QueueFree; v2.Play(); }
	}
}
