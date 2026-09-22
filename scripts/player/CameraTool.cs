using Godot;
using ProjectDS.Systems;
using ProjectDS.World;

namespace ProjectDS.Player;

/// <summary>
/// Act 1's optional photo trip. With the camera in hand, holding
/// right mouse (PlayerInput.Focus) raises it to the eye: the view zooms and the
/// viewfinder overlay appears. [Left Click] (PlayerInput.PhotoPressed) while raised
/// snaps whatever's dead ahead within range and a narrow cone. Three birds are harmless flavour; a fourth, black
/// with a glowing red eye, answers a photo with a distant scream and takes the
/// whole flock with it. The camera itself is taken away the moment the first
/// stairs take over. The shutter is on the Player bus, the scream on Unnatural;
/// Reduce Flashing softens the white flash.
///
/// Every shot goes into the album (<see cref="PhotoLog"/>) as a small print of the frame,
/// whatever it shows. Besides the birds (judged first, by the unchanged rule) any
/// <see cref="PhotoSubject"/> in group "photo_subjects" can be recognised: that only drives
/// the viewfinder's focus square, the print's caption and the subject's story flag. The
/// frame is read back one render frame after the press, with the viewfinder's marks hidden
/// for that frame, so the print holds only what the camera saw.
/// </summary>
public partial class CameraTool : Node
{
	[Export] public float Range = 14f;
	[Export] public float FovDegrees = 22f;
	[Export] public float FlashAlpha = 0.85f;
	[Export] public float ReducedFlashAlpha = 0.2f;
	[Export] public int FilmFrames = 36;
	/// <summary>Seconds to raise or lower the camera to the eye.</summary>
	[Export] public float RaiseSeconds = 0.18f;

	/// <summary>Raised for every photo taken, with the camera it was taken through (the stalker listens).</summary>
	public static event System.Action<Camera3D> PhotoTaken;

	public bool IsRaised => _raise > 0.85f;
	/// <summary>The camera is on its way up (or up): the viewfinder is drawing.</summary>
	public bool Raising => _raise > 0.01f;
	public int FramesLeft => _framesLeft;

	private PlayerController _player;
	private PlayerInventory _inv;
	private ColorRect _flash;
	private UI.CameraViewfinder _viewfinder;
	private float _raise;
	private int _framesLeft;
	private bool _pendingShot;

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
		// Continue: every picture in the album cost a frame.
		Callable.From(() => _framesLeft = Mathf.Max(0, FilmFrames - (PhotoLog.Instance?.RecordedCount ?? 0))).CallDeferred();
	}

	public override void _Process(double delta)
	{
		if (_flash.Color.A > 0f)
		{
			var c = _flash.Color;
			c.A = Mathf.MoveToward(c.A, 0f, (float)delta * 3.5f);
			_flash.Color = c;
		}
		if (_pendingShot) { _pendingShot = false; _viewfinder.HideMarks = false; Shoot(); }
		var input = _player.PlayerInput;
		bool raising = _inv.HasCamera && input.Enabled && input.Focus;
		_raise = Mathf.MoveToward(_raise, raising ? 1f : 0f, (float)delta / Mathf.Max(RaiseSeconds, 0.01f));
		_viewfinder.Raise = _raise;
		_viewfinder.FramesLeft = _framesLeft;
		if (_raise > 0.01f) _viewfinder.FocusLocked = FindSubject() != null || FindOtherSubject() != null;
		if (IsRaised && input.PhotoPressed && _framesLeft > 0 && !_pendingShot)
		{
			// The readback in Shoot returns the last rendered frame: draw one without the marks first.
			_pendingShot = true;
			_viewfinder.HideMarks = true;
		}
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

	/// <summary>Pass two, only when no bird qualifies: the most central unrecorded PhotoSubject that scores, or null.</summary>
	private PhotoSubject FindOtherSubject()
	{
		var cam = _player.CameraRig?.Camera;
		if (cam == null) return null;
		PhotoSubject best = null; float bestScore = float.MaxValue;
		foreach (var node in GetTree().GetNodesInGroup("photo_subjects"))
		{
			if (node is not PhotoSubject s || s.Captured) continue;
			if (!s.TryScore(cam, _player, out float score)) continue;
			if (score < bestScore) { bestScore = score; best = s; }
		}
		return best;
	}

	private void Shoot()
	{
		var cam = _player.CameraRig?.Camera;
		if (cam == null) return;
		var best = FindSubject();
		var other = best == null ? FindOtherSubject() : null;
		_framesLeft--;

		var log = PhotoLog.Instance;
		log?.BeginShot(GrabFrame(), cam, _viewfinder.FrameRect);

		bool reduce = GameSettings.Instance?.ReduceFlashing ?? false;
		_flash.Color = new Color(1, 1, 1, reduce ? ReducedFlashAlpha : FlashAlpha);
		PlayOneShot("res://assets/audio/sfx/camera_shutter.wav", "Player", _player, -6f);
		PhotoTaken?.Invoke(cam);

		if (best != null)
		{
			Vector3 eyes = best.GlobalPosition + Vector3.Up * 0.28f;
			bool omen = best.IsOmen;
			best.Capture();
			if (omen) TriggerScream();
			log?.Record("bird_" + ColourName(best.Color), eyes);
		}
		else if (other != null) log?.Record(other.Id);
		log?.EndShot();
	}

	private static string ColourName(BirdColor c) => c switch
	{
		BirdColor.Red => "red",
		BirdColor.Blue => "blue",
		BirdColor.Purple => "purple",
		_ => "black",
	};

	/// <summary>The last rendered frame, cropped to the viewfinder's 3:2 frame and downsampled to the print size.</summary>
	private Image GrabFrame()
	{
		var img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null) return null;
		var frame = _viewfinder.FrameRect;
		// Just inside the corner brackets.
		var crop = new Rect2I((Vector2I)frame.Position + Vector2I.One * 2, (Vector2I)frame.Size - Vector2I.One * 4);
		crop = crop.Intersection(new Rect2I(0, 0, img.GetWidth(), img.GetHeight()));
		if (crop.Size.X < 8 || crop.Size.Y < 8) return null;
		var cut = img.GetRegion(crop);
		cut.Resize(PhotoLog.ThumbWidth, PhotoLog.ThumbHeight, Image.Interpolation.Bilinear);
		return cut;
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
