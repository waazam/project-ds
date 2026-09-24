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
	("rain_loop", ambient, true, Lo, (r, sr) => Ambient.Rain(r, sr, 180)),
	("lake_loop", ambient, true, Lo, (r, sr) => Ambient.Lake(r, sr, 40)),
	("fire_crackle_loop", ambient, true, Lo, (r, sr) => Ambient.FireCrackle(r, sr, 30)),
	("choir_chant_loop", ambient, true, Lo, (r, sr) => Ambient.ChoirChant(r, sr, 108)),
	("stairs_hum_loop", ambient, true, Lo, (r, sr) => Ambient.StairsHum(r, sr, 40)),
	("radio_static_loop", ambient, true, Lo, (r, sr) => Ambient.RadioStatic(r, sr, 22)),
	("radio_burst_01", sfx, false, Lo, (r, sr) => Ambient.RadioBurst(r, sr)),
	("radio_burst_02", sfx, false, Lo, (r, sr) => Ambient.RadioBurst(r, sr)),
};
for (int i = 1; i <= 3; i++) { int k = i; jobs.Add(($"thunder_{k:00}", sfx, false, Lo, (r, sr) => Sfx.Thunder(r, sr, k))); }
for (int i = 1; i <= 3; i++) jobs.Add(($"giant_step_{i:00}", sfx, false, Lo, (r, sr) => Sfx.GiantStep(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"squelch_open_{i:00}", sfx, false, Lo, (r, sr) => Sfx.Squelch(r, sr, true)));
for (int i = 1; i <= 2; i++) jobs.Add(($"squelch_close_{i:00}", sfx, false, Lo, (r, sr) => Sfx.Squelch(r, sr, false)));
for (int i = 1; i <= 4; i++) jobs.Add(($"radio_tick_{i:00}", sfx, false, Lo, (r, sr) => Sfx.RadioTick(r, sr)));
jobs.Add(("camera_shutter", sfx, false, Lo, (r, sr) => Sfx.CameraShutter(r, sr)));
jobs.Add(("distant_scream", sfx, false, Lo, (r, sr) => Sfx.DistantScream(r, sr)));
for (int i = 1; i <= 3; i++) jobs.Add(($"whisper_voice_{i:00}", sfx, false, Lo, (r, sr) => Sfx.WhisperVoice(r, sr)));
for (int i = 1; i <= 8; i++) { int k = i; jobs.Add(($"bird_{k:00}", sfx, false, Lo, (r, sr) => Sfx.Bird(k, r, sr))); }
for (int i = 1; i <= 6; i++) jobs.Add(($"step_dirt_{i:00}", sfx, false, Lo, (r, sr) => Sfx.StepDirt(r, sr)));
for (int i = 1; i <= 4; i++) { int k = i; jobs.Add(($"step_wood_{k:00}", sfx, false, Lo, (r, sr) => Sfx.StepWood(r, sr, k % 2 == 0))); }
for (int i = 1; i <= 4; i++) jobs.Add(($"cloth_{i:00}", sfx, false, Lo, (r, sr) => Sfx.Cloth(r, sr)));
for (int i = 1; i <= 6; i++) jobs.Add(($"step_stone_{i:00}", sfx, false, Lo, (r, sr) => Sfx.StepStone(r, sr)));
jobs.Add(("rifle_distant_01", sfx, false, Lo, (r, sr) => Sfx.RifleDistant(r, sr)));
jobs.Add(("newel_seat", sfx, false, Lo, (r, sr) => Sfx.NewelSeat(r, sr)));
for (int i = 1; i <= 3; i++) jobs.Add(($"knife_slice_{i:00}", sfx, false, Lo, (r, sr) => Sfx.KnifeSlice(r, sr)));
jobs.Add(("wheel_turn_01", sfx, false, Lo, (r, sr) => Sfx.WheelTurn(r, sr)));
jobs.Add(("clock_chime_drowned", sfx, false, Lo, (r, sr) => Sfx.ClockChimeDrowned(r, sr)));
jobs.Add(("clock_break", sfx, false, Lo, (r, sr) => Sfx.ClockBreak(r, sr)));
for (int i = 1; i <= 3; i++) jobs.Add(($"wall_knock_{i:00}", sfx, false, Lo, (r, sr) => Sfx.WallKnock(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"door_slam_{i:00}", sfx, false, Lo, (r, sr) => Sfx.DoorSlam(r, sr)));
for (int i = 1; i <= 3; i++) jobs.Add(($"wall_pound_{i:00}", sfx, false, Lo, (r, sr) => Sfx.WallPound(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"cabin_slam_{i:00}", sfx, false, Lo, (r, sr) => Sfx.CabinSlam(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"body_thump_{i:00}", sfx, false, Lo, (r, sr) => Sfx.BodyThump(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"steel_door_open_{i:00}", sfx, false, Lo, (r, sr) => Sfx.SteelDoorOpen(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"steel_door_slam_{i:00}", sfx, false, Lo, (r, sr) => Sfx.SteelDoorSlam(r, sr)));
for (int i = 1; i <= 5; i++) { int k = i; jobs.Add(($"breath_in_{k:00}", sfx, false, Lo, (r, sr) => Sfx.BreathOne(r, sr, true, (k - 1) / 4.0))); }
for (int i = 1; i <= 5; i++) { int k = i; jobs.Add(($"breath_out_{k:00}", sfx, false, Lo, (r, sr) => Sfx.BreathOne(r, sr, false, (k - 1) / 4.0))); }
jobs.Add(("stalker_seen_01", sfx, false, Lo, (r, sr) => Sfx.StalkerSeen(r, sr)));
// Creature candidates (not wired into the game yet): growls, roars (near / far), screeches, snarls.
for (int i = 1; i <= 3; i++) jobs.Add(($"creature_roar_near_{i:00}", sfx, false, Lo, (r, sr) => Creature.Roar(r, sr, false)));
for (int i = 1; i <= 3; i++) jobs.Add(($"creature_roar_far_{i:00}", sfx, false, Lo, (r, sr) => Creature.Roar(r, sr, true)));
for (int i = 1; i <= 4; i++) { int k = i; jobs.Add(($"creature_screech_{k:00}", sfx, false, Lo, (r, sr) => Creature.Screech(r, sr, k))); }
for (int i = 1; i <= 8; i++) { int k = i; jobs.Add(($"creature_snarl_{k:00}", sfx, false, Lo, (r, sr) => Creature.Snarl(r, sr, k))); }
// Eight beats of the one rattle voice, 21-30 s each: the game swaps takes between bursts so the rhythm never repeats.
for (int i = 1; i <= 8; i++) { int k = i; int len = new[] { 24, 28, 22, 26, 30, 25, 21, 29 }[k - 1]; jobs.Add(($"creature_rattle_loop_{k:00}", sfx, true, Lo, (r, sr) => Creature.RattleLoop(r, sr, len, k))); }
jobs.Add(("creature_giant_rattle_loop_01", sfx, true, Lo, (r, sr) => Creature.GiantRattle(r, sr, 26)));
// Rattle candidates (Dan, 2026-09-22: the click reads as hooves): a..f, not wired in until he picks one.
jobs.Add(("creature_rattle_cand_a", sfx, true, Lo, (r, sr) => Creature.RattleCroak(r, sr, 24, Creature.RattleVar.Base)));
jobs.Add(("creature_rattle_cand_b", sfx, true, Lo, (r, sr) => Creature.RattleInsect(r, sr, 22, Creature.RattleVar.Base)));
jobs.Add(("creature_rattle_cand_c", sfx, true, Lo, (r, sr) => Creature.RattleWetClicks(r, sr, 24, Creature.RattleVar.Base)));
jobs.Add(("creature_rattle_cand_d", sfx, true, Lo, (r, sr) => Creature.RattleBone(r, sr, 26, Creature.RattleVar.Base)));
jobs.Add(("creature_rattle_cand_e", sfx, true, Lo, (r, sr) => Creature.RattleRatchet(r, sr, 22, Creature.RattleVar.Base)));
jobs.Add(("creature_rattle_cand_f", sfx, true, Lo, (r, sr) => Creature.RattleCroakClicks(r, sr, 24, Creature.RattleVar.Base)));
for (int i = 1; i <= 2; i++) jobs.Add(($"creature_foghorn_{i:00}", sfx, false, Lo, (r, sr) => Creature.Foghorn(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"creature_giant_moan_{i:00}", sfx, false, Lo, (r, sr) => Creature.GiantMoan(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"creature_giant_bellow_{i:00}", sfx, false, Lo, (r, sr) => Creature.GiantBellow(r, sr)));
for (int i = 1; i <= 3; i++) { int k = i; jobs.Add(($"creature_jumpscream_{k:00}", sfx, false, Lo, (r, sr) => Creature.JumpScream(r, sr, k))); }
for (int i = 1; i <= 3; i++) { int k = i; jobs.Add(($"creature_jumpscare_{k:00}", sfx, false, Hi, (r, sr) => Creature.Jumpscare(r, sr, k))); }
for (int i = 1; i <= 4; i++) jobs.Add(($"twig_snap_{i:00}", sfx, false, Lo, (r, sr) => Forest.TwigSnap(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"branch_drop_{i:00}", sfx, false, Lo, (r, sr) => Forest.BranchDrop(r, sr)));
for (int i = 1; i <= 3; i++) jobs.Add(($"trunk_creak_{i:00}", sfx, false, Lo, (r, sr) => Forest.TrunkCreak(r, sr)));
for (int i = 1; i <= 3; i++) jobs.Add(($"cricket_chirp_{i:00}", sfx, false, Hi, (r, sr) => Forest.CricketChirp(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"raven_{i:00}", sfx, false, Lo, (r, sr) => Forest.Raven(r, sr)));
for (int i = 1; i <= 3; i++) jobs.Add(($"rustle_{i:00}", sfx, false, Lo, (r, sr) => Forest.Rustle(r, sr)));
jobs.Add(("heartbeat_loop", ambient, true, Lo, (r, sr) => Forest.Heartbeat(r, sr, 20)));
// Act 12: the lake crossing (the rowboat, the water, the creature, the dawn).
for (int i = 1; i <= 4; i++) jobs.Add(($"oar_stroke_{i:00}", sfx, false, Lo, (r, sr) => Lake.OarStroke(r, sr)));
for (int i = 1; i <= 3; i++) jobs.Add(($"oarlock_creak_{i:00}", sfx, false, Lo, (r, sr) => Lake.OarlockCreak(r, sr)));
for (int i = 1; i <= 3; i++) jobs.Add(($"boat_creak_{i:00}", sfx, false, Lo, (r, sr) => Lake.BoatCreak(r, sr)));
jobs.Add(("boat_board_01", sfx, false, Lo, (r, sr) => Lake.BoatBoard(r, sr)));
jobs.Add(("boat_ground_01", sfx, false, Lo, (r, sr) => Lake.BoatGround(r, sr)));
for (int i = 1; i <= 4; i++) jobs.Add(($"wave_slap_{i:00}", sfx, false, Lo, (r, sr) => Lake.WaveSlap(r, sr)));
jobs.Add(("underwater_thoom_01", sfx, false, Lo, (r, sr) => Lake.UnderwaterThoom(r, sr)));
jobs.Add(("breach_erupt_01", sfx, false, Lo, (r, sr) => Lake.BreachErupt(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"tentacle_slam_{i:00}", sfx, false, Lo, (r, sr) => Lake.TentacleSlam(r, sr)));
for (int i = 1; i <= 3; i++) { int k = i; jobs.Add(($"loon_{k:00}", sfx, false, Hi, (r, sr) => Lake.Loon(r, sr, k))); }
jobs.Add(("hull_lap_loop", ambient, true, Lo, (r, sr) => Lake.HullLap(r, sr, 24)));
jobs.Add(("lake_rough_loop", ambient, true, Lo, (r, sr) => Lake.RoughWater(r, sr, 30)));
// Act 13: the forester station.
jobs.Add(("bulb_buzz_loop", sfx, true, Lo, (r, sr) => Station.BulbBuzz(r, sr, 20)));
for (int i = 1; i <= 3; i++) jobs.Add(($"bulb_sputter_{i:00}", sfx, false, Lo, (r, sr) => Station.BulbSputter(r, sr)));
jobs.Add(("bulb_pop_01", sfx, false, Lo, (r, sr) => Station.BulbPop(r, sr)));
jobs.Add(("drain_gurgle_01", sfx, false, Lo, (r, sr) => Station.DrainGurgle(r, sr)));
jobs.Add(("clock_tick_loop", sfx, true, Lo, (r, sr) => Station.ClockTick(r, sr, 20)));
jobs.Add(("glass_crack_01", sfx, false, Lo, (r, sr) => Station.GlassCrack(r, sr)));
jobs.Add(("glass_shatter_01", sfx, false, Lo, (r, sr) => Station.GlassShatter(r, sr)));
jobs.Add(("water_torrent_loop", ambient, true, Lo, (r, sr) => Station.Torrent(r, sr, 24)));
jobs.Add(("blood_suck_01", sfx, false, Lo, (r, sr) => Station.BloodSuck(r, sr)));
for (int i = 1; i <= 4; i++) jobs.Add(($"cryptex_click_{i:00}", sfx, false, Lo, (r, sr) => Station.CryptexClick(r, sr)));
for (int i = 1; i <= 2; i++) jobs.Add(($"cryptex_stick_{i:00}", sfx, false, Lo, (r, sr) => Station.CryptexStick(r, sr)));
jobs.Add(("cryptex_open_01", sfx, false, Lo, (r, sr) => Station.CryptexOpen(r, sr)));
jobs.Add(("lighter_flick_01", sfx, false, Lo, (r, sr) => Station.LighterFlick(r, sr)));
jobs.Add(("web_burn_01", sfx, false, Lo, (r, sr) => Station.WebBurn(r, sr)));
for (int i = 1; i <= 4; i++) jobs.Add(($"wade_{i:00}", sfx, false, Lo, (r, sr) => Station.Wade(r, sr)));
jobs.Add(("door_creak_01", sfx, false, Lo, (r, sr) => Station.SmallCreak(r, sr)));
jobs.Add(("industrial_drone_loop", ambient, true, Lo, (r, sr) => Station.IndustrialDrone(r, sr, 30)));

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

if (args.Contains("--voice-test"))
{
	// Intelligibility checks for the chant (not game assets): one dry singer, the dry unison, and
	// the finished distant choir, each as a few spaced phrases. Feed them to a speech recogniser.
	string outDir = Path.Combine(root, "test-output", "audio");
	Directory.CreateDirectory(outDir);
	int sr = Lo, n = 16 * sr;
	var plan = new List<Ambient.ChantPhrase> { new(0.5, 62, 1, 1), new(5.5, 62, 1, 1), new(10.5, 62, 1, 1) };
	foreach (var (name, men, women, spaced) in new[] { ("chant_solo_dry", 1, 0, false), ("chant_unison_dry", 4, 4, false), ("chant_unison_hall", 4, 4, true) })
	{
		var x = Ambient.ChantDry(new Rng(Seed ^ Fnv(name)), sr, n, plan, men, women, 1.0);
		if (spaced) x = Ambient.ChoirSpace(x, sr); else Dsp.NormPeak(x, -3);
		string path = Path.Combine(outDir, name + ".wav");
		Wav.Write(path, x, sr);
		Console.WriteLine($"  wrote {Path.GetRelativePath(root, path)}");
		if (name != "chant_solo_dry") continue;
		// Phone centres in the first phrase (starts 0.5 s, tempo 1), and where each should put its energy.
		Console.WriteLine($"    {"phone",-6} {"<400",6} {"-1.3k",6} {"-2k",6} {"-3.5k",6} {">3.5k",6}  expected");
		foreach (var (ph, at, exp) in new[]
		{
			("uh", 0.75, "400-1.3k (F1 640, F2 1190)"), ("m", 1.05, "<400 (murmur)"), ("schwa", 1.24, "400-2k"),
			("n", 1.41, "<400 (murmur)"), ("s", 1.56, ">3.5k (frication)"), ("ee", 2.2, "<400 (F1 270) + 2-3.5k (F2 2290, F3 3010)"),
		})
		{
			var b = PhoneCheck.Bands(x, sr, at);
			Console.WriteLine($"    {ph,-6} " + string.Join(" ", b.Select(v => $"{v * 100,6:0.0}")) + $"  {exp}");
		}
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
