using System.Collections.Generic;
using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// Act 1's camera album: every picture the player takes, in the order taken. Nothing asks for
/// any particular picture; there is no list. Tab opens the album (<see cref="UI.PhotoLogPage"/>),
/// and a small print slides in after every shot (<see cref="UI.PhotoThumb"/>).
///
/// A shot is framed by <see cref="BeginShot"/> / <see cref="EndShot"/> from CameraTool: the frame
/// grabbed at the press (already cropped to the viewfinder and downsampled to 96x64) becomes the
/// photo at EndShot. If a subject was recognised during the shot (<see cref="Record"/>: a bird, the
/// deer, the stairs...), the photo carries a small caption and the subject's story flag is set
/// (<see cref="StoryManager.Flag.Photo"/>): the flags are what the story reads (the birds and the
/// deer stay gone on Continue). Two prints come out wrong when <see cref="WrongPhotos"/> is on: the
/// black bird's (near black, two red eyes) and the stairs' (dark, the stamp misprinted).
///
/// The album survives Continue: each photo is written as a PNG under <see cref="Folder"/> with a
/// line in an index file as it is taken, and read back in _Ready when the level was loaded from a
/// save. A new game (checkpoint None or Act1Start, not loaded from a save) empties the folder.
/// </summary>
public partial class PhotoLog : Node
{
	public static PhotoLog Instance { get; private set; }

	/// <summary>Tests point the album somewhere else (set before the level loads; it survives scene changes).</summary>
	public static string FolderOverride;

	public sealed class Photo
	{
		public int Number;
		/// <summary>The recognised subject ("" for none).</summary>
		public string SubjectId = "";
		public ImageTexture Texture;
		public bool Wrong;
		public string Caption => CaptionFor(SubjectId);
	}

	/// <summary>Small captions for recognised subjects (a print's pencil note). Anything else has none.</summary>
	private static readonly Dictionary<string, string> Captions = new()
	{
		["bird_red"] = "a red bird", ["bird_blue"] = "a blue bird", ["bird_purple"] = "a purple bird",
		["bird_black"] = "a black bird", ["deer"] = "a deer", ["frog"] = "a frog", ["wildflowers"] = "wildflowers",
		["mushrooms"] = "mushrooms", ["waterfall"] = "the waterfall", ["weird_stone"] = "a strange stone", ["stairs"] = "stairs?",
	};

	public static string CaptionFor(string id) => id != null && Captions.TryGetValue(id, out var c) ? c : "";

	/// <summary>Thumbnail size (a 3:2 frame).</summary>
	public const int ThumbWidth = 96, ThumbHeight = 64;

	/// <summary>H2: the black bird's print comes out near black with red eyes; the stairs' print comes out dark and misexposed.</summary>
	[Export] public bool WrongPhotos = true;

	/// <summary>A photo was just taken (every shot, recognised or not).</summary>
	public event System.Action<Photo> Taken;

	public IReadOnlyList<Photo> Photos => _photos;
	/// <summary>Pictures on the roll so far (each one cost a frame of film).</summary>
	public int RecordedCount => _photos.Count;

	public static string Folder => FolderOverride
		?? (GameSettings.Instance?.AutoTest == true ? "user://test_photos/" : "user://photos/");
	private const string IndexFile = "index.txt";

	private readonly List<Photo> _photos = new();
	private readonly HashSet<string> _has = new();
	private Image _frame;
	private Camera3D _frameCam;
	private Rect2 _frameRect;
	private bool _inShot;
	private string _shotSubject;
	private Vector3? _shotEyes;

