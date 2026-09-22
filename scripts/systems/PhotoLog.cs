using System.Collections.Generic;
using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// Act 1's photo trip: the shot list and the record of what the camera got.
///
/// The list is the friend's (a notebook page tucked into the camera strap): nine
/// listed subjects, ticked off as they are photographed, and three unlisted ones
/// that write themselves in when they happen (the black bird, the stalker's
/// sting, the stairs). One story flag per subject (<see cref="StoryManager.Flag.Photo"/>),
/// so the record survives Continue; the thumbnails do not (they only ever show
/// on the print that slides in right after the shot).
///
/// A shot is framed by <see cref="BeginShot"/> / <see cref="EndShot"/> from
/// CameraTool: anything recording during that window (birds, other subjects, the
/// stalker through its Photographed event) gets the frame that was just grabbed.
/// Two photos come out wrong (the black bird, the stairs) when <see cref="WrongPhotos"/>
/// is on: cheap, deniable, no new beat.
/// </summary>
public partial class PhotoLog : Node
{
	public static PhotoLog Instance { get; private set; }

	public sealed class Entry
	{
		public readonly string Id;
		/// <summary>The pencil line on the page ('\n' breaks it over two lines).</summary>
		public readonly string Caption;
		/// <summary>On the page from the start (with a box), or written in when earned.</summary>
		public readonly bool Listed;
		public Entry(string id, string caption, bool listed) { Id = id; Caption = caption; Listed = listed; }
		/// <summary>The caption's first line: what the print shows.</summary>
		public string ShortCaption { get { int i = Caption.IndexOf('\n'); return i < 0 ? Caption : Caption[..i]; } }
	}

	/// <summary>In trail order. Ids are save-file names: never rename one.</summary>
	public static readonly Entry[] Entries =
	{
		new("trailhead_sign", "the park sign", true),
		new("cabin", "the cabin", true),
		new("bird_red", "the red bird", true),
		new("bird_blue", "the blue one", true),
		new("creek", "the creek from the bridge", true),
		new("overlook", "the view from the overlook", true),
		new("bird_purple", "the purple one\n(he swears it's real)", true),
		new("tent", "that tent", true),
		new("mushrooms", "mushrooms (don't touch)", true),
		new("bird_black", "a black bird", false),
		new("stalker", "(nothing there)", false),
		new("stairs", "stairs?", false),
	};

	public const string Header = "shots for the album — get these!!";
	/// <summary>Written under the list once every listed box is ticked. Nothing else happens.</summary>
	public const string RewardLine = "that's the lot";

	/// <summary>Thumbnail size (a 3:2 frame).</summary>
	public const int ThumbWidth = 96, ThumbHeight = 64;

	/// <summary>H2: the black bird's print comes out near black with red eyes; the stairs' print comes out dark and misexposed.</summary>
	[Export] public bool WrongPhotos = true;

	/// <summary>A subject was just recorded, with its print (null when nothing was grabbed, e.g. a scripted record).</summary>
	public event System.Action<Entry, ImageTexture> Recorded;

	private readonly HashSet<string> _has = new();
	private Image _frame;
	private Camera3D _frameCam;
	private Rect2 _frameRect;
	private bool _inShot;
	private Entities.Stalker _stalker;

	public static Entry Find(string id)
	{
		foreach (var e in Entries) if (e.Id == id) return e;
		return null;
	}

