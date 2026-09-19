using System.Globalization;
using ProjectDS.Tools.AudioGen;

// Project DS placeholder audio generator. Deterministic: same code + seed => identical files.
//   dotnet run --project tools/AudioGen              generate everything, then verify
//   dotnet run --project tools/AudioGen -- --verify  verify existing files only
//   dotnet run --project tools/AudioGen -- --only twig,raven   generate files whose name contains any fragment
//   dotnet run --project tools/AudioGen -- --music-samples   write music-direction listening samples to test-output/music

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

const ulong Seed = 0xD5_2026_0918UL;
const int Lo = 22050, Hi = 44100;

string root = FindRoot();
string ambient = Path.Combine(root, "assets", "audio", "ambient");
string sfx = Path.Combine(root, "assets", "audio", "sfx");
string music = Path.Combine(root, "assets", "audio", "music");

// name, folder, loop?, sample rate, generator
var jobs = new List<(string name, string dir, bool loop, int sr, Func<Rng, int, double[]> gen)>
{
	("wind_loop", ambient, true, Lo, (r, sr) => Ambient.Wind(r, sr, 32)),
	("leaves_loop", ambient, true, Lo, (r, sr) => Ambient.Leaves(r, sr, 28)),
	("insects_loop", ambient, true, Hi, (r, sr) => Ambient.Insects(r, sr, 30)),
	("distant_loop", ambient, true, Lo, (r, sr) => Ambient.Distant(r, sr, 36)),
	("stream_loop", ambient, true, Lo, (r, sr) => Ambient.Stream(r, sr, 24)),
	("pressure_loop", ambient, true, Lo, (r, sr) => Ambient.Pressure(r, sr, 40)),
	("undertone_loop", ambient, true, Lo, (r, sr) => Ambient.Undertone(r, sr, 40)),
	("ringing_loop", ambient, true, Hi, (r, sr) => Ambient.Ringing(sr, 20)),
};
for (int i = 1; i <= 8; i++) { int k = i; jobs.Add(($"bird_{k:00}", sfx, false, Lo, (r, sr) => Sfx.Bird(k, r, sr))); }
for (int i = 1; i <= 6; i++) jobs.Add(($"step_dirt_{i:00}", sfx, false, Lo, (r, sr) => Sfx.StepDirt(r, sr)));
for (int i = 1; i <= 4; i++) { int k = i; jobs.Add(($"step_wood_{k:00}", sfx, false, Lo, (r, sr) => Sfx.StepWood(r, sr, k % 2 == 0))); }
for (int i = 1; i <= 4; i++) jobs.Add(($"cloth_{i:00}", sfx, false, Lo, (r, sr) => Sfx.Cloth(r, sr)));
for (int i = 1; i <= 6; i++) jobs.Add(($"step_stone_{i:00}", sfx, false, Lo, (r, sr) => Sfx.StepStone(r, sr)));
jobs.Add(("rifle_distant_01", sfx, false, Lo, (r, sr) => Sfx.RifleDistant(r, sr)));
for (int i = 1; i <= 5; i++) { int k = i; jobs.Add(($"breath_in_{k:00}", sfx, false, Lo, (r, sr) => Sfx.BreathOne(r, sr, true, (k - 1) / 4.0))); }
for (int i = 1; i <= 5; i++) { int k = i; jobs.Add(($"breath_out_{k:00}", sfx, false, Lo, (r, sr) => Sfx.BreathOne(r, sr, false, (k - 1) / 4.0))); }
jobs.Add(("stalker_seen_01", sfx, false, Lo, (r, sr) => Sfx.StalkerSeen(r, sr)));
for (int i = 1; i <= 4; i++) jobs.Add(($"twig_snap_{i:00}", sfx, false, Lo, (r, sr) => Forest.TwigSnap(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"branch_drop_{i:00}", sfx, false, Lo, (r, sr) => Forest.BranchDrop(r, sr)));
for (int i = 1; i <= 3; i++) jobs.Add(($"trunk_creak_{i:00}", sfx, false, Lo, (r, sr) => Forest.TrunkCreak(r, sr)));
for (int i = 1; i <= 3; i++) jobs.Add(($"cricket_chirp_{i:00}", sfx, false, Hi, (r, sr) => Forest.CricketChirp(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"raven_{i:00}", sfx, false, Lo, (r, sr) => Forest.Raven(r, sr)));
for (int i = 1; i <= 3; i++) jobs.Add(($"rustle_{i:00}", sfx, false, Lo, (r, sr) => Forest.Rustle(r, sr)));
jobs.Add(("heartbeat_loop", ambient, true, Lo, (r, sr) => Forest.Heartbeat(r, sr, 20)));
jobs.Add(("score_hum_loop", music, true, Lo, (r, sr) => Music.ScoreHumLoop(r, sr, 40)));
jobs.Add(("score_shimmer_loop", music, true, Lo, (r, sr) => Music.ScoreShimmerLoop(r, sr, 40)));
jobs.Add(("score_pad_loop", music, true, Lo, (r, sr) => Music.ScorePadLoop(r, sr, 40)));

if (args.Contains("--music-samples"))
{
	// Listening samples for the music direction; not game assets.
	string outDir = Path.Combine(root, "test-output", "music");
	Directory.CreateDirectory(outDir);
	foreach (var (name, gen) in new (string, Func<Rng, int, double[]>)[]
	{
		("option1_reactive_score_demo", Music.ReactiveDemo),
		("option3_dark_ambient_loop", Music.DarkAmbientLoop),
	})
	{
		var x = gen(new Rng(Seed ^ Fnv(name)), Hi);
		string path = Path.Combine(outDir, name + ".wav");
		Wav.Write(path, x, Hi);
		Console.WriteLine($"  wrote {Path.GetRelativePath(root, path)}  ({x.Length / (double)Hi:0.0} s)");
	}
	return 0;
}

bool verifyOnly = args.Contains("--verify");
int oi = Array.IndexOf(args, "--only");
// --only takes a comma-separated list of name fragments.
string[] only = oi >= 0 && oi + 1 < args.Length ? args[oi + 1].Split(',', StringSplitOptions.RemoveEmptyEntries) : null;
bool Selected(string name) => only == null || only.Any(o => name.Contains(o));

if (!verifyOnly)
{
	Console.WriteLine($"Repo root: {root}");
	foreach (var j in jobs)
	{
		if (!Selected(j.name)) continue;
		var sw = System.Diagnostics.Stopwatch.StartNew();
		var rng = new Rng(Seed ^ Fnv(j.name));
		var x = j.gen(rng, j.sr);
		string path = Path.Combine(j.dir, j.name + ".wav");
		Wav.Write(path, x, j.sr);
		Console.WriteLine($"  wrote {Path.GetRelativePath(root, path)}  ({x.Length / (double)j.sr:0.00} s, {sw.ElapsedMilliseconds} ms)");
	}
}

bool ok = Verify.Run(jobs.Where(j => Selected(j.name)).Select(j => (Path.Combine(j.dir, j.name + ".wav"), j.loop, j.sr)).ToList(), root);
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
