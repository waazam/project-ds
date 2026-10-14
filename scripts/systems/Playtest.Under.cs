using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.UI;
using ProjectDS.World;
using ProjectDS.World.StationParts;

namespace ProjectDS.Systems;

/// <summary>The station and everything under it: Acts 13-21.</summary>
public partial class Playtest
{
	// ------------------------------------------------------------------ Act 13

	private async Task Act13a(CancellationToken ct)
	{
		await Control(ct, 40);
		var station = StationInterior.Instance;
		Require("the station", station != null);
		var basement = station.Basement;
		var room1 = station.Room1;
		Check("inside the lobby, kept and tidy", station.InLobby(Player.GlobalPosition) && station.Stage == 0, $"stage {station.Stage}");
		await _bot.Inside(station.ToGlobal(new Vector3(StationInterior.EntryGapX + 0.5f, 0, -StationInterior.HalfDepth + 1.0f)), station.ToGlobal(new Vector3(StationInterior.EntryGapX, 1.2f, -StationInterior.HalfDepth)), ct);
		Check("the front door won't open behind them", _bot.Prompt == "It won't open." || Player.Interaction?.Focused != null, $"'{_bot.Prompt}'");
		var knife = _bot.AllOf<Pickup>().FirstOrDefault(p => p.Kind == ToolKind.Knife && !p.Taken);
		Require("the knife stuck in the desk", knife != null);
		await _bot.UseIt(knife, ct);
		Check("the knife in hand", Inv.HasTool(ToolKind.Knife));
		// the tape over the basement door: the cut close-up
		var tapeUse = basement.GetNode<Interactable>("Door/Use");
		await _bot.Inside(station.ToGlobal(new Vector3(StationInterior.BasementGapX, 0, -StationInterior.HalfDepth + 1.5f)), tapeUse.GlobalPosition, ct);
		await _bot.UseIt(tapeUse, ct);
		Check("cut the tape: the close-up opens", TapeCutOverlay.Instance is { IsOpen: true });
		for (int i = 0; i < 40 && TapeCutOverlay.Instance is { IsOpen: true }; i++) { TapeCutOverlay.Instance.TestCompleteCut(); await _bot.Frames(2, ct); }
		Check("the tape cut, the door open", basement.TapeCut);
		await _bot.Seconds(0.8, ct);
		await _bot.Inside(basement.ToGlobal(new Vector3(0, -0.2f, -1.6f)), basement.StairFootWorld, ct);
		await _bot.WalkTo(basement.StairFootWorld, 0.8f, ct, giveUp: 12f);
		using (Fast(3)) await _bot.WaitUntil(() => basement.LightsDead, 16, ct);
		Check("down the stairs the lights struggle and die", basement.LightsDead);
		var wheel = basement.GetNode<Interactable>("Wheel/Use");
		using (Fast(2))
			for (int i = 0; i < 3 && !basement.Drained; i++) { await _bot.UseIt(wheel, ct); await _bot.Seconds(1.8, ct); }
		Check("the valve wheel turned three times", basement.Turns >= 3, $"{basement.Turns}");
		using (Fast(3)) await _bot.WaitUntil(() => basement.Drained, 14, ct);
		Check("the water drains, a dead eye in the grate", basement.Drained && basement.Eye is { Visible: true });
		using (Fast(3)) await _bot.WaitUntil(() => basement.ClockBroken, 14, ct);
		Check("the drowned clock chimes, spits a key, breaks", basement.ClockBroken);
		Shot("basement");
		var key = _bot.AllOf<Pickup>().FirstOrDefault(p => p.Kind == ToolKind.Key && !p.Taken);
		Require("the clock's key on the floor", key != null);
		Vector3 kp = key.GlobalPosition;
		Vector3 stand = kp + TestBot.Flat(basement.RoomCentreWorld - kp).Normalized() * 1.2f;
		stand.Y = basement.GlobalPosition.Y + StationBasement.Floor + 0.02f;
		await _bot.Inside(stand, kp, ct);
		await _bot.UseIt(key, ct);
		Check("the key in hand", Inv.HasTool(ToolKind.Key));
		// Room 1
		await _bot.Inside(station.ToGlobal(new Vector3(StationInterior.HalfWidth - 1.6f, 0, 0.3f)), station.ToGlobal(new Vector3(StationInterior.HalfWidth, 1f, 0)), ct);
		Check("the lobby has rotted a stage", station.Stage == 1, $"stage {station.Stage}");
		var door1 = station.GetNode<StationDoor>("Room1Door");
		await _bot.UseIt(door1.GetNode<Interactable>("Use"), ct);
		Check("the key opens Room 1", door1 is { Locked: false, IsOpen: true });
		await _bot.Seconds(0.8, ct);
		var boxUse = room1.GetNode<Interactable>("CigarBox/Use");
		await _bot.Inside(room1.ToGlobal(new Vector3(-1.2f, 0, 0.2f)), boxUse.GlobalPosition, ct);
		Shot("room1");
		await _bot.UseIt(boxUse, ct);
		for (int i = 0; i < 40 && TapeCutOverlay.Instance is { IsOpen: true }; i++) { TapeCutOverlay.Instance.TestCompleteCut(); await _bot.Frames(2, ct); }
		Check("the taped cigar box cut open on three sides: a button", room1.BoxOpen && room1.BoxCutsDone == 3, $"{room1.BoxCutsDone} cuts");
		await _bot.Seconds(0.8, ct);
		var button = room1.GetNodeOrNull<Interactable>("CigarBox/Button/Press");
		if (button != null) await _bot.UseIt(button, ct);
		Check("the button: the writing melts, saved (the Room 1 checkpoint)", room1.Solved && Story.Current == Checkpoint.Act13Room1Solved, $"{Story.Current}");
	}

