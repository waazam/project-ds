using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.UI;
using ProjectDS.World;
using ProjectDS.World.BunkerParts;

namespace ProjectDS.Systems;

/// <summary>The Hollow's woods: Acts 3-12.</summary>
public partial class Playtest
{
	private ForestTerrain Terrain => GroundSnap.FindTerrain(this);

	/// <summary>A point on the trail <paramref name="metres"/> before (negative: after) the trail point nearest <paramref name="target"/>.</summary>
	private Vector3 TrailBefore(Vector3 target, float metres)
	{
		var t = Terrain;
		if (t == null) return target + Vector3.Back * metres;
		t.TrailDistance(target.X, target.Z, out float s);
		return t.TrailPoint(Mathf.Max(0f, s - metres), out _);
	}

	// ------------------------------------------------------------------ Act 3

	private async Task Act3(CancellationToken ct)
	{
		await Control(ct, 40);
		var lantern = _bot.Pickups(ToolKind.Lantern).FirstOrDefault();
		var compass = _bot.Pickups(ToolKind.Compass).FirstOrDefault();
		Require("the lantern and the compass wait at the camp", lantern != null && compass != null);
		// the last few steps into the camp: its checkpoint
		await _bot.ApproachAndWalk(lantern.GlobalPosition, 6f, 2.0f, ct, from: TrailBefore(lantern.GlobalPosition, 8f));
		await _bot.WaitUntil(() => Story.Current >= Checkpoint.Act3DoorBoarded, 5, ct);
		Check("checkpoint 3 on reaching the camp, saved", Story.Current == Checkpoint.Act3DoorBoarded && SaveSystem.Load()?.Checkpoint == Checkpoint.Act3DoorBoarded, $"{Story.Current}");
		var note = _bot.AllOf<Readable>().OrderBy(r => r.GlobalPosition.DistanceTo(lantern.GlobalPosition)).FirstOrDefault();
		if (note != null && note.GlobalPosition.DistanceTo(lantern.GlobalPosition) < 6f) await ReadIt(note, ct);
		else Check("R.H.'s note at the camp", false);
		await _bot.Seconds(0.5, ct);
		Check("the camp note read (no code tracker on the HUD: the player remembers the digits)", Story.HasFlag(Camp.NoteReadFlag));
		await _bot.UseIt(lantern, ct);
		await _bot.UseIt(compass, ct);
		Check("lantern and compass picked up", Inv.HasLantern && Inv.HasCompass);
		Check("the compass shows at the top of the screen", _bot.FirstOf<Compass>() is { ShowingCompass: true });
		CheckObjective("the compass points at the cabin", "cabin");
		Shot("camp");
		// the inventory: the wheel moves through what's carried
		var hud = GetTree().GetFirstNodeInGroup("equipped_item_hud") as EquippedItemHud;
		await Control(ct, 8);
		var held0 = hud?.Held ?? ToolKind.None;
		await _bot.Tap(on => Input.ScriptedItemNext = on, ct);
		Check("the inventory: the wheel moves to the next thing carried", hud != null && hud.Held != held0 && Inv.OwnedItems().Count >= 3, $"{held0} -> {hud?.Held}, carrying {string.Join(", ", Inv.OwnedItems().Select(i => i.Label))}");
		// (2026-10-13j) the right hand really holds it: the lantern put the camera away, the wheel brought it back up
		await _bot.Seconds(0.7, ct);
		Check("the hands: the lantern put the camera away, the wheel brought it back to the right hand", held0 == ToolKind.None && Inv.InHand == ToolKind.Camera
			&& PlayerHands.Instance?.Tool is { Shown: ToolKind.Camera, Raise: > 0.9f },
			$"{held0} -> {Inv.InHand}, shown {PlayerHands.Instance?.Tool?.Shown}");
		await _bot.Tap(on => Input.ScriptedItemPrev = on, ct);   // (put away again: right mouse is the lantern's beam below)
		await _bot.Seconds(0.6, ct);
		Check("the inventory saves and restores what's carried", Inv.Serialize().StartsWith("lantern,compass,camera"), Inv.Serialize());
		// the lantern: F, the flame; its first few seconds lit it gutters (the blacklight still works), and takes again
		var lamp = Player.GetNodeOrNull<Lantern>("Lantern");
		Require("the lantern in hand", lamp != null);
		bool on0 = lamp.IsOn;
		await _bot.Tap(on => Input.ScriptedLight = on, ct);
		await _bot.Frames(3, ct);
		Check("F turns the lantern off and on", lamp.IsOn != on0, $"{on0} -> {lamp.IsOn}");
		if (!lamp.IsOn) await _bot.Tap(on => Input.ScriptedLight = on, ct);
		using (Fast(3))
		{
			await _bot.WaitUntil(() => Lantern.Guttered && !lamp.Dying, 10, ct);
			Check("the flame gutters out after its first seconds", Lantern.Guttered && !lamp.Shining, $"guttered {Lantern.Guttered}");
			await _bot.LanternMode(true, ct);
			await _bot.Seconds(0.5, ct);
			Check("B: the blacklight still works", lamp is { Shining: true, Blacklight: true } && Lantern.Uv.on);
			await _bot.WaitUntil(() => !Lantern.Guttered, 8, ct);
			await _bot.LanternMode(false, ct);
			await _bot.Seconds(0.4, ct);
			Check("used a while, the wick takes again", !Lantern.Guttered && lamp is { Shining: true, Blacklight: false });
		}
		// focus (right mouse): the beam narrows
		Input.ScriptedFocus = true;
		await _bot.Seconds(0.6, ct);
		Check("holding focus narrows the lantern's beam", lamp.FocusAmount > 0.5f, $"{lamp.FocusAmount:0.00}");
		Input.ScriptedFocus = false;
		// out of the safe zone: the storm
		var camp = _bot.FirstOf<Camp>();
		if (camp != null && Terrain is { } t)
		{
			t.TrailDistance(camp.GlobalPosition.X, camp.GlobalPosition.Z, out float sc);
			var inside = t.TrailPoint(sc + 11f, out _);
			var outside = t.TrailPoint(sc + 24f, out _);
			await _bot.Teleport(inside, outside, ct);
			await _bot.WalkTo(outside, 1.5f, ct, stopWhen: () => Story.HasFlag(StoryManager.Flag.StormStarted), giveUp: 10f);
			await _bot.WaitUntil(() => Story.HasFlag(StoryManager.Flag.StormStarted), 3, ct);
			Check("stepping out of the camp's safe zone starts the storm", Story.HasFlag(StoryManager.Flag.StormStarted) && StormController.Instance is { Active: true });
			await _bot.WaitUntil(() => (ForestAmbienceManager.Instance?.Silence ?? 0f) > 0.8f, 8, ct);
			Check("the forest goes silent under the rain", (ForestAmbienceManager.Instance?.Silence ?? 0f) > 0.8f, $"silence {ForestAmbienceManager.Instance?.Silence:0.00}");
		}
	}

