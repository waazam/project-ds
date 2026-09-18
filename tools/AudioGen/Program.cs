using System.Globalization;
using ProjectDS.Tools.AudioGen;

// Project DS placeholder audio generator. Deterministic: same code + seed => identical files.
//   dotnet run --project tools/AudioGen              generate everything, then verify
//   dotnet run --project tools/AudioGen -- --verify  verify existing files only
//   dotnet run --project tools/AudioGen -- --only bird_03   generate matching files only

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

const ulong Seed = 0xD5_2026_0918UL;
const int Lo = 22050, Hi = 44100;

string root = FindRoot();
string ambient = Path.Combine(root, "assets", "audio", "ambient");
string sfx = Path.Combine(root, "assets", "audio", "sfx");

// name, folder, loop?, sample rate, generator
var jobs = new List<(string name, string dir, bool loop, int sr, Func<Rng, int, double[]> gen)>
{
	("wind_loop", ambient, true, Lo, (r, sr) => Ambient.Wind(r, sr, 32)),
	("leaves_loop", ambient, true, Lo, (r, sr) => Ambient.Leaves(r, sr, 28)),
	("insects_loop", ambient, true, Hi, (r, sr) => Ambient.Insects(r, sr, 30)),
	("distant_loop", ambient, true, Lo, (r, sr) => Ambient.Distant(r, sr, 36)),
	("stream_loop", ambient, true, Lo, (r, sr) => Ambient.Stream(r, sr, 24)),
	("drone_loop", ambient, true, Lo, (r, sr) => Ambient.Drone(sr, 32)),
	("ringing_loop", ambient, true, Hi, (r, sr) => Ambient.Ringing(sr, 20)),
	("breath_loop", ambient, true, Lo, (r, sr) => Ambient.Breath(r, sr, 24)),
};
for (int i = 1; i <= 8; i++) { int k = i; jobs.Add(($"bird_{k:00}", sfx, false, Lo, (r, sr) => Sfx.Bird(k, r, sr))); }
for (int i = 1; i <= 6; i++) jobs.Add(($"step_dirt_{i:00}", sfx, false, Lo, (r, sr) => Sfx.StepDirt(r, sr)));
for (int i = 1; i <= 4; i++) { int k = i; jobs.Add(($"step_wood_{k:00}", sfx, false, Lo, (r, sr) => Sfx.StepWood(r, sr, k % 2 == 0))); }
for (int i = 1; i <= 4; i++) jobs.Add(($"cloth_{i:00}", sfx, false, Lo, (r, sr) => Sfx.Cloth(r, sr)));

bool verifyOnly = args.Contains("--verify");
int oi = Array.IndexOf(args, "--only");
string only = oi >= 0 && oi + 1 < args.Length ? args[oi + 1] : null;

if (!verifyOnly)
{
	Console.WriteLine($"Repo root: {root}");
	foreach (var j in jobs)
	{
		if (only != null && !j.name.Contains(only)) continue;
		var sw = System.Diagnostics.Stopwatch.StartNew();
		var rng = new Rng(Seed ^ Fnv(j.name));
		var x = j.gen(rng, j.sr);
		string path = Path.Combine(j.dir, j.name + ".wav");
		Wav.Write(path, x, j.sr);
		Console.WriteLine($"  wrote {Path.GetRelativePath(root, path)}  ({x.Length / (double)j.sr:0.00} s, {sw.ElapsedMilliseconds} ms)");
	}
}

bool ok = Verify.Run(jobs.Where(j => only == null || j.name.Contains(only)).Select(j => (Path.Combine(j.dir, j.name + ".wav"), j.loop, j.sr)).ToList(), root);
return ok ? 0 : 1;

static ulong Fnv(string s)
{
	ulong h = 14695981039346656037UL;
	foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
	return h;
}

static string FindRoot()
{
	foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
	{
		var d = new DirectoryInfo(start);
		while (d != null)
		{
			if (File.Exists(Path.Combine(d.FullName, "project.godot"))) return d.FullName;
			d = d.Parent;
		}
	}
	throw new InvalidOperationException("Could not find project.godot above the working directory or the tool's build folder.");
}
