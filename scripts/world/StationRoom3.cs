using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// Room 3, behind the iron door: the end of Act 13 and the start of Act 14. After the rot of the
/// lobby it is bare and hard: riveted steel plate under old paint flaking to rust, a tread-plate floor, pipework and
/// caged bulbs on conduit under a high, sooted ceiling (one bulb dead, one failing), a machine hum somewhere in the
/// walls. And the walls are hung, floor to
/// ceiling, with old portraits in ornate frames, dozens of them, every one with the face burnt out.
/// At the back, in the floor, a rectangular hole with concrete steps going down into the dark: the
/// stairwell (<see cref="Stairwell"/>).
///
/// Stepping through the door is Act 14's start (<see cref="Checkpoint.Act13Finished"/>).
///
/// Local space: z=0 is the lobby's back wall (the iron door); a short corridor runs +Z to the hall.
/// </summary>
public partial class StationRoom3 : Node3D
{
	public const float CorridorEnd = 6f, HallEnd = 20f, HallHalf = 5f, HallHeight = 7f;
	/// <summary>Where the stairwell sits (the middle of its shaft) in this room's space: its trench comes
	/// down the middle of the hall (x=0) toward the back, and the shaft is under the back wall.</summary>
	public static readonly Vector3 StairwellAt = new(Stairwell.A, 0, HallEnd);
	/// <summary>Crossing into the corridor: Act 13 done, Act 14 begun.</summary>
	public bool Entered { get; private set; }
	public Stairwell Stairs { get; private set; }
	public int Portraits { get; private set; }
	public Vector3 EntryWorld => ToGlobal(new Vector3(0, 0.05f, 1.2f));
	public Vector3 HoleWorld => Stairs?.TrenchTopWorld ?? ToGlobal(new Vector3(0, 0.05f, 13f));

	private AudioStreamPlayer3D _drone;

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		using var __timer = Systems.BuildTimer.Time("StationRoom3");
		var rng = new RandomNumberGenerator { Seed = 1313 };
		var body = new StaticBody3D { Name = "Shell", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "metal");
		AddChild(body);
		// (2026-10-14h, the station pass, with the interiors kit: drawn finely enough for a bake to sit on; the plate painted
		// and flaking, streaked with rust from the top (RoomSurface); the floor's tread plate finer and dirtier; the ceilings
		// a sooted dark plate, not a big orange rust print)
		var k = new MeshKit { Tess = 0.6f };
		k.Mat(RoomSurface.Of(StairwellTextures.PlateMat, RoomSurface.Concrete with { Streaks = 0.85f, StreakLen = 3f, Tide = 0.5f, Mottle = 0.3f, Tint = new Color(0.62f, 0.42f, 0.3f) }, 161));
		k.Color = Colors.White;
		// (2026-10-14b: the corridor's floor and every ceiling were the walls' own riveted plate; the floor is the hall's tread
		// plate now, the ceilings a darker, sootier rusted plate)
		var tread = RoomSurface.Of(StairwellTextures.TreadMat, RoomSurface.Concrete with { FloorDirt = 0.7f, Wear = 0.5f, Streaks = 0.2f }, 162);
		var under = new MeshKit { Tess = 0.6f };
		under.Mat(tread);
		under.Color = Colors.White;
		var over = new MeshKit { Tess = 0.6f };
		over.Mat(RoomSurface.Of(StairwellTextures.SootPlateMat, RoomSurface.Concrete with { CeilingStains = 0.7f }, 163));
		over.Color = Colors.White;
		void Slab(Vector3 c, Vector3 s, bool collide = true, MeshKit into = null)
		{
			// (the walls' plate at 0.25: its texture is a 4 m tile of eight plates now, 2026-10-15)
			BuildKit.Box(into ?? k, c, s, into == under ? 1.6f : into == over ? 0.8f : 0.25f);
			if (collide) body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		}
		// the corridor
		// (their tops 2 cm up into the corridor's ceiling slab, not in the plane of its underside: the clip audit, 2026-10-15)
		Slab(new Vector3(-1.3f, 1.31f, CorridorEnd * 0.5f), new Vector3(0.2f, 2.62f, CorridorEnd));
		Slab(new Vector3(1.3f, 1.31f, CorridorEnd * 0.5f), new Vector3(0.2f, 2.62f, CorridorEnd));
		Slab(new Vector3(0, 2.7f, CorridorEnd * 0.5f), new Vector3(2.8f, 0.2f, CorridorEnd), false, over);
		Slab(new Vector3(0, -0.05f, CorridorEnd * 0.5f), new Vector3(2.8f, 0.1f, CorridorEnd), true, under);
		// the hall
		float hz = (HallEnd - CorridorEnd) * 0.5f, hc = CorridorEnd + hz;
		Slab(new Vector3(-HallHalf, HallHeight * 0.5f, hc), new Vector3(0.3f, HallHeight, hz * 2f));
		Slab(new Vector3(HallHalf, HallHeight * 0.5f, hc), new Vector3(0.3f, HallHeight, hz * 2f));
		Slab(new Vector3(0, HallHeight * 0.5f, HallEnd), new Vector3(HallHalf * 2f, HallHeight, 0.3f));
		Slab(new Vector3(-(HallHalf + 1.3f) * 0.5f, HallHeight * 0.5f, CorridorEnd), new Vector3(HallHalf - 1.3f, HallHeight, 0.3f));
		Slab(new Vector3((HallHalf + 1.3f) * 0.5f, HallHeight * 0.5f, CorridorEnd), new Vector3(HallHalf - 1.3f, HallHeight, 0.3f));
		Slab(new Vector3(0, (HallHeight + 2.6f) * 0.5f, CorridorEnd), new Vector3(2.6f, HallHeight - 2.6f, 0.3f));
		Slab(new Vector3(0, HallHeight + 0.1f, hc), new Vector3(HallHalf * 2f, 0.2f, hz * 2f), false, over);