	// ------------------------------------------------------------------ Act 4

	private async Task Act4(CancellationToken ct)
	{
		await Control(ct, 40);
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		var lot = GetTree().GetFirstNodeInGroup("survey_lot") as SurveyLot;
		Require("the cabin and R.H.'s two arrow trails", cabin != null && lot != null && lot.Trail(1).Count > 0 && lot.Trail(2).Count > 0, $"{lot?.Notes.Count} arrow trees");
		Check("the storm rages (restored from the save)", StormController.Instance is { Active: true });
		await TakeKeyHalf(lot, 1, ct);
		// the stalker's introduction, and the giant's far footfalls, on a short walk toward the cabin
		var st = GetTree().GetFirstNodeInGroup("stalker") as Stalker;
		var giant = _bot.FirstOf<GiantStalkerEvent>();
		Check("the stalker stalks the storm walk", st is { Intro: true }, $"{st?.Current}");
		var t = Terrain;
		Require("the trail", t != null);
		t.TrailDistance(cabin.GlobalPosition.X, cabin.GlobalPosition.Z, out float sCabin);
		var from = t.TrailPoint(sCabin - 70f, out _);
		await _bot.Teleport(from, t.TrailPoint(sCabin - 60f, out _), ct);
		for (float s = sCabin - 64f; s < sCabin - 20f; s += 6f)
			if (!await _bot.WalkTo(t.TrailPoint(s, out _), 2.5f, ct, () => st != null && st.PeekCount >= 1 && st.ShadowSpells >= 1 && giant is { FarFootfallBouts: >= 1 })) break;
		using (Fast(2)) await _bot.WaitUntil(() => st == null || st.PeekCount >= 1 && giant is { FarFootfallBouts: >= 1 }, 10, ct);
		Check("it shows itself on the walk, and its steps answer theirs", st is { PeekCount: >= 1 }, $"peeks {st?.PeekCount}, spells {st?.ShadowSpells}, sectors {st?.DirectionsUsed}");
		Check("it never snarls", st is { SnarlCount: 0 });
		Check("the giant's far footfalls, never its body", giant is { FarFootfallBouts: >= 1 } && TestBot.FindUnder<Node3D>(GetTree().CurrentScene, "Act7Giant") == null, $"bouts {giant?.FarFootfallBouts}");
		CheckObjective("the compass still points at the cabin", "cabin");
		Shot("storm_walk");
	}

	/// <summary>(2026-10-15) Half <paramref name="piece"/> of the key: the trail's first arrow (nothing by the flame, the arrow by
	/// the blacklight), its last, then the tin at the end glinting under the beam, opened, the half taken and saved.</summary>
	private async Task TakeKeyHalf(SurveyLot lot, int piece, CancellationToken ct)
	{
		var trail = lot.Trail(piece);
		var cache = lot.Cache(piece);
		Require($"trail {piece} and its cache", trail.Count > 0 && cache != null);
		foreach (var note in new[] { trail[0], trail[^1] })
		{
			Vector3 near = note.PlateWorld + TestBot.Flat(note.PlateWorld - note.GlobalPosition).Normalized() * 2.4f;
			near.Y = note.GlobalPosition.Y;
			await _bot.LanternMode(false, ct);
			await _bot.Teleport(near, note.PlateWorld, ct);
			await _bot.Aim(note.PlateWorld, ct);
			await _bot.Seconds(0.5, ct);
			Check($"arrow {note.Label}: nothing to see by the flame", !note.Revealed, $"glow {note.Glow:0.00}");
			await _bot.LanternMode(true, ct);
			await _bot.Aim(note.PlateWorld, ct);
			await _bot.WaitUntil(() => note.Revealed, 4, ct);
			Check($"arrow {note.Label} shows under the blacklight", note.Revealed, $"glow {note.Glow:0.00}");
			if (piece == 1 && note == trail[0]) Shot("blacklight_arrow");
		}
		Vector3 c = cache.TinWorld;
		await _bot.Teleport(c + TestBot.Flat(trail[^1].GlobalPosition - c).Normalized() * 1.6f, c, ct);
		await _bot.Aim(c, ct);
		await _bot.WaitUntil(() => cache.Glint > 0.3f, 3, ct);
		Check($"half {piece}'s tin glints under the blacklight", cache.Glint > 0.3f, $"glint {cache.Glint:0.00}");
		Shot($"key_half_{piece}");
		await _bot.LanternMode(false, ct);
		await _bot.Aim(c, ct);
		Check($"the tin offers itself", _bot.Prompt == "Open the tin", $"'{_bot.Prompt}'");
		await _bot.Press(ct);
		await _bot.WaitUntil(() => Story.HasFlag(StoryManager.Flag.KeyPiece(piece)), 4, ct);
		Check($"half {piece} taken, saved", cache.Taken && Story.HasFlag(StoryManager.Flag.KeyPiece(piece)));
	}

	// ------------------------------------------------------------------ Act 5

