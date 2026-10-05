using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// Act 23's end (the owner's document). The front doors, from inside: chained, and "Requires Master Key." (a fake-out:
/// there's no master key). After the crawlspace, with the lodge frozen over, they stand ajar. Pushed, they give a
/// little at a time against the snow banked outside, and behind them, a howl. They turn, and look up: it's on the
/// balcony, watching. "STARVING..... FREEZING..... FOREVER...." Then it comes off the balcony at them in one leap no
/// living thing could make; they throw themselves aside, and it goes on through the front doors and out, and the doors
/// are gone. They get up, walk to the splintered doorway, and look out, left, and right. The end of Act 23.
/// </summary>
public partial class SkiLodge
{
	public Node3D FrontLeafL { get; private set; }
	public Node3D FrontLeafR { get; private set; }
	public Node3D FrontChain { get; private set; }
	public bool FrontBroken { get; private set; }
	public Wendigo FinaleWendigo { get; private set; }
	public bool FinaleDone { get; private set; }
	private bool _finaleRunning;

	private StaticBody3D _frontBlock;

	/// <summary>Where the front doors are, from inside (lodge-local).</summary>
	public static Vector3 FrontDoorInside => new(0, 1.55f, HexIn - 0.03f);

	/// <summary>The two leaves (hinged at their outer edges), the chain through their handles and its padlock, and the
	/// use: "Requires Master Key." until the lodge has frozen over; then they can be pushed.</summary>
	private void BuildFrontDoorsInside()
	{
		Vector3 dc = FrontDoorInside;
		// the doorway through the wall's thickness, shut by the leaves (inside and out) until the wendigo goes through them
		_frontBlock = new StaticBody3D { Name = "FrontDoorBlock", CollisionLayer = 1, CollisionMask = 0 };
		_frontBlock.SetMeta("surface", "wood");
		_frontBlock.AddChild(new CollisionShape3D { Position = new Vector3(0, FloorY + 1.55f, Apothem + 0.045f), Shape = new BoxShape3D { Size = new Vector3(2.6f, 3.1f, 0.89f) } });
		AddChild(_frontBlock);
		foreach (float s in new[] { -1f, 1f })
		{
			var leaf = new Node3D { Name = s < 0 ? "FrontLeafL" : "FrontLeafR", Position = new Vector3(s * 1.3f, FloorY, dc.Z) };
			AddChild(leaf);
			var k = new MeshKit();
			k.Mat(LodgeTextures.DarkWoodMat);
			k.Color = Colors.White;
			k.Box(new Vector3(-s * 0.65f, dc.Y - FloorY, 0), new Vector3(1.29f, 3.1f, 0.06f), 1f);
			k.Mat(LodgeTextures.IronMat);
			k.Box(new Vector3(-s * 0.65f, dc.Y - FloorY - 0.9f, -0.035f), new Vector3(1.2f, 0.08f, 0.02f), 1f);
			k.Box(new Vector3(-s * 0.65f, dc.Y - FloorY + 0.9f, -0.035f), new Vector3(1.2f, 0.08f, 0.02f), 1f);
			k.Mat(LodgeTextures.BrassMat);
			k.Cylinder(new Vector3(-s * 1.1f, dc.Y - FloorY + 0.05f, -0.03f), new Vector3(-s * 1.1f, dc.Y - FloorY + 0.05f, -0.12f), 0.035f, 0.035f, 6, true);
			k.CommitTo(leaf, "Leaf", true);
			if (s < 0) FrontLeafL = leaf; else FrontLeafR = leaf;
		}
		FrontChain = new Node3D { Name = "FrontChain", Position = dc };
		AddChild(FrontChain);
		var ck = new MeshKit();
		ck.Mat(LodgeTextures.IronMat);
		ck.Color = Colors.White;
		for (int i = 0; i < 11; i++)
		{
			float u = (i - 5) / 5f;
			Vector3 p = new(u * 0.45f, 0.05f - Mathf.Cos(u * 1.2f) * 0.1f + 0.1f, -0.09f);
			ck.Cylinder(p - new Vector3(0.035f, 0, 0), p + new Vector3(0.035f, 0, 0), 0.02f, 0.02f, 4, true);
		}
		ck.Box(new Vector3(0.05f, -0.15f, -0.11f), new Vector3(0.12f, 0.15f, 0.05f), 1f);
		ck.CommitTo(FrontChain, "Chain", false);
		FrontInsideUse = new PickupInteractable
		{
			Name = "FrontInsideUse", PickRadius = 1.2f, MaxDistance = 2.8f, Position = dc + new Vector3(0, 0, -0.3f),
			PromptFor = _ => FrontBroken ? "" : Frozen ? "Push the door open" : "Requires Master Key.",
			CanUse = _ => !FrontBroken && !_finaleRunning,
		};
		FrontInsideUse.Interacted += p =>
		{
			if (Frozen && !FrontBroken && !_finaleRunning) _ = Cutscene.Run(this, ct => Finale(p, ct), lockInput: true);
			else AudioDirector.OneShot(this, "haunt_chain", 2, ToGlobal(dc), -8f);
		};
		AddChild(FrontInsideUse);
	}