		// the bake: the corridor and the hall each their own room, dark in their seams and corners, the light in pools
		// under the bulbs (the dead one leaves a dark stretch), damp down the back corners, the floor worn on the way from
		// the door to the hole
		float hx0 = StairwellAt.X - Stairwell.H, hx1 = StairwellAt.X - Stairwell.Inner;   // the hole's sides
		float hz0 = StairwellAt.Z + Stairwell.TrenchZ0, hz1 = StairwellAt.Z - Stairwell.H;  // and its ends
		var corridorShade = new RoomShade(-1.2f, 1.2f, 0f, CorridorEnd - 0.15f, 0f, 2.6f) { Dim = 0.6f };
		corridorShade.Light(new Vector3(0, 2.2f, CorridorEnd * 0.5f), 3.2f);
		var hallShade = new RoomShade(-HallHalf + 0.15f, HallHalf - 0.15f, CorridorEnd + 0.15f, HallEnd - 0.15f, 0f, HallHeight) { Dim = 0.74f, Reach = 1.6f, Head = 0.3f };
		foreach (var (lp, lit) in LampSpots()) if (lit) hallShade.Light(lp + Vector3.Down * 0.3f, 4.6f);
		hallShade.Damp(new Vector3(-HallHalf + 0.15f, 3f, HallEnd - 0.15f), 2.5f, 0.85f).Damp(new Vector3(HallHalf - 0.15f, 5f, HallEnd - 0.15f), 2.2f, 0.7f)
			.Damp(new Vector3(HallHalf - 0.15f, 1f, CorridorEnd + 3f), 1.6f, 0.6f)
			.Path(new Vector3(0, 0, CorridorEnd), new Vector3((hx0 + hx1) * 0.5f, 0, hz0 - 0.4f), 1.2f, 0.7f)
			.Under(new Vector3((hx0 + hx1) * 0.5f, 0, hz0 - 0.45f), new Vector3(hx1 - hx0 + 1f, 0.9f, 0.2f), 0.25f, 0.35f);
		corridorShade.Path(new Vector3(0, 0, 0), new Vector3(0, 0, CorridorEnd), 0.9f, 0.7f);
		foreach (var kit in new[] { k, under, over }) { hallShade.Apply(kit); corridorShade.Apply(kit); }
		MeshKit.Nudge(k.CommitTo(this, "Plate", true), Vector3.Left);   // flush on the lobby's wall: a hair proud of it (the clip audit)
		under.CommitTo(this, "CorridorFloor", true);
		over.CommitTo(this, "Ceilings", false);

