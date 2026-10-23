using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// The textures drawn in code, kept on disk once drawn (the load overhaul, 2026-10-09: drawing them pixel by pixel took
/// about seven seconds of every launch's first load). Raw image data, mipmaps and all, under user://texcache/, keyed by
/// the code that draws them (<see cref="SourceKey"/>; an exported game, by its build). `--no-tex-cache` draws them all afresh.
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
			string build = SourceKey() ?? typeof(TexCache).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..12];
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

	/// <summary>The drawing code's own fingerprint (2026-10-19): keyed by the build, any C# change at all threw the whole
	/// cache away, and the next load drew every texture again (135 of them, 40 s of the Hollow's). Where the scripts are
	/// on disk (the editor, the tests) it's a hash of the ones that draw: every script that sets pixels, starts a timed draw
	/// or saves to the cache, and the helpers they draw with. An exported game has no scripts to read and keeps the build's key.</summary>
	private static readonly string[] Helpers = { "SurfaceKit.cs", "SurfaceSets.cs", "TexelBoost.cs", "DetailKit.cs" };

	private static string SourceKey()
	{
		// (an exported game carries the scripts as stubs: it keys by its build, and trusts the shipped copy)
		if (!OS.HasFeature("editor") || !DirAccess.DirExistsAbsolute("res://scripts")) return null;
		var files = new System.Collections.Generic.List<string>();
		void Walk(string dir)
		{
			foreach (var d in DirAccess.GetDirectoriesAt(dir)) Walk($"{dir}/{d}");
			foreach (var f in DirAccess.GetFilesAt(dir)) if (f.EndsWith(".cs")) files.Add($"{dir}/{f}");
		}
		Walk("res://scripts");
		if (files.Count == 0) return null;
		files.Sort(System.StringComparer.Ordinal);
		using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA1);
		int n = 0;
		foreach (var path in files)
		{
			string text = FileAccess.GetFileAsString(path);
			// (the drawing sites only: a script that just reads the counters, TexGen.Ms, is not drawing code)
			if (System.Array.IndexOf(Helpers, path.GetFile()) < 0 && !text.Contains("SetPixel(") && !text.Contains("TexCache.Save(") && !text.Contains("TexGen.Start(")) continue;
			sha.AppendData(System.Text.Encoding.UTF8.GetBytes(path));
			sha.AppendData(System.Text.Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")));
			n++;
		}
		string key = "src" + System.Convert.ToHexString(sha.GetHashAndReset())[..12].ToLowerInvariant();
		GD.Print($"[texcache] keyed by the drawing code: {n} scripts, {key}");
		return key;
	}

	public static int Hits { get; private set; }
	/// <summary>For the log and tests: textures found in the copy shipped with the game.</summary>
	public static int BakedHits { get; private set; }

	private static string FileName(string key) => $"{key.Replace('/', '_').Replace(':', '_')}.img";
	private static string PathFor(string key) => $"{Dir}/{FileName(key)}";

	// ------------------------------------------------------------------ the copy shipped with the game
	// (2026-10-22, for the playtests: an exported game's cache starts empty, so a tester's first load into the Hollow drew
	// every texture, half a minute of it) `--bake-texcache` (with the clip audit, which builds every level) copies the cache
	// into the project, beside the drawing code's key; an exported game reads from it whenever its own cache hasn't the
	// texture yet. In the editor it's used only while its key is the drawing code's own (a stale copy is never read).

	private const string BakedDir = "res://assets/texcache_baked";
	private static bool? _bakedOk;
	private static System.Collections.Generic.Dictionary<string, string> _bakedByPrefix;

	private static bool BakedOk
	{
		get
		{
			if (_bakedOk is { } ok) return ok;
			string keyFile = $"{BakedDir}/key.txt";
			if (!FileAccess.FileExists(keyFile)) return (_bakedOk = false).Value;
			string baked = FileAccess.GetFileAsString(keyFile).Trim();
			string src = SourceKey();
			_bakedOk = src == null || src == baked;   // (exported: no scripts to check it by; it was baked for this build)
			if (_bakedOk == false) GD.Print($"[texcache] the shipped copy ({baked}) isn't this drawing code's ({src}): not used");
			return _bakedOk.Value;
		}
	}

	/// <summary>A shipped file for the key, exactly, or (a cavity map, whose key carries its source PNG's hash, which an
	/// exported game can't read) by its map's name.</summary>
	private static string BakedPath(string key)
	{
		string exact = $"{BakedDir}/{FileName(key)}z";
		if (FileAccess.FileExists(exact)) return exact;
		if (!key.StartsWith("Cavity_")) return null;
		if (_bakedByPrefix == null)
		{
			_bakedByPrefix = new();
			foreach (var f in DirAccess.GetFilesAt(BakedDir))
				if (f.StartsWith("Cavity_") && f.EndsWith(".imgz")) _bakedByPrefix[WithoutHash(f[..^5])] = $"{BakedDir}/{f}";
		}
		return _bakedByPrefix.TryGetValue(WithoutHash(FileName(key)[..^4]), out var p) ? p : null;
	}

	// (Cavity_<png>_<its md5>_<floor> to Cavity_<png>__<floor>: an exported game reads the md5 as "")
	private static string WithoutHash(string name) => System.Text.RegularExpressions.Regex.Replace(name, "_[0-9a-f]{32}_", "__");

	/// <summary>`--bake-texcache`: this run's cache copied into the project as the shipped copy (the editor only).</summary>
	public static void BakeToProject()
	{
		string from = ProjectSettings.GlobalizePath(Dir), to = ProjectSettings.GlobalizePath(BakedDir);
		DirAccess.MakeDirRecursiveAbsolute(to);
		foreach (var f in DirAccess.GetFilesAt(to)) DirAccess.RemoveAbsolute($"{to}/{f}");
		int n = 0;
		// (zstd: the raw drawn textures came to 166 MB; the game reads them back through the same compression)
		foreach (var f in DirAccess.GetFilesAt(from))
		{
			var bytes = FileAccess.GetFileAsBytes($"{from}/{f}");
			using var w = FileAccess.OpenCompressed($"{BakedDir}/{f}z", FileAccess.ModeFlags.Write, FileAccess.CompressionMode.Zstd);
			if (w == null) continue;
			w.StoreBuffer(bytes);
			n++;
		}
		using (var k = FileAccess.Open($"{BakedDir}/key.txt", FileAccess.ModeFlags.Write)) k?.StoreString(System.IO.Path.GetFileName(Dir));
		GD.Print($"[texcache] baked {n} textures into {BakedDir} for {System.IO.Path.GetFileName(Dir)}");
	}

	public static Image Load(string key)
	{
		if (!On) return null;
		string p = PathFor(key);
		bool baked = false;
		if (!FileAccess.FileExists(p))
		{
			if (!BakedOk || BakedPath(key) is not { } bp) return null;
			p = bp; baked = true;
		}
		if (baked) BakedHits++;
		using var f = baked ? FileAccess.OpenCompressed(p, FileAccess.ModeFlags.Read, FileAccess.CompressionMode.Zstd) : FileAccess.Open(p, FileAccess.ModeFlags.Read);
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
