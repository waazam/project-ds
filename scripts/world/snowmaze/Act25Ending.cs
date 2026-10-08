using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.SnowMaze;

/// <summary>
/// Act 25, the endings (the owner's "Real Act 25": "the endings will make players speculate a loop theory for the game
/// which is intentional ... The factors are: usage of the camera ... amount of times the player has died throughout the
/// game ... and lastly their speed"). Here, the part in the maze, and the choice:
/// <list type="bullet">
/// <item>The last burn: the wendigo staggers back from them burning, into the maze's wall, and goes down against it; it
/// burns on, and its burning melts a hole through the snow: daylight beyond. They walk out through it (and on in the
/// trailhead level: <see cref="EndingDirector"/>).</item>
/// <item>Which ending, by the game's record (<see cref="Choose"/>).</item>
/// </list>
/// </summary>
public static class Act25Ending
{
	public const string FlagPrefix = "ending_";
	/// <summary>"All pictures": every subject in the catalogue photographed.</summary>
	public static bool AllPictures => PhotoLog.Instance is { } log && log.SubjectsFound >= PhotoCatalog.Total;
	public static int Pictures => PhotoLog.Instance?.RecordedCount ?? 0;

	/// <summary>
	/// The ending for a record (the owner's table):
	/// <list type="number">
	/// <item>more than 10 pictures, 1-10 deaths, over two hours: the camera back in the trunk; the credits;</item>
	/// <item>no deaths, every picture: the camera back in the trunk; five seconds of black, it howls far off; the credits;</item>
	/// <item>10 or more deaths, under 10 pictures, over two hours: it crushes the car, comes on burning; the stalker;</item>
	/// <item>more than 10 pictures, exactly 1 death, under two hours (the hell run): the fog; the stairs where the car was;
	/// blood down them; up them;</item>
	/// <item>no deaths, every picture, under two hours (the perfect run): the fog; the car really there; the camera in the
	/// trunk; black; at the wheel, the engine starting: away.</item>
	/// </list>
	/// Records the table doesn't name fall to the nearest: many deaths, the third; otherwise the first.
	/// </summary>
	public static int Choose(int pictures, bool allPictures, int deaths, double hours)
	{
		bool fast = hours < 2.0;
		if (deaths == 0 && allPictures) return fast ? 5 : 2;
		if (pictures > 10 && deaths == 1 && fast) return 4;
		if (deaths >= 10 && pictures < 10 && !fast) return 3;
		if (pictures > 10 && deaths >= 1 && deaths <= 10 && !fast) return 1;
		return deaths >= 10 ? 3 : 1;
	}

	/// <summary>The ending for this game (`--ending=N` forces one, for tests).</summary>
	public static int ForThisGame()
	{
		foreach (var a in OS.GetCmdlineUserArgs())
			if (a.StartsWith("--ending=") && int.TryParse(a["--ending=".Length..], out int n) && n >= 1 && n <= 5) return n;
		var s = StoryManager.Instance;
		return Choose(Pictures, AllPictures, s?.TotalDeaths ?? 0, (s?.PlaySeconds ?? 0) / 3600.0);
	}

	public static int Chosen(StoryManager s)
	{
		for (int n = 1; n <= 5; n++) if (s != null && s.HasFlag(FlagPrefix + n)) return n;
		return 1;
	}