		// the floor: steel plate, with the hole cut out of it
		var f = new MeshKit { Tess = 0.6f };
		f.Mat(tread);
		f.Color = Colors.White;
		void Floor(float x0, float x1, float z0, float z1)
		{
			if (x1 - x0 < 0.01f || z1 - z0 < 0.01f) return;
			var c = new Vector3((x0 + x1) * 0.5f, -0.05f, (z0 + z1) * 0.5f);
			var s = new Vector3(x1 - x0, 0.1f, z1 - z0);
			BuildKit.Box(f, c, s, 1.6f);
			body.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		}
		Floor(-HallHalf, HallHalf, CorridorEnd, hz0);
		Floor(-HallHalf, hx0, hz0, hz1);
		Floor(hx1, HallHalf, hz0, hz1);
		Floor(-HallHalf, HallHalf, hz1, HallEnd);
		hallShade.Apply(f);
		f.CommitTo(this, "Floor", true);

		// the trim (the interiors pass, 2026-10-03): a riveted steel kick plate round the walls' feet, and an angle-iron
		// cornice where they meet the ceiling, in the hall and down the corridor; never over the stairwell's hole
		{
			var tk = new MeshKit();
			tk.Mat(RoomSurface.Of(StairwellTextures.SteelMat, RoomSurface.Concrete with { Streaks = 0.5f, Tint = new Color(0.62f, 0.42f, 0.3f) }, 164));
			void Run(Vector3 a, Vector3 b, Vector3 n, float ceil, bool foot)
			{
				float len = a.DistanceTo(b);
				if (len < 0.2f) return;
				Vector3 along = (b - a) / len;
				var basis = new Basis(along, Vector3.Up, along.Cross(Vector3.Up).Normalized());
				if (basis.Z.Dot(n) < 0) basis = new Basis(-along, Vector3.Up, -along.Cross(Vector3.Up).Normalized());
				Vector3 mid = (a + b) * 0.5f - n * 0.002f;
				if (foot)
				{
					tk.Color = new Color(0.55f, 0.57f, 0.6f);
					tk.Box(mid + n * 0.011f + Vector3.Up * 0.152f, new Vector3(len - 0.006f, 0.3f, 0.022f), 1f, basis);
					tk.Color = new Color(0.42f, 0.44f, 0.46f);
					for (float u = 0.12f; u < len - 0.06f; u += 0.3f)
						foreach (float y in new[] { 0.06f, 0.24f })
							tk.Cylinder(a + along * u + n * 0.02f + Vector3.Up * y, a + along * u + n * 0.03f + Vector3.Up * y, 0.012f, 0.009f, 6);
				}
				// the cornice: an angle iron, its flange along the wall and its leg along the ceiling
				tk.Color = new Color(0.5f, 0.52f, 0.55f);
				tk.Box(mid + n * 0.007f + Vector3.Up * (ceil - 0.002f - 0.11f), new Vector3(len - 0.006f, 0.22f, 0.014f), 1f, basis);
				tk.Box(mid + n * 0.08f + Vector3.Up * (ceil - 0.002f - 0.008f), new Vector3(len - 0.006f, 0.016f, 0.16f), 1f, basis);
			}
			float hi = HallHalf - 0.15f;
			Run(new Vector3(-hi, 0, CorridorEnd + 0.15f), new Vector3(-hi, 0, HallEnd - 0.15f), Vector3.Right, HallHeight, true);
			Run(new Vector3(hi, 0, CorridorEnd + 0.15f), new Vector3(hi, 0, HallEnd - 0.15f), Vector3.Left, HallHeight, true);
			// the back wall: its foot only either side of the hole
			Run(new Vector3(-hi, 0, HallEnd - 0.15f), new Vector3(hx0 - 0.05f, 0, HallEnd - 0.15f), Vector3.Forward, HallHeight, true);
			Run(new Vector3(hx1 + 0.05f, 0, HallEnd - 0.15f), new Vector3(hi, 0, HallEnd - 0.15f), Vector3.Forward, HallHeight, true);
			Run(new Vector3(hx0 - 0.05f, 0, HallEnd - 0.15f), new Vector3(hx1 + 0.05f, 0, HallEnd - 0.15f), Vector3.Forward, HallHeight, false);
			// the front wall, either side of the corridor's mouth
			Run(new Vector3(-hi, 0, CorridorEnd + 0.15f), new Vector3(-1.3f, 0, CorridorEnd + 0.15f), Vector3.Back, HallHeight, true);
			Run(new Vector3(1.3f, 0, CorridorEnd + 0.15f), new Vector3(hi, 0, CorridorEnd + 0.15f), Vector3.Back, HallHeight, true);
			Run(new Vector3(-1.3f, 0, CorridorEnd + 0.15f), new Vector3(1.3f, 0, CorridorEnd + 0.15f), Vector3.Back, HallHeight, false);
			// the corridor
			Run(new Vector3(-1.2f, 0, 0.3f), new Vector3(-1.2f, 0, CorridorEnd - 0.15f), Vector3.Right, 2.6f, true);
			Run(new Vector3(1.2f, 0, 0.3f), new Vector3(1.2f, 0, CorridorEnd - 0.15f), Vector3.Left, 2.6f, true);
			hallShade.Apply(tk);
			corridorShade.Apply(tk);
			tk.CommitTo(this, "Trim", false);
		}