	private async Task Act13b(CancellationToken ct)
	{
		await Control(ct, 40);
		var station = StationInterior.Instance;
		Require("the station", station != null);
		var room1 = station.Room1; var room2 = station.Room2; var door3 = station.Door3; var basement = station.Basement;
		var room2Door = station.Room2Door;
		if (Phase > 0)
			Check("drowned in Room 2: back at the Room 1 save, the death counted", Story.Current == Checkpoint.Act13Room1Solved && station.InLobby(Player.GlobalPosition) && PlayerDeath.Deaths >= 1, $"{Story.Current}");
		await _bot.WaitUntil(() => !room2Door.Locked, 5, ct);
		Check("Room 2's door stands open", !room2Door.Locked);
		await _bot.Inside(station.ToGlobal(new Vector3(-StationInterior.HalfWidth + 1.4f, 0, 0.3f)), room2.GlobalPosition, ct);
		await _bot.WalkTo(room2.ToGlobal(new Vector3(1.2f, 0, 0.9f)), 0.6f, ct, giveUp: 8f);
		await _bot.WaitUntil(() => room2.DoorShut, 3, ct);
		Check("in: the door slams behind them", room2.DoorShut && room2Door.Locked);
		await _bot.UseIt(room2.BoxUse, ct);
		using (Fast(2)) await _bot.WaitUntil(() => room2.Flooding, 8, ct);
		Check("the box: a tentacle breaks the window, the lake comes in", room2.WindowBroken && room2.Flooding);
		await Control(ct, 10);
		if (Phase == 0)
		{
			// leave it: the room fills, and drowns them
			_s.ExpectReload = true;
			using (Fast(6))
			{
				await _bot.WaitUntil(() => room2.Blood, 30, ct);
				Check("at the knees the water turns to blood", room2.Blood);
				await _bot.WaitUntil(() => PlayerDeath.Dying, 40, ct);
				Check("the room fills: drowned", PlayerDeath.Dying);
			}
			await ExpectReload(ct, 40);
			return;
		}
		// the cryptex: six rings to STAIRS
		await _bot.UseIt(room2.BoxUse, ct);
		Check("the cryptex close-up opens", CryptexOverlay.Instance is { IsOpen: true });
		var box = room2.Box;
		int stuck = 0;
		for (int r = 0; r < Cryptex.Rings && !box.Solved; r++)
		{
			box.Select(r);
			int guard = 0;
			while (box.Letters[r] != Cryptex.Word[r] - 'A' && guard++ < 60)
			{
				int diff = ((Cryptex.Word[r] - 'A') - box.Letters[r] + 26) % 26;
				if (!box.Turn(diff <= 13 ? 1 : -1)) { stuck++; await _bot.Seconds(0.35, ct); }
				await _bot.Frames(2, ct);
			}
		}
		Check("the cryptex spells STAIRS", box.Solved, box.Reading);
		using (Fast(3)) await _bot.WaitUntil(() => room2.Solved, 12, ct);
		Check("the blood goes back out of the window", room2.Solved && room2.Level < 0.05f, $"level {room2.Level:0.00}");
		await Control(ct, 8);
		var lighter = _bot.AllOf<Pickup>().FirstOrDefault(p => p.Kind == ToolKind.Lighter && !p.Taken);
		if (lighter != null) await _bot.UseIt(lighter, ct);
		Check("the cryptex held a lighter: in hand", Inv.HasTool(ToolKind.Lighter));
		// the web over the door behind the desk: burned
		await _bot.Inside(station.ToGlobal(new Vector3(-1.4f, 0, 3.8f)), door3.GlobalPosition + Vector3.Up * 1.2f, ct);
		await _bot.UseIt(door3.WebUse, ct);
		using (Fast(2)) await _bot.WaitUntil(() => Story.HasFlag(StoryManager.Flag.StationWebBurned), 8, ct);
		Check("the lighter burns the web away", door3.WebBurned);
		await _bot.Inside(station.ToGlobal(new Vector3(0.6f, 0, 3.8f)), door3.GlobalPosition + Vector3.Up * 1.3f, ct);
		await _bot.WaitUntil(() => door3.Seen, 3, ct);
		Check("an iron door: LOOK, TOUCH, CLIMB", door3.Seen);
		Shot("iron_door");
		// CLIMB: the staircase grown in Room 2
		await _bot.Inside(room2.ToGlobal(new Vector3(1.6f, 0, 1.8f)), room2.StairTopWorld, ct);
		Check("a staircase has grown in Room 2", room2.StairsUp);
		await _bot.WalkTo(room2.StairFootWorld, 0.5f, ct, giveUp: 6f);
		await _bot.WalkTo(room2.StairTopWorld, 0.4f, ct, () => Story.HasFlag(StoryManager.Flag.StationStepTaken), giveUp: 10f);
		using (Fast(2)) await _bot.WaitUntil(() => Story.HasFlag(StoryManager.Flag.StationStepTaken), 8, ct);
		Check("climbed: one of its steps in hand", Inv.HasTool(ToolKind.StairTread));
		await Control(ct, 8);
		// TOUCH: the wrong puddle grabs, the right one gives the hand
		await _bot.Inside(room1.ToGlobal(new Vector3(-1.4f, 0, 0f)), room1.Puddles[0].GlobalPosition, ct);
		await _bot.UseIt(room1.Puddles[0], ct);
		using (Fast(2)) await _bot.Seconds(3.5, ct);
		Check("the wrong puddle: something takes the wrist and lets go", room1.WrongReaches == 1 && !Inv.HasTool(ToolKind.PaleHand));
		await _bot.UseIt(room1.Puddles[1], ct);
		await _bot.Seconds(1.0, ct);
		Check("the right one: the pale hand", Inv.HasTool(ToolKind.PaleHand));
		// LOOK: the eye loose in the dark basement, caught looking
		await _bot.Inside(basement.StairFootWorld, basement.RoomCentreWorld, ct);
		var eye = basement.Eye;
		Check("the eye is loose now", eye.Wandering);
		await _bot.Aim(eye.GlobalPosition, ct);
		Player.CameraRig.SnapBehind(Player.CameraRig.Yaw + Mathf.Pi);
		using (Fast(2)) await _bot.Seconds(5, ct);
		Check("unwatched, it moves", eye.Hops > 0, $"{eye.Hops} hops");
		Vector3 at = eye.GlobalPosition;
		Vector3 near = at + TestBot.Flat(basement.RoomCentreWorld - at).Normalized() * 1.4f;
		await _bot.Inside(near with { Y = basement.GlobalPosition.Y + StationBasement.Floor + 0.05f }, at, ct);
		await _bot.Aim(eye.GlobalPosition, ct);
		var take = eye.GetNode<Interactable>("Take");
		await _bot.WaitUntil(() => take.Enabled, 2, ct);
		await _bot.UseIt(take, ct);
		Check("caught looking: the eye", Inv.HasTool(ToolKind.DeadEye));
		// all three set in the iron door
		await _bot.Inside(station.ToGlobal(new Vector3(0.6f, 0, 3.8f)), door3.GlobalPosition + Vector3.Up * 1.3f, ct);
		for (int i = 0; i < 3 && !door3.Opened; i++) { await _bot.UseIt(door3.DoorUse, ct); await _bot.Seconds(0.8, ct); }
		using (Fast(2)) await _bot.WaitUntil(() => door3.Opened, 6, ct);
		Check("the eye, the hand, the step set: the iron door opens", door3.Opened && door3.PiecesSet == 3, $"{door3.PiecesSet} set");
		using (Fast(2)) await _bot.Seconds(3, ct);
		var room3 = station.Room3;
		await _bot.Inside(station.ToGlobal(new Vector3(0, 0, StationInterior.HalfDepth - 1.2f)), room3.EntryWorld, ct);
		await _bot.WalkTo(room3.ToGlobal(new Vector3(0, 0, 2.2f)), 0.6f, ct, () => Story.Current >= Checkpoint.Act13Finished, giveUp: 8f);
		await _bot.WaitUntil(() => Story.Current >= Checkpoint.Act13Finished, 5, ct);
		Check("through it into Room 3: Act 13's end (checkpoint 13)", Story.Current == Checkpoint.Act13Finished && room3.Entered && station.Stage == 4, $"{Story.Current}, stage {station.Stage}");
	}

	// ------------------------------------------------------------------ Act 14

	private int CornerOf(Stairwell sw) => Mathf.Max(0, Mathf.FloorToInt((Stairwell.Y0 - sw.ToLocal(Player.GlobalPosition).Y) / Stairwell.FlightDrop + 0.25f));

	/// <summary>Walks the stairwell corner to corner down to <paramref name="toCorner"/>.</summary>
	private async Task<bool> DescendTo(Stairwell sw, int toCorner, CancellationToken ct)
	{
		int stuck = 0;
		for (int guard = 0; guard < 200; guard++)
		{
			int k = Mathf.Min(CornerOf(sw), toCorner);
			if (k >= toCorner && Player.GlobalPosition.DistanceTo(sw.CornerWorld(toCorner)) < 0.9f) return true;
			int next = Mathf.Min(k + 1, toCorner);
			float y = Player.GlobalPosition.Y;
			await _bot.WalkTo(sw.CornerWorld(next), 0.45f, ct, giveUp: 6f);
			if (Mathf.Abs(Player.GlobalPosition.Y - y) < 0.05f && Player.GlobalPosition.DistanceTo(sw.CornerWorld(next)) > 0.9f) { if (++stuck > 3) return false; }
			else stuck = 0;
		}
		return false;
	}