	/// <summary>The lodge frozen: the chain's gone (snapped, lying on the floor), the right leaf standing a hand open.</summary>
	private void FrontDoorsAjar()
	{
		if (FrontChain != null) FrontChain.Position = FrontDoorInside with { Y = FloorY + 0.03f } + new Vector3(0.2f, 0, -0.35f);
		if (FrontChain != null) FrontChain.Rotation = new Vector3(Mathf.Pi * 0.5f, 0.4f, 0);
		if (FrontLeafR != null) FrontLeafR.Rotation = new Vector3(0, 0.12f, 0);
	}

	private async Task Finale(PlayerController player, CancellationToken ct)
	{
		_finaleRunning = true;
		GD.Print("[story] Act 23: the front door - pushing it open against the snow");
		player.Velocity = Vector3.Zero;
		var stand = ToGlobal(new Vector3(0.35f, FloorY, HexIn - 1.0f));
		var walk = player.CreateTween();
		walk.TweenProperty(player, "global_position", stand, Mathf.Clamp(player.GlobalPosition.DistanceTo(stand) / 1.5f, 0.3f, 1.6f)).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await StoryBeat.PanTowards(this, player, ToGlobal(FrontDoorInside + new Vector3(0, 0, 1f)), 0.8f, ct);
		await Cutscene.Tween(this, walk, ct);
		// it gives a little at a time against the snow outside (a lean and a push; small: the owner's comfort)
		for (int i = 1; i <= 3; i++)
		{
			var home = player.GlobalPosition;
			var fwd = GlobalBasis * Vector3.Back;
			var tw = player.CreateTween();
			tw.TweenProperty(player, "global_position", home + fwd * 0.12f, 0.35f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			tw.TweenProperty(player, "global_position", home + fwd * 0.05f, 0.4f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			AudioDirector.OneShot(this, "door_strain", 2, ToGlobal(FrontDoorInside), -4f + i * 2f, "Events", 4f, 0.05f);
			var give = CreateTween();
			give.TweenProperty(FrontLeafR, "rotation:y", 0.12f + 0.09f * i, 0.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			await Cutscene.Tween(this, tw, ct);
			await Cutscene.Wait(this, 0.35, ct);
		}
		// behind them: a howl. It's on the balcony, across the hall, watching
		var walkTop = ToGlobal(BalconyPerch);
		FinaleWendigo = new Wendigo { Name = "FinaleWendigo" };
		AddChild(FinaleWendigo);
		FinaleWendigo.StandAt(walkTop, player.GlobalPosition);
		AudioDirector.OneShot(this, "wendigo_howl_04", 1, walkTop + Vector3.Up * 3f, 7.5f, "Unnatural", 12f, 0.02f);
		GD.Print("[story] Act 23: a howl behind them - it's on the balcony, watching");
		await Cutscene.Wait(this, 0.6, ct);
		// they turn, and look up, stumbling back from the doors into the hall
		var back = player.CreateTween();
		back.TweenProperty(player, "global_position", ToGlobal(FinaleRetreat), 1.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		await StoryBeat.PanTowards(this, player, walkTop + Vector3.Up * 3.4f, 1.7f, ct, pitch: true);
		await Cutscene.Wait(this, 0.8, ct);
		// it speaks
		AudioDirector.OneShot(this, "wendigo_forever", 1, walkTop + Vector3.Up * 3f, 6f, "Voice", 10f, 0f);
		FinaleWendigo.Speak(6.3f);   // (its jaw working with the words)
		foreach (var (line, t) in new[] { ("STARVING.....", 1.8), ("FREEZING.....", 1.9), ("FOREVER....", 2.2) })
		{
			_ = StoryBeat.Caption(this, line, 0.2f, (float)t - 0.5f, 0.3f);
			await Cutscene.Wait(this, t, ct);
		}
		await Cutscene.Wait(this, 0.4, ct);
		// from here the view stays on it, eased round after it wherever it goes (gently: the owner's comfort)
		bool follow = true;
		_ = StoryBeat.Follow(this, player, () => IsInstanceValid(FinaleWendigo) ? FinaleWendigo.ChestWorld : player.GlobalPosition + Vector3.Forward,
			() => !follow || !IsInstanceValid(FinaleWendigo) || !FinaleWendigo.Visible, ct, 3.5f, 0.04f);
		// it comes to the edge, and up onto the rail: crouched on the log handrail over the hall, the rail groaning
		var rail = ToGlobal(BalconyRail);
		var from = FinaleWendigo.GlobalPosition;
		FinaleWendigo.Play("crouch", 0.8f * Pace, 0.3);
		AudioDirector.OneShot(this, "wendigo_step", 6, from + Vector3.Up, 0f, "Events", 6f, 0.05f);
		var climb = CreateTween();
		climb.TweenMethod(Callable.From<float>(u => FinaleWendigo.GlobalPosition = from.Lerp(rail, u) + Vector3.Up * Mathf.Sin(u * Mathf.Pi) * 0.45f), 0f, 1f, 0.8f * Pace)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await Cutscene.Tween(this, climb, ct);
		FinaleWendigo.Hold("crouch", 1f, 0.15 * Pace);
		AudioDirector.OneShot(this, "lodge_rail_groan", 2, rail, 3f, "Events", 7f, 0.03f);
		GD.Print("[story] Act 23: it climbs onto the balcony's rail, crouched over the hall");
		var land = ToGlobal(FinaleLanding);
		await TurnWendigo(land, 0.6f * Pace, ct);
		await Cutscene.Wait(this, 1.0 * Pace, ct);
		// it comes off the rail at them, falling on them; they throw themselves aside, and it lands where they stood
		GD.Print("[story] Act 23: it comes off the balcony at them");
		bool landed = false;
		FinaleWendigo.Leap(land, 1.6f * Pace, () => landed = true, stay: true, arc: 1.6f, ballistic: true, landClip: "land", landSeconds: 1.1f * Pace);
		AudioDirector.OneShot(this, "wendigo_howl_03", 1, rail + Vector3.Up * 2f, 7.5f, "Unnatural", 12f, 0.02f);
		await Cutscene.Wait(this, 0.3 * Pace, ct);
		var dive = player.CreateTween();
		var diveTo = ToGlobal(FinaleDive);
		dive.TweenProperty(player, "global_position", diveTo, 0.45f * Pace).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		player.SetCrouch(true);
		AudioDirector.OneShot(this, "body_dive", 1, diveTo + Vector3.Up * 0.3f, -2f, "Player", 3f, 0.04f);
		for (int w = 0; w < 80 && !landed; w++) await Cutscene.Wait(this, 0.05, ct);
		FinaleWendigo.GlobalPosition = land;
		FinaleLanded = true;
		AudioDirector.OneShot(this, "wendigo_land", 2, land + Vector3.Up * 0.5f, 7f, "Events", 9f, 0.03f);
		GD.Print("[story] Act 23: it lands in the hall, a few steps from them, down on its hands on the boards");
		await Cutscene.Wait(this, 1.3 * Pace, ct);
		// it turns to them, slowly, still down on the boards, and looks at them; the copied voices muttering in it
		await TurnWendigo(player.GlobalPosition, 1.0f * Pace, ct);
		AudioDirector.OneShot(this, "wendigo_mumble", 4, FinaleWendigo.MouthWorld, 0f, "Voice", 6f, 0f);
		FinaleWendigo.Speak(1.2f);
		await Cutscene.Wait(this, 1.0 * Pace, ct);
		// (the owner: "more intimidating toward the player") up off its hands to its full height over them; a step at them,
		// and another; then down into their face, its jaw working, one of its stolen voices close enough to feel
		GD.Print("[story] Act 23: it rises over them, comes at them a step, and leans down into their face");
		FinaleWendigo.Play("idle", 4f, 0.8 * Pace);
		AudioDirector.OneShot(this, "creature_snarl", 8, FinaleWendigo.MouthWorld, -2f, "Unnatural", 6f, 0.05f);
		await Cutscene.Wait(this, 0.9 * Pace, ct);
		var wp = FinaleWendigo.GlobalPosition;
		var toThem = (player.GlobalPosition - wp) with { Y = 0 };
		float gap = toThem.Length();
		var closer = wp + toThem.Normalized() * Mathf.Max(0f, gap - 2.3f);
		var step = CreateTween();
		step.TweenProperty(FinaleWendigo, "global_position", wp.Lerp(closer, 0.5f), 0.45f * Pace).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		step.TweenProperty(FinaleWendigo, "global_position", closer, 0.45f * Pace).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		AudioDirector.OneShot(this, "wendigo_step", 6, wp, 1f, "Events", 6f, 0.05f);
		await Cutscene.Wait(this, 0.45 * Pace, ct);
		AudioDirector.OneShot(this, "wendigo_step", 6, closer, 2f, "Events", 6f, 0.05f);
		await Cutscene.Tween(this, step, ct);
		FinaleLeanedIn = true;
		FinaleWendigo.Hold("crouch", 0.6f, 0.5 * Pace);   // (its head brought down to theirs)
		AudioDirector.OneShot(this, "wendigo_mimic", 14, FinaleWendigo.MouthWorld, -1f, "Voice", 4f, 0f);
		FinaleWendigo.Speak(1.8f);
		await Cutscene.Wait(this, 2.0 * Pace, ct);
		// then away from them, to the doors: up off the boards, drawn back, the arms cocked
		GD.Print("[story] Act 23: it turns from them to the doors");
		await TurnWendigo(ToGlobal(FrontDoorInside), 0.7f * Pace, ct);
		FinaleWendigo.Hold("smash", 0f, 0.6 * Pace);
		var drawBack = CreateTween();
		drawBack.TweenProperty(FinaleWendigo, "global_position", ToGlobal(FinaleWindup), 0.6f * Pace).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		AudioDirector.OneShot(this, "creature_snarl", 8, FinaleWendigo.MouthWorld, 1f, "Unnatural", 7f, 0.05f);
		await Cutscene.Tween(this, drawBack, ct);
		await Cutscene.Wait(this, 0.5 * Pace, ct);
		// the first blow: into the doors, shoulder and hands; they bow out against the snow, and hold. Its claws drag
		// down through them
		await Ram(false, ct);
		// back, and again: through them
		FinaleWendigo.Hold("smash", 0f, 0.5 * Pace);
		var again = CreateTween();
		again.TweenProperty(FinaleWendigo, "global_position", ToGlobal(FinaleWindup), 0.7f * Pace).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		await Cutscene.Tween(this, again, ct);
		await Cutscene.Wait(this, 0.35 * Pace, ct);
		await Ram(true, ct);
		// on through the doorway, across the porch, and off it into the storm
		FinaleWendigo.Play("pounce", 0.9f * Pace, 0.1);
		var outT = CreateTween();
		outT.TweenProperty(FinaleWendigo, "global_position", ToGlobal(new Vector3(0, 0.1f, Apothem + 0.8f)), 0.4f * Pace);
		outT.TweenProperty(FinaleWendigo, "global_position", ToGlobal(new Vector3(0.3f, 0.1f, Apothem + 4.9f)), 0.5f * Pace);
		for (int st = 0; st < 3; st++)
		{
			await Cutscene.Wait(this, 0.28 * Pace, ct);
			AudioDirector.OneShot(this, "wendigo_step", 6, FinaleWendigo.GlobalPosition, 2f, "Events", 6f, 0.05f);
		}
		await Cutscene.Tween(this, outT, ct);
		bool gone = false;
		FinaleWendigo.Leap(ToGlobal(new Vector3(3f, 3.5f, Apothem + 22f)), 1.0f * Pace, () => gone = true, arc: 1.6f);
		for (int w = 0; w < 50 && !gone; w++) await Cutscene.Wait(this, 0.05, ct);
		follow = false;
		GD.Print("[story] Act 23: through the doors, across the porch, and gone into the storm");
		await Cutscene.Wait(this, 1.4, ct);
		AudioDirector.OneShot(this, "wendigo_howl_far", 3, ToGlobal(new Vector3(-10f, 6f, Apothem + 60f)), 4f, "Unnatural", 30f, 0.03f);
		await Cutscene.Wait(this, 1.2, ct);
		// up off the floor
		player.SetCrouch(false);
		await Cutscene.Wait(this, 0.8, ct);
		// to the doorway, through it, and out onto the step: left, and right (from inside the doorway's depth a look to
		// the side only met its jamb)
		var at = ToGlobal(new Vector3(0, FloorY + 0.12f, Apothem + 0.95f));
		var go = player.CreateTween();
		go.TweenProperty(player, "global_position", at, 3.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await StoryBeat.PanTowards(this, player, ToGlobal(new Vector3(0, 1.5f, Apothem + 9f)), 1.2f, ct);
		await Cutscene.Tween(this, go, ct);
		await Cutscene.Wait(this, 0.6, ct);
		await StoryBeat.PanTowards(this, player, ToGlobal(new Vector3(-9f, 1.4f, Apothem + 3.2f)), 1.5f, ct);
		await Cutscene.Wait(this, 1.0, ct);
		await StoryBeat.PanTowards(this, player, ToGlobal(new Vector3(9f, 1.4f, Apothem + 3.2f)), 2.0f, ct);
		await Cutscene.Wait(this, 1.2, ct);
		FinaleDone = true;
		GD.Print("[story] Act 23: at the splintered doorway, looking left, and right - the end of Act 23");
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act23Finished);
		// on into Act 24: its tracks go off across the snow, to a hole in a drift (the maze is built, if it wasn't)
		EnsureAct24();
		_ = StoryBeat.Caption(this, "Its tracks go off across the snow.", 0.6f, 2.8f, 1f);
	}

	// (the finale's marks, lodge-local: the walk over the hall's west-back side (the west-front's was behind the
	// Christmas tree, seen from the doors), on it and on its rail; where they stumble back to, where they dive, where it
	// lands, where it draws back to strike the doors and where it strikes)
	private static readonly Vector3 N240 = new(-0.8660254f, 0, -0.5f), T240 = new(-0.5f, 0, 0.8660254f);
	private static Vector3 BalconyPerch => (N240 * 8.8f + T240 * 1.5f) with { Y = UpperY + 0.02f };
	private static Vector3 BalconyRail => (N240 * BalconyIn + T240 * 1.5f) with { Y = UpperY + 1.07f };
	private static readonly Vector3 FinaleRetreat = new(1.8f, FloorY, 7.0f), FinaleDive = new(3.1f, FloorY, 4.7f),
		FinaleLanding = new(0.5f, FloorY, 7.0f), FinaleWindup = new(0.15f, FloorY, 6.75f), FinaleImpact = new(0.1f, FloorY, 7.4f);

	/// <summary>For the tests: it came down in the hall (not straight out), and how many times it struck the doors.</summary>
	/// <summary>The wendigo's pace through all of it (the owner, 2026-10-04: "he needs to move like 25% faster"): every one
	/// of its moves and holds, from the speech on, takes this share of the time it was first made with.</summary>
	private const float Pace = 0.8f;

	public bool FinaleLanded { get; private set; }
	/// <summary>Act 24's maze and the hole down to it (built once the front doors are gone).</summary>
	public World.SnowMaze.Act24Maze Act24 { get; private set; }
	public void EnsureAct24() { if (Act24 == null || !IsInstanceValid(Act24)) Act24 = World.SnowMaze.Act24Maze.Build(this); }

	/// <summary>For tests: it came at them and leaned down into their face before it turned to the doors.</summary>
	public bool FinaleLeanedIn { get; private set; }
	public int FinaleRams { get; private set; }

	/// <summary>The wendigo turning on the spot to face <paramref name="target"/> (its whole crouched body, slowly).</summary>
	private async Task TurnWendigo(Vector3 target, float seconds, CancellationToken ct)
	{
		var w = FinaleWendigo;
		var d = (target - w.GlobalPosition) with { Y = 0 };
		if (d.LengthSquared() < 0.01f) return;
		var q0 = w.GlobalBasis.GetRotationQuaternion();
		var q1 = Basis.LookingAt(d.Normalized(), Vector3.Up).GetRotationQuaternion();
		var tw = CreateTween();
		tw.TweenMethod(Callable.From<float>(u => { if (IsInstanceValid(w)) w.GlobalBasis = new Basis(q0.Slerp(q1, u)); }), 0f, 1f, seconds)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await Cutscene.Tween(this, tw, ct);
	}

	/// <summary>A blow at the doors: from drawn back, into them (the "smash" clip: drawn back, frame 0; the shoulder and
	/// hands into the wood, 7; held, to 11; the claws dragged down through it, 20). The first, they bow out against the
	/// snow and hold, and it rakes them; the second, they go.</summary>
	private async Task Ram(bool burst, CancellationToken ct)
	{
		var w = FinaleWendigo;
		float len = (burst ? 1.0f : 1.5f) * Pace;
		w.Play("smash", len, 0.1);
		var go = CreateTween();
		go.TweenProperty(w, "global_position", ToGlobal(FinaleImpact), len * 7f / 20f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		await Cutscene.Tween(this, go, ct);
		FinaleRams++;
		var door = ToGlobal(FrontDoorInside);
		if (burst)
		{
			GD.Print("[story] Act 23: it strikes the doors again, and they go");
			ExplodeFrontDoor();
			return;
		}
		GD.Print("[story] Act 23: it strikes the doors; they bow out against the snow, and hold");
		AudioDirector.OneShot(this, "door_ram", 2, door, 8f, "Events", 10f, 0.03f);
		var bow = CreateTween().SetParallel();
		float r0 = FrontLeafR.Rotation.Y;
		bow.TweenProperty(FrontLeafR, "rotation:y", r0 + 0.22f, 0.1f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		bow.TweenProperty(FrontLeafL, "rotation:y", -0.14f, 0.1f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		bow.Chain().TweenProperty(FrontLeafR, "rotation:y", r0 + 0.07f, 0.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		bow.TweenProperty(FrontLeafL, "rotation:y", -0.04f, 0.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		Puff(FrontDoorInside + new Vector3(0, 1.45f, 0.1f), 40, new Vector3(1.3f, 0.1f, 0.1f));
		// held, then the claws dragged down through the wood
		await Cutscene.Wait(this, len * 4f / 20f, ct);
		AudioDirector.OneShot(this, "claw_rake", 2, door + Vector3.Down * 0.4f, 4f, "Events", 7f, 0.04f);
		await Cutscene.Wait(this, len * 9f / 20f + 0.2f, ct);
	}

	/// <summary>A puff of dust and snow shaken loose (lodge-local).</summary>
	private void Puff(Vector3 at, int amount, Vector3 extents)
	{
		AddChild(new GpuParticles3D
		{
			Name = "DoorPuff", Amount = amount, Lifetime = 2.0f, OneShot = true, Explosiveness = 0.9f, Emitting = true, Position = at,
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = extents,
				Direction = new Vector3(0, -0.3f, -1f), Spread = 50f, InitialVelocityMin = 0.3f, InitialVelocityMax = 1.2f, Gravity = new Vector3(0, -0.9f, 0), ScaleMin = 0.8f, ScaleMax = 2f,
			},
			DrawPass1 = new QuadMesh { Size = new Vector2(0.05f, 0.05f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.62f, 0.66f, 0.7f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles } },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	// (where the leaves end up: the left wrenched open to the jamb and hanging off its lower hinge; the right torn off
	// and flung out across the porch, lying flat on the stone, its inside face up)
	private static readonly Vector3 LeafLWreckRot = new(0, -1.42f, -0.06f);
	private static Transform3D LeafRWreck => new(new Basis(Vector3.Up, -0.25f) * new Basis(Vector3.Right, Mathf.Pi * 0.5f), new Vector3(1.9f, 0.13f, Apothem + 1.6f));

	/// <summary>It goes through the doors: the left leaf wrenched open to the jamb, hanging off its lower hinge; the
	/// right torn off and flung out across the porch, in a burst of splinters and snow; boards and the chain thrown
	/// across the floor; the storm in the doorway. (<paramref name="instant"/>: as it was left, on a load.)</summary>
	private void ExplodeFrontDoor(bool instant = false)
	{
		if (FrontBroken) return;
		FrontBroken = true;
		StoryManager.Instance?.SetFlag(LodgeFlag.FrontBroken);
		WreckLeaves(instant);
		if (FrontChain != null) FrontChain.Visible = false;
		if (PorchDoors != null) PorchDoors.Visible = false;
		if (IsInstanceValid(_frontBlock)) { _frontBlock.QueueFree(); _frontBlock = null; }
		if (FrontUse != null) FrontUse.Enabled = false;
		// what's left: splintered boards across the floor, in and out
		var k = new MeshKit();
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Color = Colors.White;
		var rng = new RandomNumberGenerator { Seed = 2399 };
		for (int i = 0; i < 16; i++)
		{
			var p = new Vector3(rng.RandfRange(-2.4f, 2.4f), FloorY + 0.03f, HexIn + rng.RandfRange(-2.5f, 4f));
			k.Box(p, new Vector3(rng.RandfRange(0.08f, 0.2f), 0.05f, rng.RandfRange(0.4f, 1.3f)), 1f, new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Pi)));
		}
		// splintered stumps of the torn-off leaf still on its hinges
		for (int j = 0; j < 4; j++)
			k.Box(new Vector3(1.26f - rng.RandfRange(0f, 0.15f), FloorY + 0.3f + j * 0.75f, HexIn - 0.03f), new Vector3(0.12f, rng.RandfRange(0.4f, 0.7f), 0.06f), 1f);
		k.CommitTo(this, "FrontDoorWreck", false);
		if (instant) return;
		AudioDirector.OneShot(this, "door_explode", 1, ToGlobal(FrontDoorInside), 7.5f, "Events", 10f, 0.02f);
		AudioDirector.OneShot(this, "door_ram_burst", 1, ToGlobal(FrontDoorInside), 8f, "Events", 10f, 0.02f);
		var burst = new GpuParticles3D
		{
			Name = "DoorSplinters", Amount = 90, Lifetime = 1.8f, OneShot = true, Explosiveness = 0.95f, Emitting = true, Position = FrontDoorInside,
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(1.3f, 1.5f, 0.1f),
				Direction = Vector3.Back, Spread = 55f, InitialVelocityMin = 3f, InitialVelocityMax = 9f, Gravity = new Vector3(0, -9f, 0), ScaleMin = 0.6f, ScaleMax = 2f,
			},
			DrawPass1 = new BoxMesh { Size = new Vector3(0.04f, 0.02f, 0.18f), Material = LodgeTextures.DarkWoodMat },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(burst);
		var snow = new GpuParticles3D
		{
			Name = "DoorSnow", Amount = 120, Lifetime = 2.4f, OneShot = true, Explosiveness = 0.8f, Emitting = true, Position = FrontDoorInside + new Vector3(0, 0, 0.6f),
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(1.4f, 1.4f, 0.4f),
				Direction = new Vector3(0, 0.2f, -1f), Spread = 45f, InitialVelocityMin = 0.8f, InitialVelocityMax = 2.6f, Gravity = new Vector3(0, -0.8f, 0), ScaleMin = 0.8f, ScaleMax = 2f,
			},
			DrawPass1 = new QuadMesh { Size = new Vector2(0.05f, 0.05f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.88f, 0.94f, 0.85f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles } },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(snow);
		GD.Print("[story] Act 23: it goes through the front doors and out - the doors gone");
	}

	/// <summary>The leaves, broken: straight to where they ended up, or thrown there (the left slammed open to the jamb,
	/// bouncing back off it and sagging on its last hinge; the right ripped off, tipping over outward as it flies, down
	/// flat on the porch's stone with a slap and a skid).</summary>
	private void WreckLeaves(bool instant)
	{
		if (FrontLeafL == null || FrontLeafR == null) return;
		var endR = LeafRWreck;
		if (instant)
		{
			FrontLeafL.Rotation = LeafLWreckRot;
			FrontLeafL.Position = FrontLeafL.Position with { Y = FloorY + 0.08f };
			FrontLeafR.Transform = endR;
			return;
		}
		var l = CreateTween();
		l.TweenProperty(FrontLeafL, "rotation:y", -1.5f, 0.12f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		l.TweenProperty(FrontLeafL, "rotation:y", -1.12f, 0.3f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		l.TweenProperty(FrontLeafL, "rotation", LeafLWreckRot, 0.7f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		l.Parallel().TweenProperty(FrontLeafL, "position:y", FloorY + 0.08f, 0.7f);
		var start = FrontLeafR.Transform;
		var q0 = start.Basis.GetRotationQuaternion();
		var q1 = endR.Basis.GetRotationQuaternion();
		var short_ = endR.Origin + new Vector3(0, 0, -0.35f);
		var r = CreateTween();
		r.TweenMethod(Callable.From<float>(u =>
		{
			float tip = Mathf.Pow(u, 1.6f);   // (it goes out first, then over)
			FrontLeafR.Transform = new Transform3D(new Basis(q0.Slerp(q1, tip)), start.Origin.Lerp(short_, u) + Vector3.Up * (4f * u * (1f - u) * 0.8f));
		}), 0f, 1f, 0.7f);
		r.TweenCallback(Callable.From(() => AudioDirector.OneShot(this, "door_slam", 2, ToGlobal(endR.Origin + new Vector3(-0.6f, 0, 1.5f)), 4f, "Events", 8f, 0.06f)));
		r.TweenProperty(FrontLeafR, "position", endR.Origin, 0.3f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
	}
}
