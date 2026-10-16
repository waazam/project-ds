using System.Collections.Generic;
using System.Linq;
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
		/// <summary>(Rev 5) Taken after the reveal: it developed with the black figure standing behind.</summary>
		public bool Figure;
		/// <summary>How good a picture it is (0-100 each; all 0 for a picture of nothing on the list).</summary>
		public int Clarity, Focus, Framing, Zoom;
		public bool Scored => SubjectId.Length > 0 && PhotoCatalog.Contains(SubjectId);
		public int Total => Scored ? (Clarity + Focus + Framing + Zoom) / 4 : 0;
		/// <summary>One to five stars (0 for nothing on the list).</summary>
		public int Stars => Scored ? Mathf.Clamp(Mathf.RoundToInt(Total / 20f), 1, 5) : 0;
		public bool Monster => PhotoCatalog.Get(SubjectId)?.Monster ?? false;
		/// <summary>Points: the total, doubled for a monster.</summary>
		public int Points => Total * (Monster ? 2 : 1);
		public string Caption => CaptionFor(SubjectId);
	}

	/// <summary>How a scored shot came out (CameraTool works it out at the press).</summary>
	public struct Score
	{
		public int Clarity, Focus, Framing, Zoom;
		public int Total => (Clarity + Focus + Framing + Zoom) / 4;
	}

	/// <summary>Small captions for recognised subjects (a print's pencil note, from <see cref="PhotoCatalog"/>). Anything else has none.</summary>
	public static string CaptionFor(string id) => PhotoCatalog.Get(id)?.Caption ?? "";

	/// <summary>Print size (a 3:2 frame; big enough for the credits' polaroids).</summary>
	public const int ThumbWidth = 192, ThumbHeight = 128;

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
	private Score _shotScore;
	private readonly Dictionary<string, int> _best = new();

	/// <summary>The best points for each subject on the list photographed so far.</summary>
	public IReadOnlyDictionary<string, int> Best => _best;
	/// <summary>How many of the sixty have been photographed (ever: the flags), and the album's score (the best of each).</summary>
	public int SubjectsFound => PhotoCatalog.All.Count(e => _has.Contains(e.Id));
	public int TotalScore => _best.Values.Sum();

	/// <summary>Whether this subject has ever been photographed (its story flag).</summary>
	public bool Has(string id) => _has.Contains(id);
	/// <summary>The print's exposure stamp reads wrong for the stairs.</summary>
	public bool IsWrong(string id) => WrongPhotos && id == "stairs";

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		_figureRng.Randomize();
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
	public void Record(string id, Vector3? worldPoint = null, Score score = default)
	{
		if (string.IsNullOrEmpty(id)) return;
		if (_has.Add(id)) StoryManager.Instance?.SetFlag(StoryManager.Flag.Photo(id));
		if (_inShot) { _shotSubject = id; _shotEyes = worldPoint; _shotScore = score; }
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
			else if (!World.SaveLamps.InPhoto(img, _frameCam, _frameRect)) World.SecondLook.MaybeFigure(img, id);
			// (Rev 5) after the reveal, every picture develops with it standing behind the subject: never in the world,
			// only in the print
			bool figure = StalkerRevealed && StalkerInPrint(img, _figureRng);
			var photo = new Photo { Number = _photos.Count + 1, SubjectId = id, Texture = ImageTexture.CreateFromImage(img), Wrong = IsWrong(id), Figure = figure,
				Clarity = _shotScore.Clarity, Focus = _shotScore.Focus, Framing = _shotScore.Framing, Zoom = _shotScore.Zoom };
			_photos.Add(photo);
			NoteBest(photo);
			SavePhoto(photo, img);
			GD.Print($"[photo] #{photo.Number} taken{(id.Length > 0 ? " (" + id + ")" : "")}{(photo.Scored ? $" clarity {photo.Clarity} focus {photo.Focus} framing {photo.Framing} zoom {photo.Zoom}: {photo.Stars} stars, {photo.Points} pts" : "")}");
			Taken?.Invoke(photo);
		}
		_frame = null;
		_frameCam = null;
		_inShot = false;
		_shotSubject = null;
		_shotEyes = null;
		_shotScore = default;
	}

	private void NoteBest(Photo p)
	{
		if (!p.Scored) return;
		if (!_best.TryGetValue(p.SubjectId, out int b) || p.Points > b) _best[p.SubjectId] = p.Points;
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
		f.StoreLine($"{p.Number}|{p.SubjectId}|{(p.Wrong ? 1 : 0)}|{p.Clarity}|{p.Focus}|{p.Framing}|{p.Zoom}|{(p.Figure ? 1 : 0)}");
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
			var ph = new Photo { Number = _photos.Count + 1, SubjectId = parts[1], Texture = ImageTexture.CreateFromImage(img), Wrong = parts[2] == "1" };
			if (parts.Length >= 7)
			{
				int.TryParse(parts[3], out ph.Clarity); int.TryParse(parts[4], out ph.Focus);
				int.TryParse(parts[5], out ph.Framing); int.TryParse(parts[6], out ph.Zoom);
			}
			ph.Figure = parts.Length >= 8 && parts[7] == "1";
			_photos.Add(ph);
			NoteBest(ph);
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

	// ------------------------------------------------------------------ the figure behind (Rev 5)

	/// <summary>Set at the reveal (Act 25: the wendigo's mask burns off and the stalker stands in the flames). From then
	/// on every picture develops with it standing behind the subject.</summary>
	public const string StalkerFlag = "stalker_revealed";
	public static bool StalkerRevealed => StoryManager.Instance?.HasFlag(StalkerFlag) == true;

	/// <summary>The cut-outs of it: the real stalker (stalker.glb) rendered flat from the front in a few of its poses by
	/// tools/Blender/wendigo_story.py; alpha its silhouette, red the pale plane of its face.</summary>
	public const string CutoutDir = "res://assets/textures/stalker_prints/";
	private static List<Image> _cutouts;
	private static int _lastCutout = -1, _lastSide;
	private readonly RandomNumberGenerator _figureRng = new();

	private static List<Image> Cutouts()
	{
		if (_cutouts != null) return _cutouts;
		_cutouts = new List<Image>();
		for (int i = 1; i <= 12; i++)
		{
			string path = $"{CutoutDir}stalker_print_{i:00}.png";
			if (!ResourceLoader.Exists(path)) break;
			var img = GD.Load<Texture2D>(path)?.GetImage();
			if (img == null || img.IsEmpty()) continue;
			if (img.IsCompressed()) img.Decompress();
			img.Convert(Image.Format.Rgba8);
			_cutouts.Add(img);
		}
		return _cutouts;
	}

	/// <summary>
	/// Develops the black figure into a print (STORY.md Act 25, "the photos after"; and the credits' slideshow): standing
	/// somewhere behind the subject at the edge of the frame, tall and black with only the faint pale plane of its face,
	/// fogged into the picture's own light (never pure black, never bright: nothing flashes). Its pose, its side, how far
	/// back it stands and how dark it comes out are drawn from <paramref name="rng"/>, and never the same pose twice
	/// running. False if there was nothing to draw it with.
	/// </summary>
	public static bool StalkerInPrint(Image img, RandomNumberGenerator rng)
	{
		var cuts = Cutouts();
		if (img == null || img.IsEmpty() || cuts.Count == 0) return false;
		rng ??= new RandomNumberGenerator();
		if (img.GetFormat() != Image.Format.Rgb8 && img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
		int w = img.GetWidth(), h = img.GetHeight();
		int k = rng.RandiRange(0, cuts.Count - 1);
		if (cuts.Count > 1 && k == _lastCutout) k = (k + 1 + rng.RandiRange(0, cuts.Count - 2)) % cuts.Count;
		_lastCutout = k;
		var cut = (Image)cuts[k].Duplicate();
		if (rng.Randf() < 0.5f) cut.FlipX();
		// how far back it stands (its height in the frame); the cut-out's figure fills about 0.83 of it, its feet at 0.93
		float tall = h * rng.RandfRange(0.3f, 0.66f);
		int ch = Mathf.Max(10, Mathf.RoundToInt(tall / 0.83f));
		int cw = Mathf.Max(5, Mathf.RoundToInt(ch * cut.GetWidth() / (float)cut.GetHeight()));
		cut.Resize(cw, ch, Image.Interpolation.Bilinear);
		// at the edge of the frame (more often than not the other side from the last), its feet low in it
		int side = rng.Randf() < 0.5f ? -1 : 1;
		if (side == _lastSide && rng.Randf() < 0.5f) side = -side;
		_lastSide = side;
		float cx = w * (side < 0 ? rng.RandfRange(0.07f, 0.25f) : rng.RandfRange(0.75f, 0.93f));
		float feet = h * rng.RandfRange(0.66f, 0.98f);
		int x0 = Mathf.RoundToInt(cx - cw * 0.5f), y0 = Mathf.RoundToInt(feet - ch * 0.93f);
		// the print's own light where it stands: what the fog over it is
		Color mean = new(0, 0, 0);
		int n = 0;
		for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y0 + ch); y += 2)
			for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x0 + cw); x += 2) { mean += img.GetPixel(x, y); n++; }
		if (n == 0) return false;
		mean /= n;
		var fog = new Color(mean.R * 0.3f, mean.G * 0.3f, mean.B * 0.32f);
		var pale = new Color(0.62f, 0.62f, 0.6f).Lerp(mean, 0.45f);
		float dark = rng.RandfRange(0.62f, 0.86f);
		for (int j = 0; j < ch; j++)
		{
			int y = y0 + j;
			if (y < 0 || y >= h) continue;
			// (its feet lost in the ground's murk)
			float low = 1f - 0.55f * Mathf.SmoothStep(0.84f, 0.95f, j / (float)ch);
			for (int i = 0; i < cw; i++)
			{
				int x = x0 + i;
				if (x < 0 || x >= w) continue;
				var s = cut.GetPixel(i, j);
				float a = s.A * dark * low;
				if (a <= 0.003f) continue;
				var c = img.GetPixel(x, y);
				var o = c.Lerp(fog, a).Lerp(pale, s.R * 0.3f * dark);
				img.SetPixel(x, y, new Color(o.R, o.G, o.B, 1f));
			}
		}
		return true;
	}

	/// <summary>The credits' print of <paramref name="p"/>: as it was taken, with the black figure developed into it (a
	/// copy; its album print untouched), unless it was taken with it already.</summary>
	public static Texture2D WithFigure(Photo p, RandomNumberGenerator rng)
	{
		if (p?.Texture == null || p.Figure) return p?.Texture;
		var img = p.Texture.GetImage();
		if (img == null || img.IsEmpty()) return p.Texture;
		img = (Image)img.Duplicate();
		return StalkerInPrint(img, rng) ? ImageTexture.CreateFromImage(img) : p.Texture;
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
		// two red eyes, a pixel or two each at the print's size
		int k = Mathf.Max(1, w / 96);
		for (int dx = 0; dx < k; dx++)
			for (int dy = 0; dy < k; dy++)
			{
				img.SetPixel(Mathf.Min(w - 1, ex + dx), Mathf.Min(h - 1, ey + dy), red);
				img.SetPixel(Mathf.Min(w - 1, ex + 2 * k + dx), Mathf.Min(h - 1, ey + dy), red);
			}
	}
}