	private async Task Act5Hammer(CancellationToken ct)
	{
		await Control(ct, 40);
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		Require("the cabin", cabin != null);
		Check("its door boarded shut", cabin.DoorBoarded && !cabin.IsOpen);
		await _bot.Teleport(cabin.WideApproachPoint, cabin.DoorCenter, ct);
		await _bot.WalkTo(cabin.ApproachPoint, 1f, ct);
		await _bot.Aim(cabin.DoorCenter, ct);
		Check("empty-handed: the door is boarded shut", _bot.Prompt == "The door is boarded shut.", $"'{_bot.Prompt}'");
		var hammer = _bot.Pickups(ToolKind.Hammer).FirstOrDefault();
		Require("the hammer in the shed", hammer != null);
		// walked in on foot from the yard, not set down beside it (the doorway's sill once stood as a ledge)
		if (GetTree().CurrentScene.FindChild("Shed", true, false) is Shed shed)
		{
			Vector3 inShed = shed.GlobalTransform * new Vector3(0, 0.1f, 0f), yard = shed.GlobalTransform * new Vector3(0, 0, shed.Depth * 0.5f + 3f);
			await _bot.Teleport(yard, inShed, ct);
			bool walkedIn = await _bot.WalkTo(inShed, 0.5f, ct, giveUp: 10f);
			Check("the shed walked into from the yard", walkedIn, $"stopped at {_bot.Player.GlobalPosition}, wanted {inShed}");
		}
		await _bot.UseAround(hammer, ct);
		Check("the hammer picked up", Inv.HasTool(ToolKind.Hammer));
		await _bot.Teleport(cabin.ApproachPoint, cabin.DoorCenter, ct);
		await _bot.WalkTo(cabin.DoorCenter, 1.6f, ct);
		await _bot.Aim(cabin.DoorCenter, ct);
		Check("with the hammer: pry the boards loose (a hold)", _bot.Prompt == "Pry the boards loose", $"'{_bot.Prompt}'");
		using (Fast(3))
		{
			await _bot.Press(ct, hold: 9.6);
			await _bot.WaitUntil(() => cabin.IsOpen, 6, ct);
		}
		Check("held long enough, the boards come away: the door open, the hammer used up", cabin.IsOpen && !Inv.HasTool(ToolKind.Hammer) && Story.HasFlag(StoryManager.Flag.CabinDoorOpen));
		Shot("door_open");
		// in: checkpoint 4, the knocks, the slam
		Vector3 inside = cabin.ToGlobal(new Vector3(0.3f, 0f, -0.8f));
		await _bot.WalkTo(cabin.DoorCenter, 0.6f, ct);
		await _bot.WalkTo(inside, 1f, ct);
		await _bot.WaitUntil(() => Story.Current >= Checkpoint.Act5CabinEntered, 10, ct);
		Check("checkpoint 4 on stepping inside", Story.Current == Checkpoint.Act5CabinEntered);
		var reveal = _bot.FirstOf<FriendReveal>();
		await _bot.WaitUntil(() => reveal is { Knocked: true }, 3, ct);
		Check("three knocks on the wall as they come in", reveal is { Knocked: true });
		await _bot.WaitUntil(() => cabin.SlammedShut && cabin.DoorReopen != null, 3, ct);
		Check("the door slams shut behind them", cabin.SlammedShut);
		Check("the rain is muffled inside", StormController.Instance is { RainMuffled: true });
		var post = _bot.Pickups(ToolKind.NewelPost).FirstOrDefault();
		Require("the cap (the newel post) on the table", post != null);
		await _bot.WalkTo(post.GlobalPosition, 1.1f, ct);
		await _bot.UseIt(post, ct);
		Check("the cap in hand", Inv.HasNewelPost && Story.NewelPostTaken);
		await _bot.Seconds(0.5, ct);
		Check("the storm breaks with the cap taken", StormController.Instance is not { Active: true } && Story.HasFlag(StoryManager.Flag.DawnBroke));
		CheckObjective("the compass swings to the footbridge", "bridge_marker");
		Shot("cabin_inside");
		await _bot.WalkTo(cabin.DoorCenter, 1.5f, ct);
		if (cabin.DoorReopen != null) await _bot.UseIt(cabin.DoorReopen, ct);
		await _bot.Seconds(0.6, ct);
		Check("E opens the slammed door again", cabin.DoorReopen == null);
		// the clearing's stairs, its ring and the blood rain come into being as the door opens with the cap
		var act6 = _bot.FirstOf<Act6ClearingEvent>();
		await _bot.WaitUntil(() => act6 is { Spawned: true }, 3, ct);
		Check("the door opened with the cap: the clearing's stairs, ring and blood rain spawn (none of it at the cabin)", act6 is { Spawned: true, MiniStairCount: 15, Fenced: true, BloodRaining: true, BloodPresence: < 0.05f },
			$"spawned {act6?.Spawned}, minis {act6?.MiniStairCount}, blood {act6?.BloodRaining} presence {act6?.BloodPresence:0.00}");
		await _bot.WalkTo(cabin.DoorCenter, 0.6f, ct);
		await _bot.WalkTo(cabin.GlobalTransform * new Vector3(-6f, 0f, 3.5f), 1f, ct);
		await _bot.WaitUntil(() => Story.HasFlag(StoryManager.Flag.DawnBroke), 8, ct);
		Check("out with the cap: the rain stops, still night", Story.HasFlag(StoryManager.Flag.DawnBroke) && StormController.Instance is not { Active: true } && StoryBeat.Atmosphere(this)?.CurrentMood == ForestAtmosphere.Mood.Night);
	}

	private async Task Act5Axe(CancellationToken ct)
	{
		await Control(ct, 40);
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		var axe = _bot.Pickups(ToolKind.Axe).FirstOrDefault();
		Require("the cabin, and the axe round its far side", cabin != null && axe != null);
		await _bot.Teleport(axe.GlobalPosition + TestBot.Flat(axe.GlobalPosition - cabin.GlobalPosition).Normalized() * 3f, axe.GlobalPosition, ct);
		await _bot.WalkTo(axe.GlobalPosition, 1.3f, ct);
		await _bot.UseIt(axe, ct);
		Check("the axe taken from the chopping block", Inv.HasTool(ToolKind.Axe));
		await _bot.Teleport(cabin.ApproachPoint, cabin.DoorCenter, ct);
		await _bot.WalkTo(cabin.DoorCenter, 1.6f, ct);
		await _bot.Aim(cabin.DoorCenter, ct);
		Check("with the axe: chop the boards", _bot.Prompt == "Chop the boards with the axe", $"'{_bot.Prompt}'");
		await _bot.Press(ct, hold: 0.3);
		using (Fast(2)) await _bot.WaitUntil(() => cabin.IsOpen, 8, ct);
		Check("the axe breaks the boards, and is used up", cabin.IsOpen && !Inv.HasTool(ToolKind.Axe));
	}

	// ------------------------------------------------------------------ the creek, not by the bridge

	/// <summary>
	/// (2026-10-13, Dan: events must fire "even if character doesnt cross the bridge") Over the creek well beside the
	/// footbridge, never touching its deck: from the near bank <paramref name="offset"/> m to one side of it, wading
	/// straight across; if the water or the banks stop the bot, a teleport to the far bank (a jump, a way round) stands
	/// in for it. Returns how it got over ("waded" / "teleported").
	/// </summary>
	private async Task<string> CrossCreekBesideBridge(Footbridge fb, System.Func<bool> done, CancellationToken ct, float offset = 14f)
	{
		var t = Terrain;
		Require("the footbridge and its creek", fb != null && t != null && fb.HasCrossingLine);
		t.TrailDistance(fb.GlobalPosition.X, fb.GlobalPosition.Z, out float sb);
		Vector3 c = t.TrailPoint(sb, out Vector3 dir);
		Vector3 side = dir.Cross(Vector3.Up).Normalized() * offset;
		// (whichever side of the bridge the creek runs straight across; in the Hollow it bends away right at the bridge)
		if (fb.IsAcross(c - dir * 9f + side) || !fb.IsAcross(c + dir * 9f + side)) side = -side;
		Vector3 from = c - dir * 9f + side, to = c + dir * 9f + side;
		Check("the start, beside the bridge, is on the near side of the creek", !fb.IsAcross(from), $"{from}");
		Check("the goal, beside the bridge, is on the far side", fb.IsAcross(to), $"{to}");
		await _bot.Teleport(from, to, ct);
		await _bot.WalkTo(to, 1.5f, ct, done, giveUp: 6f);
		await _bot.Frames(15, ct);
		if (done()) return "waded";
		await _bot.Teleport(to, to + dir * 5f, ct);
		await _bot.Frames(15, ct);
		return "teleported";
	}