	/// <summary>Whether this subject has ever been photographed (its story flag).</summary>
	public bool Has(string id) => _has.Contains(id);
	/// <summary>The print's exposure stamp reads wrong for the stairs.</summary>
	public bool IsWrong(string id) => WrongPhotos && id == "stairs";

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		var story = StoryManager.Instance;
		if (story != null)
			foreach (var f in story.Flags)
				if (f.StartsWith(StoryManager.Flag.PhotoPrefix)) _has.Add(f[StoryManager.Flag.PhotoPrefix.Length..]);
		bool fresh = story == null || (!story.LoadedFromSave && story.Current <= Checkpoint.Act1Start);
		if (fresh) ClearFolder();
		else LoadFolder();
	}

	// ------------------------------------------------------------------ shots

	/// <summary>CameraTool: the frame just grabbed (already cropped to the viewfinder and downsampled), and how it was taken.</summary>
	public void BeginShot(Image frame, Camera3D cam, Rect2 frameRect)
	{
		_frame = frame;
		_frameCam = cam;
		_frameRect = frameRect;
		_inShot = true;
		_shotSubject = null;
		_shotEyes = null;
	}

	/// <summary>
	/// A subject recognised in the shot being taken (or, outside a shot, just marked as photographed):
	/// sets its story flag (saved at once). <paramref name="worldPoint"/> is where the subject's eyes
	/// were, for the black bird's wrong print.
	/// </summary>
	public void Record(string id, Vector3? worldPoint = null)
	{
		if (string.IsNullOrEmpty(id)) return;
		if (_has.Add(id)) StoryManager.Instance?.SetFlag(StoryManager.Flag.Photo(id));
		if (_inShot) { _shotSubject = id; _shotEyes = worldPoint; }
	}

	/// <summary>CameraTool, after the shot: the picture goes into the album (and to disk), whatever it shows.</summary>
	public void EndShot()
	{
		if (_inShot)
		{
			var img = _frame != null ? (Image)_frame.Duplicate() : Image.CreateEmpty(ThumbWidth, ThumbHeight, false, Image.Format.Rgb8);
			string id = _shotSubject ?? "";
			if (WrongPhotos && id == "bird_black") DarkenWithEyes(img, _shotEyes);
			else if (WrongPhotos && id == "stairs") Multiply(img, 0.3f);
			var photo = new Photo { Number = _photos.Count + 1, SubjectId = id, Texture = ImageTexture.CreateFromImage(img), Wrong = IsWrong(id) };
			_photos.Add(photo);
			SavePhoto(photo, img);
			GD.Print($"[photo] #{photo.Number} taken{(id.Length > 0 ? " (" + id + ")" : "")}");
			Taken?.Invoke(photo);
		}
		_frame = null;
		_frameCam = null;
		_inShot = false;
		_shotSubject = null;
		_shotEyes = null;
	}

	// ------------------------------------------------------------------ disk

	private static string FileName(int n) => $"photo_{n:000}.png";

	private static void SavePhoto(Photo p, Image img)
	{
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Folder));
		var err = img.SavePng(Folder + FileName(p.Number));
		if (err != Error.Ok) { GD.PushWarning($"[photo] could not save {FileName(p.Number)}: {err}"); return; }
		bool exists = FileAccess.FileExists(Folder + IndexFile);
		using var f = FileAccess.Open(Folder + IndexFile, exists ? FileAccess.ModeFlags.ReadWrite : FileAccess.ModeFlags.Write);
		if (f == null) return;
		f.SeekEnd();
		f.StoreLine($"{p.Number}|{p.SubjectId}|{(p.Wrong ? 1 : 0)}");
	}

	private void LoadFolder()
	{
		if (!FileAccess.FileExists(Folder + IndexFile)) return;
		using var f = FileAccess.Open(Folder + IndexFile, FileAccess.ModeFlags.Read);
		if (f == null) return;
		while (!f.EofReached())
		{
			var parts = f.GetLine().Split('|');
			if (parts.Length < 3 || !int.TryParse(parts[0], out int n)) continue;
			var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(Folder + FileName(n)));
			if (img == null || img.IsEmpty()) continue;
			_photos.Add(new Photo { Number = _photos.Count + 1, SubjectId = parts[1], Texture = ImageTexture.CreateFromImage(img), Wrong = parts[2] == "1" });
		}
		GD.Print($"[photo] album restored: {_photos.Count} picture(s)");
	}

	private static void ClearFolder()
	{
		using var dir = DirAccess.Open(Folder);
		if (dir == null) return;
		foreach (var file in dir.GetFiles())
			if (file == IndexFile || (file.StartsWith("photo_") && file.EndsWith(".png"))) dir.Remove(file);
	}

	// ------------------------------------------------------------------ wrong prints

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
