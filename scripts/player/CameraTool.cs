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
	/// <summary>Four rolls of 36: enough for the sixty and for second tries at the good ones.</summary>
	[Export] public int FilmFrames = 144;
	/// <summary>The zoom steps the wheel moves through while the camera is up (35mm to 140mm).</summary>
	public static readonly float[] ZoomSteps = { 1f, 1.5f, 2f, 3f, 4f };
	/// <summary>Seconds to raise or lower the camera to the eye.</summary>
	[Export] public float RaiseSeconds = 0.18f;

	/// <summary>Raised for every photo taken, with the camera it was taken through (the stalker listens).</summary>
	public static event System.Action<Camera3D> PhotoTaken;

	public bool IsRaised => _raise > 0.85f;
	/// <summary>The camera is on its way up (or up): the viewfinder is drawing.</summary>
	public bool Raising => _raise > 0.01f;
	public int FramesLeft => _framesLeft;
	public int ZoomIndex { get; private set; }
	public float Zoom => ZoomSteps[ZoomIndex];
	/// <summary>How far the autofocus has pulled in on what's in the middle (0..1).</summary>
	public float FocusLevel => _focus;
	/// <summary>The last shot's score (tests read it).</summary>
	public PhotoLog.Score LastScore { get; private set; }
	/// <summary>Tests: set the zoom directly.</summary>
	public void SetZoom(int index) { ZoomIndex = Mathf.Clamp(index, 0, ZoomSteps.Length - 1); _focus *= 0.35f; }

	private PlayerController _player;
	private PlayerInventory _inv;
	private ColorRect _flash;
	private UI.CameraViewfinder _viewfinder;
	private float _raise;
	private int _framesLeft;
	private bool _pendingShot;
	// the autofocus and the steadiness of the shot
	private Node3D _focusOn;
	private float _focus, _angSpeed, _subjSpeed;
	private Vector3 _lastFwd, _lastSubjDir;
	private bool _haveLast;

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
		if (_player.CameraRig != null) _player.CameraRig.PhotoZoom = raising ? Zoom : 1f;
		if (IsRaised && !_pendingShot)
		{
			int was = ZoomIndex;
			if (input.ItemPrevPressed) ZoomIndex = Mathf.Min(ZoomIndex + 1, ZoomSteps.Length - 1);
			if (input.ItemNextPressed) ZoomIndex = Mathf.Max(ZoomIndex - 1, 0);
			if (ZoomIndex != was) { _focus *= 0.35f; PlayOneShot("res://assets/audio/sfx/cryptex_click_01.wav", "Player", _player, -20f); }
		}
		Track((float)delta);
		_viewfinder.Zoom = Zoom;
		_viewfinder.FocusLevel = _focus;
		if (_raise > 0.01f) _viewfinder.FocusLocked = _focusOn != null && _focus > 0.85f;
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

	/// <summary>Each frame: how fast the view is swinging, how fast the subject moves across it, and the
	/// autofocus pulling in on what's in the middle (slower the more the view shakes; a new subject
	/// starts it over, a zoom step sets it back).</summary>
	private void Track(float dt)
	{
		var cam = _player.CameraRig?.Camera;
		if (cam == null || dt <= 0f) return;
		Vector3 fwd = -cam.GlobalBasis.Z;
		Node3D node = _raise > 0.01f ? (Node3D)FindSubject() ?? FindOtherSubject() : null;
		Vector3? at = SubjectPoint(node);
		Vector3 subjDir = at is Vector3 p ? (p - cam.GlobalPosition).Normalized() : fwd;
		if (_haveLast)
		{
			float ang = Mathf.Acos(Mathf.Clamp(fwd.Dot(_lastFwd), -1f, 1f)) / dt;
			float sub = node != null && node == _focusOn ? Mathf.Acos(Mathf.Clamp(subjDir.Dot(_lastSubjDir), -1f, 1f)) / dt : 0f;
			float k = 1f - Mathf.Exp(-10f * dt);
			_angSpeed = Mathf.Lerp(_angSpeed, ang, k);
			_subjSpeed = Mathf.Lerp(_subjSpeed, sub, k);
		}
		_lastFwd = fwd; _lastSubjDir = subjDir; _haveLast = true;
		if (node != _focusOn) { _focusOn = node; _focus = Mathf.Min(_focus, 0.25f); }
		if (node != null)
		{
			float steady = Mathf.Clamp(1f - _angSpeed * Zoom / 1.6f, 0.12f, 1f);
			_focus = Mathf.Min(1f, _focus + dt / 0.55f * steady);
		}
		else _focus = Mathf.MoveToward(_focus, 0.3f, dt);
	}

	private static Vector3? SubjectPoint(Node3D node) => node switch
	{
		Bird b => b.GlobalPosition,
		PhotoSubject s => s.Centre,
		_ => null,
	};

	/// <summary>
	/// How good the shot of <paramref name="centre"/> (a subject <paramref name="radius"/> metres across) is:
	/// <b>clarity</b> - a steady camera and a still subject (at the zoom's magnification), in enough light;
	/// <b>focus</b> - how far the autofocus had pulled in; <b>framing</b> - the whole subject in the frame,
	/// near the middle or a third; <b>zoom</b> - the subject filling a good share of the frame, not a speck,
	/// not cut off.
	/// </summary>
	private PhotoLog.Score Judge(Camera3D cam, Vector3 centre, float radius, Image frame)
	{
		var rect = _viewfinder.FrameRect;
		var sc = new PhotoLog.Score { Focus = Mathf.RoundToInt(_focus * 100f) };
		if (cam.IsPositionBehind(centre)) return sc;
		Vector2 c = cam.UnprojectPosition(centre);
		Vector3 side = cam.GlobalBasis.X * radius;
		float r = Mathf.Max(0.5f, (cam.UnprojectPosition(centre + side) - c).Length());
		float fh = rect.Size.Y;
		// framing
		float inX = Mathf.Clamp((Mathf.Min(c.X + r, rect.End.X) - Mathf.Max(c.X - r, rect.Position.X)) / (2f * r), 0f, 1f);
		float inY = Mathf.Clamp((Mathf.Min(c.Y + r, rect.End.Y) - Mathf.Max(c.Y - r, rect.Position.Y)) / (2f * r), 0f, 1f);
		// a subject bigger than the frame can't be wholly in it: judge it on how central it is instead
		float inside = 2f * r > fh ? 1f : inX * inY;
		float best = float.MaxValue;
		foreach (var (fx, fy) in new[] { (0.5f, 0.5f), (1f / 3f, 1f / 3f), (2f / 3f, 1f / 3f), (1f / 3f, 2f / 3f), (2f / 3f, 2f / 3f) })
			best = Mathf.Min(best, (c - (rect.Position + rect.Size * new Vector2(fx, fy))).Length());
		float placement = Mathf.Clamp(1f - best / (fh * 0.32f), 0f, 1f);
		sc.Framing = Mathf.RoundToInt(100f * (0.55f * inside + 0.45f * placement) * (rect.HasPoint(c) ? 1f : 0.5f));
		// zoom: how much of the frame's height it fills
		float fill = 2f * r / fh;
		float z = fill < 0.3f ? Mathf.SmoothStep(0.02f, 0.3f, fill) : fill <= 1.1f ? 1f : Mathf.Lerp(1f, 0.5f, Mathf.Clamp((fill - 1.1f) / 0.9f, 0f, 1f));
		sc.Zoom = Mathf.RoundToInt(100f * z);
		// clarity: the shake and the subject's own movement, magnified by the zoom; and the light
		float blur = (_angSpeed + _subjSpeed) * Zoom;
		float steady = Mathf.Clamp(1f - blur / 1.8f, 0f, 1f);
		float light = frame != null ? Mathf.SmoothStep(0.025f, 0.16f, Luma(frame)) : 1f;
		sc.Clarity = Mathf.RoundToInt(100f * steady * (0.45f + 0.55f * light));
		return sc;
	}

	private static float Luma(Image img)
	{
		int w = img.GetWidth(), h = img.GetHeight();
		float sum = 0f; int n = 0;
		for (int y = h / 6; y < h - h / 6; y += 3)
			for (int x = w / 6; x < w - w / 6; x += 3) { var p = img.GetPixel(x, y); sum += 0.3f * p.R + 0.59f * p.G + 0.11f * p.B; n++; }
		return n > 0 ? sum / n : 0f;
	}

	/// <summary>The print shows what went wrong: a shaken or unfocused shot comes out soft.</summary>
	private static void Soften(Image img, float amount)
	{
		if (img == null || amount < 0.12f) return;
		int w = img.GetWidth(), h = img.GetHeight();
		float f = 1f + amount * 5f;
		img.Resize(Mathf.Max(4, Mathf.RoundToInt(w / f)), Mathf.Max(4, Mathf.RoundToInt(h / f)), Image.Interpolation.Bilinear);
		img.Resize(w, h, Image.Interpolation.Bilinear);
	}

	/// <summary>Pass two, only when no bird qualifies: the most central PhotoSubject that scores, or null.</summary>
	private PhotoSubject FindOtherSubject()
	{
		var cam = _player.CameraRig?.Camera;
		if (cam == null) return null;
		PhotoSubject best = null; float bestScore = float.MaxValue;
		foreach (var node in GetTree().GetNodesInGroup("photo_subjects"))
		{
			if (node is not PhotoSubject s) continue;
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

		var frame = GrabFrame();
		var score = default(PhotoLog.Score);
		if (best != null) score = Judge(cam, best.GlobalPosition, PhotoCatalog.Get("bird_" + ColourName(best.Color))?.Radius ?? 0.15f, frame);
		else if (other != null) score = Judge(cam, other.Centre, other.SubjectRadius, frame);
		if (best != null || other != null) Soften(frame, 1f - Mathf.Min(score.Clarity, score.Focus) / 100f);
		LastScore = score;
		var log = PhotoLog.Instance;
		log?.BeginShot(frame, cam, _viewfinder.FrameRect);

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
			log?.Record("bird_" + ColourName(best.Color), eyes, score);
		}
		else if (other != null) log?.Record(other.Id, null, score);
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
