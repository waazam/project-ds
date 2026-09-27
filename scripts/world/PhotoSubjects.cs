using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Entities;
using ProjectDS.Systems;
using ProjectDS.World.BossParts;

namespace ProjectDS.World;

/// <summary>
/// The camera's subjects round the Hollow and below it (the owner: sixty important pictures, see
/// <see cref="PhotoCatalog"/>). Act 1's subjects, the deer, the frog, the waterfall and the monsters hang
/// their own; this hangs the rest on the places once the Hollow has built itself (it waits for the
/// library, the last thing, like <see cref="BlacklightSecrets"/>). One look at the whole scene for the
/// set dressing that repeats (every trail sign, every valve...): any one of them makes the picture.
/// </summary>
public partial class PhotoSubjects : Node
{
	public int Attached { get; private set; }
	public bool Done { get; private set; }

	public override void _Ready() => _ = Place();

	private async Task Place()
	{
		for (int i = 0; i < 90 && !IsInsideTree(); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		for (int i = 0; i < 240; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (StationInterior.Instance?.Boss?.Library?.Round?.Dais != null) break;
		}
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		var found = new List<Node>();
		Walk(GetTree().CurrentScene, found);
		foreach (var n in found)
		{
			switch (n)
			{
				case Camp c: A(c, "camp", c.StumpLocal + new Vector3(-0.9f, 0.5f, 0.3f), 1.5f, 30f); break;
				case Shed s: A(s, "shed", new Vector3(0, 1.4f, 0), 2.5f, 40f); break;
				case Woodpile w: A(w, "woodpile", new Vector3(0, 0.6f, 0), 1.2f, 25f); break;
				case SignPost sp: A(sp, "signpost", new Vector3(0, 1.1f, 0), 0.8f, 18f); break;
				case TreeNote t: A(t, "tree_number", new Vector3(0, 1.6f, 0), 0.8f, 20f); break;
				case Footbridge f: A(f, "footbridge", new Vector3(0, 0.9f, 0), 2f, 45f); break;
				case FallenTree ft: A(ft, "fallen_tree", new Vector3(0, 1.5f, 0), 3f, 60f, 16f, false); break;
				case FriendBody fb: A(fb, "friend", new Vector3(0, 0.3f, 0), 0.8f, 20f); break;
				case WalkiePickup wp: A(wp, "walkie", new Vector3(0, 0.03f, 0), 0.3f, 5f); break;
				case Bunker b: A(b, "bunker_door", b.ToLocal(b.HatchWorld), 2f, 40f); break;
				case Valve v: A(v, "valve", Vector3.Zero, 0.6f, 14f); break;
				case Lake l:
					// the lake itself from anywhere on it or its shore, and the station on its far side
					A(l, "the_lake", Vector3.Zero, 8f, 400f, 30f, false);
					if (l.StationDoorWorld != Vector3.Zero) A(l, "forester_station", l.ToLocal(l.StationDoorWorld) + Vector3.Up * 2f, 4f, 120f, 14f, false);
					break;
			}
		}
		if (BunkerInterior.Instance is { } bi)
		{
			A(bi, "crt_room", bi.ToLocal(bi.CrtTargetApproachWorld), 1f, 14f, 18f, false);
			A(bi, "vine_door", bi.ToLocal(bi.VineDoorInteractWorld), 1f, 16f, 16f, false);
		}
		if (StationInterior.Instance is { } st)
		{
			A(st, "lobby", new Vector3(0, 1.6f, 0), 1f, 14f, 22f, false);
			if (st.Room1 is { } r1) A(r1, "writing_on_wall", new Vector3(0, 1.5f, 0), 0.5f, 9f, 24f, false);
			if (st.Room2 is { } r2)
			{
				A(r2, "red_room", new Vector3(0, 1.5f, 0), 0.5f, 10f, 24f, false);
				if (r2.Box is Node3D box) A(box, "cryptex", Vector3.Zero, 0.3f, 4f, 16f, false);
			}
			if (st.Basement is { } bs)
			{
				A(bs, "flooded_basement", bs.ToLocal(bs.RoomCentreWorld) + Vector3.Up * 1f, 1f, 18f, 22f, false);
				if (bs.Eye is Node3D eye) A(eye, "dead_eye", Vector3.Zero, 0.4f, 12f, 12f, false);
			}
			if (st.Room3?.Stairs is { } well)
			{
				A(well, "the_well", well.ToLocal(well.LandingSpotWorld), 6f, 600f, 10f, false);
				if (well.Hallway is { } hw)
				{
					A(hw, "hallway", hw.ToLocal(hw.DoorWorld) + Vector3.Up * 1.2f, 6f, 250f, 8f, false);
					A(hw, "closet", hw.ToLocal(hw.ClosetWorld) + Vector3.Up * 1f, 1f, 12f, 18f, false);
				}
			}
			if (st.Sewer is { } sw)
			{
				A(sw, "sewer", sw.ToLocal(sw.TunnelEndWorld) + Vector3.Up * 1.4f, 6f, 110f, 10f, false);
				A(sw, "the_hole", Sewer.HoleLocal, 1f, 16f, 18f, false);
			}
			if (st.Boss is { } boss)
			{
				A(boss, "the_pit", new Vector3(0, -8f, 0), 6f, 80f, 20f, false);
				if (boss.Library is { } lib)
				{
					if (lib.Puzzle is Node3D pb) A(pb, "puzzle_box", Vector3.Zero, 0.4f, 5f, 16f, false);
					if (lib.FindChild("Fire", true, false) is Node3D fire) A(fire, "fireplace", Vector3.Zero, 1f, 12f, 16f, false);
					A(lib, "secret_bookcase", new Vector3(-0.6f, 1.6f, Library.Depth - 0.2f), 1f, 12f, 16f, false);
					A(lib, "journal", Library.JournalLocal, 0.3f, 3f, 14f, false);
					if (lib.Round is { } rr)
					{
						if (rr.Dais != null) A(rr.Dais, "the_ring", new Vector3(0, 0.3f, 0), 1.5f, 20f, 18f, false);
						var windows = new Vector3[8];
						for (int i = 0; i < 8; i++)
						{
							float a = Mathf.Tau * (i + 0.5f) / 8f;
							windows[i] = new Vector3(Mathf.Sin(a) * RoundRoom.Radius, 9f, Mathf.Cos(a) * RoundRoom.Radius);
						}
						A(rr, "bricked_windows", windows[0], 2f, 40f, 14f, false, windows[1..]);
						var plates = new List<Vector3>();
						foreach (var w in rr.Switches) plates.Add(rr.ToLocal(w.Root.GlobalPosition));
						if (plates.Count > 0) A(rr, "switches", plates[0], 0.5f, 8f, 16f, false, plates.GetRange(1, plates.Count - 1).ToArray());
						A(rr, "room_above", RoundRoom.TopLocal + new Vector3(0, 1.5f, 1.5f), 1f, 10f, 24f, false);
						if (rr.Stair?.Church is { } ch)
						{
							A(ch, "church_nave", new Vector3(0, 12f, 40f), 10f, 90f, 22f, false);
							A(ch, "font", Church.FontLocal + Vector3.Up * 1f, 1f, 10f, 16f, false);
							A(ch, "altar", new Vector3(0, Church.ChancelY + 2f, Church.ChancelEnd - 0.4f), 3f, 60f, 16f, false);
							A(ch, "crypt", new Vector3(0, Church.CryptFloor + 1.5f, 56f), 3f, 30f, 24f, false);
							A(ch, "church_door", new Vector3(0, 3f, -0.6f), 2f, 40f, 16f, false);
						}
					}
				}
			}
		}
		Done = true;
		GD.Print($"[photo] {Attached} subjects hung round the Hollow");
	}

	private void A(Node3D host, string id, Vector3 local, float min, float max, float cone = 14f, bool los = true, params Vector3[] more)
	{
		if (PhotoSubject.Attach(host, id, local, min, max, cone, los, more) != null) Attached++;
	}

	private static void Walk(Node n, List<Node> into)
	{
		if (n == null) return;
		if (n is Camp or Shed or Woodpile or SignPost or TreeNote or Footbridge or FallenTree or FriendBody or WalkiePickup or Bunker or Valve or Lake) into.Add(n);
		foreach (var c in n.GetChildren()) Walk(c, into);
	}
}