	/// <summary>Act 6 by the water: checkpoint 5, the compass and the clearing's turn without setting foot on the bridge.</summary>
	private async Task Act6Creek(CancellationToken ct)
	{
		await Control(ct, 40);
		var fb = _bot.FirstOf<Footbridge>();
		var deck = _bot.FirstOf<BridgeCrossEvent>();
		string how = await CrossCreekBesideBridge(fb, () => Story.Current >= Checkpoint.Act6BridgeCrossed, ct);
		await _bot.WaitUntil(() => Story.Current >= Checkpoint.Act6BridgeCrossed, 4, ct);
		Check($"checkpoint 5 across the creek beside the bridge ({how})", Story.Current == Checkpoint.Act6BridgeCrossed, $"{Story.Current}");
		Check("never on the deck", deck == null || StoryBeat.PlayerInside(deck) == null);
		CheckObjective("the compass points at the clearing", "stairs_clearing_marker");
		var act6 = _bot.FirstOf<Act6ClearingEvent>();
		await _bot.WaitUntil(() => act6 != null && act6.LoopVoiceCount >= 1, 12, ct);
		Check("across the creek the voice calls all the same", act6 != null && act6.VoiceCalling && act6.LoopVoiceCount >= 1);
		Shot("creek");
	}

	// ------------------------------------------------------------------ Act 6

	private async Task Act6(CancellationToken ct)
	{
		await Control(ct, 40);
		var bridge = GetTree().GetFirstNodeInGroup("bridge_marker") as Node3D;
		var t = Terrain;
		Require("the footbridge", bridge != null && t != null);
		t.TrailDistance(bridge.GlobalPosition.X, bridge.GlobalPosition.Z, out float sb);
		await _bot.Teleport(t.TrailPoint(sb - 10f, out _), bridge.GlobalPosition, ct);
		await _bot.WalkTo(bridge.GlobalPosition, 1.5f, ct);
		await _bot.WalkTo(t.TrailPoint(sb + 10f, out _), 1.5f, ct, () => Story.Current >= Checkpoint.Act6BridgeCrossed);
		await _bot.WaitUntil(() => Story.Current >= Checkpoint.Act6BridgeCrossed, 6, ct);
		Check("checkpoint 5 across the footbridge", Story.Current == Checkpoint.Act6BridgeCrossed);
		CheckObjective("the compass points at the clearing", "stairs_clearing_marker");
		var clearing = GetTree().GetFirstNodeInGroup("stairs_clearing_marker") as Node3D;
		var act6 = _bot.FirstOf<Act6ClearingEvent>();
		Require("the clearing", clearing != null && act6 != null);
		await _bot.WaitUntil(() => act6.LoopVoiceCount >= 1, 12, ct);
		Check("across the creek the voice calls: come up and see", act6.VoiceCalling && act6.LoopVoiceCount >= 1);
		// into the clearing (its last few metres walked)
		t.TrailDistance(clearing.GlobalPosition.X, clearing.GlobalPosition.Z, out float sClear);
		Vector3 centre = clearing.GlobalPosition;
		await _bot.Teleport(centre + (act6.EntranceWorld - centre).Normalized() * 14f, centre, ct);
		await _bot.WalkTo(centre, 6f, ct, () => act6.VoiceSpoken, giveUp: 10f);
		await _bot.WaitUntil(() => act6.VoiceSpoken, 8, ct);
		Check("the clearing's voice, and a red light finds them", act6.VoiceSpoken && act6.SpotlightActive && Story.ClearingVoiceHeard);
		Check("fifteen small staircases round the first", act6.MiniStairCount == 15, $"{act6.MiniStairCount}");
		Check("the clearing is closed but for the way in", act6.Fenced);
		Check("the cap stays in hand", Inv.HasNewelPost);
		using (Fast(2)) await _bot.WaitUntil(() => act6.LoopArmed, 12, ct);
		Check("then it waits for a choice: nothing lit", act6.LoopArmed && !act6.LoopStarted);
		Shot("clearing");
		// a flight chosen: up from the ground onto it
		var chosen = act6.LoopStairs.OrderBy(m => TestBot.Flat(m.GlobalPosition).DistanceTo(TestBot.Flat(Player.GlobalPosition))).FirstOrDefault();
		Require("whole flights to climb", chosen != null && act6.LoopStairs.Count == 10, $"{act6.LoopStairs.Count}");
		await _bot.Teleport(chosen.ToGlobal(new Vector3(0, 0.05f, 3.4f)), Act6ClearingEvent.FootOf(chosen), ct);
		await _bot.WalkTo(chosen.ToGlobal(new Vector3(0, 0.6f, -1.6f)), 0.6f, ct, () => act6.LoopStarted, giveUp: 8f);
		await _bot.WaitUntil(() => act6.LoopStarted, 2, ct);
		Require("stepping onto a flight starts the loop", act6.LoopStarted && act6.LoopTarget == chosen, $"{act6.LoopTarget?.Name}");
		CheckObjective("the light and the compass follow the flight", "clearing_loop_marker");
		Check("blood starts to rain", act6.BloodRaining);
		int legs = act6.LoopLegs;
		Check("four to six flights planned", legs is >= 4 and <= 6, $"{legs}");
		bool handOffs = true, blocked = false;
		for (int leg = 0; leg < legs && !act6.LoopDone; leg++)
		{
			await _bot.WaitUntil(() => !act6.LoopBusy || act6.LoopDone, 8, ct);
			if (act6.LoopDone || act6.LoopTarget == null) break;
			var stair = act6.LoopTarget;
			int stage = act6.LoopStage;
			if (leg == 0)
			{
				// a few treads up there's no going back down
				await _bot.WalkFor(act6.LoopTargetPoint, 0.8, ct);
				await _bot.WaitUntil(() => act6.OneWay is { Armed: true }, 2, ct);
				var b0 = Player.GlobalPosition;
				await _bot.PushFor(new Vector2(0, -1), 1.0, ct);
				blocked = act6.OneWay is { Armed: true } && TestBot.Flat(Player.GlobalPosition).DistanceTo(TestBot.Flat(b0)) < 0.5f;
			}
			// jumped up the flight to a few treads short of its top; the top walked
			int tread = Mathf.Max(0, stair.Steps - 4);
			var near = stair.ToGlobal(new Vector3(0, tread * stair.Rise + 0.1f, -tread * stair.Run));
			await _bot.Inside(near, act6.LoopTargetPoint, ct);
			await _bot.WalkTo(act6.LoopTargetPoint, 0.9f, ct, () => act6.LoopStage > stage, giveUp: 10f);
			await _bot.WaitUntil(() => act6.LoopStage > stage, 3, ct);
			if (act6.LoopStage == stage) { Check($"flight {leg + 1}: the top reached", false, $"{Player.GlobalPosition} vs {act6.LoopTargetPoint}"); break; }
			if (act6.LoopDone || act6.LoopStage >= legs) break;
			await _bot.WaitUntil(() => !act6.LoopBusy && act6.LoopTarget != stair, 5, ct);
			handOffs &= act6.LoopTarget != null && act6.LoopTarget != stair && Input.Enabled;
			if (leg == 0) Shot("loop_handoff");
		}
		Check("no backing down a flight", blocked);
		Check("at every top the fog hands them on to another flight, still walking", handOffs);
		using (Fast(3)) await _bot.WaitUntil(() => act6.LoopDone && Input.Enabled, 25, ct);
		Check("the last landing gives way: the fall, saved", act6.LoopDone && Story.HasFlag(StoryManager.Flag.ClearingLoopDone));
		Check("the blood stops with the fall", !act6.BloodRaining);
		Check("woken past the clearing: the stairs and the ring gone", act6 is { WorldReset: true, Fenced: false, MiniStairCount: 0 });
		CheckObjective("the compass points on to the lookout", "fire_lookout_marker");
		Shot("wake");
	}