	private async Task Act14(CancellationToken ct)
	{
		await Control(ct, 40);
		var sw = StationInterior.Instance?.Room3?.Stairs;
		Require("Room 3 and its stairwell", sw != null);
		if (Phase > 0)
		{
			// Continued from Act 14's end (the jump across): back up, and down the other way
			Check("Continue from Act 14's end: on the chamber floor under the shaft", Story.Current == Checkpoint.Act14Finished && Player.GlobalPosition.DistanceTo(sw.LandingSpotWorld) < 2f, $"{Story.Current}");
			await _bot.Inside(sw.CornerWorld(sw.GapCorner - 2), sw.CornerWorld(sw.GapCorner - 1), ct);
			await DescendTo(sw, sw.GapCorner, ct);
			await _bot.WalkTo(sw.GapEdgeWorld, 0.3f, ct, () => sw.Choosing, giveUp: 4f);
			await _bot.WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
			Check("at the edge again: the question", ChoicePrompt.Instance is { IsOpen: true });
			ChoicePrompt.Instance?.TestChoose(1);
			using (Fast(3)) await _bot.WaitUntil(() => sw.StoodUp, 20, ct);
			Check("jumped down: on their feet in the chamber, remembered", sw.Jumped && !sw.JumpedAcross && sw.StoodUp && Story.HasFlag(StoryManager.Flag.Act14JumpedDown));
			return;
		}
		Check("the compass is dead down here", _bot.FirstOf<Compass>() is { Dead: true } && Story.ObjectivePosition == null);
		Check("Room 3: the walls hung with burnt-out portraits", sw.GetParent() is StationRoom3 { Portraits: >= 20 });
		// into the trench, onto the top landing
		await _bot.WalkTo(sw.TrenchTopWorld, 0.6f, ct, giveUp: 12f);
		await _bot.WalkTo(sw.CornerWorld(0), 0.5f, ct, giveUp: 8f);
		Check("down the steps to the top landing", Player.GlobalPosition.DistanceTo(sw.CornerWorld(0)) < 1f);
		await DescendTo(sw, 3, ct);
		Check("walking down: turn by turn, lower each turn", Player.GlobalPosition.Y < sw.CornerWorld(0).Y - 1f, $"{sw.CornerWorld(0).Y - Player.GlobalPosition.Y:0.0} m down");
		// jumped a quarter of the way down: the flame dies there
		int quarter = sw.Revolutions / 4 * 4;
		await _bot.Inside(sw.CornerWorld(quarter - 2), sw.CornerWorld(quarter - 1), ct);
		await DescendTo(sw, quarter + 3, ct);
		await _bot.Seconds(0.5, ct);
		Check("a quarter of the way down the lantern's flame gutters out, saved", Lantern.FlameDead && Story.HasFlag(StoryManager.Flag.LanternFlameDead));
		var lamp = Player.GetNodeOrNull<Lantern>("Lantern");
		await _bot.LanternMode(false, ct);
		await _bot.Tap(on => Input.ScriptedLight = on, ct);
		await _bot.Tap(on => Input.ScriptedLight = on, ct);
		Check("F won't light it again", lamp is { Shining: false });
		await _bot.LanternMode(true, ct);
		Check("the blacklight still works", lamp is { Blacklight: true, Shining: true });
		await _bot.LanternMode(false, ct);
		// on the run: nothing taken back
		int revBefore = sw.PlayerRev;
		float y0 = Player.GlobalPosition.Y, yPrev = y0, worstJump = 0f;
		Input.ScriptedRun = true;
		int k = CornerOf(sw);
		double t = 0;
		while (t < 3)
		{
			var d = TestBot.Flat(sw.CornerWorld(k + 1) - Player.GlobalPosition);
			if (d.Length() < 0.5f) { k++; continue; }
			Player.CameraRig.SnapBehind(Mathf.Atan2(-d.X, -d.Z));
			Input.ScriptedMove = new Vector2(0, 1);
			await _bot.Frames(1, ct);
			t += GetProcessDeltaTime();
			worstJump = Mathf.Max(worstJump, Player.GlobalPosition.Y - yPrev);
			yPrev = Player.GlobalPosition.Y;
		}
		_bot.Release();
		float yNow = Player.GlobalPosition.Y;
		Check("running down the stairs: further down, never jumped back up", sw.PlayerRev >= revBefore && yNow < y0 - 0.5f && worstJump < 0.6f, $"turn {revBefore} -> {sw.PlayerRev}, {y0 - yNow:0.0} m down, biggest rise in a frame {worstJump:0.00} m");
		// jumped to the bottom: the fallen flight, the question
		await _bot.Inside(sw.CornerWorld(sw.GapCorner - 2), sw.CornerWorld(sw.GapCorner - 1), ct);
		await DescendTo(sw, sw.GapCorner, ct);
		Check("the crawler came down after them", sw.Crawler != null);
		await _bot.WalkTo(sw.GapEdgeWorld, 0.3f, ct, () => sw.Choosing, giveUp: 4f);
		await _bot.WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		Require("at the edge: jump across? or jump down?", ChoicePrompt.Instance is { IsOpen: true } && sw.Choosing);
		Shot("the_choice");
		ChoicePrompt.Instance?.TestChoose(-1);
		await _bot.Seconds(1.0, ct);
		Check("stepping back lets go of the question", !sw.Choosing && Input.Enabled);
		await _bot.Seconds(1.5, ct);
		await _bot.WalkTo(sw.GapEdgeWorld, 0.3f, ct, () => sw.Choosing, giveUp: 4f);
		await _bot.WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		ChoicePrompt.Instance?.TestChoose(0);
		using (Fast(3))
		{
			await _bot.WaitUntil(() => sw.AcrossFell, 10, ct);
			Check("jumped across: the far landing holds, then goes", sw.JumpedAcross && sw.AcrossFell);
			await _bot.WaitUntil(() => sw.StoodUp, 30, ct);
		}
		Check("came to on the floor, got up: Act 14's end saved", sw.StoodUp && Story.Current == Checkpoint.Act14Finished && Story.HasFlag(StoryManager.Flag.Act14JumpedAcross), $"{Story.Current}");
		// Continue from here, and the other way down
		_s.ExpectReload = true;
		Story.ContinueGame();
		await ExpectReload(ct, 30);
	}

	// ------------------------------------------------------------------ Act 15

	/// <summary>Down the hall toward local z <paramref name="toZ"/>, moving only in the green if <paramref name="obey"/>.</summary>
	private async Task<bool> HallWalk(Act15Hallway hw, float toZ, bool obey, CancellationToken ct, double giveUp = 300)
	{
		double t = 0;
		try
		{
			while (hw.PlayerZ < toZ)
			{
				ct.ThrowIfCancellationRequested();
				bool go = Input.Enabled && (!obey || hw.State is Act15Hallway.Phase.Waiting or Act15Hallway.Phase.Green);
				if (go)
				{
					Vector3 target = hw.AlongWorld(Mathf.Min(hw.PlayerZ + 6f, toZ + 0.5f));
					Vector3 sl = hw.ToLocal(hw.Shadow.GlobalPosition);
					if (sl.Z > hw.PlayerZ - 1f && sl.Z < hw.PlayerZ + 6f && Mathf.Abs(sl.X) < 1.2f)
						target = hw.ToGlobal(new Vector3(sl.X > 0 ? -1.2f : 1.2f, 0.05f, Mathf.Min(hw.PlayerZ + 3f, toZ + 0.5f)));
					_bot.Steer(target);
					Input.ScriptedMove = new Vector2(0, 1);
				}
				else Input.ScriptedMove = Vector2.Zero;
				await _bot.Frames(1, ct);
				t += GetProcessDeltaTime();
				if (t > giveUp) return false;
			}
			return true;
		}
		finally { _bot.Release(); }
	}

