using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.UI;
using ProjectDS.World;
using ProjectDS.World.LodgeParts;
using ProjectDS.World.SnowMaze;

namespace ProjectDS.Systems;

/// <summary>The winter: the woods, the lodge, the maze (Acts 22-24).</summary>
public partial class Playtest
{
	private static SkiLodge Lodge => StationInterior.Instance?.Boss?.Library?.Round?.Stair?.Church?.Woods?.Lodge;
	private static float Upper => SkiLodge.UpperY + 0.05f;

	// ------------------------------------------------------------------ Act 22

	private async Task Act22(CancellationToken ct)
	{
		await Control(ct, 40);
		var church = StationInterior.Instance?.Boss?.Library?.Round?.Stair?.Church;
		var woods = church?.Woods;
		var lodge = woods?.Lodge;
		Require("the church, its woods, the lodge", woods != null && lodge != null);
		Vector3 R(float along) => woods.ToGlobal(WinterWoods.RoadAt(along, out _));
		await _bot.WalkTo(R(6f), 0.8f, ct, giveUp: 12f);
		await _bot.WalkTo(R(20f), 0.8f, ct, giveUp: 12f);
		Check("out on the plowed road in the snow", woods.PlayerOutside && Player.IsOnFloor());
		Check("the compass lives again: down the road to the lodge", _bot.FirstOf<Compass>() is { Dead: false } && Story.ObjectivePosition is Vector3 o && o.DistanceTo(lodge.FrontDoorWorld) < 4f, $"{Story.ObjectivePosition}");
		Shot("road");
		// the wendigo: turn round, it's there by a tree; look at it and it's up into the trees
		await _bot.Aim(R(60f) + Vector3.Up * 1.6f, ct);
		bool behind = false;
		for (int i = 0; i < 20 && !behind; i++) { behind = woods.DebugAppearBehind(); if (!behind) await _bot.Seconds(0.3, ct); }
		Check("the wendigo takes its place behind them, by a tree", behind && woods.Wendigo.Visible);
		int leaps = woods.Wendigo.Leaps;
		await _bot.Aim(woods.Wendigo.ChestWorld, ct);
		await _bot.WaitUntil(() => woods.Wendigo.Leaps > leaps, 2, ct);
		Check("looked at, it leaps up into the trees", woods.Wendigo.Leaps > leaps);
		Check("it never lays a hand on them", Input.Enabled && Player.IsOnFloor());
		// deep snow off the road
		var tree = woods.Props["antler_tree"];
		await _bot.Teleport(woods.ToGlobal(WinterWoods.RoadAt(WinterWoods.PropSpots[2].s, out _)), tree.GlobalPosition, ct, snapToTerrain: false);
		await _bot.WalkTo(tree.GlobalPosition, 2.0f, ct, giveUp: 10f);
		await _bot.Seconds(0.8, ct);
		Check("off the road in the deep snow: wading, slowed", woods.PlayerDeepSnow > 0.6f && Player.WadeScale < 0.6f, $"deep {woods.PlayerDeepSnow:0.00}, pace {Player.WadeScale:0.00}");
		// past halfway: the road's own save
		await _bot.Teleport(R(WinterWoods.MidwayS - 6f), R(WinterWoods.MidwayS + 10f), ct, snapToTerrain: false);
		await _bot.WalkTo(R(WinterWoods.MidwayS + 6f), 1.2f, ct, () => Story.HasFlag(StoryManager.Flag.Act22Midway), giveUp: 10f);
		await _bot.WaitUntil(() => Story.HasFlag(StoryManager.Flag.Act22Midway), 3, ct);
		Check("past halfway down the road: a save there", Story.HasFlag(StoryManager.Flag.Act22Midway) && Story.Current == Checkpoint.Act21Finished);
		// the lodge: the front doors chained
		await _bot.Teleport(lodge.FrontStandWorld + (lodge.FrontStandWorld - lodge.FrontDoorWorld).Normalized() with { Y = 0 } * 5f, lodge.FrontDoorWorld, ct, snapToTerrain: false);
		await _bot.WalkTo(lodge.FrontStandWorld, 0.6f, ct, giveUp: 12f);
		await _bot.Aim(lodge.FrontDoorWorld + Vector3.Up * 1.4f, ct);
		Check("the front doors: chained shut from inside", _bot.Prompt == "Chained shut from inside.", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		await _bot.Seconds(0.4, ct);
		Check("tried: the compass turns to the back door", Story.HasFlag(StoryManager.Flag.LodgeFrontTried) && Story.ObjectivePosition is Vector3 b && b.DistanceTo(lodge.BackDoorWorld) < 3f, $"{Story.ObjectivePosition}");
		await _bot.Teleport(lodge.BackStandWorld + (lodge.BackStandWorld - lodge.BackDoorWorld).Normalized() with { Y = 0 } * 4f, lodge.BackDoorWorld, ct, snapToTerrain: false);
		await _bot.WalkTo(lodge.BackStandWorld, 0.6f, ct, giveUp: 12f);
		await _bot.Aim(lodge.BackDoorWorld, ct);
		Check("round the back: iced in, force the door", _bot.Prompt == "Force the door", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		using (Fast(2)) await _bot.WaitUntil(() => Story.Current == Checkpoint.Act22Finished, 20, ct);
		Check("three shoves and it gives: inside, checkpoint 23", Story.Current == Checkpoint.Act22Finished && lodge.BackOpen && lodge.Inside(Player.GlobalPosition));
		Check("behind them the roof's snow buries the door: snowed in", lodge.SnowPile.Visible && Story.HasFlag(LodgeFlag.SnowedIn));
		using (Fast(3)) await _bot.WaitUntil(() => ChapterCard.Shown.Contains(7), 15, ct);
		Check("CHAPTER SEVEN drops over the mudroom", ChapterCard.Shown.Contains(7));
	}

	// ------------------------------------------------------------------ Act 23

	private Vector3 LL(float x, float y, float z) => Lodge.ToGlobal(new Vector3(x, y, z));

	private async Task Act23a(CancellationToken ct)
	{
		await Control(ct, 40);
		var lodge = Lodge;
		Require("the lodge", lodge != null);
		Check("Act 23 starts in the mudroom, snowed in", Story.Current == Checkpoint.Act22Finished && lodge.InMudroom(Player.GlobalPosition) && lodge.SnowPile.Visible);
		await _bot.UseIt(lodge.MudroomDoor.Use, ct);
		await _bot.Seconds(0.8, ct);
		if (lodge.MudroomDoor.Current != LodgeDoor.State.Open) { await _bot.UseIt(lodge.MudroomDoor.Use, ct); await _bot.Seconds(0.8, ct); }
		Check("the mudroom's inner door opens", lodge.MudroomDoor.Current == LodgeDoor.State.Open);
		// the bar: 202's card under the counter, by blacklight only
		await _bot.Inside(LL(-18.3f, 0.05f, 2.2f), LL(-18.3f, 0.05f, 3.9f), ct);
		await _bot.WalkTo(LL(-18.3f, 0.05f, 3.9f), 0.4f, ct, giveUp: 6f);
		await _bot.LanternMode(false, ct);
		await _bot.Aim(lodge.Card202Pickup.GlobalPosition, ct);
		Check("under the counter, by the flame: nothing", _bot.Prompt != "Peel the card off", $"'{_bot.Prompt}'");
		await _bot.LanternMode(true, ct);
		await _bot.Aim(lodge.Card202Pickup.GlobalPosition, ct);
		await _bot.WaitUntil(() => _bot.Prompt == "Peel the card off", 2, ct);
		Check("by the blacklight: a card taped under the lip", _bot.Prompt == "Peel the card off", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		await _bot.Seconds(0.5, ct);
		Check("202's keycard, the staff's note on its back", Inv.HasTool(ToolKind.Keycard202) && NoteOverlay.Instance is { IsOpen: true } && (NoteOverlay.Instance.Current?.Text ?? "").Contains("closing staff"));
		await _bot.PutDown(ct);
		await _bot.LanternMode(false, ct);
		Shot("bar");
		// a curio on the low table: picked up, looked at, put back
		int examined = Curio.Examined;
		var curio = lodge.Curios[0];
		var toFire = (lodge.ToGlobal(lodge.HearthAt) - curio.GlobalPosition) with { Y = 0 };
		var curioStand = curio.GlobalPosition + toFire.Normalized() * 0.85f;
		curioStand.Y = LL(0, 0.05f, 0).Y;
		await _bot.Inside(curioStand, curio.GlobalPosition, ct);
		await _bot.UseIt(curio, ct);
		if (Curio.Examined == examined) { await _bot.Seconds(0.6, ct); await _bot.UseIt(curio, ct); }
		Check("the whiskey on the low table, picked up and put back", Curio.Examined == examined + 1, $"focused '{Player.Interaction?.Focused?.Name}'");
		using (Fast(2)) await _bot.Seconds(2.5, ct);
		await Control(ct, 6);
		await _bot.Inside(LL(0.3f, 0.05f, 6.5f), lodge.ToGlobal(SkiLodge.FrontDoorInside), ct);
		await _bot.Aim(lodge.ToGlobal(SkiLodge.FrontDoorInside), ct);
		Check("the front doors from inside: Requires Master Key.", _bot.Prompt == "Requires Master Key.", $"'{_bot.Prompt}'");
		// 202
		await _bot.Inside(LL(-12.4f, Upper, 0f), LL(-13.7f, Upper, 0f), ct);
		await _bot.WalkTo(LL(-13.7f, Upper, 0f), 0.45f, ct, giveUp: 6f);
		var d202 = lodge.RoomDoors[202];
		await _bot.Aim(d202.CentreWorld, ct);
		Check("202's reader: use the keycard", _bot.Prompt == "Use the keycard", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		await _bot.Seconds(0.6, ct);
		Check("the keycard swiped through the reader (in the hand)", HeldUse.Last == ToolKind.Keycard202, $"{HeldUse.Last}");
		using (Fast(2)) await _bot.WaitUntil(() => d202.Current == LodgeDoor.State.Open, 4, ct);
		Check("the reader goes green, the door opens", d202.Current == LodgeDoor.State.Open);
		await _bot.WalkTo(LL(-13.7f, Upper, 2.2f), 0.45f, ct, giveUp: 8f);
		await _bot.WalkTo(LL(-15.5f, Upper, 4.2f), 0.45f, ct, giveUp: 8f);
		var empty = lodge.Search202.First(x => x != lodge.Search202[lodge.PantryKeyIn] && !x.Name.ToString().Contains("Vanity"));
		await SearchIt(lodge, empty, ct);
		Check("an empty drawer: nothing", empty.IsOpen && !empty.HasSomething);
		var holds = lodge.Search202[lodge.PantryKeyIn];
		await SearchIt(lodge, holds, ct, leave: false);
		var find = holds.Part.GetNodeOrNull<Pickup>("Find");
		Check($"the {holds.What}: a key wrapped in a note", holds.IsOpen && find != null);
		await _bot.UseIt(find, ct);
		await _bot.Seconds(0.5, ct);
		Check("the servants' pantry key, its note", Inv.HasTool(ToolKind.PantryKey) && (NoteOverlay.Instance?.Current?.Text ?? "").Contains("Servant's Pantry key"));
		await _bot.PutDown(ct);
		await LeaveBath(lodge, holds, ct);
		await _bot.Inside(LL(-14.2f, Upper, 2f), LL(-14.2f, Upper, 0.2f), ct);
		await _bot.WalkTo(LL(-14.2f, Upper, 0.2f), 0.45f, ct, giveUp: 6f);
		await _bot.WalkTo(LL(-15.5f, Upper, 0f), 0.45f, ct, giveUp: 6f);
		using (Fast(2)) await _bot.WaitUntil(() => lodge.Room202Jammed && Story.Current == Checkpoint.Act23Room202Done, 4, ct);
		Check("out: 202 slams and jams behind them (checkpoint 24)", lodge.Room202Jammed && d202.Current == LodgeDoor.State.Jammed && Story.Current == Checkpoint.Act23Room202Done, $"{Story.Current}");
		await _bot.Aim(d202.CentreWorld, ct);
		using (Fast(2)) await _bot.WaitUntil(() => lodge.Whispers > 0, 16, ct);
		Check("at the door: something inside saying it's cold", lodge.Whispers > 0);
	}

	private async Task Act23b(CancellationToken ct)
	{
		await Control(ct, 40);
		var lodge = Lodge;
		Require("the lodge", lodge != null);
		await _bot.Inside(lodge.PantryDoor.FrontWorld, lodge.PantryDoor.CentreWorld, ct);
		await _bot.Aim(lodge.PantryDoor.CentreWorld, ct);
		Check("the pantry door: unlock it", _bot.Prompt == "Unlock it", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		using (Fast(2)) await _bot.Seconds(1.2, ct);
		Check("open: the mop falls across the doorway", lodge.PantryDoor.Current == LodgeDoor.State.Open && lodge.MopFell);
		var drawer = lodge.SearchPantry[lodge.Card203In];
		await _bot.Inside(LL(-9.0f, 0.05f, -6.5f), drawer.GlobalPosition, ct);
		await SearchIt(lodge, drawer, ct);
		var card203 = drawer.Part.GetNodeOrNull<Pickup>("Find");
		Check("203's keycard in a drawer", card203 != null);
		await _bot.UseIt(card203, ct);
		Check("203's keycard taken", Inv.HasTool(ToolKind.Keycard203));
		// 203: the open window, the dining key on the sill, the hole into 204
		await _bot.Inside(LL(-12.4f, Upper, 0f), LL(-13.7f, Upper, 0f), ct);
		await _bot.WalkTo(LL(-13.7f, Upper, 0f), 0.45f, ct, giveUp: 6f);
		var d203 = lodge.RoomDoors[203];
		await _bot.Aim(d203.CentreWorld, ct);
		await _bot.Press(ct);
		using (Fast(2)) await _bot.Seconds(1.2, ct);
		Check("203 opens: the cold comes out", d203.Current == LodgeDoor.State.Open);
		await _bot.WalkTo(LL(-13.7f, Upper, -2.2f), 0.45f, ct, giveUp: 8f);
		await _bot.WalkTo(LL(-13.95f, Upper, -5.0f), 0.45f, ct, giveUp: 8f);
		using (Fast(2)) await _bot.WaitUntil(() => lodge.Mumbles > 0, 12, ct);
		Check("the window open on the storm, a voice mumbling outside", lodge.Mumbles > 0);
		Shot("room203");
		await _bot.WalkTo(LL(-14.0f, Upper, -6.4f), 0.45f, ct, giveUp: 6f);
		await _bot.UseIt(lodge.GetNodeOrNull<Pickup>("DiningKey"), ct);
		Check("the dining room's key from the sill", Inv.HasTool(ToolKind.DiningKey));
		await Control(ct, 5);
		await _bot.Inside(LL(-16.6f, Upper, -2.65f), LL(-21.6f, Upper, -2.45f), ct);
		await _bot.WalkTo(LL(-18.6f, Upper, -2.45f), 0.45f, ct, giveUp: 8f);
		await _bot.WalkTo(LL(-21.6f, Upper, -2.45f), 0.45f, ct, giveUp: 8f);
		Check("through the hole in the bathroom's wall, into 204", lodge.ToLocal(Player.GlobalPosition).X < SkiLodge.RoomSplitX - 0.5f, $"{lodge.ToLocal(Player.GlobalPosition)}");
		var hides = lodge.Search204[lodge.Note204In];
		await _bot.WalkTo(LL(-22.6f, Upper, -3.3f), 0.45f, ct, giveUp: 6f);
		await SearchIt(lodge, hides, ct, leave: false);
		Check("in one of 204's drawers, a note", lodge.Note204 != null);
		await _bot.UseIt(lodge.Note204, ct);
		await _bot.Seconds(0.4, ct);
		Check("smudged: \"If you can hear it, it already knows you are here.\"", Story.HasFlag(LodgeFlag.Note204) && NoteOverlay.Instance?.Current?.Style == Readable.NoteStyle.Smudged);
		await _bot.PutDown(ct);
		await LeaveBath(lodge, hides, ct);
		var d204 = lodge.RoomDoors[204];
		await _bot.WalkTo(LL(-21.3f, Upper, -2.0f), 0.45f, ct, giveUp: 8f);
		await _bot.UseIt(d204.Use, ct);
		using (Fast(2)) await _bot.Seconds(1.2, ct);
		Check("204's door opens from inside", d204.Current == LodgeDoor.State.Open);
		await _bot.WalkTo(LL(-21.3f, Upper, -0.2f), 0.45f, ct, giveUp: 6f);
		await _bot.WalkTo(LL(-19f, Upper, 0f), 0.45f, ct, giveUp: 6f);
		using (Fast(2)) await _bot.WaitUntil(() => lodge.Room203Jammed && Story.Current == Checkpoint.Act23Room203Done, 4, ct);
		Check("in the corridor 203 slams and jams too (checkpoint 25)", lodge.Room203Jammed && Story.Current == Checkpoint.Act23Room203Done, $"{Story.Current}");
	}

	private async Task Act23c(CancellationToken ct)
	{
		await Control(ct, 40);
		var lodge = Lodge;
		Require("the lodge", lodge != null);
		await _bot.Inside(lodge.DiningDoorL.FrontWorld + (lodge.DiningDoorL.FrontWorld - lodge.DiningDoorL.CentreWorld).Normalized() with { Y = 0 } * 1.5f, lodge.DiningDoorL.CentreWorld, ct);
		await _bot.WalkTo(lodge.DiningDoorL.FrontWorld, 0.45f, ct, giveUp: 6f);
		await _bot.UseIt(lodge.DiningDoorL.Use, ct);
		using (Fast(2)) await _bot.Seconds(1.5, ct);
		Check("the dining key opens both doors", lodge.DiningDoorL.Current == LodgeDoor.State.Open && lodge.DiningDoorR.Current == LodgeDoor.State.Open);
		int[] order = { 0, 3, 1, 4, 2, 5 };
		string[] want = { "dishes", "blood", "skeleton", "skull", "collapse", "card" };
		for (int i = 0; i < 6; i++)
		{
			int t = order[i];
			var c = SkiLodge.TableCentre(t);
			float side = c.Z > 0 ? -1f : 1f;
			await _bot.Inside(LL(c.X, 0.05f, c.Z + side * 1.55f), lodge.ToGlobal(c), ct);
			await _bot.Aim(lodge.ToGlobal(c + new Vector3(0, SkiLodge.TableH + 0.1f, 0)), ct);
			if (_bot.Prompt != "Pull the sheet off") { await _bot.UseIt(lodge.SheetUses[t], ct); }
			else await _bot.Press(ct);
			using (Fast(2))
			{
				await _bot.WaitUntil(() => lodge.TableEvents.Count > i, 6, ct);
				await _bot.Seconds(i is 1 or 2 or 3 ? 3.0 : 1.2, ct);
			}
			Check($"sheet {i + 1} off: under it, {want[i]}", lodge.TableEvents.Count > i && lodge.TableEvents[i] == want[i], lodge.TableEvents.Count > i ? lodge.TableEvents[i] : "nothing");
			if (i == 3) Shot("skull");
		}
		Check("the storm got up, the fifth table broke", lodge.Storm > 0.2f && lodge.Collapsed == order[4]);
		Require("201's keycard in a bowl of snow", lodge.Card201Pickup != null && IsInstanceValid(lodge.Card201Pickup));
		await _bot.UseIt(lodge.Card201Pickup, ct);
		using (Fast(2)) await _bot.WaitUntil(() => lodge.BowlMelted, 6, ct);
		Check("taken: the snow melts into blood (checkpoint 26)", Inv.HasTool(ToolKind.Keycard201) && lodge.BowlMelted && Story.Current == Checkpoint.Act23Keycard201, $"{Story.Current}");
	}

	private async Task Act23d(CancellationToken ct)
	{
		await Control(ct, 40);
		var lodge = Lodge;
		Require("the lodge", lodge != null);
		await _bot.Inside(LL(-19.5f, Upper, 0f), LL(-21.3f, Upper, 0f), ct);
		await _bot.WalkTo(LL(-21.3f, Upper, 0f), 0.45f, ct, giveUp: 6f);
		var d201 = lodge.RoomDoors[201];
		await _bot.Aim(d201.CentreWorld, ct);
		Check("201: use the keycard", _bot.Prompt == "Use the keycard", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		using (Fast(2)) await _bot.Seconds(1.0, ct);
		Check("201 opens on the cleanest room in the lodge, every light on", d201.Current == LodgeDoor.State.Open && lodge.RoomSwitches[201].On);
		await _bot.WalkTo(LL(-21.3f, Upper, 2.4f), 0.45f, ct, giveUp: 6f);
		await _bot.WalkTo(LL(-22.6f, Upper, 4.2f), 0.45f, ct, giveUp: 6f);
		await _bot.UseIt(lodge.Envelope201Use, ct);
		using (Fast(2)) await _bot.WaitUntil(() => NoteOverlay.Instance is { IsOpen: true }, 8, ct);
		Check("\"Welcome Back. We missed you as a valued guest.\"", NoteOverlay.Instance?.Current == lodge.Letter201 && Story.HasFlag(LodgeFlag.Letter201));
		Shot("welcome_back");
		await _bot.PutDown(ct);
		using (Fast(2)) await _bot.WaitUntil(() => lodge.Room201Jammed && Story.Current == Checkpoint.Act23Letter201, 6, ct);
		Check("put down: the door slams for good (checkpoint 27)", lodge.Room201Jammed && Story.Current == Checkpoint.Act23Letter201, $"{Story.Current}");
		var bath = lodge.BathSwitches[201];
		await _bot.UseIt(bath.Use, ct);
		Check("the bathroom's switch: a deep red light", bath.On && bath.Lights.Count > 0 && bath.Lights[0].LightColor.R > 0.6f && bath.Lights[0].LightColor.G < 0.2f);
		await _bot.WalkTo(LL(-24.9f, Upper, 2.6f), 0.45f, ct, giveUp: 6f);
		await _bot.WalkTo(LL(-26.2f, Upper, 1.95f), 0.45f, ct, giveUp: 6f);
		// the crawlspace: in on foot, jumped to before each arm and walked under it crouched, the end walked out
		var path = lodge.CrawlPath;
		Vector3 Cell(int i)
		{
			var c = path[i];
			bool entry = i < 4;
			bool exit = i > path.Count - 6 && new[] { new Vector2I(64, -8), new Vector2I(63, -8), new Vector2I(62, -8), new Vector2I(61, -8), new Vector2I(61, -7) }.Contains(c);
			return lodge.ToGlobal(SkiLodge.CellCentre(c) + (entry || exit ? Vector3.Zero : SkiLodge.CrawlDown) + Vector3.Up * 0.05f);
		}
		var low = new System.Collections.Generic.HashSet<Vector2I>(SkiLodge.CrouchPipes);
		foreach (var (ac, _) in SkiLodge.ArmSpots) low.Add(ac);
		bool NearLow(int i) { foreach (var q in low) if (Mathf.Abs(q.X - path[i].X) + Mathf.Abs(q.Y - path[i].Y) <= 2) return true; return false; }
		bool stuck = false;
		async Task Walk(int from, int to)
		{
			for (int i = from; i <= to && i < path.Count && !stuck; i++)
			{
				await _bot.Crouch(NearLow(i) || (i > 0 && NearLow(i - 1)), ct);
				if (!await _bot.WalkTo(Cell(i), 0.3f, ct, giveUp: 8f)) { stuck = true; Check($"crawlspace: on to cell {path[i]} (#{i})", false, $"at {lodge.ToLocal(Player.GlobalPosition)}"); }
			}
		}
		await Walk(0, 7);
		Check("round 201's first turn: carried into the walls seamlessly", lodge.InMaze && lodge.CrawlShifts == 1, $"in maze {lodge.InMaze}");
		Check("in the walls the lodge freezes over", lodge.Frozen && Story.HasFlag(LodgeFlag.Frozen));
		int save = path.IndexOf(SkiLodge.CrawlSave);
		var arms = SkiLodge.ArmSpots.Select(a => path.IndexOf(a.cell)).Where(i => i > 0).OrderBy(i => i).ToList();
		foreach (int armAt in arms)
		{
			if (stuck) break;
			if (save > 7 && save < armAt && Story.Current < Checkpoint.Act23Crawlspace)
			{
				await _bot.Inside(Cell(save - 2), Cell(save), ct);
				await Walk(save - 1, save + 1);
				await _bot.Seconds(0.3, ct);
				Check("halfway: a save in the crawlspace (checkpoint 28)", Story.Current == Checkpoint.Act23Crawlspace, $"{Story.Current}");
			}
			int from = Mathf.Max(8, armAt - 3);
			await _bot.Crouch(true, ct);
			await _bot.Inside(Cell(from), Cell(from + 1), ct);
			if (armAt == arms[0] && lodge.CrawlTins.Count > 0 && IsInstanceValid(lodge.CrawlTins[0]) && lodge.CrawlTins[0].GlobalPosition.DistanceTo(Player.GlobalPosition) < 6f)
			{
				await _bot.UseIt(lodge.CrawlTins[0], ct);
				Check("a tin on the boards: picked up to throw", HeldProp.For(Player) is { Has: true });
			}
			await Walk(from, Mathf.Min(armAt - 1, path.Count - 1));
			if (armAt == arms[0] && HeldProp.For(Player) is { Has: true })
			{
				await _bot.Aim(Cell(Mathf.Min(armAt + 3, path.Count - 1)) + Vector3.Up * 0.8f, ct);
				int d0 = lodge.Arms[0].Distractions;
				await _bot.Tap(on => Input.ScriptedThrow = on, ct);
				await _bot.WaitUntil(() => lodge.Arms[0].Distractions > d0, 2.5, ct);
				Check("thrown on past the arm: it claws after the noise", lodge.Arms[0].Distractions > d0);
			}
			await Walk(armAt, Mathf.Min(armAt + 2, path.Count - 1));
		}
		Check("the arm through the boards, four times: under it, crouched, never grabbed", lodge.ArmsBurst == 4 && lodge.ArmGrabs == 0, $"burst {lodge.ArmsBurst}, grabs {lodge.ArmGrabs}");
		if (!stuck)
		{
			await _bot.Inside(Cell(path.Count - 8), Cell(path.Count - 7), ct);
			await Walk(path.Count - 7, path.Count - 1);
		}
		await _bot.Crouch(false, ct);
		Check("out at the dining hall's wall", !lodge.InMaze && lodge.CrawlShifts == 2, $"shifts {lodge.CrawlShifts}");
		await _bot.Aim(lodge.WardrobeUse.GlobalPosition, ct);
		await _bot.UseIt(lodge.WardrobeUse, ct);
		using (Fast(2)) await _bot.WaitUntil(() => lodge.WardrobeDown, 6, ct);
		Check("the back of a wardrobe: pushed over", lodge.WardrobeDown);
		using (Fast(2)) await _bot.Seconds(1.4, ct);   // (it goes over: the slit is clear once it's down)
		await _bot.Crouch(true, ct);
		await _bot.WalkTo(LL(SkiLodge.CellCentre(new Vector2I(61, -7)).X, 0.05f, -3.4f), 0.3f, ct, giveUp: 8f);
		await _bot.Crouch(false, ct);
		await _bot.WalkTo(LL(SkiLodge.CellCentre(new Vector2I(61, -7)).X, 0.05f, -2.2f), 0.3f, ct, () => Story.Current == Checkpoint.Act23Frozen, giveUp: 6f);
		await _bot.Crouch(false, ct);
		using (Fast(2)) await _bot.WaitUntil(() => Story.Current == Checkpoint.Act23Frozen, 4, ct);
		Check("out into the frozen dining hall (checkpoint 29)", Story.Current == Checkpoint.Act23Frozen && lodge.WindowsBroken > 10 && !lodge.ClockTicking, $"{Story.Current} at lodge-local {lodge.ToLocal(Player.GlobalPosition)}, windows {lodge.WindowsBroken}");
		Shot("frozen");
	}

	private async Task Act23e(CancellationToken ct)
	{
		await Control(ct, 40);
		var lodge = Lodge;
		Require("the lodge, frozen", lodge is { Frozen: true });
		await _bot.Inside(LL(2f, 0.05f, 6.5f), lodge.ToGlobal(SkiLodge.FrontDoorInside), ct);
		await _bot.WalkTo(LL(0.3f, 0.05f, 8.4f), 0.45f, ct, giveUp: 6f);
		await _bot.Aim(lodge.ToGlobal(SkiLodge.FrontDoorInside), ct);
		Check("the front doors: ajar now", _bot.Prompt == "Push the door open", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		using (Fast(2))
		{
			await _bot.WaitUntil(() => lodge.FinaleWendigo != null && lodge.FinaleWendigo.Visible, 12, ct);
			Check("a howl behind: it's on the balcony", lodge.FinaleWendigo is { Visible: true });
			await _bot.WaitUntil(() => lodge.FinaleLanded, 25, ct);
			Check("off the rail at them; they dive; it lands a few steps off", lodge.FinaleLanded && !lodge.FrontBroken);
			Shot("it_lands");
			await _bot.WaitUntil(() => lodge.OnFire, 20, ct);
			Check("the lantern in its face, struck away, the lodge catches", lodge.LanternThrust && lodge.LanternStruck && lodge.OnFire);
			await _bot.WaitUntil(() => lodge.FinaleBadgeSeen, 15, ct);
			Shot("what_it_wears");
			await _bot.WaitUntil(() => lodge.FrontBroken, 30, ct);
			Check("in the firelight what it wears (his badge, his name plate); \"Dad?\"; his father's voice: \"Come and see.\"",
				lodge.FinaleBadgeSeen && lodge.FinaleFatherAnswered && lodge.FinaleWendigo is { WearsMitchell: true });
			Check("it strikes the doors, again, and goes through them", lodge.FrontBroken && lodge.FinaleRams == 2);
			await _bot.WaitUntil(() => lodge.FinaleDone, 30, ct);
		}
		Check("at the splintered doorway, left and right (checkpoint 30)", lodge.FinaleDone && Story.Current == Checkpoint.Act23Finished, $"{Story.Current}");
		Shot("doorway");
	}

	/// <summary>Stands square in front of a drawer or cabinet (through its bathroom's door for a vanity) and opens it.</summary>
	private async Task SearchIt(SkiLodge lodge, Searchable sr, CancellationToken ct, bool leave = true)
	{
		float floorY = lodge.ToLocal(sr.GlobalPosition).Y > 3f ? Upper : 0.05f;
		if (sr.Name.ToString().Contains("Vanity"))
		{
			// through the bathroom's door: jumped to its threshold, the step in walked
			var (outside, inside) = BathDoor(sr.Name.ToString());
			await _bot.Inside(lodge.ToGlobal(outside with { Y = floorY }), lodge.ToGlobal(inside with { Y = floorY }), ct);
			await _bot.WalkTo(lodge.ToGlobal(inside with { Y = floorY }), 0.4f, ct, giveUp: 5f);
		}
		Vector3 front = sr.Type == Searchable.Kind.Drawer ? new Vector3(0, 0, 1.0f) : new Vector3(0.55f, 0, 1.1f);
		var stand = sr.GlobalPosition + sr.GlobalBasis * front;
		stand.Y = lodge.ToGlobal(new Vector3(0, floorY, 0)).Y;
		var outDir = (sr.GlobalBasis * front).Normalized();
		float far = 1.9f;
		var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(sr.GlobalPosition + outDir * 0.3f + Vector3.Up * 0.2f, sr.GlobalPosition + outDir * 3f + Vector3.Up * 0.2f, 1));
		if (hit.Count > 0) far = Mathf.Clamp(((Vector3)hit["position"] - sr.GlobalPosition).Length() - 0.5f, front.Length(), 1.9f) / front.Length();
		var approach = sr.GlobalPosition + sr.GlobalBasis * (front * far);
		approach.Y = stand.Y;
		await _bot.WalkTo(approach, 0.4f, ct, giveUp: 10f);
		await _bot.WalkTo(stand, 0.35f, ct, giveUp: 10f);
		for (int tries = 0; tries < 3 && !sr.IsOpen; tries++) await _bot.UseIt(sr.Use, ct);
		await _bot.Seconds(0.6, ct);
		if (leave) await LeaveBath(lodge, sr, ct);
	}

	private async Task LeaveBath(SkiLodge lodge, Searchable sr, CancellationToken ct)
	{
		if (!sr.Name.ToString().Contains("Vanity")) return;
		var (outside, inside) = BathDoor(sr.Name.ToString());
		await _bot.Inside(lodge.ToGlobal(inside with { Y = Upper }), lodge.ToGlobal(outside with { Y = Upper }), ct);
		await _bot.WalkTo(lodge.ToGlobal(outside with { Y = Upper }), 0.4f, ct, giveUp: 5f);
	}

	private static (Vector3 outside, Vector3 inside) BathDoor(string name)
	{
		bool front = name.Contains("201") || name.Contains("202");
		float x = name.Contains("201") || name.Contains("204") ? SkiLodge.CorrX0 + 2.7f : SkiLodge.RoomSplitX + 2.7f;
		float z = front ? SkiLodge.CorrHalf + 1.65f : -SkiLodge.CorrHalf - 1.65f;
		return (new Vector3(x + 0.8f, 0, z), new Vector3(x - 0.9f, 0, z));
	}

	// ------------------------------------------------------------------ Act 24

	private async Task Act24(CancellationToken ct)
	{
		if (Phase > 0)
		{
			// the last burn handed the story to the trailhead: Act 25 begins at the snow's edge
			await _bot.WaitUntil(() => EndingDirector.Instance != null, 30, ct);
			Check("the wendigo finished: out into the trailhead's woods (Act 25)", EndingDirector.Instance != null && Story.Current == Checkpoint.Act24Finished && GetTree().CurrentScene?.SceneFilePath == StoryManager.TrailheadScene);
			return;
		}
		var lodge = _bot.FirstOf<SkiLodge>();
		using (Fast(2)) await _bot.WaitUntil(() => lodge?.Act24?.Cave is { Built: true }, 40, ct);
		var maze = lodge?.Act24;
		Require("the snow maze built under the lodge", maze?.Cave?.Layout != null);
		var cave = maze.Cave;
		var L = cave.Layout;
		await Control(ct, 20);
		// off the porch: it drives them to the drift
		await _bot.WalkTo(lodge.ToGlobal(new Vector3(0, 0.1f, SkiLodge.Apothem + 7.5f)), 0.8f, ct, giveUp: 15f);
		await _bot.WaitUntil(() => maze.Herder != null, 3, ct);
		Check("off the porch, the wendigo comes out of the trees behind them", maze.Herder != null);
		var hole = maze.GetNodeOrNull<Node3D>("Hole");
		Require("the hole in the drift", hole != null);
		await _bot.Teleport(hole.GlobalPosition + hole.GlobalBasis * new Vector3(0, 0, -3.5f), hole.GlobalPosition, ct, snapToTerrain: false);
		await _bot.WalkTo(hole.GlobalPosition + hole.GlobalBasis * new Vector3(0, 0, 0.6f), 0.4f, ct, () => maze.InMaze, giveUp: 10f);
		using (Fast(2)) await _bot.WaitUntil(() => maze.InMaze && Input.Enabled, 12, ct);
		Require("down the hole: the maze (checkpoint 31)", maze.InMaze && Story.Current == Checkpoint.Act24Maze, $"{Story.Current}");
		Check("never caught on the way: it drives, doesn't chase", !maze.HerdCaught);
		var hunter = maze.Hunter;
		Require("the wendigo hunts the maze", hunter?.Body != null && hunter.State != WendigoHunter.Mode.Off);
		Shot("maze");
		// its senses
		hunter.Paused = true;
		hunter.Calm();   // (it came down after them hunting: settled, so its senses start from nothing)
		Vector3 Node(int v) => cave.ToGlobal(L.Nodes[v] + Vector3.Up * 0.1f);
		var a = L.Solution[1];
		await _bot.Inside(Node(a), Node(L.Solution[2]), ct);
		hunter.Body.GlobalPosition = Node(a) + (Node(L.Solution[2]) - Node(a)).Normalized() * 5.5f;
		hunter.Body.LookAt(Node(L.Solution[2]) + (Node(L.Solution[2]) - Node(a)), Vector3.Up);
		await _bot.Crouch(true, ct);
		int heard0 = hunter.Heard;
		// (held where it's put, facing where it's put: its senses work, its feet don't - its patrol turning round onto them made this a coin toss)
		hunter.HoldStill = true;
		hunter.Paused = false;
		await _bot.PushFor(new Vector2(0.4f, 0), 1.5, ct);
		Check("crouched behind it: unheard, unseen", hunter.Heard == heard0 && hunter.State is not (WendigoHunter.Mode.Chase or WendigoHunter.Mode.Kill or WendigoHunter.Mode.Ambush) && !PlayerDeath.Dying, $"{hunter.State}, heard {hunter.Heard - heard0}");
		hunter.Paused = true;
		await _bot.Crouch(false, ct);
		// running, away from it (behind it, it hears half as well; running carries far all the same)
		await _bot.Aim(Node(L.Solution[0]) + Vector3.Up * 1.5f, ct);
		hunter.Paused = false;
		Input.ScriptedRun = true; Input.ScriptedMove = new Vector2(0, 1);
		await _bot.WaitUntil(() => hunter.Heard > heard0, 4, ct);
		_bot.Release();
		Check("running: it hears them, comes to look", hunter.Heard > heard0 && hunter.State is WendigoHunter.Mode.Investigate or WendigoHunter.Mode.Chase, $"{hunter.State}, heard {hunter.Heard - heard0}");
		hunter.Paused = true;
		hunter.HoldStill = false;
		hunter.Body.GlobalPosition = Player.GlobalPosition + (Player.GlobalPosition - Node(L.Solution[0])).Normalized() * -7f;
		hunter.Body.LookAt(Player.GlobalPosition with { Y = hunter.Body.GlobalPosition.Y }, Vector3.Up);
		Check("in its cone, in the open, seven metres off: it sees them", hunter.CanSee(cave.ToLocal(Player.GlobalPosition)));
		// a flare taken and thrown: it goes to it
		var flares = MazeExtras.Flares;
		Check("green flares along the way", flares.Count >= 2 && flares.TrueForAll(f => IsInstanceValid(f) && !f.Out), $"{flares.Count}");
		if (flares.Count > 0)
		{
			var back = Player.GlobalPosition;
			var f0 = flares[0];
			await _bot.Inside(f0.GlobalPosition + (back - f0.GlobalPosition).Normalized() * 1.5f, f0.GlobalPosition, ct);
			await _bot.UseIt(f0, ct);
			var held = HeldFlare.For(Player);
			Check("a flare in hand, burning green", held is { Lit: true });
			if (held is { Lit: true })
			{
				int pn = L.NearestConnected(cave.ToLocal(Player.GlobalPosition));
				int nb = L.Adj[pn][0];
				await _bot.Aim(cave.ToGlobal(L.Nodes[nb] + Vector3.Up * 1.8f), ct);
				int lured = hunter.Lured;
				hunter.Body.GlobalPosition = cave.ToGlobal(L.Nodes[L.Adj[nb].Find(x => x != pn) is int far && far != 0 ? far : nb]);
				hunter.Body.LookAt(hunter.Body.GlobalPosition + (hunter.Body.GlobalPosition - Player.GlobalPosition) with { Y = 0 }, Vector3.Up);
				hunter.Paused = false;
				await _bot.Tap(on => Input.ScriptedThrow = on, ct);
				if (held.Lit) held.Throw();
				await _bot.WaitUntil(() => hunter.Lured > lured, 4, ct);
				Check("thrown: it lands burning, and the wendigo goes to it", hunter.Lured > lured, $"{hunter.State}");
			}
			hunter.Paused = true;
			hunter.Calm();
		}
		// a hollow: crouched in it, unseen; breath held, it sniffs and moves on
		if (MazeExtras.Hollows.Count > 0)
		{
			var (lintel, mouth, dir) = MazeExtras.Hollows[0];
			await _bot.Inside(cave.ToGlobal(lintel + dir * 1.9f + Vector3.Up * 0.1f), cave.ToGlobal(mouth), ct);
			await _bot.Crouch(true, ct);
			await _bot.Seconds(0.5, ct);
			bool hidden = MazeExtras.PlayerHidden(cave, Player);
			hunter.Body.GlobalPosition = cave.ToGlobal(mouth - dir * 2.5f);
			hunter.Body.LookAt(cave.ToGlobal(lintel + dir * 2f) with { Y = hunter.Body.GlobalPosition.Y }, Vector3.Up);
			Check("crouched in a hollow: it can't see them", hidden && !hunter.CanSee(cave.ToLocal(Player.GlobalPosition)));
			int sniffed = hunter.SniffedHollows;
			hunter.Paused = false;
			hunter.Lure(cave.ToGlobal(mouth), 99f, 0.5f, "the test");
			Input.ScriptedBreath = true;
			using (Fast(2)) await _bot.WaitUntil(() => hunter.SniffedHollows > sniffed && (hunter.State != WendigoHunter.Mode.Investigate || PlayerDeath.Dying), 10, ct);
			Input.ScriptedBreath = false;
			Check("breath held: it sniffs at the mouth and moves on", hunter.SniffedHollows > sniffed && !PlayerDeath.Dying, $"{hunter.State}");
			await _bot.Crouch(false, ct);
		}
		hunter.Paused = true;
		hunter.Calm();
		hunter.Body.GlobalPosition = Node(L.Nodes.Count - 1);
		// the right way through: jumped to the junction before the cavern, the last tunnel walked
		int lastJunction = L.Solution[L.Solution.Count - 2];
		var mouthAt = cave.ToGlobal(L.EndAt(lastJunction, L.Cavern) + (SnowMazeLayout.CavernCentre - L.EndAt(lastJunction, L.Cavern)).Normalized() * 2f);
		await _bot.Inside(Node(lastJunction), mouthAt, ct);
		await _bot.WalkTo(mouthAt, 0.9f, ct, () => maze.InCavern, giveUp: 25f);
		await _bot.WaitUntil(() => Story.Current == Checkpoint.Act24Trenches, 3, ct);
		Check("into the cavern of trenches (checkpoint 32)", maze.InCavern && Story.Current == Checkpoint.Act24Trenches, $"{Story.Current}");
		await _bot.Inside(cave.ToGlobal(maze.CrowbarLocal + new Vector3(0.8f, 0.2f, 0)), cave.ToGlobal(maze.CrowbarLocal), ct);
		await _bot.Aim(cave.ToGlobal(maze.CrowbarLocal), ct);
		await _bot.Press(ct);
		await _bot.Seconds(0.5, ct);
		Check("the crowbar in the trenches", Inv.HasTool(ToolKind.Crowbar));
		var crate = cave.ToGlobal(maze.CrateLocal);
		await _bot.Inside(cave.ToGlobal(maze.CrateLocal + Vector3.Back * 1.3f) + Vector3.Up * 0.2f, crate, ct);
		await _bot.Aim(crate + Vector3.Up * 0.4f, ct);
		Check("the crate: prise it open", _bot.Prompt == "Prise the crate open", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		using (Fast(2)) await _bot.WaitUntil(() => Inv.HasTool(ToolKind.Flamethrower) && Input.Enabled, 15, ct);
		Check("the flamethrower (checkpoint 33)", Inv.HasTool(ToolKind.Flamethrower) && Story.Current == Checkpoint.Act24Flamethrower, $"{Story.Current}");
		var ft = Player.GetNodeOrNull<Flamethrower>("Flamethrower");
		Require("the flamethrower in their hands", ft is { Active: true });
		Shot("flamethrower");
		await _bot.Aim(Player.GlobalPosition + (-Player.CameraRig.GlobalBasis.Z) * 8f + Vector3.Up * 0.5f, ct);
		ft.ScriptedFire = true;
		await _bot.Seconds(0.8, ct);
		Check("its fire: burning napalm arcing out", ft.Stream.Alight > 10 && ft.Firing, $"{ft.Stream.Alight} alight");
		using (Fast(3))
		{
			await _bot.WaitUntil(() => ft.Overheated, 10, ct);
			Check("held too long it locks hot", ft.Overheated);
			ft.ScriptedFire = false;
			await _bot.WaitUntilGame(() => !ft.Overheated, 9, ct);
			Check("vents, then ready", !ft.Overheated);
		}
		ft.ScriptedFire = false;
		// four burns; the last hands the story on to the trailhead
		_s.ExpectReload = false;
		hunter.Paused = false;
		for (int burn = 1; burn <= hunter.HitsToKill && !hunter.Dead; burn++)
		{
			await _bot.WaitUntil(() => Input.Enabled && !PlayerDeath.Dying, 10, ct);
			hunter.Paused = true;
			var at = Player.GlobalPosition + (-Player.CameraRig.GlobalBasis.Z) with { Y = 0 } * 6f;
			hunter.Body.GlobalPosition = at with { Y = Player.GlobalPosition.Y - 0.05f };
			hunter.Body.LookAt(Player.GlobalPosition with { Y = hunter.Body.GlobalPosition.Y }, Vector3.Up);
			await _bot.Aim(hunter.Body.GlobalPosition + Vector3.Up * 1.6f, ct);
			using (Fast(3)) await _bot.WaitUntilGame(() => !ft.Overheated, 9, ct);
			hunter.HoldStill = true;
			hunter.Paused = false;
			int before = hunter.Hits;
			if (burn == hunter.HitsToKill) _s.ExpectReload = true;
			ft.ScriptedFire = true;
			await _bot.WaitUntil(() => hunter.Hits > before || ft.Overheated, 5, ct);
			ft.ScriptedFire = false;
			hunter.HoldStill = false;
			Check($"burn {burn}: the fire on it", hunter.Hits == before + 1, $"{hunter.Hits} hits");
			if (burn == 1 && !hunter.Dead)
			{
				var openAt = Player.GlobalPosition;
				var openLook = openAt + (-Player.CameraRig.GlobalBasis.Z) with { Y = 0 } * 6f;
				using (Fast(2)) await _bot.WaitUntil(() => hunter.State == WendigoHunter.Mode.Hide, 30, ct);
				Check("burned, it runs to hide in a dead end", hunter.State == WendigoHunter.Mode.Hide && hunter.HideNode >= 0);
				var near = hunter.HideApproach(5.5f);
				await _bot.Inside(near with { Y = near.Y + 0.1f }, hunter.Body.GlobalPosition, ct);
				await _bot.WaitUntil(() => hunter.HideAmbushes > 0, 5, ct);
				Check("tracked down, it comes out of hiding at them", hunter.HideAmbushes > 0);
				hunter.Paused = true;
				await _bot.Inside(openAt, openLook, ct);
			}
			using (Fast(2)) await _bot.Seconds(2.0, ct);
		}
		Check("four burns: it's finished (checkpoint 34)", hunter.Dead && Story.Current == Checkpoint.Act24Finished, $"{Story.Current}");
		Shot("finished");
		// (Rev 5) the mask burns off; the stalker stands in the flames and is gone; the badge in the ash, E, and out
		_s.ExpectReload = false;
		using (Fast(2)) await _bot.WaitUntil(() => World.SnowMaze.Act25Ending.BadgeUse != null && Input.Enabled, 50, ct);
		Check("the mask burns off: the stalker stands in the flames (photos haunted from here)", World.SnowMaze.Act25Ending.Revealed && Story.HasFlag(PhotoLog.StalkerFlag));
		Require("Mitchell's badge in the ash, to take", World.SnowMaze.Act25Ending.BadgeUse != null);
		Shot("badge_in_ash");
		_s.ExpectReload = true;
		await _bot.UseAround(World.SnowMaze.Act25Ending.BadgeUse, ct, 1.3f, snapToTerrain: false);
		Check("E takes the badge", World.SnowMaze.Act25Ending.BadgeTaken && Story.HasFlag(World.SnowMaze.Act25Ending.BadgeFlag));
		using (Fast(2)) await ExpectReload(ct, 60);
	}

	private async Task Act24Death(CancellationToken ct)
	{
		await Control(ct, 40);
		var maze = Act24Maze.Instance;
		using (Fast(2)) await _bot.WaitUntil(() => maze?.Hunter?.Body != null, 20, ct);
		Require("in the maze at its save, the wendigo hunting", maze is { InMaze: true } && maze.Hunter?.Body != null && Story.Current == Checkpoint.Act24Maze);
		if (Phase > 0)
		{
			Check("caught: back at the maze's save, the death counted", Story.Current == Checkpoint.Act24Maze && maze.InMaze && PlayerDeath.Deaths >= 1 && Story.TotalDeaths >= 1, $"deaths {Story.TotalDeaths}");
			return;
		}
		var hunter = maze.Hunter;
		var fwd = (-Player.CameraRig.GlobalBasis.Z) with { Y = 0 };
		hunter.Paused = true;
		hunter.Body.GlobalPosition = Player.GlobalPosition + fwd.Normalized() * 4f;
		hunter.Body.LookAt(Player.GlobalPosition with { Y = hunter.Body.GlobalPosition.Y }, Vector3.Up);
		hunter.Paused = false;
		hunter.Lure(Player.GlobalPosition, 99f, 0.5f, "the test");
		_s.ExpectReload = true;
		using (Fast(2)) await _bot.WaitUntil(() => PlayerDeath.Dying, 20, ct);
		Check("standing in the open in front of it: it found them", PlayerDeath.Dying, $"{hunter.State}");
		await ExpectReload(ct, 40);
	}
}