	// ------------------------------------------------------------------ Act 7

	private async Task Act7(CancellationToken ct)
	{
		await Control(ct, 40);
		var lookout = GetTree().GetFirstNodeInGroup("fire_lookout_marker") as Node3D;
		var cabin = GetTree().GetFirstNodeInGroup("cabin") as Cabin;
		var fire = _bot.FirstOf<CabinFireEvent>();
		Require("the lookout, the cabin, its fire", lookout != null && cabin != null && fire != null);
		Check("the cabin burns already (they woke to it)", cabin.Burning > 0.3f, $"{cabin.Burning:0.00}");
		await _bot.ApproachAndWalk(lookout.GlobalPosition, 8f, 2.5f, ct, () => Story.Current >= Checkpoint.Act7CabinBurning, from: TrailBefore(lookout.GlobalPosition, 10f));
		await _bot.Aim(cabin.GlobalPosition + Vector3.Up * 2f, ct);
		using (Fast(2)) await _bot.WaitUntil(() => fire.Struck && Story.Current >= Checkpoint.Act7CabinBurning, 10, ct);
		Check("the burning cabin seen from the lookout: checkpoint 6", fire.Sighted && Story.Current == Checkpoint.Act7CabinBurning, $"struck {fire.Struck}, sighted {fire.Sighted}, {Story.Current}");
		Check("control kept through it", Input.Enabled);
		Shot("cabin_burning");
		var giant = _bot.FirstOf<GiantStalkerEvent>();
		await _bot.WaitUntil(() => giant?.Body != null || Story.GiantEventDone, 8, ct);
		Check("the giant crosses beyond the fire", giant?.Body != null || Story.GiantEventDone);
		if (giant?.Body is { } body)
		{
			await _bot.Aim((body as StalkerBody)?.EyesWorld ?? body.GlobalPosition + Vector3.Up * 40f, ct);
			Shot("giant");
		}
		using (Fast(4)) await _bot.WaitUntil(() => Story.GiantEventDone, 30, ct);
		Check("it passes and is gone: saved", Story.GiantEventDone && giant?.Body == null);
		CheckObjective("the compass points at the bunker", "bunker_marker");
		Vector3 fwd = cabin.GlobalBasis.Z.Normalized();
		Check("the burning cabin is sealed: no way in", cabin.Sealed && await _bot.RayHits(cabin.DoorCenter + fwd * 1.2f, cabin.DoorCenter - fwd * 1.2f, ct));
		var lot = GetTree().GetFirstNodeInGroup("survey_lot") as SurveyLot;
		if (lot != null) await TakeKeyHalf(lot, 2, ct);
		Check("both halves of the key before the bunker", SurveyLot.PiecesFound == 2, $"{SurveyLot.PiecesFound} of 2");
	}

	// ------------------------------------------------------------------ Acts 8-10