	private async Task Act15(CancellationToken ct)
	{
		await Control(ct, 40);
		var sw = StationInterior.Instance?.Room3?.Stairs;
		await _bot.WaitUntil(() => sw?.Hallway?.Shadow != null, 10, ct);
		var hw = sw?.Hallway;
		Require("the long hallway", hw != null);
		if (Phase == 0)
		{
			Check("Act 15 starts on the chamber floor", Story.Current == Checkpoint.Act14Finished && Player.GlobalPosition.DistanceTo(sw.LandingSpotWorld) < 2.5f);
			await _bot.WalkTo(sw.ChamberExitWorld, 0.6f, ct, giveUp: 10f);
			// jumped down the narrow stretch to a few metres short of him; walked past him
			await _bot.Inside(hw.AlongWorld(Act15Hallway.ShadowZ - 8f), hw.AlongWorld(Act15Hallway.ShadowZ), ct);
			Check("he stands frozen in the green", hw.State == Act15Hallway.Phase.Waiting);
			await HallWalk(hw, Act15Hallway.ShadowZ + 3f, false, ct, 30);
			Check("past him: red light, green light begins, a save", hw.State != Act15Hallway.Phase.Waiting && Story.HasFlag(StoryManager.Flag.Act15PastShadow));
			// walking on into the red: taken
			_s.ExpectReload = true;
			using (Fast(3))
			{
				_ = HallWalk(hw, Act15Hallway.End - 2f, false, ct, 120);
				await _bot.WaitUntil(() => hw.State == Act15Hallway.Phase.Red, 40, ct);
				Check("red: the lights change, he's behind them", hw.State == Act15Hallway.Phase.Red);
				await _bot.WaitUntil(() => PlayerDeath.Dying, 8, ct);
				Check("moving in the red: taken", PlayerDeath.Dying);
			}
			await ExpectReload(ct, 40);
			return;
		}
		Check("taken: back just past him (its save), the death counted", Story.HasFlag(StoryManager.Flag.Act15PastShadow) && Mathf.Abs(hw.ToLocal(Player.GlobalPosition).Z - (Act15Hallway.ShadowZ + 2.5f)) < 2.5f && PlayerDeath.Deaths >= 1,
			$"z {hw.ToLocal(Player.GlobalPosition).Z:0.0}");
		// the game, played: walk in the green, still in the red
		int reds0 = hw.Reds;
		using (Fast(3))
		{
			await HallWalkUntil(hw, Act15Hallway.End, () => hw.Reds >= reds0 + 1 && hw.State == Act15Hallway.Phase.Green, ct, 90);
			Check("a red stood still through (he comes up behind them, a jump at a time), alive", hw.Reds >= reds0 + 1 && !PlayerDeath.Dying, $"{hw.Reds - reds0} reds");
		}
		// jumped to the far half (he's in front there): one red in front, then the door
		await _bot.WaitUntil(() => hw.State == Act15Hallway.Phase.Green, 30, ct);
		await _bot.Inside(hw.AlongWorld(Act15Hallway.FrontFromZ + 12f), hw.AlongWorld(Act15Hallway.End), ct);
		int front0 = hw.FrontReds;
		using (Fast(3))
		{
			await HallWalkUntil(hw, Act15Hallway.End - Act15Hallway.DoorKeepClear - 4f, () => hw.FrontReds > front0 && hw.State == Act15Hallway.Phase.Green, ct, 120);
			Check("from halfway the reds put him in front of them: stand still, go round", hw.FrontReds > front0 && !PlayerDeath.Dying, $"{hw.FrontReds - front0} in front");
			await _bot.WaitUntil(() => hw.State == Act15Hallway.Phase.Green, 30, ct);
		}
		await _bot.Inside(hw.AlongWorld(Act15Hallway.End - 5f), hw.DoorWorld, ct);
		await HallWalk(hw, Act15Hallway.End - 1.2f, true, ct, 60);
		while (hw.State is Act15Hallway.Phase.Red or Act15Hallway.Phase.Flicker) await _bot.Frames(1, ct);
		await _bot.UseIt(hw.GetNode<Interactable>("ClosetDoor/Use"), ct);
		await _bot.WaitUntil(() => hw.DoorOpen, 3, ct);
		Check("the closet door at the end opens", hw.DoorOpen);
		await _bot.Seconds(1.2, ct);
		await _bot.WalkTo(hw.ClosetWorld, 0.4f, ct, () => hw.InCloset, giveUp: 10f);
		await _bot.WaitUntil(() => hw.Finished, 5, ct);
		Check("into the closet, the door shut behind: checkpoint 15", hw.Finished && Story.Current == Checkpoint.Act15Finished, $"{Story.Current}");
	}

	/// <summary>Obeying the lights toward <paramref name="toZ"/> until <paramref name="until"/>.</summary>
	private async Task HallWalkUntil(Act15Hallway hw, float toZ, System.Func<bool> until, CancellationToken ct, double giveUp)
	{
		double t = 0;
		try
		{
			while (!until() && t < giveUp)
			{
				ct.ThrowIfCancellationRequested();
				bool go = Input.Enabled && hw.State is Act15Hallway.Phase.Waiting or Act15Hallway.Phase.Green && hw.PlayerZ < toZ;
				if (go)
				{
					Vector3 target = hw.AlongWorld(Mathf.Min(hw.PlayerZ + 6f, toZ + 0.5f));
					Vector3 sl = hw.ToLocal(hw.Shadow.GlobalPosition);
					if (sl.Z > hw.PlayerZ - 1f && sl.Z < hw.PlayerZ + 6f && Mathf.Abs(sl.X) < 1.2f)
						target = hw.ToGlobal(new Vector3(sl.X > 0 ? -1.2f : 1.2f, 0.05f, Mathf.Min(hw.PlayerZ + 3f, toZ + 0.5f)));
					_bot.Steer(target);
					Input.ScriptedMove = new Vector2(0, 1);
				}
				else Input.ScriptedMove = Vector2.Zero;
				await _bot.Frames(1, ct);
				t += GetProcessDeltaTime();
			}
		}
		finally { _bot.Release(); }
	}

	// ------------------------------------------------------------------ Acts 16-17

	private async Task Act16(CancellationToken ct)
	{
		await Control(ct, 40);
		var hw = StationInterior.Instance?.Room3?.Stairs?.Hallway;
		await _bot.WaitUntil(() => hw?.SwitchUse != null, 10, ct);
		Require("the closet", hw?.SwitchUse != null);
		Check("Act 16 starts shut in the closet", Story.Current == Checkpoint.Act15Finished && Player.GlobalPosition.DistanceTo(hw.ClosetWorld) < 1.5f);
		await _bot.UseIt(hw.InsideDoorUse, ct);
		await _bot.Seconds(0.4, ct);
		Check("in the dark the door won't open", !hw.ClosetDoorOpen);
		await _bot.UseIt(hw.SwitchUse, ct);
		Check("the light switch", hw.LightsOn);
		using (Fast(3)) await _bot.WaitUntil(() => hw.LightsSettled, 8, ct);
		Check("red, green, then white", hw.LightsSettled);
		Shot("closet");
		await _bot.UseIt(hw.InsideDoorUse, ct);
		Check("with the light on the door opens: black beyond", hw.ClosetDoorOpen);
		await _bot.Seconds(1.2, ct);
		var sewer = StationInterior.Instance.Sewer;
		await _bot.WalkTo(hw.DoorWorld, 0.3f, ct, () => hw.Through, giveUp: 6f);
		await _bot.WaitUntil(() => Story.Current == Checkpoint.Act16Finished && Input.Enabled, 10, ct);
		Check("through the black into the sewer: checkpoint 16", Story.Current == Checkpoint.Act16Finished && Player.GlobalPosition.DistanceTo(sewer.EntranceWorld) < 2f, $"{Story.Current}");
	}