		// hazard paint round the hole, and two bollards with a chain sagging between them; (2026-10-14h) the paint old and
		// worn through where feet went, a dull ochre, not a bright safety yellow
		var hz_ = new MeshKit();
		hz_.Mat(RoomSurface.Of(StationParts.StationTextures.Flat("r3_hazard2", new Color(0.44f, 0.36f, 0.1f), 0.8f, 0.15f), RoomSurface.Concrete with { FloorDirt = 0.9f, Wear = 0.9f }, 165));
		hz_.Color = Colors.White;
		for (float z = hz0 - 0.35f; z < hz1 - 0.2f; z += 0.5f)
		{
			BuildKit.Box(hz_, new Vector3(hx0 - 0.2f, 0.003f, z), new Vector3(0.22f, 0.006f, 0.25f));
			BuildKit.Box(hz_, new Vector3(hx1 + 0.2f, 0.003f, z), new Vector3(0.22f, 0.006f, 0.25f));
		}
		for (float x = hx0 - 0.3f; x < hx1 + 0.4f; x += 0.5f)
			BuildKit.Box(hz_, new Vector3(x, 0.003f, hz0 - 0.35f), new Vector3(0.25f, 0.006f, 0.22f));
		hallShade.Apply(hz_);
		hz_.CommitTo(this, "Hazard", false);
		var bol = new MeshKit();
		bol.Mat(RoomSurface.Of(StationParts.StationTextures.Flat("r3_bollard2", new Color(0.42f, 0.34f, 0.09f), 0.6f, 0.3f), RoomSurface.Concrete, 166));
		bol.Color = Colors.White;
		Vector3 b0 = new(hx0 - 0.45f, 0, hz0 - 0.45f), b1 = new(hx1 + 0.45f, 0, hz0 - 0.45f);
		foreach (var b in new[] { b0, b1 })
		{
			bol.Cylinder(b, b + Vector3.Up * 0.9f, 0.05f, 0.05f, 8, true);
			body.AddChild(new CollisionShape3D { Position = b + Vector3.Up * 0.45f, Shape = new CylinderShape3D { Radius = 0.05f, Height = 0.9f } });
		}
		bol.Mat(StairwellTextures.RailMat);
		Vector3 prev = b0 + Vector3.Up * 0.85f;
		for (int i = 1; i <= 8; i++)
		{
			float u = i / 8f;
			Vector3 p = b0.Lerp(b1, u) + Vector3.Up * (0.85f - 0.3f * 4f * u * (1f - u));
			bol.Cylinder(prev, p, 0.012f, 0.012f, 4, false);
			prev = p;
		}
		bol.CommitTo(this, "Bollards", true);   // (the posts have their colliders above: one box round it all sealed the steps)

		// pipes along the top of the walls and across the roof, conduit, a sealed hatch
		var pk = new MeshKit();
		pk.Mat(StairwellTextures.SteelMat);
		pk.Color = new Color(0.6f, 0.58f, 0.55f);
		for (float z = CorridorEnd + 1f; z < HallEnd; z += 2.8f)
			StationParts.StationProps.Pipe(pk, new Vector3(-HallHalf + 0.3f, HallHeight - 0.4f, z), new Vector3(HallHalf - 0.3f, HallHeight - 0.4f, z), 0.1f);
		foreach (float x in new[] { -HallHalf + 0.25f, HallHalf - 0.25f })
		{
			StationParts.StationProps.Pipe(pk, new Vector3(x, HallHeight - 0.9f, CorridorEnd + 0.4f), new Vector3(x, HallHeight - 0.9f, HallEnd - 0.4f), 0.14f);
			StationParts.StationProps.Pipe(pk, new Vector3(x, HallHeight - 1.3f, CorridorEnd + 0.4f), new Vector3(x, HallHeight - 1.3f, HallEnd - 0.4f), 0.06f);
		}
		StationParts.StationProps.Pipe(pk, new Vector3(-1.1f, 2.35f, 0.3f), new Vector3(-1.1f, 2.35f, CorridorEnd - 0.2f), 0.07f);
		hallShade.Apply(pk);
		pk.CommitTo(this, "Pipes", true);

