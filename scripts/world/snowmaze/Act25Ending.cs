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
/// burns on, and its burning melts a hole through the snow: daylight beyond.</item>
/// <item>(STORY.md Rev 5, the reveal) The mask burns off it: out of the falling, burning shape the gaunt black figure
/// stands up, unburned in the flames, watches them, and steps back into the snow and is gone (<see cref="MaskBurnsOff"/>;
/// from here every picture develops with it standing behind, <see cref="PhotoLog.StalkerFlag"/>). Where the mask fell,
/// Mitchell's badge lies in the ash: E takes it, "You can rest now, Dad.", and they walk out through the hole (and on in
/// the trailhead level: <see cref="EndingDirector"/>).</item>
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

	/// <summary>
	/// Decides the ending once and saves it (its flag), or returns the one already decided. Called before the Act 24
	/// checkpoint is saved (2026-10-13: the checkpoint went first, so quitting in the gap Continued into ending 1 whatever
	/// the record said), and again by <see cref="Play"/>, which then keeps the same one even if the clock has crossed a
	/// threshold in between.
	/// </summary>
	public static int Decide(StoryManager s)
	{
		for (int n = 1; n <= 5; n++) if (s != null && s.HasFlag(FlagPrefix + n)) return n;
		int ending = ForThisGame();
		s?.SetFlag(FlagPrefix + ending);
		return ending;
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
		Revealed = BadgeTaken = _out = false;
		Badge = null;
		BadgeUse = null;
		int ending = Decide(StoryManager.Instance);
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
		body.AddChild(new NapalmBurn { Name = "Burning", Size = WendigoHunter.BodyScale, LightEnergy = 1.4f });   // (2026-10-14d: and on it, all over it)
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
		// (Rev 5) the reveal: the mask burns off it, and out of the falling, burning shape the stalker stands up, unburned
		// in the flames, and watches them; then it steps back into the snow and is gone
		fire.Reparent(cave);   // (the flames stay where it fell)
		var side = away.Cross(Vector3.Up).Normalized();
		var ground = body.GlobalPosition;
		var stalker = await MaskBurnsOff(owner, player, body, ground, 1.05f, ct);
		Revealed = true;
		await Cutscene.Wait(owner, 3.4, ct);
		// it steps back, into the snow beside the burned hole, and is gone (the snow closes round it)
		var into = (wall + side * 1.9f - wallN * 1.4f) with { Y = ground.Y };
		AudioDirector.OneShot(owner, "creature_rattle_long", 12, stalker.GlobalPosition + Vector3.Up * 1.8f, -4f, "Unnatural", 5f, 0.04f);
		var back = owner.CreateTween();
		back.TweenProperty(stalker, "global_position", into, 2.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		// (lost into the snow's murk as it goes: slow, never a pop)
		back.Parallel().TweenMethod(Callable.From<float>(v => { if (GodotObject.IsInstanceValid(stalker)) stalker.Skin?.SetShaderParameter("visibility", v); }), 1f, 0f, 1.2f).SetDelay(1.4f);
		await Cutscene.Tween(owner, back, ct);
		stalker.QueueFree();
		GD.Print("[story] Act 25: it steps back into the snow and is gone");
		// the flames sink to a smoulder; where the mask fell, his badge lies in the ash
		var sink = owner.CreateTween();
		sink.TweenMethod(Callable.From<float>(v => { if (GodotObject.IsInstanceValid(fire)) fire.Intensity = v; }), 1f, 0.18f, 3.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await Cutscene.Wait(owner, 1.2, ct);
		Badge = LayBadge(cave, ground, (player.GlobalPosition - ground) with { Y = 0 });
		if (BadgeUse is { } take)
			take.Interacted += p => { if (!_out) _ = Cutscene.Run(owner, c => TakeBadgeAndGo(owner, p, wall, wallN, stop, c), lockInput: true); };
		await StoryBeat.PanTowards(owner, player, ground, 1.0f, ct, pitch: true);
		GD.Print("[story] Act 25: where the mask fell, Mitchell's badge lies in the ash");
		// (control back: E takes it, and on out)
		if (Badge == null) await TakeBadgeAndGo(owner, player, wall, wallN, stop, ct);
	}

	/// <summary>The stalker shown to the player (tests).</summary>
	public static bool Revealed { get; private set; }
	public static bool BadgeTaken { get; private set; }
	/// <summary>The badge in the ash, with its "Take the badge" use (null once taken).</summary>
	public static Node3D Badge { get; private set; }
	/// <summary>E on the badge (tests use it).</summary>
	public static PickupInteractable BadgeUse { get; private set; }
	private static bool _out;

	/// <summary>The badge, lying face up in the ash where the mask fell (the ash and the badge are Blender's:
	/// tools/Blender/wendigo_story.py), and E on it.</summary>
	private static Node3D LayBadge(Node3D parent, Vector3 at, Vector3 toPlayer)
	{
		var root = new Node3D { Name = "BadgeInTheAsh" };
		parent.AddChild(root);
		// (on the snow floor itself: where it stood its feet were a little in it, and the badge under the floor can't be
		// seen or taken)
		var down = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 1.2f, at + Vector3.Down * 1.5f, 1);
		var floor = parent.GetWorld3D().DirectSpaceState.IntersectRay(down);
		if (floor.Count > 0) at = (Vector3)floor["position"];
		root.GlobalPosition = at;
		if (toPlayer.LengthSquared() > 0.01f) root.GlobalBasis = Basis.LookingAt(-toPlayer.Normalized(), Vector3.Up);
		var src = ResourceLoader.Exists(Wendigo.StoryModel) ? GD.Load<PackedScene>(Wendigo.StoryModel).Instantiate<Node3D>() : null;
		if (src?.FindChild("AshPile", true, false) is MeshInstance3D ash)
		{
			ash.GetParent().RemoveChild(ash);
			ash.Owner = null;
			ash.Transform = new Transform3D(Basis.Identity, new Vector3(0, 0.01f, 0));
			ash.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			root.AddChild(ash);
		}
		Node3D badge = null;
		if (src?.FindChild("Badge", true, false) is MeshInstance3D b)
		{
			b.GetParent().RemoveChild(b);
			b.Owner = null;
			// face up (its face is its +Z), a little toward them, turned askew, on the ash's top
			b.Transform = new Transform3D(new Basis(Vector3.Up, 0.5f) * new Basis(Vector3.Right, -Mathf.Pi * 0.5f + 0.12f), new Vector3(0.1f, 0.1f, -0.15f));
			b.Scale = Vector3.One * 1.3f;
			root.AddChild(b);
			badge = b;
		}
		src?.Free();
		if (badge == null) return null;
		// a faint steady warmth on it from the embers, so it catches the eye (never flickering)
		root.AddChild(new OmniLight3D { Name = "Glint", LightColor = new Color(1f, 0.7f, 0.42f), LightEnergy = 0.35f, OmniRange = 1.1f, ShadowEnabled = false, Position = badge.Position + new Vector3(0, 0.35f, 0.2f) });
		var take = new PickupInteractable
		{
			Name = "Take", PickRadius = 0.5f, MaxDistance = 2.8f, Position = badge.Position + Vector3.Up * 0.22f,
			PromptFor = _ => "Take the badge", CanUse = _ => !_out,
		};
		root.AddChild(take);
		BadgeUse = take;
		return badge;
	}

	/// <summary>E on the badge: it's taken; "You can rest now, Dad."; then out through the burned hole into the light.</summary>
	private static async Task TakeBadgeAndGo(Node owner, PlayerController player, Vector3 wall, Vector3 wallN, Vector3 stop, CancellationToken ct)
	{
		if (_out) return;
		_out = true;
		if (Badge != null && GodotObject.IsInstanceValid(Badge))
		{
			AudioDirector.OneShot(owner, "item_take", 3, Badge.GlobalPosition, -6f, "Player", 2f, 0.05f);
			Badge.GetParent()?.GetNodeOrNull<PickupInteractable>("Take")?.QueueFree();
			Badge.QueueFree();
		}
		Badge = null;
		BadgeUse = null;
		BadgeTaken = true;
		StoryManager.Instance?.SetFlag(BadgeFlag);
		GD.Print("[story] Act 25: he takes his father's badge from the ash");
		await Cutscene.Wait(owner, 0.6, ct);
		await PlayerVoice.Say(owner, "rest_now", 2.6f, ct);
		await Cutscene.Wait(owner, 0.5, ct);
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

	public const string BadgeFlag = "mitchell_badge";

	/// <summary>
	/// (STORY.md Rev 5: "the burning mask drops away, and the stalker stands behind it") The burning wendigo goes down: it
	/// sinks and falls, blackened, and is gone in its flames; out of it the stalker stands up, its true form, black and
	/// unburned in the fire, and watches the player (its head on them). Sets the flag that puts it in every picture from
	/// here on (<see cref="PhotoLog.StalkerFlag"/>). Returns it, standing, for the caller to take away.
	/// </summary>
	public static async Task<StalkerBody> MaskBurnsOff(Node owner, PlayerController player, Wendigo mask, Vector3 at, float size, CancellationToken ct)
	{
		StoryManager.Instance?.SetFlag(PhotoLog.StalkerFlag);
		var host = mask.GetParent() as Node3D ?? (Node3D)owner;
		var ground = at;
		var st = new StalkerBody { Name = "TrueForm", Size = size };
		host.AddChild(st);
		var to = (player.GlobalPosition - ground) with { Y = 0 };
		// (it faces its +Z)
		st.GlobalPosition = ground + Vector3.Down * 1.6f * size;
		st.Rotation = new Vector3(0, Mathf.Atan2(to.X, to.Z), 0);
		AudioDirector.OneShot(owner, "wendigo_howl_04", 1, mask.GlobalPosition + Vector3.Up * 2.5f, 6f, "Unnatural", 12f, 0.03f);
		AudioDirector.OneShot(owner, "flame_ignite", 1, ground + Vector3.Up * 0.5f, 4f, "Events", 6f, 0.05f);
		// the mask falls: burned black, it folds down and over, back from them, and sinks into its own fire
		mask.Char(1f);
		var fallAxis = to.Normalized().Cross(Vector3.Up);
		var b0 = mask.GlobalBasis;
		var p0 = mask.GlobalPosition;
		var fall = owner.CreateTween();
		fall.TweenMethod(Callable.From<float>(u =>
		{
			if (!GodotObject.IsInstanceValid(mask)) return;
			float e = u * u;
			// (about the axis across its front: positive tips its top away from them)
			mask.GlobalBasis = new Basis(fallAxis.LengthSquared() > 0.001f ? fallAxis.Normalized() : Vector3.Right, 1.1f * e) * b0;
			mask.GlobalPosition = p0 + Vector3.Down * 1.4f * e;
		}), 0f, 1f, 2.0f);
		// and out of it, up through the flames, the black figure stands
		var rise = owner.CreateTween();
		rise.TweenInterval(0.6f);
		rise.TweenProperty(st, "global_position", ground, 2.4f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		st.TrackTarget = player.CameraRig?.Camera?.GlobalPosition ?? player.GlobalPosition + Vector3.Up * 1.6f;
		var look = StoryBeat.PanTowards(owner, player, ground + Vector3.Up * 1.9f * size, 2.6f, ct, pitch: true);
		await Cutscene.Tween(owner, fall, ct);
		if (GodotObject.IsInstanceValid(mask)) mask.Visible = false;
		await Cutscene.Tween(owner, rise, ct);
		await look;
		GD.Print("[story] the mask burns off: the stalker stands in the flames, unburned, watching");
		return st;
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