	private async Task Act17(CancellationToken ct)
	{
		await Control(ct, 40);
		var sewer = StationInterior.Instance?.Sewer;
		await _bot.WaitUntil(() => sewer?.HoleUse != null, 10, ct);
		Require("the sewer", sewer?.HoleUse != null);
		await _bot.WalkTo(sewer.AlongWorld(5f), 0.5f, ct, giveUp: 8f);
		await _bot.Seconds(0.8, ct);
		Check("ankle-deep: wading, dragged", sewer.PlayerWading && Player.WadeScale < 0.75f, $"scale {Player.WadeScale:0.00}");
		// jumped down the pipe, the platform's last step walked
		await _bot.Inside(sewer.TunnelEndWorld, sewer.PlatformEdgeWorld, ct);
		await _bot.WalkTo(sewer.PlatformEdgeWorld, 0.5f, ct, giveUp: 20f);
		Check("up onto the platform out of the water", !sewer.PlayerWading);
		await _bot.Aim(sewer.HoleWorld, ct);
		Shot("the_hole");
		await _bot.UseIt(sewer.HoleUse, ct);
		await _bot.WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		Check("\"Drop down into the hole?\"", ChoicePrompt.Instance is { IsOpen: true } && ChoicePrompt.Instance.Question.StartsWith("Drop down"), ChoicePrompt.Instance?.Question);
		ChoicePrompt.Instance?.TestChoose(1);
		using (Fast(2)) await _bot.Seconds(5.0, ct);
		Check("no: the whispers come round the room", sewer.Noes == 1 && sewer.Whispers >= 3, $"{sewer.Whispers}");
		await _bot.UseIt(sewer.HoleUse, ct);
		await _bot.WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		ChoicePrompt.Instance?.TestChoose(0);
		await _bot.Seconds(0.6, ct);
		await _bot.WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		Check("yes asks again: are you sure?", ChoicePrompt.Instance is { IsOpen: true } && ChoicePrompt.Instance.Question.StartsWith("Are you sure"), ChoicePrompt.Instance?.Question);
		ChoicePrompt.Instance?.TestChoose(1);
		int w0 = sewer.Whispers;
		using (Fast(2)) await _bot.Seconds(8.0, ct);
		Check("the second no: the teal haze, more whispers", sewer.Noes == 2 && sewer.Teal && sewer.Whispers >= w0 + 3, $"{sewer.Whispers - w0} more");
		await _bot.Aim(sewer.HoleWorld, ct);
		await _bot.UseIt(sewer.HoleUse, ct);
		await _bot.WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		ChoicePrompt.Instance?.TestChoose(0);
		await _bot.Seconds(0.6, ct);
		await _bot.WaitUntil(() => ChoicePrompt.Instance is { IsOpen: true }, 3, ct);
		ChoicePrompt.Instance?.TestChoose(0);
		using (Fast(3)) await _bot.WaitUntil(() => Story.Current == Checkpoint.Act17Finished, 30, ct);
		var boss = StationInterior.Instance?.Boss;
		Check("yes and yes: down the hole onto the pit's catwalk (checkpoint 17)", sewer.Dropped && Story.Current == Checkpoint.Act17Finished && boss != null && Player.GlobalPosition.DistanceTo(boss.LandingWorld) < 2f, $"{Story.Current}");
	}

	// ------------------------------------------------------------------ Act 18

	private async Task Act18(CancellationToken ct)
	{
		var boss = StationInterior.Instance?.Boss;
		await _bot.WaitUntil(() => boss?.Beast != null, 10, ct);
		Require("the pit", boss?.Beast != null);
		using (Fast(3)) await _bot.WaitUntil(() => boss.State == BossRoom.Phase.Fight, 40, ct);
		Require("the fight begins: the spotlight picks out a valve", boss.State == BossRoom.Phase.Fight && boss.ActiveValve != null, $"{boss.State}");
		if (Phase == 0)
		{
			Check("on the catwalk at checkpoint 17, the thing healthy, the blood high", Story.Current == Checkpoint.Act17Finished && boss.Beast.Rot < 0.01f);
			_s.ExpectReload = true;
			boss.TestSlamAt(Player.GlobalPosition, 1.4f);
			await _bot.WaitUntil(() => PlayerDeath.Dying, 5, ct);
			Check("stood under a slam: crushed", PlayerDeath.Dying);
			await ExpectReload(ct, 40);
			return;
		}
		Check("crushed: back on the catwalk, the fight over again", PlayerDeath.Deaths >= 1 && boss.ValvesTurned == 0);
		// the fight: to each lit valve (jumped along the catwalk when it's clear), held E, out from under the slams
		var start = Time.GetTicksMsec();
		int evades = 0;
		string last = "";
		using (Fast(2))
		{
			while (boss.ValvesTurned < BossRoom.ValvesToTurn && !PlayerDeath.Dying && (Time.GetTicksMsec() - start) < 150_000)
			{
				ct.ThrowIfCancellationRequested();
				string what = Input.Enabled ? await BossStep(boss, ct) : "cutscene";
				if (what != "evade") Trace18($"{what} at {boss.RingOf(boss.ToLocal(Player.GlobalPosition)):0.0}");
				if (what == "evade" && last != "evade") evades++;
				last = what;
				await _bot.Frames(1, ct);
			}
		}
		_bot.Release();
		if (PlayerDeath.Dying)
		{
			foreach (var line in _trace18) GD.Print($"[act18trace] {line}");
			Check("the fight: never crushed", false, $"crushed at valve {boss.ValvesTurned}");
			_s.ExpectReload = true;
			await ExpectReload(ct, 30);
			return;
		}
		Check("all ten valves turned, never crushed", boss.ValvesTurned == BossRoom.ValvesToTurn, $"{boss.ValvesTurned} valves, {boss.Slams} slams, {evades} dodged");
		Check("with every valve it rotted: fully gone", boss.Beast.Rot > 0.99f, $"rot {boss.Beast.Rot:0.00}");
		using (Fast(3)) await _bot.WaitUntil(() => boss.DoorOpen, 45, ct);
		Check("its eyes burst, it dies, the far door opens", boss.Beast.Dead && boss.DoorOpen);
		Shot("dead");
		await Control(ct, 10);
		// round the catwalk to the door (jumped), through it on foot
		Vector3 doorRing = boss.ToGlobal(boss.RingLocal(boss.RingOf(boss.ToLocal(boss.DoorWorld))));
		await _bot.Inside(doorRing, boss.DoorWorld, ct);
		await _bot.WalkTo(boss.DoorWorld, 0.5f, ct, giveUp: 6f);
		await _bot.WalkTo(boss.TidyWorld, 0.6f, ct, () => boss.Finished, giveUp: 8f);
		await _bot.WaitUntil(() => Story.Current == Checkpoint.Act18Finished, 5, ct);
		Check("into the clean, lit room: checkpoint 18", Story.Current == Checkpoint.Act18Finished, $"{Story.Current}");
	}

	/// <summary>The fight's last frames (what the bot did, the threats round it), printed if it's crushed.</summary>
	private readonly System.Collections.Generic.Queue<string> _trace18 = new();
	private void Trace18(string s)
	{
		_trace18.Enqueue($"{Time.GetTicksMsec() % 100000:00000} {s}");
		while (_trace18.Count > 400) _trace18.Dequeue();
	}