	private async Task Act8To10(CancellationToken ct)
	{
		await Control(ct, 40);
		var bunker = GetTree().GetFirstNodeInGroup("bunker_marker") as Bunker;
		var lot = GetTree().GetFirstNodeInGroup("survey_lot") as SurveyLot;
		Require("the bunker", bunker != null);
		await _bot.Teleport(bunker.ApproachPointWorld + (bunker.ApproachPointWorld - bunker.HatchWorld).Normalized() * 5f, bunker.HatchWorld, ct);
		await _bot.WalkTo(bunker.ToGlobal(new Vector3(0, 0.3f, 2.0f)), 0.6f, ct);
		Check("the hatch is shut", !bunker.IsOpen);
		// (2026-10-15) the keyhole in the door: both halves joined in the hand, then turned
		await _bot.Aim(bunker.KeyholeWorld, ct);
		Shot("keyhole");
		bool both = SurveyLot.PiecesFound == 2;
		Check("the door offers the key", !both || _bot.Prompt.Contains("key"), $"'{_bot.Prompt}'");
		int shown = HeldUse.Shown;
		await _bot.Press(ct);
		await _bot.WaitUntil(() => HeldUse.Shown > shown, 2, ct);
		Check("the halves come up in the hand", HeldUse.Last == ToolKind.BunkerKey, $"{HeldUse.Last}");
		await _bot.Seconds(1.0, ct);
		Shot("key_joining");
		await _bot.WaitUntil(() => bunker.IsOpen, 5, ct);
		Check("the key opens it, saved", bunker.IsOpen && Story.HasFlag(StoryManager.Flag.BunkerUnlocked) && Story.HasFlag(StoryManager.Flag.KeyAssembled));
		await _bot.WaitUntil(() => !HeldUse.Busy && !Input.Modal, 4, ct);
		Check("control comes back", !HeldUse.Busy && !Input.Modal);
		await _bot.WalkTo(bunker.EntryPointWorld, 0.3f, ct, () => Story.Current >= Checkpoint.Act8BunkerEntered);
		await _bot.WaitUntil(() => Story.Current >= Checkpoint.Act8BunkerEntered, 10, ct);
		Check("checkpoint 7 entering the bunker", Story.Current == Checkpoint.Act8BunkerEntered);
		var bi = BunkerInterior.Instance;
		Require("the interior", bi != null);
		await _bot.WaitUntil(() => Player.GlobalPosition.DistanceTo(bi.GlobalPosition) < 500f && Input.Enabled, 10, ct);
		Check("carried into the interior: indoors", ForestAmbienceManager.Instance?.IsIndoor ?? false);
		// Act 8: the hallway, run (time-compressed)
		using (Fast(2)) await _bot.WalkTo(bi.VineDoorApproachWorld + new Vector3(0, 0, -4f), 0.6f, ct, giveUp: 20f, run: true);
		Check("down the hallway the lights go red", bi.RedTriggered);
		Check("the lamps die, it's in their face, and gone", bi.JumpscareFired && bi.Hallway is { BlackoutCount: >= 1 });
		Shot("hallway");
		await _bot.Aim(bi.VineDoorInteractWorld, ct);
		await _bot.Press(ct);
		Check("the vine door opens", bi.VineDoorOpenState);
		await _bot.WalkTo(bi.CrtRoomInteriorWorld, 1.2f, ct);
		await _bot.Seconds(1.2, ct);
		Check("it swings shut behind them, locked", bi.VineDoor is { ShutBehind: true } && bi.Flow is { VineDoorUnlocked: false });
		// Act 9: the screens, the walkie
		await _bot.WalkTo(bi.CrtTargetApproachWorld, 1.0f, ct);
		await _bot.Aim(bi.CrtSwitchWorld, ct);
		await _bot.Press(ct);
		await _bot.Seconds(0.3, ct);
		Check("E on the marked screen: they all go dark", bi.ScreensOff);
		using (Fast(3)) await _bot.WaitUntil(() => Story.CrtPuzzleDone, 10, ct);
		Check("and come back showing the stairs", Story.CrtPuzzleDone);
		Shot("crt_room");
		CheckObjective("the compass points at the dead walkie-talkie", "walkie_marker");
		var walkie = _bot.FirstOf<WalkiePickup>();
		if (walkie != null) { await _bot.WalkTo(walkie.GlobalPosition + new Vector3(0, 0, 1.0f), 1.0f, ct); await _bot.UseIt(walkie, ct); }
		Check("the walkie-talkie taken: dead, silent", Inv.HasRadio && Story.HasFlag(StoryManager.Flag.WalkieTaken));
		Check("now the door gives", bi.Flow is { VineDoorUnlocked: true });
		// Act 10: the rooms
		double tt = 0;
		while (!bi.MazeActive && tt < 15)
		{
			ct.ThrowIfCancellationRequested();
			_bot.Steer(bi.VineDoorApproachWorld);
			Input.ScriptedMove = new Vector2(0, 1);
			await _bot.Frames(1, ct);
			tt += GetProcessDeltaTime();
		}
		_bot.Release();
		Check("through it: a bare room with three doors", bi.MazeActive && bi.Rooms is { Doors.Count: 3 });
		var rooms = bi.Rooms;
		int opened = 0;
		while (rooms != null && !rooms.JumpscareDone && opened < 8)
		{
			await _bot.WaitUntil(() => Input.Enabled && !rooms.Busy, 6, ct);
			await _bot.UseIt(rooms.Doors[opened++ % rooms.Doors.Count], ct);
			await _bot.WaitUntil(() => !rooms.Busy && Input.Enabled, 8, ct);
		}
		Check("every door: the same room, until it hangs there", rooms is { JumpscareDone: true, EyesShown: true }, $"opened {opened}");
		Check("black but its eyes, shoved out into the hall, the door shut", rooms is { BlackedOut: true, ShovedOut: true, EntryOpen: false });
		Shot("room_black");
		if (rooms != null)
		{
			await _bot.WalkTo(rooms.ToGlobal(new Vector3(0, 0, 14f)), 1.0f, ct, () => rooms.Exited, giveUp: 10f);
			await _bot.Aim(rooms.ToGlobal(new Vector3(0, 1.5f, -3f)), ct);
			await _bot.WaitUntil(() => rooms.FlashFired, 2, ct);
			Check("look back and it's there, once", rooms is { LookedBack: true, FlashFired: true });
			await _bot.Aim(rooms.ExitWorld + Vector3.Up * 1.5f, ct);
			await _bot.ApproachAndWalk(rooms.ExitWorld, 4f, 0.8f, ct, () => rooms.Exited, inside: true, giveUp: 12f);
			await _bot.WaitUntil(() => rooms.Exited, 4, ct);
		}
		Check("out through the round door", rooms is { Exited: true });
		await _bot.WaitUntil(() => Story.Current >= Checkpoint.Act10WalkieFound, 8, ct);
		Check("checkpoint 8 outside, the walkie in the save", Story.Current == Checkpoint.Act10WalkieFound && (SaveSystem.Load()?.Inventory ?? "").Contains("radio"));
	}

	// ------------------------------------------------------------------ Act 11