		// (2026-10-14h, the station pass) caged bulbs hung on conduit drops: warm, dirty incandescent light in pools down
		// the hall, one dead, one buzzing and failing. They had been cool-white enamel shades, a "lab corridor" (the look
		// survey), and the shades threw huge hard half-moons of shadow across both walls: the cage casts nothing now.
		foreach (var (lp, lit) in LampSpots())
			Lamp(lp, lit ? 3.0f : 0f, lp.Z > 16f && lp.X > 0f);
		Lamp(new Vector3(0, 2.4f, CorridorEnd * 0.5f), 0.85f);

		// the portraits: salon-hung, crowding the side walls and the back, all their faces burnt out
		HangPortraits(rng);
		SecondLook.PortraitEyes(this);
		// (2026-10-14h) stains by rule: rust and water streaks, cracks, flaking paint, the ceiling's stains, scuffs on the
		// floor; not on the side walls, under the portraits
		DecalDresser.Room(this, new DecalDresser.RoomSpec
		{
			Box = new Aabb(new Vector3(-HallHalf + 0.15f, 0, CorridorEnd + 0.15f), new Vector3(HallHalf * 2f - 0.3f, HallHeight, HallEnd - CorridorEnd - 0.3f)),
			Seed = 163, Painted = true, Amount = 1.2f,
			Doors = { (new Vector3(0, 0, CorridorEnd + 0.15f), Vector3.Back, 2.6f, 2.6f) },
			SkipWalls = { Vector3.Right, Vector3.Left },
			Keep = { (new Vector3(StairwellAt.X - Stairwell.A, 0, StairwellAt.Z - 4.4f), 2.7f) },   // (the hole)
		});

		// the stairwell under the hole
		AudioDirector.Zone(this, new Vector3(0, 1.3f, CorridorEnd * 0.5f), new Vector3(2.6f, 2.6f, CorridorEnd), AudioDirector.Space.Tunnel, "CorridorVerb");
		AudioDirector.Zone(this, new Vector3(0, HallHeight * 0.5f, (CorridorEnd + HallEnd) * 0.5f), new Vector3(HallHalf * 2f, HallHeight, HallEnd - CorridorEnd), AudioDirector.Space.Hall, "GalleryVerb");
		Stairs = new Stairwell { Name = "Stairwell", Position = StairwellAt };
		AddChild(Stairs);