	/// <summary>One frame of the fight: get out from under a slam, else to the lit valve (jumped there if it's clear and far), else turn it.</summary>
	private async Task<string> BossStep(BossRoom boss, CancellationToken ct)
	{
		var v = boss.ActiveValve;
		float sp = boss.RingOf(boss.ToLocal(Player.GlobalPosition)), per = boss.Perimeter;
		var threats = boss.Threats.ToList();
		bool Under(Vector3 w, float margin) { foreach (var (tw, _, r) in threats) if (TestBot.Flat(tw - w).Length() < r + margin) return true; return false; }
		if (Under(Player.GlobalPosition, 0.95f))
		{
			float best = float.MaxValue, bestOff = 6f;
			for (float off = -11f; off <= 11.01f; off += 0.5f)
			{
				if (Mathf.Abs(off) < 0.1f) continue;
				Vector3 dest = boss.ToGlobal(boss.RingLocal(sp + off));
				if (Under(dest, 1.05f)) continue;
				bool pathOk = true;
				for (float k = 0.5f; k < Mathf.Abs(off) && pathOk; k += 0.5f)
				{
					float tAt = k / 4.6f;
					Vector3 pt = boss.ToGlobal(boss.RingLocal(sp + Mathf.Sign(off) * k));
					foreach (var (tw, left, r) in threats) if (Mathf.Abs(left - tAt) < 0.45f && TestBot.Flat(tw - pt).Length() < r + 0.5f) pathOk = false;
				}
				if (pathOk && Mathf.Abs(off) < best) { best = Mathf.Abs(off); bestOff = off; }
			}
			Vector3 target = boss.ToGlobal(boss.RingLocal(sp + Mathf.Sign(bestOff) * Mathf.Min(Mathf.Abs(bestOff) + 0.8f, 2.2f)));
			Trace18($"evade sp {sp:0.0} off {bestOff:0.0} v {TestBot.Flat(Player.Velocity).Length():0.0} run {Player.Stamina?.CanRun} | " + string.Join("; ", threats.Select(th => $"{boss.RingOf(boss.ToLocal(th.Item1)):0.0}@{TestBot.Flat(th.Item1 - Player.GlobalPosition).Length():0.0}m r{th.Item3:0.0} {th.Item2:0.00}s")));
			Input.ScriptedInteract = false;
			// (2026-10-14c, the flaky "never crushed": Steer turns the view 5 degrees a frame, so a slam behind them on
			// the way to a valve had the bot run on toward it for a third of a second while it came round, into the
			// circle, then stall against the valve; a player flicks the mouse round. The view is turned at once.)
			var away = TestBot.Flat(target - Player.GlobalPosition);
			if (away.LengthSquared() > 0.01f) Player.CameraRig.SnapBehind(Mathf.Atan2(-away.X, -away.Z));
			Input.ScriptedMove = new Vector2(0, 1);
			Input.ScriptedRun = true;
			return "evade";
		}
		Input.ScriptedRun = false;
		if (v == null) { Input.ScriptedMove = Vector2.Zero; Input.ScriptedInteract = false; return "wait"; }
		Vector3 stand = v.StandWorld;
		float dist = TestBot.Flat(stand - Player.GlobalPosition).Length();
		if (dist > 0.6f)
		{
			Input.ScriptedInteract = false;
			Input.ScriptedMove = Vector2.Zero;
			if (Under(stand, 1.6f)) return "hold back";
			// the walk round the ring is the slow part, not what's tested: jumped to a step short of the valve, the step walked
			if (dist > 2.5f)
			{
				float sg = boss.RingOf(boss.ToLocal(stand));
				float delta = Mathf.PosMod(sg - sp + per * 0.5f, per) - per * 0.5f;
				Vector3 before = boss.ToGlobal(boss.RingLocal(sg - Mathf.Sign(delta) * 1.5f));
				if (!Under(before, 1.6f)) { await _bot.Inside(before, stand, ct); return "jump"; }
				return "hold back";
			}
			_bot.Steer(stand);
			Input.ScriptedMove = new Vector2(0, 1);
			return "walk";
		}
		Input.ScriptedMove = Vector2.Zero;
		Vector3 to = v.WheelWorld - Player.CameraRig.Camera.GlobalPosition;
		Player.CameraRig.SnapBehind(Mathf.Atan2(-to.X, -to.Z));
		Player.CameraRig.SetPitch(Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length()));
		Input.ScriptedInteract = true;
		return "turn";
	}

	// ------------------------------------------------------------------ Acts 19-20

	private async Task Act19(CancellationToken ct)
	{
		await Control(ct, 40);
		var lib = StationInterior.Instance?.Boss?.Library;
		await _bot.WaitUntil(() => lib?.Puzzle != null && lib.SheetUse != null, 10, ct);
		Require("the library", lib?.Puzzle != null);
		Check("Act 19 starts in the library", Story.Current == Checkpoint.Act18Finished && lib.PlayerInside(Player.GlobalPosition));
		// (with the flame dead the lantern switches itself to the blacklight once: back to the dead flame, to look by lamplight)
		await _bot.LanternMode(false, ct);
		await _bot.WalkTo(lib.BookcaseFrontWorld, 0.6f, ct, giveUp: 10f);
		await _bot.Aim(lib.BookUse.GlobalPosition, ct);
		Check("one book sticks out (nothing to do with it yet)", _bot.Prompt == "One of the books sticks out." && !lib.BookcaseOpen, $"'{_bot.Prompt}'");
		await _bot.WalkTo(lib.JournalStandWorld, 0.5f, ct, giveUp: 12f);
		await _bot.Aim(lib.JournalWorld, ct);
		Check("by lamplight the journal is just another book", !lib.JournalFound);
		await _bot.LanternMode(true, ct);
		await _bot.Aim(lib.JournalWorld, ct);
		await _bot.WaitUntil(() => lib.JournalFound, 3, ct);
		Check("under the blacklight: a hand on it, READ, found", lib.JournalFound && Story.HasFlag(StoryManager.Flag.LibraryJournalFound));
		await _bot.LanternMode(false, ct);
		await _bot.WaitUntil(() => _bot.Prompt == "Read the journal", 1, ct);
		await _bot.Press(ct);
		await _bot.WaitUntil(() => NoteOverlay.Instance is { IsOpen: true }, 3, ct);
		Check("the journal reads: the switches' riddle", NoteOverlay.Instance is { IsOpen: true } && NoteOverlay.Instance.Current == lib.Journal);
		await _bot.PutDown(ct);
		await _bot.WalkTo(lib.TableWorld, 0.6f, ct, giveUp: 10f);
		await _bot.UseIt(lib.SheetUse, ct);
		using (Fast(2)) await _bot.WaitUntil(() => lib.BoxUse.Enabled, 8, ct);
		Check("the sheet pulled off: a bamboo puzzle box, five pieces", lib.SheetOff && lib.BoxUse.Enabled && lib.Puzzle.Pieces.Count == 5);
		await Control(ct, 5);
		_mouseBeforePuzzle = Godot.Input.MouseMode;
		await _bot.UseIt(lib.BoxUse, ct);
		var o = PuzzleOverlay.Instance;
		await _bot.WaitUntil(() => o is { IsOpen: true }, 3, ct);
		Require("working the box opens its close-up", o is { IsOpen: true });
		Check("the cursor out to work it with", Godot.Input.MouseMode == Godot.Input.MouseModeEnum.Visible, $"{Godot.Input.MouseMode}");
		var plan = lib.Puzzle.Solve();
		Check("the box has a solution", plan.Count == 5);
		int longIdx = lib.Puzzle.Pieces.FindIndex(p => p.Name == "long");
		o.TestSelect(longIdx);
		o.TestHold(new Vector2I(2, 0), 0);
		await _bot.Frames(3, ct);
		int bumps = o.Bumps;
		Check("a piece that doesn't fit knocks the rim and stays in hand", !o.SetIn() && o.Bumps == bumps + 1 && o.Holding);
		var c0 = o.Cursor;
		await _bot.Key("move_left", ct);
		Check("A moves the held piece a cell left", o.Cursor == c0 + new Vector2I(-1, 0), $"{c0} -> {o.Cursor}");
		o.TestPutBack();
		await _bot.Seconds(0.4, ct);
		await PuzzleMouse(o, plan, ct);
		foreach (var (p, rot, at) in plan)
		{
			if (p.At != null) continue;   // (put in with the mouse)
			o.TestSelect(lib.Puzzle.Pieces.IndexOf(p));
			o.TestHold(at, rot);
			await _bot.Frames(2, ct);
			if (!o.SetIn()) { Check($"piece {p.Name} fits", false); break; }
			await _bot.Seconds(0.15, ct);
		}
		Check("every piece in: solved, the close-up shut", lib.Puzzle.IsSolved && !o.IsOpen);
		Check("the cursor captured again", Godot.Input.MouseMode == Godot.Input.MouseModeEnum.Captured || _mouseBeforePuzzle == Godot.Input.MouseModeEnum.Visible, $"{Godot.Input.MouseMode}");
		await _bot.Seconds(1.2, ct);
		Shot("puzzle_solved");
		using (Fast(3)) await _bot.WaitUntil(() => lib.Bookmark != null, 12, ct);
		Check("it glows, goes, and leaves a red bookmark", lib.PuzzleSolved && lib.Bookmark != null);
		await Control(ct, 10);
		await _bot.UseIt(lib.Bookmark, ct);
		Check("the bookmark taken", Inv.HasTool(ToolKind.Bookmark));
		await _bot.WalkTo(lib.BookcaseFrontWorld, 0.6f, ct, giveUp: 10f);
		await _bot.Aim(lib.BookUse.GlobalPosition, ct);
		Check("the sticking-out book: slide the bookmark in", _bot.Prompt == "Slide the bookmark in", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		using (Fast(3)) await _bot.WaitUntil(() => lib.BookcaseOpen && Input.Enabled, 15, ct);
		Check("the bookcase swings open", lib.BookcaseOpen && !Inv.HasTool(ToolKind.Bookmark));
		Shot("bookcase");
		await _bot.WalkTo(lib.PassageWorld, 0.5f, ct, giveUp: 10f);
		await _bot.WalkTo(lib.Round.EntryWorld, 0.6f, ct, () => Story.Current == Checkpoint.Act19Finished, giveUp: 10f);
		await _bot.WaitUntil(() => Story.Current == Checkpoint.Act19Finished, 5, ct);
		Check("down the passage into the round room: checkpoint 19", Story.Current == Checkpoint.Act19Finished);
	}

	private Godot.Input.MouseModeEnum _mouseBeforePuzzle;

	/// <summary>Act 19's box worked with the mouse, through the overlay's own pointer path (the points it gets are the
	/// viewport's, as a real mouse's are): a drag in, a blocked drop, a click out, a turn in hand.</summary>
	private async Task PuzzleMouse(PuzzleOverlay o, System.Collections.Generic.List<(World.LibraryParts.PuzzleBox.Piece p, int rot, Vector2I at)> plan, CancellationToken ct)
	{
		var box = o.Box;
		var (p0, rot0, at0) = plan[0];
		Vector2 grab = o.ScreenOfCube(p0, 0);
		bool down = o.PointerDown(grab);
		Check("left-click on a piece picks it up by the cube under the cursor", down && o.Dragging && box.Pieces[o.Selected] == p0 && o.GrabbedCube == 0, $"down {down}, piece {box.Pieces[o.Selected].Name}, cube {o.GrabbedCube}");
		if (!down || !o.Dragging) return;
		int turns = 0;
		while (o.HeldRot != rot0 && turns++ < 4) o.PointerTurn(grab, 1);
		Check("right-click turns the held piece", o.HeldRot == rot0, $"{o.HeldRot} (want {rot0})");
		var cells = World.LibraryParts.PuzzleBox.Rotated(p0.Shape, rot0);
		// across the table first (it follows the cursor), then over its cell in the box
		o.PointerMove(o.ScreenOf(new Vector3(0.45f, OverlayHeldY, 0.1f)));
		await _bot.Seconds(0.25, ct);
		Check("dragged over the table: no snap, no ghost", o.DragSnap == null && !o.GhostFits);
		Vector2 drop = o.ScreenOfHeldCell(at0 + cells[0]);
		for (int i = 1; i <= 6; i++) { o.PointerMove(grab.Lerp(drop, i / 6f)); await _bot.Frames(1, ct); }
		await _bot.Seconds(0.35, ct);
		Check("over the box it snaps to the cells, the ghost warm: it fits", o.DragSnap == at0 && o.GhostFits, $"{o.DragSnap} (want {at0}), fits {o.GhostFits}");
		Shot("puzzle_drag_fits");
		bool placed = o.PointerUp(drop);
		Check("let go: it drops into the box", placed && p0.At == at0 && !o.Holding, $"{placed} at {p0.At}");
		await _bot.Seconds(0.3, ct);
		// a second piece dragged onto the first: red, and let go it goes back to the table
		var (p1, _, _) = plan[1];
		var restBefore = p1.Rest;
		Vector2 g1 = o.ScreenOfCube(p1, 0);
		bool down1 = o.PointerDown(g1);
		Vector2 onto = o.ScreenOfHeldCell(at0 + cells[0]);
		for (int i = 1; i <= 6; i++) { o.PointerMove(g1.Lerp(onto, i / 6f)); await _bot.Frames(1, ct); }
		// (kept inside the box it may snap clear of the first piece: over each of its cells till one clashes)
		foreach (var c in cells)
		{
			if (!o.GhostFits) break;
			onto = o.ScreenOfHeldCell(at0 + c);
			o.PointerMove(onto);
			await _bot.Frames(2, ct);
		}
		await _bot.Seconds(0.35, ct);
		Check("dragged onto a taken cell: the ghost red", o.Dragging && o.DragSnap != null && !o.GhostFits, $"{p1.Name} at {g1}: down {down1}, dragging {o.Dragging}, snap {o.DragSnap}, fits {o.GhostFits}");
		Shot("puzzle_drag_blocked");
		int bumps = o.Bumps;
		bool wrong = o.PointerUp(onto);
		Check("let go there: it knocks the rim and goes back to the table", !wrong && p1.At == null && o.Bumps == bumps + 1 && !o.Holding && p1.Rest == restBefore);
		await _bot.Seconds(0.3, ct);
		// a click on the piece in the box takes it out
		Vector2 c0 = o.ScreenOfCube(p0, 0);
		o.PointerDown(c0);
		o.PointerUp(c0);
		Check("a click on a piece in the box takes it out", p0.At == null && !o.Holding, $"{p0.At}");
		await _bot.Seconds(0.3, ct);
		// real clicks, the wheel and a number key (in a corner, off every piece): the puzzle's, never the item in hand
		// or the camera raised
		var hands = PlayerHands.Instance;
		int switches = hands?.Switches ?? 0;
		var inHand = Inv.InHand;
		void Mouse(MouseButton b, bool pressed) { Godot.Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Pressed = pressed, Position = new Vector2(2, 2) }); Godot.Input.FlushBufferedEvents(); }
		Mouse(MouseButton.Right, true);
		await _bot.Frames(3, ct);
		bool raised = Player.GetNodeOrNull<CameraTool>("CameraTool") is { Raising: true } || Player.PlayerInput.Focus;
		Mouse(MouseButton.Right, false);
		Mouse(MouseButton.WheelDown, true); Mouse(MouseButton.WheelDown, false);
		Mouse(MouseButton.WheelUp, true); Mouse(MouseButton.WheelUp, false);
		Godot.Input.ParseInputEvent(new InputEventAction { Action = "item_slot_2", Pressed = true }); Godot.Input.FlushBufferedEvents();
		await _bot.Frames(3, ct);
		Godot.Input.ParseInputEvent(new InputEventAction { Action = "item_slot_2", Pressed = false }); Godot.Input.FlushBufferedEvents();
		await _bot.Frames(3, ct);
		Check("right-click, the wheel and 1-9 don't switch the item in hand or raise the camera", !raised && (hands?.Switches ?? 0) == switches && Inv.InHand == inHand && o.IsOpen,
			$"raised {raised}, switches {switches} -> {hands?.Switches}, in hand {inHand} -> {Inv.InHand}");
	}

	private const float OverlayHeldY = PuzzleOverlay.HeldY;

	private async Task Act20(CancellationToken ct)
	{
		await Control(ct, 40);
		var rr = StationInterior.Instance?.Boss?.Library?.Round;
		await _bot.WaitUntil(() => rr?.Dais != null && rr.WebUse != null, 10, ct);
		Require("the round room", rr?.Dais != null);
		Check("the lighter in hand", Inv.HasTool(ToolKind.Lighter));
		await _bot.Inside(rr.DaisEdgeWorld, rr.CentreWorld, ct);
		await _bot.WalkFor(rr.CentreWorld, 1.2, ct);
		Check("the web keeps them off the dais", TestBot.Flat(Player.GlobalPosition - rr.CentreWorld).Length() > RoundRoom.DaisFoot - 0.4f);
		await _bot.WalkTo(rr.DaisEdgeWorld, 0.5f, ct, giveUp: 8f);
		await _bot.Aim(rr.CentreWorld + Vector3.Up * 0.6f, ct);
		Check("the lighter: burn the webs", _bot.Prompt == "Burn the webs", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		using (Fast(3)) await _bot.WaitUntil(() => rr.WebsBurned && Input.Enabled && rr.BurnRadius > RoundRoom.Radius + RoundRoom.DaisFoot, 15, ct);
		Check("the webs burn away, out to the walls, saved", rr.WebsBurned && Story.HasFlag(StoryManager.Flag.RoundRoomWebBurned));
		await _bot.WalkTo(rr.CentreWorld, 0.3f, ct, () => rr.Rising, giveUp: 6f);
		await _bot.Seconds(0.8, ct);
		Check("the ring red: the dais does nothing", !rr.Rising && !rr.Powered);
		Check("six three-way switches, none set right", rr.Switches.Count == 6 && !rr.Switches.Any(w => w.Pos == w.Want));
		for (int i = 0; i < rr.Switches.Count; i++)
		{
			var w = rr.Switches[i];
			await _bot.Inside(rr.SwitchStandWorld(i), rr.SwitchGripWorld(i), ct);
			for (int k = 0; k < 3 && w.Pos != w.Want; k++)
			{
				await _bot.Aim(rr.SwitchGripWorld(i), ct);
				var was = w.Pos;
				await _bot.Press(ct);
				await _bot.Seconds(0.4, ct);
				if (w.Pos == was) { Check($"switch {i + 1} throws", false, $"'{_bot.Prompt}'"); break; }
			}
		}
		Check("all six set right", rr.AllSwitchesRight, string.Join(" ", rr.Switches.Select(w => w.Pos)));
		using (Fast(3)) await _bot.WaitUntil(() => rr.Powered && Input.Enabled, 20, ct);
		Check("the power surges: candlelight, the ring green, saved", rr.Powered && Story.HasFlag(StoryManager.Flag.RoundRoomPowered));
		Shot("ring_green");
		// the camera: the green ring is one of the pictures to take
		await _bot.Inside(rr.DaisEdgeWorld + (rr.DaisEdgeWorld - rr.CentreWorld).Normalized() * 2f, rr.CentreWorld, ct);
		var ring = await _bot.TakePicture(rr.ToGlobal(new Vector3(0, 0.3f, 0)), 0, ct);
		Check("a photo of the green ring, scored", ring is { SubjectId: "the_ring", Scored: true }, Describe(ring));
		var zoomed = await _bot.TakePicture(rr.SwitchGripWorld(0), 2, ct);
		Check("zoomed (x2) on a switch", zoomed is { SubjectId: "switches", Scored: true }, Describe(zoomed));
		await _bot.WalkTo(rr.CentreWorld, 0.3f, ct, () => rr.Rising, giveUp: 8f);
		await _bot.WaitUntil(() => rr.Rising, 3, ct);
		Check("into the middle: the dais rises, the player held", rr.Rising && !Input.Enabled);
		using (Fast(4)) await _bot.WaitUntil(() => rr.Arrived && Input.Enabled, 30, ct);
		Check("up through the ceiling: checkpoint 20", rr.Arrived && Story.Current == Checkpoint.Act20Finished);
		await _bot.WalkTo(rr.TopWorld, 0.5f, ct, giveUp: 4f);
		Check("off the dais onto the floor above", Player.IsOnFloor());
		Check("after Act 20 the flame works again", !Lantern.FlameDead);
	}

	// ------------------------------------------------------------------ Act 21

	private async Task Act21(CancellationToken ct)
	{
		await Control(ct, 40);
		var rr = StationInterior.Instance?.Boss?.Library?.Round;
		await _bot.WaitUntil(() => rr?.Stair?.Church?.FontKeyPickup != null, 15, ct);
		var stair = rr?.Stair;
		var church = stair?.Church;
		Require("the long stair and the church", church != null);
		await _bot.WalkTo(stair.BottomWorld, 0.5f, ct, giveUp: 8f);
		// a few seconds climbed at a run, then jumped to just short of the top
		float p0 = stair.Progress(Player.GlobalPosition);
		await _bot.WalkFor(stair.PointAt(0.05f), 3.0, ct, run: true);
		Check("up the long stair on foot", stair.Progress(Player.GlobalPosition) > p0, $"{p0:0.000} -> {stair.Progress(Player.GlobalPosition):0.000}");
		await _bot.Inside(stair.PointAt(0.995f), stair.TopLandingWorld, ct);
		await _bot.WalkTo(stair.TopLandingWorld, 0.4f, ct, giveUp: 8f);
		await _bot.Aim(stair.ToGlobal(LongStair.HatchLocal), ct);
		Check("a hatch in the ceiling: push it open", _bot.Prompt == "Push the hatch open", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		using (Fast(2)) await _bot.WaitUntil(() => Story.Current == Checkpoint.Act21ChurchReached && Input.Enabled, 12, ct);
		Check("up into the church's crypt: checkpoint 21", Story.Current == Checkpoint.Act21ChurchReached && church.Inside(Player.GlobalPosition));
		await _bot.Aim(church.ToGlobal(Church.HatchLocal), ct);
		await _bot.WaitUntil(() => _bot.Prompt == "It won't lift. Shut fast.", 1, ct);
		Check("the hatch shut fast behind them", _bot.Prompt == "It won't lift. Shut fast.", $"'{_bot.Prompt}'");
		Check("winter above ground", StoryBeat.Atmosphere(Player) is { Winter: > 0.5f, Underground: < 0.2f });
		// up the crypt's stairs (walked: it's their own steps into the nave)
		await _bot.Inside(church.ToGlobal(new Vector3(2f, Church.CryptFloor + 0.05f, 42f)), church.CryptStairFootWorld, ct);
		await _bot.WalkTo(church.CryptStairFootWorld, 0.5f, ct, giveUp: 12f);
		await _bot.WalkTo(church.CryptStairTopWorld, 0.4f, ct, giveUp: 14f);
		Check("up the crypt's stairs into the nave", Mathf.Abs(Player.GlobalPosition.Y - church.ToGlobal(Vector3.Zero).Y) < 0.2f, $"{church.ToLocal(Player.GlobalPosition)}");
		Vector3 L(float x, float y, float z) => church.ToGlobal(new Vector3(x, y, z));
		// the locked things
		await _bot.Inside(L(0, 0.05f, 3.5f), L(0, 1.5f, 0), ct);
		await _bot.Aim(L(0, 2.6f, -0.6f), ct);
		Check("the great door: locked, four empty niches", _bot.Prompt == "Locked fast. Four empty niches in it.", $"'{_bot.Prompt}'");
		await _bot.Inside(church.FontStandWorld, church.FontWorld, ct);
		await _bot.Aim(church.FontWorld, ct);
		Check("the font chained and locked", _bot.Prompt == "The font's lid is chained and locked.", $"'{_bot.Prompt}'");
		// the four candles
		for (int i = 0; i < 4; i++)
		{
			await _bot.Inside(church.CandleStandWorld(i) + (church.CandleStandWorld(i) - church.CandleWorld(i)).Normalized() with { Y = 0 } * 1.2f, church.CandleWorld(i), ct);
			await _bot.WalkTo(church.CandleStandWorld(i), 0.5f, ct, giveUp: 6f);
			await _bot.Aim(church.CandleWorld(i) + Vector3.Up * 0.1f, ct);
			if (i == 0) Check("a tall candle: light it", _bot.Prompt == "Light the candle", $"'{_bot.Prompt}'");
			await _bot.Press(ct);
			using (Fast(2)) await _bot.Seconds(0.8, ct);
			Check($"candle {i + 1} lit", church.CandleLit(i));
			await Control(ct, 12);
		}
		Check("all four burning: the vestry door opens", church.VestryOpen && Story.HasFlag(StoryManager.Flag.ChurchVestryOpen));
		await _bot.Inside(church.VestryDoorWorld, church.VestryInsideWorld, ct);
		await _bot.WalkTo(church.VestryInsideWorld, 0.6f, ct, giveUp: 10f);
		await _bot.UseIt(church.FontKeyPickup, ct);
		Check("the font's iron key off its hook", Inv.HasTool(ToolKind.FontKey));
		await _bot.Inside(church.FontStandWorld, church.FontWorld, ct);
		await _bot.Aim(church.FontWorld, ct);
		Check("with the key: unlock the font", _bot.Prompt == "Unlock the font", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		using (Fast(2)) await _bot.WaitUntil(() => church.ChalicePickup != null && Input.Enabled, 8, ct);
		await _bot.UseIt(church.ChalicePickup, ct);
		Check("the chalice out of the font's black water", Inv.HasTool(ToolKind.Chalice));
		await _bot.Inside(church.NicheStandWorld, church.NicheWorld(0), ct);
		await _bot.Aim(church.NicheWorld(0), ct);
		Check("the niche of its shape: set it in", _bot.Prompt == "Set the chalice in its place", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		using (Fast(2)) await _bot.WaitUntil(() => Story.Current == Checkpoint.Act21Finished, 14, ct);
		Check("the great door swings open on the snow: checkpoint 22", Story.Current == Checkpoint.Act21Finished && church.DoorOpen && !Inv.HasTool(ToolKind.Chalice));
		Shot("great_door");
	}
}
