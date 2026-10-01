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
		// behind them: a howl
		var perch = ToGlobal(HexAt(292f, (HexIn + BalconyIn) * 0.5f) with { Y = UpperY + 0.02f });
		FinaleWendigo = new Wendigo { Name = "FinaleWendigo" };
		AddChild(FinaleWendigo);
		FinaleWendigo.StandAt(perch, player.GlobalPosition);
		AudioDirector.OneShot(this, "wendigo_howl_04", 1, perch + Vector3.Up * 3f, 7.5f, "Unnatural", 12f, 0.02f);
		GD.Print("[story] Act 23: a howl behind them - it's on the balcony, watching");
		await Cutscene.Wait(this, 0.6, ct);
		// they turn, and look up, stumbling back out from under the balcony over the doors (from under it, its own edge
		// hid the far side)
		var back = player.CreateTween();
		back.TweenProperty(player, "global_position", ToGlobal(new Vector3(-0.4f, FloorY, BalconyIn - 1.4f)), 1.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		await StoryBeat.PanTowards(this, player, perch + Vector3.Up * 3.4f, 1.7f, ct, pitch: true);
		await Cutscene.Wait(this, 0.8, ct);
		// it speaks
		AudioDirector.OneShot(this, "wendigo_forever", 1, perch + Vector3.Up * 3f, 6f, "Voice", 10f, 0f);
		FinaleWendigo.Speak(6.3f);   // (its jaw working with the words)
		foreach (var (line, t) in new[] { ("STARVING.....", 1.8), ("FREEZING.....", 1.9), ("FOREVER....", 2.2) })
		{
			_ = StoryBeat.Caption(this, line, 0.2f, (float)t - 0.5f, 0.3f);
			await Cutscene.Wait(this, t, ct);
		}
		await Cutscene.Wait(this, 0.4, ct);
		// it leaps: they dive aside, and it goes on through the doors
		var door = ToGlobal(FrontDoorInside with { Y = FloorY + 0.4f } + new Vector3(0, 0, 0.6f));
		bool landed = false;
		FinaleWendigo.Leap(door, 0.8f, () => { landed = true; ExplodeFrontDoor(); });
		AudioDirector.OneShot(this, "wendigo_howl_03", 1, perch + Vector3.Up * 2f, 7.5f, "Unnatural", 12f, 0.02f);
		var dive = player.CreateTween();
		var diveTo = player.GlobalPosition + GlobalBasis * new Vector3(-1.7f, 0, -0.7f);
		dive.TweenProperty(player, "global_position", diveTo, 0.45f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		player.SetCrouch(true);
		_ = StoryBeat.PanTowards(this, player, door + Vector3.Up * 1.2f, 0.5f, ct);
		AudioDirector.OneShot(this, "body_dive", 1, diveTo + Vector3.Up * 0.3f, -2f, "Player", 3f, 0.04f);
		for (int w = 0; w < 60 && !landed; w++) await Cutscene.Wait(this, 0.05, ct);
		if (!landed) { landed = true; ExplodeFrontDoor(); }
		await Cutscene.Wait(this, 1.6, ct);
		// up off the floor
		player.SetCrouch(false);
		await Cutscene.Wait(this, 0.8, ct);
		// to the doorway, through it, and out onto the step: left, and right (from inside the doorway's depth a look to
		// the side only met its jamb)
		var at = ToGlobal(new Vector3(0, FloorY + 0.12f, Apothem + 0.95f));
		var go = player.CreateTween();
		go.TweenProperty(player, "global_position", at, 2.8f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
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
		// the end (of the demo, for now): the fade, and the credits
		var fader = StoryBeat.Fader(this);
		if (fader != null) await fader.Fade(1f, 3f, ct);
		if (GetTree().GetFirstNodeInGroup("act11_ending") is Act11Ending ending) await ending.Credits(fader, ct);
	}

	/// <summary>It goes through the doors: both leaves, inside and out, gone in a burst of splinters and snow; boards and
	/// the chain thrown across the floor; the storm in the doorway.</summary>
	private void ExplodeFrontDoor(bool instant = false)
	{
		if (FrontBroken) return;
		FrontBroken = true;
		StoryManager.Instance?.SetFlag(LodgeFlag.FrontBroken);
		if (FrontLeafL != null) FrontLeafL.Visible = false;
		if (FrontLeafR != null) FrontLeafR.Visible = false;
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
		// splintered stumps of the leaves still on their hinges
		foreach (float s in new[] { -1f, 1f })
			for (int j = 0; j < 4; j++)
				k.Box(new Vector3(s * (1.26f - rng.RandfRange(0f, 0.15f)), FloorY + 0.3f + j * 0.75f, HexIn - 0.03f), new Vector3(0.12f, rng.RandfRange(0.4f, 0.7f), 0.06f), 1f);
		k.CommitTo(this, "FrontDoorWreck", false);
		if (instant) return;
		AudioDirector.OneShot(this, "door_explode", 1, ToGlobal(FrontDoorInside), 7.5f, "Events", 10f, 0.02f);
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
}