		_drone = Loop("res://assets/audio/ambient/industrial_drone_loop.wav", new Vector3(0, 3f, 13f), "Unnatural", -12f, 10f);
		StoryBeat.MakeTrigger(this, new BoxShape3D { Size = new Vector3(2.4f, 2.4f, 1.2f) }, new Vector3(0, 1.2f, 1.4f), OnEntered, "Room3Entry");
		var s = StoryManager.Instance;
		if (s != null && s.Current >= Checkpoint.Act13Finished) Entered = true;
		SetProcess(true);
	}

	/// <summary>The hall's bulbs: two rows down it, hung on conduit drops 3.2 m under the 7 m ceiling; whether each still
	/// works (one is dead, its stretch of the hall left dark).</summary>
	private static IEnumerable<(Vector3 at, bool lit)> LampSpots()
	{
		for (float z = CorridorEnd + 2f; z < HallEnd; z += 4.5f)
			foreach (float x in new[] { -2.4f, 2.4f })
				yield return (new Vector3(x, HallHeight - 3.2f, z), !(x < 0f && z > 12f && z < 13f));
	}

	private readonly List<(OmniLight3D light, StandardMaterial3D glass, float energy)> _failing = new();
	private AudioStreamPlayer3D _buzz;
	private float _flickerT, _flickerLevel = 1f, _flickerTarget = 1f;

	/// <summary>A caged bulb on a conduit drop from the ceiling at <paramref name="at"/> (the bulb's height): a steel box
	/// and a porcelain socket, the bulb in a wire guard. <paramref name="energy"/> 0 is a dead one (dark, grimed glass);
	/// <paramref name="failing"/> buzzes and dips.</summary>
	private void Lamp(Vector3 at, float energy, bool failing = false)
	{
		float ceil = at.Y > 3f ? HallHeight : 2.6f;
		var k = new MeshKit();
		k.Mat(StairwellTextures.SteelMat);
		k.Color = new Color(0.5f, 0.48f, 0.45f);
		k.Cylinder(new Vector3(at.X, ceil, at.Z), at + Vector3.Up * 0.12f, 0.014f, 0.014f, 6, false);   // the conduit
		k.BevelBox(new Vector3(at.X, ceil - 0.05f, at.Z), new Vector3(0.12f, 0.08f, 0.12f), 0.008f);      // its box on the ceiling
		k.Cylinder(at + Vector3.Up * 0.12f, at + Vector3.Up * 0.05f, 0.03f, 0.035f, 10, true);             // the socket's cup
		k.Mat(StationParts.StationTextures.Flat("r3_porcelain", new Color(0.42f, 0.4f, 0.35f), 0.5f, 0.4f));
		k.Color = Colors.White;
		k.Cylinder(at + Vector3.Up * 0.05f, at + Vector3.Up * 0.02f, 0.028f, 0.025f, 10, true);
		// the wire guard: six bars round the bulb, a ring at its waist, closed at the foot
		k.Mat(StairwellTextures.RailMat);
		const float gr = 0.075f;
		for (int i = 0; i < 6; i++)
		{
			float a = Mathf.Tau * i / 6f;
			Vector3 o = new(Mathf.Cos(a) * gr, 0, Mathf.Sin(a) * gr);
			k.Cylinder(at + Vector3.Up * 0.03f + o * 0.5f, at + Vector3.Down * 0.06f + o, 0.004f, 0.004f, 3, false);
			k.Cylinder(at + Vector3.Down * 0.06f + o, at + Vector3.Down * 0.15f, 0.004f, 0.004f, 3, false);
		}
		Vector3 prev = at + Vector3.Down * 0.06f + new Vector3(gr, 0, 0);
		for (int i = 1; i <= 12; i++)
		{
			float a = Mathf.Tau * i / 12f;
			Vector3 p = at + Vector3.Down * 0.06f + new Vector3(Mathf.Cos(a) * gr, 0, Mathf.Sin(a) * gr);
			k.Cylinder(prev, p, 0.004f, 0.004f, 3, false);
			prev = p;
		}
		k.CommitTo(this, "Lamp", false);   // (nothing of it casts: the guard's bars and the box would stripe the walls)
		bool dead = energy <= 0f;
		var glassMat = dead ? StationParts.StationTextures.Flat("r3_deadbulb", new Color(0.16f, 0.14f, 0.11f), 0.3f, 0.6f)
			: new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.75f, 0.55f), EmissionEnabled = true, Emission = new Color(1f, 0.7f, 0.4f), EmissionEnergyMultiplier = 1.4f };
		AddChild(new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.04f, Height = 0.1f }, Position = at + Vector3.Down * 0.04f,
			MaterialOverride = glassMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		if (dead) return;
		var light = new OmniLight3D
		{
			Position = at + Vector3.Down * 0.12f, LightColor = new Color(1f, 0.72f, 0.46f), LightEnergy = energy, OmniRange = 7.5f,
			OmniAttenuation = 1.0f, ShadowEnabled = true,
		};
		AddChild(light);
		if (!failing) return;
		_failing.Add((light, glassMat, energy));
		_buzz = Loop("res://assets/audio/sfx/bulb_buzz_loop.wav", at, "Events", -20f, 2.5f);
	}

	/// <summary>The failing bulb: it sags and recovers, now and then cuts out for a moment.</summary>
	private void Flicker(float dt)
	{
		_flickerT -= dt;
		if (_flickerT <= 0f)
		{
			float r = GD.Randf();
			_flickerTarget = r < 0.12f ? 0.05f : r < 0.4f ? 0.55f + GD.Randf() * 0.2f : 0.85f + GD.Randf() * 0.15f;
			_flickerT = _flickerTarget < 0.1f ? 0.06f + GD.Randf() * 0.1f : 0.15f + GD.Randf() * 1.4f;
		}
		_flickerLevel = Mathf.MoveToward(_flickerLevel, _flickerTarget, dt * 9f);
		foreach (var (light, glass, energy) in _failing)
		{
			light.LightEnergy = energy * _flickerLevel;
			glass.EmissionEnergyMultiplier = 1.4f * _flickerLevel;
		}
	}

	/// <summary>Frames of every size crowded up the walls (as old galleries hung them), some askew.</summary>
	private void HangPortraits(RandomNumberGenerator rng)
	{
		int n = 0;
		// (2026-10-15, the scan: a wall of clones. Its own dice for what's new, so the hanging itself stays as it was: more of
		// them askew; here and there one gone, its nail and its paler ghost left on the plate; a few hanging empty, the
		// canvas cut out; and a few come down, leaning at the wall's foot or face down on the floor)
		var vr = new RandomNumberGenerator { Seed = 1317 };
		var nails = new MeshKit();
		nails.Mat(StairwellTextures.RailMat);
		nails.Color = Colors.White;
		var ghostMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.42f, 0.4f, 0.34f, 0.22f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoTexture = LakeParts.LakeFx.SoftDot(), Roughness = 1f,
		};
		var fallen = new List<(Vector3 at, float yaw, float w, float h)>();
		int gone = 0;
		void Wall(float x, float z0, float z1, float yaw)
		{
			float z = z0;
			while (z < z1 - 0.5f)
			{
				float w = rng.RandfRange(0.45f, 0.95f), h = w * rng.RandfRange(1.15f, 1.4f);
				if (z + w + 0.3f > z1) break;
				// a column of one to three, stacked
				int stack = rng.RandiRange(1, 3);
				float y = rng.RandfRange(1.0f, 1.6f);
				for (int i = 0; i < stack && y + h < HallHeight - 1.6f; i++)
				{
					float ww = i == 0 ? w : w * rng.RandfRange(0.7f, 1.05f), hh = ww * rng.RandfRange(1.15f, 1.4f);
					float tilt = rng.Randf() < 0.2f ? rng.RandfRange(-0.08f, 0.08f) : 0f;
					var at = new Vector3(x, y + hh * 0.5f, z + w * 0.5f);
					float fate = vr.Randf();
					if (tilt == 0f && vr.Randf() < 0.3f) tilt = vr.RandfRange(-0.05f, 0.05f);
					if (fate < 0.06f || (fate < 0.2f && i == 0))
					{
						// gone: its nail, and a paler patch where it kept the soot off. The low ones are on the floor below.
						Vector3 outN = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)), into = -outN;   // (out of the wall, into it)
						Vector3 nail = at + Vector3.Up * (hh * 0.5f + 0.06f);
						nails.Cylinder(nail + into * 0.03f, nail + outN * 0.03f, 0.004f, 0.004f, 5, false);
						nails.Cylinder(nail + outN * 0.026f, nail + outN * 0.03f, 0.009f, 0.009f, 6, true);
						var ghost = new MeshInstance3D
						{
							Name = "PortraitGhost", Mesh = new QuadMesh { Size = new Vector2(ww * 1.25f, hh * 1.25f) },
							MaterialOverride = ghostMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
						};
						AddChild(ghost);
						ghost.Position = at + into * 0.012f;   // (on the plate: the frames hang 15 mm off it)
						ghost.Rotation = new Vector3(0, yaw, 0);
						gone++;
						if (fate >= 0.06f) fallen.Add((at, yaw, ww, hh));
					}
					else
					{
						var p = new Node3D { Name = $"Portrait{n}", Position = at };
						AddChild(p);
						p.Rotation = new Vector3(0, yaw, tilt);
						PortraitKit.Build(p, n, ww, hh, empty: fate > 0.94f);
						n++;
					}
					y += hh + rng.RandfRange(0.35f, 0.6f);
				}
				z += w + rng.RandfRange(0.35f, 0.7f);
			}
		}
		// canvas faces +Z in its own space: turn it to face into the room from each wall
		// (their backs 15 mm off the plate, at 0.15 from the wall's middle; they hung 5 cm out in the air, 2026-10-15)
		Wall(-HallHalf + 0.165f, CorridorEnd + 0.6f, HallEnd - 0.4f, Mathf.Pi * 0.5f);
		Wall(HallHalf - 0.165f, CorridorEnd + 0.6f, HallEnd - 0.4f, -Mathf.Pi * 0.5f);
		// the back wall, either side of where the hole leads. (2026-10-16, STORY.md Rev 5: these few survive as what they
		// were, the station's staff photographs, the rangers in a row in their hats, every face scratched out; Blender,
		// tools/Blender/station_story.py ss_staff_photo_a/_b. Named apart, so the second look's eyes never land on them.)
		for (int i = 0; i < 4; i++)
		{
			float x = -4.2f + i * 1.1f, w = rng.RandfRange(0.5f, 0.8f), h = w * 1.3f;
			if (x > -1.2f) x += 3.4f;
			var p = new Node3D { Name = $"StaffPhoto{i}", Position = new Vector3(x, rng.RandfRange(2.2f, 3.4f), HallEnd - 0.165f) };
			AddChild(p);
			p.Rotation = new Vector3(0, Mathf.Pi, vr.Randf() < 0.5f ? vr.RandfRange(-0.06f, 0.06f) : 0f);
			if (StationParts.StationStory.Place(p, i % 2 == 0 ? "ss_staff_photo_a" : "ss_staff_photo_b",
				new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(0, 0, -0.015f)), StationParts.StationStory.StaffRoles) == null)
				PortraitKit.Build(p, n, w, h);
			n++;
		}
		// a big one over the corridor door, looking down the hall at the hole
		var big = new Node3D { Name = "PortraitBig", Position = new Vector3(0, 4.6f, CorridorEnd + 0.165f) };
		AddChild(big);
		PortraitKit.Build(big, 99, 1.4f, 1.8f);
		// the ones come down: on the floor under where they hung, leaning back on the wall's foot (over its kick plate), or
		// one face down where it fell; past the trench's ends and clear of the way down the middle
		int f = 0;
		foreach (var (at, yaw, w, h) in fallen)
		{
			if (f >= 4) break;
			Vector3 outN = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
			var p = new Node3D { Name = $"FallenPortrait{f}" };
			AddChild(p);
			float rim = PortraitKit.Rim(200 + f, w, h);
			if (f == 1)
			{
				// face down, a little skewed, just out from the wall
				p.Position = new Vector3(at.X, PortraitKit.Depth(200 + f) + 0.003f, at.Z) + outN * (h * 0.5f + rim + 0.25f);   // (its back up, its moulding on the floor)
				p.Basis = new Basis(Vector3.Up, yaw + vr.RandfRange(-0.4f, 0.4f)) * new Basis(Vector3.Right, Mathf.Pi * 0.5f) * new Basis(Vector3.Back, Mathf.Pi * 0.5f);
			}
			else
			{
				float a = vr.RandfRange(0.16f, 0.3f), ht = h * 0.5f + rim;
				p.Position = new Vector3(at.X, ht * Mathf.Cos(a) + 0.01f, at.Z) + outN * (ht * Mathf.Sin(a) + 0.03f);
				p.Basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, -a) * new Basis(Vector3.Back, vr.RandfRange(-0.04f, 0.04f));
			}
			PortraitKit.Build(p, 200 + f, w, h, backed: true);
			f++;
		}
		nails.CommitTo(this, "Nails", false);
		GD.Print($"[station] Room 3: {n} portraits hung, {gone} gone from their nails, {f} of them on the floor");
		Portraits = n + 1;
	}

	private void OnEntered(PlayerController player)
	{
		if (Entered) return;
		Entered = true;
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act13Finished);
		GD.Print($"[story] Act 13 done: through the iron door. Act 14: Room 3 ({Portraits} portraits, faces burnt out)");
		if (player.GetNodeOrNull<Lantern>("Lantern") is { IsOn: false } && player.Inventory is { HasLantern: true })
			_ = StoryBeat.Caption(this, "It's dark down there.   [F] lantern", 6f, 2.6f, 1f);
	}

	public override void _Process(double delta)
	{
		if (StoryBeat.Player(this) is not { } p) return;
		Vector3 l = ToLocal(p.GlobalPosition);
		bool here = l.Z > 0.2f && l.Y > -1.5f && Mathf.Abs(l.X) < HallHalf + 0.5f;
		if (_drone != null && here != _drone.Playing) { if (here) _drone.Play(); else _drone.Stop(); }
		if (_buzz != null && here != _buzz.Playing) { if (here) _buzz.Play(); else _buzz.Stop(); }
		if (here && _failing.Count > 0) Flicker((float)delta);
	}

	private AudioStreamPlayer3D Loop(string path, Vector3 at, string bus, float db, float unit)
	{
		if (!ResourceLoader.Exists(path)) return null;
		var stream = GD.Load<AudioStream>(path);
		if (stream is AudioStreamWav wav)
		{
			wav = (AudioStreamWav)wav.Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			stream = wav;
		}
		var p = new AudioStreamPlayer3D { Stream = stream, Bus = bus, VolumeDb = db, UnitSize = unit, MaxDistance = 40f, Position = at };
		AddChild(p);
		return p;
	}
}