	private async Task Act11(CancellationToken ct)
	{
		using (Fast(3)) await _bot.WaitUntil(() => Story.Act11DialogueDone, 40, ct);
		Check("the radio: \"Did you see them?\"", Story.Act11DialogueDone);
		await Control(ct, 20);
		CheckObjective("the compass points to the last staircase", "final_stairs_marker");
		var act11 = _bot.FirstOf<Act11Ending>();
		Require("the last staircase, tall now", act11 is { StairsTall: true } && act11.OriginalStairs != null);
		await _bot.WaitUntil(() => act11.Pursuer != null, 10, ct);
		Check("the thing from the bunker follows them out", act11.Pursuer != null);
		var stairs = act11.OriginalStairs;
		var top = stairs.GetNodeOrNull<Node3D>("TopTrigger");
		Require("its top landing", top != null);
		// to its foot (the last stretch walked), then up: jumped to a third of the way, walked through the half-way call, jumped near the top
		var foot = stairs.GlobalPosition;
		await _bot.Teleport(act11.ApproachWorld ?? foot + stairs.GlobalBasis.Z * 6f, foot, ct);
		await _bot.WalkTo(foot, 0.6f, ct, giveUp: 10f);
		Vector3 Tread(float frac) { float t = stairs.Steps * frac; return stairs.ToGlobal(new Vector3(0, t * stairs.Rise + 0.1f, -t * stairs.Run)); }
		await _bot.Inside(Tread(0.38f), Tread(0.6f), ct);
		await _bot.WalkTo(Tread(0.5f), 0.5f, ct, giveUp: 10f);
		await _bot.Inside(Tread(0.94f), top.GlobalPosition, ct);
		await _bot.WalkTo(top.GlobalPosition, 1.2f, ct, giveUp: 12f);
		Check("up the tall flight: the hum rising with it", act11.Progress > 0.9f, $"progress {act11.Progress:0.00}");
		Check("the stairs called once or twice on the way", act11.CallCount is 1 or 2, $"{act11.CallCount}");
		Check("the cap still in hand at the top", Inv.HasNewelPost);
		Shot("top_landing");
		Require("the broken post waits for the cap", act11.CapSeat != null);
		await _bot.Aim(act11.CapSeat.ToGlobal(act11.CapSeat.PickOffset), ct);
		Check("E: put the cap back", _bot.Prompt == Act11Ending.SeatPrompt, $"'{_bot.Prompt}'");
		await _bot.UseIt(act11.CapSeat, ct);
		await _bot.WaitUntil(() => !Inv.HasNewelPost, 6, ct);
		Check("the cap leaves their hands", !Inv.HasNewelPost);
		using (Fast(2))
		{
			await _bot.WaitUntil(() => act11.OriginalStairs is { NewelCapped: true }, 12, ct);
			Check("it seats on the post: the stairs made whole", act11.OriginalStairs is { NewelCapped: true });
			await _bot.WaitUntil(() => Story.Current >= Checkpoint.Act11GiantEncounter, 60, ct);
			Check("the giant rises and reaches: checkpoint 9", Story.Current == Checkpoint.Act11GiantEncounter && act11.EndingStarted && act11.Grabbed, $"{Story.Current}, started {act11.EndingStarted}, grabbed {act11.Grabbed}");
			Check("they looked up into its eyes, trembling", act11.LookedUp && act11.Trembled, $"looked up {act11.LookedUp}, trembled {act11.Trembled}, passed out {act11.PassedOut}");
			Check("the view never flipped through it", !act11.ViewFlipped);
			await _bot.WaitUntil(() => Input.Enabled, 20, ct);
		}
		var lake = GetTree().GetFirstNodeInGroup("lake_marker") as Lake;
		Check("they come to at the lake, with control", Input.Enabled && lake != null && Player.GlobalPosition.DistanceTo(lake.WakeSpotWorld) < 30f);
		Shot("lake");
	}

	// ------------------------------------------------------------------ Act 12

	/// <summary>Rows until <paramref name="done"/> or <paramref name="seconds"/> of game time: W held (Shift with
	/// <paramref name="hard"/>, A/D with <paramref name="steer"/>), or the old A, D, A, D with <paramref name="alternate"/>;
	/// <paramref name="smart"/> rows like a steady player: Shift while it's coming (and there's breath), the lit steer key
	/// while a limb strikes.</summary>
	private async Task RowUntil(LakeCrossingEvent crossing, System.Func<bool> done, CancellationToken ct, double seconds,
		bool hard = false, int steer = 0, bool alternate = false, bool smart = false)
	{
		double t = 0, side = 0; bool left = true;
		try
		{
			while (!done() && t < seconds && !crossing.Landed)
			{
				ct.ThrowIfCancellationRequested();
				if (Input.Enabled)
				{
					if (alternate)
					{
						side -= GetPhysicsProcessDeltaTime();
						if (side <= 0) { left = !left; side = 0.33; }
						Input.ScriptedMove = new Vector2(left ? -1f : 1f, 0f);
						Input.ScriptedRun = false;
					}
					else
					{
						int s = smart ? crossing.DodgeHint : steer;
						bool h = smart ? (crossing.Danger > 0.5f || crossing.FinalDash) && (crossing.RowingHard || crossing.Breath > 0.3f) : hard;
						Input.ScriptedMove = new Vector2(s, 1f);
						Input.ScriptedRun = h;
					}
				}
				await _bot.Frames(1, ct);
				t += GetPhysicsProcessDeltaTime();
			}
		}
		finally { _bot.Release(); }
	}