	/// <summary>The last burn, the hole, and out (then the trailhead's part of it).</summary>
	public static async Task Play(Node owner, PlayerController player, WendigoHunter hunter, SnowMazeCave cave, CancellationToken ct)
	{
		var body = hunter.Body;
		int ending = ForThisGame();
		StoryManager.Instance?.SetFlag(FlagPrefix + ending);
		GD.Print($"[story] Act 25: the ending - {ending} (pictures {Pictures}, all {AllPictures}, deaths {StoryManager.Instance?.TotalDeaths}, {StoryManager.Instance?.PlaySeconds / 3600.0:0.00} h)");
		// it staggers back from them into the wall: away from them, to the snow behind it
		var from = body.GlobalPosition;
		var away = ((from - player.GlobalPosition) with { Y = 0 }).Normalized();
		var q = PhysicsRayQueryParameters3D.Create(from + Vector3.Up * 1.4f, from + Vector3.Up * 1.4f + away * 6f, 1);
		var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(q);
		var wall = hit.Count > 0 ? (Vector3)hit["position"] : from + away * 3f + Vector3.Up * 1.4f;
		var wallN = hit.Count > 0 ? (Vector3)hit["normal"] : -away;
		var stop = (wall - away * 0.9f) with { Y = from.Y };
		AudioDirector.OneShot(owner, "wendigo_howl_04", 1, from + Vector3.Up * 3f, 8f, "Unnatural", 12f, 0.02f);
		// (its stride matched to the ground it covers, at its size in the tunnels)
		float pace = Mathf.Max(from.DistanceTo(stop) / 1.6f, 0.3f);
		body.Play("walk", Mathf.Clamp(2.55f * WendigoHunter.BodyScale / pace, 0.5f, 3f), 0.2);
		var look = Watch(owner, player, body, 9.5f, ct);
		var tw = owner.CreateTween();
		tw.TweenProperty(body, "global_position", stop, 1.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		await Cutscene.Tween(owner, tw, ct);
		// down against the wall, burning (the fire up all over it)
		AudioDirector.OneShot(owner, "wendigo_land", 2, stop, 4f, "Events", 8f, 0.03f);
		body.Hold("crouch", 1f, 0.5);
		var fire = new FireVfx { Name = "Pyre", Extent = new Vector3(1.0f, 2.2f, 1.0f), FlameScale = 1f, LightRange = 9f, LightEnergy = 2.6f, LightShadows = false, Position = new Vector3(0, 0.2f, 0) };
		body.AddChild(fire);
		await Cutscene.Wait(owner, 1.6, ct);
		// it burns through: the snow at its back sags and melts away, and there's light beyond
		var hole = new MeshInstance3D
		{
			Name = "BurntThrough", Mesh = new SphereMesh { Radius = 1.4f, Height = 2.6f, RadialSegments = 18, Rings = 9 },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.66f, 0.7f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
			Scale = Vector3.One * 0.05f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		cave.AddChild(hole);
		hole.GlobalPosition = wall + wallN * -0.4f;
		var light = new OmniLight3D { LightColor = new Color(0.75f, 0.8f, 0.85f), LightEnergy = 0f, OmniRange = 10f, ShadowEnabled = false };
		cave.AddChild(light);
		light.GlobalPosition = wall + wallN * 0.8f;
		AudioDirector.OneShot(owner, "door_strain", 2, wall, 0f, "Events", 6f, 0.05f);
		var melt = owner.CreateTween().SetParallel();
		melt.TweenProperty(hole, "scale", Vector3.One, 3.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		melt.TweenProperty(light, "light_energy", 1.6f, 3.2f);
		await Cutscene.Tween(owner, melt, ct);
		GD.Print("[story] Act 25: it burns through the wall - light beyond");
		await look;
		// they walk out through it, past it, into the light
		var through = wall - wallN * 1.6f;
		var walk = player.CreateTween();
		walk.TweenProperty(player, "global_position", (stop + (through - stop) * 0.4f) with { Y = player.GlobalPosition.Y } + (player.GlobalPosition - stop).Normalized() * 0.6f, 2.4f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await StoryBeat.PanTowards(owner, player, through + Vector3.Up * 1.2f, 1.2f, ct);
		await Cutscene.Tween(owner, walk, ct);
		var fader = StoryBeat.Fader(owner);
		if (fader != null) await fader.Fade(1f, 2.2f, ct);
		// out: on in the trailhead, where it began
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act24Finished);
		GD.Print("[story] Act 25: out through the hole, into the woods it all began in");
		StoryManager.Instance?.ContinueGame();
	}

	private static async Task Watch(Node owner, PlayerController player, Node3D body, float seconds, CancellationToken ct)
	{
		double t = 0;
		while (t < seconds)
		{
			if (!GodotObject.IsInstanceValid(body)) break;
			await StoryBeat.PanTowards(owner, player, body.GlobalPosition + Vector3.Up * 2.2f, 0.25f, ct, pitch: true);
			t += 0.25;
		}
	}
}
