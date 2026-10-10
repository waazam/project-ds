using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// The textures drawn in code, kept on disk once drawn (the load overhaul, 2026-10-09: drawing them pixel by pixel took
/// about seven seconds of every launch's first load). Raw image data, mipmaps and all, under user://texcache/, keyed by
/// this build of the game (a change to the code that draws one may change it). `--no-tex-cache` draws them all afresh.
/// </summary>
public static class TexCache
{
	private static string _dir;
	private static bool? _on;
	private static bool On => _on ??= System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-tex-cache") < 0;

	private static string Dir
	{
		get
		{
			if (_dir != null) return _dir;
			string build = typeof(TexCache).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..12];
			_dir = $"user://texcache/{build}";
			DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(_dir));
			// (older builds' caches cleared away)
			var root = DirAccess.Open("user://texcache");
			if (root != null)
				foreach (var d in root.GetDirectories())
					if (d != build) { var path = ProjectSettings.GlobalizePath($"user://texcache/{d}"); foreach (var f in DirAccess.GetFilesAt(path)) DirAccess.RemoveAbsolute($"{path}/{f}"); DirAccess.RemoveAbsolute(path); }
			return _dir;
		}
	}

	public static int Hits { get; private set; }

	private static string PathFor(string key) => $"{Dir}/{key.Replace('/', '_').Replace(':', '_')}.img";

	public static Image Load(string key)
	{
		if (!On) return null;
		string p = PathFor(key);
		if (!FileAccess.FileExists(p)) return null;
		using var f = FileAccess.Open(p, FileAccess.ModeFlags.Read);
		if (f == null) return null;
		int w = (int)f.Get32(), h = (int)f.Get32(), fmt = (int)f.Get32();
		bool mips = f.Get8() != 0;
		int len = (int)f.Get32();
		var data = f.GetBuffer(len);
		if (data.Length != len) return null;
		Hits++;
		return Image.CreateFromData(w, h, mips, (Image.Format)fmt, data);
	}

	public static void Save(string key, Image img)
	{
		if (!On || img == null) return;
		using var f = FileAccess.Open(PathFor(key), FileAccess.ModeFlags.Write);
		if (f == null) return;
		var data = img.GetData();
		f.Store32((uint)img.GetWidth()); f.Store32((uint)img.GetHeight()); f.Store32((uint)img.GetFormat());
		f.Store8((byte)(img.HasMipmaps() ? 1 : 0));
		f.Store32((uint)data.Length);
		f.StoreBuffer(data);
	}
}