	/// <summary>Three frames actually drawn (time-compressed, a few physics frames can pass inside one drawn frame).</summary>
	private async Task Drawn(CancellationToken ct)
	{
		for (int i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		ct.ThrowIfCancellationRequested();
	}

	/// <summary>Seated in the boat, mid-stroke (Dan, 2026-10-13: "the heads or something's clipping in the rowboat"; it
	/// was the inside of the jacket's collar, the eye inside the chest): a picture looking forward, back, to each side and
	/// down, and the middle of the view looking forward must be the lake, not the body.</summary>
	private async Task BoatLook(LakeCrossingEvent crossing, CancellationToken ct)
	{
		var boat = crossing.Boat;
		Vector3 eye() => Player.CameraRig.Camera.GlobalPosition;
		Vector3 fwd = (-boat.GlobalBasis.Z with { Y = 0 }).Normalized(), side = (boat.GlobalBasis.X with { Y = 0 }).Normalized();
		float Centre()
		{
			var img = GetViewport().GetTexture().GetImage();
			int w = img.GetWidth(), h = img.GetHeight(); float sum = 0; int n = 0;
			for (int y = h * 3 / 8; y < h * 5 / 8; y += 8) for (int x = w / 4; x < w * 3 / 4; x += 8) { sum += img.GetPixel(x, y).Luminance; n++; }
			return sum / n;
		}
		float ahead = 0f;
		var looks = new (string name, Vector3 dir)[]
		{
			("boat_fwd", fwd), ("boat_back", -fwd), ("boat_left", -side), ("boat_right", side),
			("boat_down", fwd * 0.5f + Vector3.Down), ("boat_back_down", -fwd * 0.6f + Vector3.Down * 0.5f),
		};
		foreach (var (name, dir) in looks)
		{
			await RowUntil(crossing, () => false, ct, 0.45);
			await _bot.Aim(eye() + dir * 10f, ct);
			await Drawn(ct);
			if (name == "boat_fwd") ahead = Centre();
			Shot(name);
		}
		await _bot.Aim(eye() + fwd * 10f, ct);
		Check("seated in the boat: the view ahead is the lake, nothing of the body in front of the eye", ahead > 0.1f, $"middle of the view {ahead:0.00} bright");
	}

	private async Task Act12(CancellationToken ct)
	{
		await Control(ct, 40);
		var lake = GetTree().GetFirstNodeInGroup("lake_marker") as Lake;
		var crossing = _bot.FirstOf<LakeCrossingEvent>();
		Require("the lake and its boat", lake != null && crossing?.BoardPrompt != null);
		if (Phase == 0) Check("checkpoint 9: on the shore by the dock", Player.GlobalPosition.DistanceTo(lake.WakeSpotWorld) < 4f && Story.Current == Checkpoint.Act11GiantEncounter, $"{Player.GlobalPosition.DistanceTo(lake.WakeSpotWorld):0.0} m");
		else Check("drowned: back on the shore at checkpoint 9, the death counted", Player.GlobalPosition.DistanceTo(lake.WakeSpotWorld) < 4f && Story.Current == Checkpoint.Act11GiantEncounter && PlayerDeath.Deaths >= 1 && Story.TotalDeaths >= 1,
			$"deaths {PlayerDeath.Deaths}, saved {Story.TotalDeaths}");
		// onto the dock, into the boat
		Vector3 onDock = lake.ToGlobal(new Vector3(0, ProjectDS.World.LakeParts.LakeShape.DockDeck + 0.05f, ProjectDS.World.LakeParts.LakeShape.DockEndZ + 2.5f));
		await _bot.Inside(onDock, crossing.BoardPrompt.GlobalPosition, ct);
		await _bot.WalkTo(crossing.BoardPrompt.GlobalPosition, 1.5f, ct, giveUp: 10f);
		await _bot.UseIt(crossing.BoardPrompt, ct);
		using (Fast(3)) await _bot.WaitUntil(() => crossing.Boarded, 12, ct);
		Require("boarded the boat", crossing.Boarded);
		using (Fast(3)) await _bot.WaitUntil(() => crossing.Paddling, 8, ct);
		Check("seated, pushed off, the oars in hand", crossing.Paddling && Player.CameraRig.EyeHeight < 1.2f);
		if (Phase == 0) await BoatLook(crossing, ct);
		using (Fast(2))
		{
			if (Phase == 0)
			{
				// the controls: alternating A and D still rows; W rows on its own rhythm; Shift rows hard, on breath; D steers
				await RowUntil(crossing, () => false, ct, 2.5, alternate: true);
				Check("the old way still rows: alternating A and D", crossing.StrokeCount >= 4 && crossing.Speed > 1.2f, $"{crossing.StrokeCount} strokes, {crossing.Speed:0.0} m/s");
				await RowUntil(crossing, () => false, ct, 3.0);
				Check("holding W rows on its own steady rhythm", crossing.Speed > 2.6f && !crossing.RowingHard, $"{crossing.Speed:0.0} m/s");
				await RowUntil(crossing, () => false, ct, 2.5, hard: true);
				Check("Shift with W rows hard: quicker, and it spends breath", crossing.Speed > 3.6f && crossing.Breath < 0.8f, $"{crossing.Speed:0.0} m/s, breath {crossing.Breath:0.00}");
				await RowUntil(crossing, () => false, ct, 1.6, steer: 1);
				Check("D steers the boat across", Mathf.Abs(crossing.Sideways) > 0.8f, $"{crossing.Sideways:0.0} m off the line");
			}
			await RowUntil(crossing, () => crossing.InBreach || !crossing.Paddling, ct, 60, smart: true);
			Check("rowed out to the breach: bumps under the hull on the way", crossing.InBreach && crossing.Bumps >= 1, $"progress {crossing.Progress:0.00}, {crossing.Bumps} bumps, {crossing.Glimpses} limbs");
			await _bot.WaitUntil(() => crossing.LastCreature is { ColossusUp: true }, 15, ct);
			Check("limbs covered in eyes breach the water", crossing.LastCreature is { Breaching: true } or { ColossusUp: true });
			Shot("breach");
			await _bot.WaitUntil(() => !crossing.InBreach, 25, ct);
			Check("the water turns white-capped, something follows", crossing.InCurrent && crossing.HuntGap < 100f, $"gap {crossing.HuntGap:0.0}");
			if (Phase == 0)
			{
				// dawdle: fair warning first, then it takes the boat
				_s.ExpectReload = true;
				await _bot.WaitUntil(() => crossing.Warnings >= 1 || crossing.Caught, 30, ct);
				Check("fair warning before the take: it closes in, knocks, the prompt goes red", crossing.Warnings >= 1 && !crossing.Caught, $"gap {crossing.HuntGap:0.0}, danger {crossing.Danger:0.00}");
				await _bot.WaitUntil(() => crossing.Caught, 40, ct);
				Check("stopped rowing: the hunter takes the boat", crossing.Caught, $"gap {crossing.HuntGap:0.0}");
				await _bot.WaitUntil(() => Player.GlobalPosition.Y < lake.GlobalPosition.Y - 2f, 15, ct);
				Check("dragged under", Player.GlobalPosition.Y < lake.GlobalPosition.Y - 2f);
				await ExpectReload(ct, 40);
				return;
			}
			// a steady rower: W, Shift when it comes, the lit steer key when a limb strikes
			await RowUntil(crossing, () => crossing.Landed, ct, 120, smart: true);
		}
		Check("strikes and surges on the way, and it gave chase to the beach", crossing.Strikes >= 1 && crossing.Surges >= 1, $"{crossing.Strikes} strikes ({crossing.StrikeHits} hit), {crossing.Surges} surges, dash {crossing.FinalDash}");
		Check("one near miss: it reared right behind the stern, and sank away", crossing.NearMisses == 1 && !crossing.InNearMiss && crossing.NearMissClosest < 4.5f,
			$"{crossing.NearMisses} near misses, {crossing.NearMissClosest:0.0} m at the closest");
		Check("rowed hard: across the current, landed", crossing.Landed, $"progress {crossing.Progress:0.00}");
		await _bot.WaitUntil(() => Input.Enabled, 10, ct);
		Check("stood up on the far beach", Player.CameraRig.EyeHeight > 1.5f);
		Shot("landed");
		await _bot.WalkTo(lake.StationApproachWorld, 1.5f, ct, giveUp: 20f);
		await _bot.WalkTo(lake.StationDoorWorld, 1.0f, ct, () => Story.Current >= Checkpoint.Act12LakeCrossed, giveUp: 15f);
		await _bot.WaitUntil(() => Story.Current >= Checkpoint.Act12LakeCrossed, 10, ct);
		Check("checkpoint 10 at the station's door", Story.Current == Checkpoint.Act12LakeCrossed);
		await _bot.WaitUntil(() => Input.Enabled, 8, ct);
	}
}