	public bool Has(string id) => _has.Contains(id);
	public int RecordedCount => _has.Count;
	public int ListedRecorded
	{
		get { int n = 0; foreach (var e in Entries) if (e.Listed && _has.Contains(e.Id)) n++; return n; }
	}
	public static int ListedTotal
	{
		get { int n = 0; foreach (var e in Entries) if (e.Listed) n++; return n; }
	}
	public bool ListComplete => ListedRecorded >= ListedTotal;
	/// <summary>The print's exposure stamp reads wrong for the stairs.</summary>
	public bool IsWrong(string id) => WrongPhotos && id == "stairs";

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		if (_stalker != null && IsInstanceValid(_stalker)) _stalker.Photographed -= OnStalkerPhotographed;
	}

	public override void _Ready()
	{
		// Restore: the flags are already in the autoload when the level loads.
		var story = StoryManager.Instance;
		if (story != null)
			foreach (var e in Entries)
				if (story.HasFlag(StoryManager.Flag.Photo(e.Id))) _has.Add(e.Id);
	}

	public override void _Process(double delta)
	{
		// The stalker comes and goes with the level; subscribe to whichever one is out there.
		if (_stalker == null || !IsInstanceValid(_stalker))
		{
			_stalker = GetTree().GetFirstNodeInGroup("stalker") as Entities.Stalker;
			if (_stalker != null) _stalker.Photographed += OnStalkerPhotographed;
		}
	}

	private void OnStalkerPhotographed() => Record("stalker");

	/// <summary>CameraTool: the frame just grabbed (already cropped to the viewfinder and downsampled), and how it was taken.</summary>
	public void BeginShot(Image frame, Camera3D cam, Rect2 frameRect)
	{
		_frame = frame;
		_frameCam = cam;
		_frameRect = frameRect;
		_inShot = true;
	}

	public void EndShot()
	{
		_frame = null;
		_frameCam = null;
		_inShot = false;
	}

	/// <summary>
	/// Records the subject: sets its flag (saved at once) and raises <see cref="Recorded"/> with the
	/// print. Unknown or already-recorded ids do nothing. <paramref name="worldPoint"/> is where the
	/// subject's eyes were, for the black bird's wrong print.
	/// </summary>
	public void Record(string id, Vector3? worldPoint = null)
	{
		var entry = Find(id);
		if (entry == null || _has.Contains(id)) return;
		_has.Add(id);
		StoryManager.Instance?.SetFlag(StoryManager.Flag.Photo(id));
		var tex = MakePrint(id, worldPoint);
		GD.Print($"[photo] {id} recorded ({RecordedCount}/{Entries.Length})");
		Recorded?.Invoke(entry, tex);
	}

	private ImageTexture MakePrint(string id, Vector3? worldPoint)
	{
		if (!_inShot || _frame == null) return null;
		var img = (Image)_frame.Duplicate();
		if (WrongPhotos && id == "bird_black") DarkenWithEyes(img, worldPoint);
		else if (WrongPhotos && id == "stairs") Multiply(img, 0.3f);
		return ImageTexture.CreateFromImage(img);
	}

	private static void Multiply(Image img, float k)
	{
		int w = img.GetWidth(), h = img.GetHeight();
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var c = img.GetPixel(x, y);
				img.SetPixel(x, y, new Color(c.R * k, c.G * k, c.B * k, 1f));
			}
	}

	/// <summary>Near black, with two red pixels where the eyes were.</summary>
	private void DarkenWithEyes(Image img, Vector3? worldPoint)
	{
		Multiply(img, 0.06f);
		int w = img.GetWidth(), h = img.GetHeight();
		int ex = w / 2, ey = h / 2;
		if (worldPoint is Vector3 p && _frameCam != null && IsInstanceValid(_frameCam) && !_frameCam.IsPositionBehind(p) && _frameRect.Size.X > 1f)
		{
			Vector2 s = _frameCam.UnprojectPosition(p) - _frameRect.Position;
			ex = Mathf.Clamp(Mathf.RoundToInt(s.X / _frameRect.Size.X * w), 1, w - 3);
			ey = Mathf.Clamp(Mathf.RoundToInt(s.Y / _frameRect.Size.Y * h), 0, h - 1);
		}
		var red = new Color(0.82f, 0.07f, 0.05f);
		img.SetPixel(ex, ey, red);
		img.SetPixel(ex + 2, ey, red);
	}
}
