using System.Collections.Generic;
using System.Linq;
using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// The achievements (2026-10-11, the owner's list: "Steam store polish ... achievements"). Kept here, in the game, in
/// user://achievements.cfg (across every save slot: an achievement is the player's, not the save's), each with the API name
/// it will have on Steamworks (docs/steam/achievements.md), so the hookup is one call in <see cref="Unlock"/>. Nothing on
/// screen when one is earned: Steam's own pop-up will say it; until then they show in the main menu's count only.
/// <list type="bullet">
/// <item>the story's turns: a checkpoint reached;</item>
/// <item>the five endings: the one played out to the credits;</item>
/// <item>the camera: the first picture, ten subjects, every subject, a five-star picture.</item>
/// </list>
/// </summary>
public partial class Achievements : Node
{
	public sealed record Def(string Id, string Name, string Text, bool Hidden = false);

	/// <summary>Every achievement, in the order they're listed (and on Steam).</summary>
	public static readonly Def[] All =
	{
		new("ACH_THE_STAIRS", "The Stairs", "Climb the stairs in the woods."),
		new("ACH_THE_CABIN", "Someone's Home", "Find the cabin."),
		new("ACH_THE_BRIDGE", "Don't Look Down", "Cross the bridge."),
		new("ACH_THE_BUNKER", "Below", "Go down into the bunker."),
		new("ACH_THE_GIANT", "Something Big", "Stand before the giant.", true),
		new("ACH_THE_LAKE", "Still Water", "Cross the lake."),
		new("ACH_THE_STATION", "Signal", "Leave the station."),
		new("ACH_THE_CHURCH", "Sanctuary", "Reach the church in the snow."),
		new("ACH_THE_LODGE", "Checkout", "Get out of the lodge.", true),
		new("ACH_THE_WENDIGO", "Dead Silent", "End the hunt.", true),
		new("ACH_FINISHED", "Back to the Car", "Finish the story."),
		new("ACH_ENDING_1", "The Trunk", "See the first ending.", true),
		new("ACH_ENDING_2", "Far Off", "See the second ending.", true),
		new("ACH_ENDING_3", "Burning", "See the third ending.", true),
		new("ACH_ENDING_4", "Hell Run", "See the fourth ending.", true),
		new("ACH_ENDING_5", "The Perfect Run", "See the fifth ending.", true),
		new("ACH_FIRST_PICTURE", "Say Cheese", "Take a picture."),
		new("ACH_TEN_SUBJECTS", "Field Notes", "Photograph ten different subjects."),
		new("ACH_ALL_SUBJECTS", "The Whole Album", "Photograph every subject."),
		new("ACH_FIVE_STARS", "Hold Still", "Take a five-star picture."),
	};

	private static readonly Dictionary<Checkpoint, string> AtCheckpoint = new()
	{
		[Checkpoint.Act2StairsClimbed] = "ACH_THE_STAIRS",
		[Checkpoint.Act5CabinEntered] = "ACH_THE_CABIN",
		[Checkpoint.Act6BridgeCrossed] = "ACH_THE_BRIDGE",
		[Checkpoint.Act8BunkerEntered] = "ACH_THE_BUNKER",
		[Checkpoint.Act11GiantEncounter] = "ACH_THE_GIANT",
		[Checkpoint.Act12LakeCrossed] = "ACH_THE_LAKE",
		[Checkpoint.Act13Finished] = "ACH_THE_STATION",
		[Checkpoint.Act21ChurchReached] = "ACH_THE_CHURCH",
		[Checkpoint.Act23Finished] = "ACH_THE_LODGE",
		[Checkpoint.Act24Finished] = "ACH_THE_WENDIGO",
		[Checkpoint.GameFinished] = "ACH_FINISHED",
	};

	public static Achievements Instance { get; private set; }
	private static string Path => GameSettings.Instance?.AutoTest == true ? "user://achievements_test.cfg" : "user://achievements.cfg";
	private readonly HashSet<string> _got = new();
	private double _photoT;

	public static bool Has(string id) => Instance?._got.Contains(id) ?? false;
	public static int Count => Instance?._got.Count ?? Load().Count;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		if (StoryManager.Instance is { } s) s.CheckpointReached -= OnCheckpoint;
	}

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Pausable;
		_got.UnionWith(Load());
		var s = StoryManager.Instance;
		if (s == null) return;
		s.CheckpointReached += OnCheckpoint;
		// (a save made before achievements were in: what it's already past counts)
		foreach (var (cp, id) in AtCheckpoint)
			if (s.Current >= cp) Unlock(id);
		if (s.Current >= Checkpoint.GameFinished) EndingSeen(s);
	}

	private void OnCheckpoint(Checkpoint cp)
	{
		if (AtCheckpoint.TryGetValue(cp, out var id)) Unlock(id);
		if (cp == Checkpoint.GameFinished && StoryManager.Instance is { } s) EndingSeen(s);
	}

	/// <summary>The ending's: once it has played out to the credits (its flag is set earlier, at the last burn).</summary>
	private void EndingSeen(StoryManager s)
	{
		for (int n = 1; n <= 5; n++)
			if (s.HasFlag(World.SnowMaze.Act25Ending.FlagPrefix + n)) Unlock($"ACH_ENDING_{n}");
	}

	public override void _Process(double delta)
	{
		// the camera's: looked at now and then (a picture is rare)
		_photoT -= delta;
		if (_photoT > 0 || PhotoLog.Instance is not { } log) return;
		_photoT = 2.0;
		if (log.RecordedCount > 0) Unlock("ACH_FIRST_PICTURE");
		int found = log.SubjectsFound;
		if (found >= 10) Unlock("ACH_TEN_SUBJECTS");
		if (found >= PhotoCatalog.Total) Unlock("ACH_ALL_SUBJECTS");
		if (log.Photos.Any(p => p.Stars >= 5)) Unlock("ACH_FIVE_STARS");
	}

	/// <summary>Earned (once). The Steamworks call goes here: SetAchievement(id), StoreStats().</summary>
	public void Unlock(string id)
	{
		if (!_got.Add(id)) return;
		GD.Print($"[achievement] {id}");
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue("got", id, Time.GetDatetimeStringFromSystem());
		cfg.Save(Path);
	}

	private static HashSet<string> Load()
	{
		var cfg = new ConfigFile();
		var got = new HashSet<string>();
		if (cfg.Load(Path) == Error.Ok && cfg.HasSection("got"))
			got.UnionWith(cfg.GetSectionKeys("got"));
		return got;
	}
}
