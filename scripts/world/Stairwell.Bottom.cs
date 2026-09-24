using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// The bottom of the stairwell (Act 14's end). The next flight has fallen away: the landing beyond
/// it still hangs off the wall, a jump away, and past its edge there is nothing but the drop. Coming
/// near the edge puts the question: jump across, or jump down?
/// <list type="bullet">
/// <item><b>Across</b>: the player makes the jump and lands on the far landing. It holds for a
/// moment, then the bolts go and it falls with them. They come to on the floor far below, and getting
/// up takes a while.</item>
/// <item><b>Down</b>: they step off into the drop and land on their feet, ready to run.</item>
/// </list>
/// Either way they end up in the chamber under the shaft, where Act 15 begins (<see cref="Checkpoint.Act14Finished"/>).
/// </summary>
public partial class Stairwell
{
	public bool Choosing { get; private set; }
	public bool Jumped { get; private set; }
	public bool JumpedAcross { get; private set; }
	public bool Landed { get; private set; }
	public bool StoodUp { get; private set; }
	public bool AcrossFell { get; private set; }
	public Vector3 GapEdgeWorld => ToGlobal(GapEdgeLocal);
	public Vector3 LandingSpotWorld => ToGlobal(new Vector3(0, BottomY + 0.05f, 0));
	public Vector3 ChamberExitWorld => ToGlobal(new Vector3(0, BottomY + 0.05f, 6.5f));
	/// <summary>The broken flight's direction (from the gap landing toward the far one).</summary>
	public Vector3 GapDir => FlightStart(GapCorner).dir;

	private Vector3 GapEdgeLocal => FlightStart(GapCorner).start;
	private Node3D _across;
	/// <summary>Where the chamber's passage meets Act 15's hallway (this node's space).</summary>
	public const float HallwayZ = 15f;
	public Act15Hallway Hallway { get; private set; }
	private Area3D _edge;
	private float _edgeCooldown;

	private void BuildBottom(StaticBody3D concrete)
	{
		var (start, dir) = FlightStart(GapCorner);
		Vector3 perp = new(dir.Z, 0, -dir.X);
		Vector2 gc = CornerXZ(GapCorner);
		if (perp.Dot(new Vector3(gc.X, 0, gc.Y)) < 0) perp = -perp;

		// what is left of the fallen flight: a bent tread hanging off the edge, two torn stringer stubs
		var wreck = new MeshKit();
		wreck.Mat(StairwellTextures.TreadMat);
		wreck.Color = Colors.White;
		Vector3 hang = start + dir * 0.05f + Vector3.Down * 0.35f;
		BuildKit.Box(wreck, hang, new Vector3(Mathf.Abs(dir.X) > 0.5f ? 0.04f : W * 0.6f, 0.6f, Mathf.Abs(dir.X) > 0.5f ? W * 0.6f : 0.04f), 1.4f,
			BuildKit.Face.None, new Basis(perp, 0.3f));
		wreck.Mat(StairwellTextures.SteelMat);
		foreach (float side in new[] { -1f, 1f })
		{
			Vector3 s0 = start + perp * side * (W * 0.5f - 0.03f) + Vector3.Down * 0.14f;
			wreck.Beam(s0, s0 + dir * 0.35f + Vector3.Down * (0.2f + 0.15f * side), 0.05f, 0.24f, 1f, Vector3.Up);
		}
		wreck.CommitTo(this, "Wreck", true);

		// the far landing, still bolted to the wall, a stub of the next flight hanging from it
		int ka = GapCorner + 1;
		Vector2 pa = CornerXZ(ka);
		_across = new Node3D { Name = "AcrossLanding", Position = new Vector3(pa.X, CornerY(ka), pa.Y) };
		AddChild(_across);
		var ak = new MeshKit();
		ak.Mat(StairwellTextures.TreadMat);
		ak.Color = Colors.White;
		BuildKit.Box(ak, new Vector3(0, -0.02f, 0), new Vector3(W, 0.04f, W), 1.4f);
		ak.Mat(StairwellTextures.SteelMat);
		BuildKit.Box(ak, new Vector3(0, -0.1f, 0), new Vector3(W - 0.1f, 0.12f, W - 0.1f), 1f);
		var (s2, d2) = FlightStart(ka);
		Vector3 local2 = s2 - _across.Position;
		ak.Beam(local2 + Vector3.Down * 0.14f, local2 + d2 * 0.5f + Vector3.Down * 0.6f, 0.05f, 0.24f, 1f, Vector3.Up);
		ak.Mat(StairwellTextures.RailMat);
		Vector3 post = new Vector3(Mathf.Sign(pa.X) * Inner, 0, Mathf.Sign(pa.Y) * Inner) - new Vector3(pa.X, 0, pa.Y);
		ak.Cylinder(post + Vector3.Down * 0.2f, post + Vector3.Up * 1.0f, 0.03f, 0.03f, 6, true);
		ak.CommitTo(_across, "Landing", true);
		var ab = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		ab.SetMeta("surface", "metal");
		ab.AddChild(new CollisionShape3D { Position = new Vector3(0, -0.05f, 0), Shape = new BoxShape3D { Size = new Vector3(W, 0.1f, W) } });
		_across.AddChild(ab);

		// an invisible lip at the broken edge (thick, out over the drop): the only ways on from here are
		// the two the question offers
		_body.AddChild(new CollisionShape3D
		{
			Position = start + dir * 0.25f + Vector3.Up * 1.4f,
			Shape = new BoxShape3D { Size = Mathf.Abs(dir.X) > 0.5f ? new Vector3(0.5f, 3f, W) : new Vector3(W, 3f, 0.5f) },
		});

		// coming up to the edge asks the question (polled, so stepping back and coming again asks again)
		var edgeBox = new BoxShape3D { Size = Mathf.Abs(dir.X) > 0.5f ? new Vector3(0.3f, 2f, W * 0.9f) : new Vector3(W * 0.9f, 2f, 0.3f) };
		_edge = StoryBeat.MakeTrigger(this, edgeBox, start - dir * 0.2f + Vector3.Up * 1f, OnEdge, "GapEdge");

		BuildChamber(concrete);
	}

	/// <summary>The chamber under the shaft, where the fall ends and Act 15 begins: low, black, wet
	/// concrete, the wreckage of stairs that fell before, one way on.</summary>
	private void BuildChamber(StaticBody3D concrete)
	{
		float y = BottomY;
		const float half = 7f, height = 8f;
		var k = new MeshKit();
		k.Mat(StairwellTextures.GrimeMat);
		k.Color = new Color(0.55f, 0.55f, 0.55f);
		void Slab(Vector3 c, Vector3 s, bool collide = true)
		{
			BuildKit.Box(k, c, s, 0.4f);
			if (collide) concrete.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		}
		Slab(new Vector3(0, y - 0.1f, 0), new Vector3(half * 2f, 0.2f, half * 2f));
		Slab(new Vector3(-half, y + height * 0.5f, 0), new Vector3(0.3f, height, half * 2f));
		Slab(new Vector3(half, y + height * 0.5f, 0), new Vector3(0.3f, height, half * 2f));
		Slab(new Vector3(0, y + height * 0.5f, -half), new Vector3(half * 2f, height, 0.3f));
		// the far wall has the way on in it, a low doorway into the dark
		Slab(new Vector3(-(half + 0.8f) * 0.5f, y + height * 0.5f, half), new Vector3(half - 0.8f, height, 0.3f));
		Slab(new Vector3((half + 0.8f) * 0.5f, y + height * 0.5f, half), new Vector3(half - 0.8f, height, 0.3f));
		Slab(new Vector3(0, y + (height + 2.3f) * 0.5f, half), new Vector3(1.6f, height - 2.3f, 0.3f));
		// the roof, open where the shaft comes down through it
		Slab(new Vector3(-(half + H) * 0.5f, y + height, 0), new Vector3(half - H, 0.3f, half * 2f), false);
		Slab(new Vector3((half + H) * 0.5f, y + height, 0), new Vector3(half - H, 0.3f, half * 2f), false);
		Slab(new Vector3(0, y + height, -(half + H) * 0.5f), new Vector3(2f * H, 0.3f, half - H), false);
		Slab(new Vector3(0, y + height, (half + H) * 0.5f), new Vector3(2f * H, 0.3f, half - H), false);
		// the passage beyond the doorway, going on into black
		Slab(new Vector3(-0.9f, y + 1.2f, half + 4f), new Vector3(0.2f, 2.4f, 8f));
		Slab(new Vector3(0.9f, y + 1.2f, half + 4f), new Vector3(0.2f, 2.4f, 8f));
		Slab(new Vector3(0, y + 2.45f, half + 4f), new Vector3(2f, 0.1f, 8f), false);
		Slab(new Vector3(0, y - 0.1f, half + 4f), new Vector3(2f, 0.2f, 8f));
		k.CommitTo(this, "Chamber", true);
		// the passage opens into Act 15's hallway
		Hallway = new Act15Hallway { Name = "Act15Hallway", Position = new Vector3(0, y, HallwayZ) };
		AddChild(Hallway);

		// stairs that came down before: twisted flights, treads, a railing, heaped against the walls
		var junk = new MeshKit();
		var rng = new RandomNumberGenerator { Seed = 1515 };
		junk.Mat(StairwellTextures.SteelMat);
		junk.Color = Colors.White;
		for (int i = 0; i < 22; i++)
		{
			float ang = rng.RandfRange(0, Mathf.Tau), r = rng.RandfRange(2.4f, half - 1f);
			Vector3 c = new(Mathf.Cos(ang) * r, y + rng.RandfRange(0.05f, 0.5f), Mathf.Sin(ang) * r);
			if (c.Z > half - 2.2f && Mathf.Abs(c.X) < 1.6f) continue;   // keep the way out clear
			var rot = new Basis(new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(-1, 1), rng.RandfRange(-1, 1)).Normalized(), rng.RandfRange(0.2f, 1.2f));
			if (i % 3 == 0) { junk.Mat(StairwellTextures.TreadMat); BuildKit.Box(junk, c, new Vector3(W, 0.04f, Run), 1.4f, BuildKit.Face.None, rot); junk.Mat(StairwellTextures.SteelMat); }
			else junk.Beam(c, c + rot * new Vector3(rng.RandfRange(1f, 2.5f), 0, 0), 0.05f, 0.24f, 1f, Vector3.Up);
		}
		junk.CommitTo(this, "Wreckage", true);

		// a little light: a cold glow far down the passage, as if something is lit round the corner
		AddChild(new OmniLight3D { Name = "FarGlow", Position = new Vector3(0, y + 1.8f, half + 7.5f), LightColor = new Color(0.4f, 0.9f, 0.5f), LightEnergy = 0.7f, OmniRange = 7f, ShadowEnabled = false });
		AddChild(new OmniLight3D { Name = "ChamberDim", Position = new Vector3(0, y + 5f, 0), LightColor = new Color(0.45f, 0.5f, 0.6f), LightEnergy = 0.25f, OmniRange = 9f, ShadowEnabled = false });
	}

	private bool _mustLeaveEdge;

	private void OnEdge(PlayerController player)
	{
		if (Choosing || Jumped || _mustLeaveEdge) return;
		_ = Ask(player);
	}

	/// <summary>Per frame: once they've stepped back out of the edge zone, coming back asks again.</summary>
	private void PollEdge()
	{
		if (_edge == null || Jumped || Choosing) return;
		var inside = StoryBeat.PlayerInside(_edge);
		if (_mustLeaveEdge) { if (inside == null && _edgeCooldown < Time.GetTicksMsec() * 0.001f) _mustLeaveEdge = false; return; }
		if (inside != null) _ = Ask(inside);
	}

	private async Task Ask(PlayerController player)
	{
		Choosing = true;
		if (ChoicePrompt.Instance == null) Cutscene.SceneRoot(this).AddChild(new ChoicePrompt { Name = "ChoicePrompt" });
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		GD.Print("[story] Act 14: the fallen flight - jump across, or jump down?");
		int choice = await ChoicePrompt.Instance.Ask(player, "Jump across?", "Jump down?");
		Choosing = false;
		if (!GodotObject.IsInstanceValid(player)) return;
		if (choice < 0)
		{
			// step back from the edge; asked again when they come back to it
			_edgeCooldown = Time.GetTicksMsec() * 0.001f + 1.0f;
			_mustLeaveEdge = true;
			var tw = player.CreateTween();
			tw.TweenProperty(player, "global_position", player.GlobalPosition - GapDir * 0.5f, 0.35f).SetTrans(Tween.TransitionType.Sine);
			return;
		}
		Jumped = true;
		JumpedAcross = choice == 0;
		GD.Print($"[story] Act 14: chose to jump {(JumpedAcross ? "across" : "down")}");
		_ = Cutscene.Run(this, ct => JumpedAcross ? JumpAcross(player, ct) : JumpDown(player, ct), lockInput: true, freezeBody: true);
	}

	/// <summary>Turns the view toward a point over <paramref name="seconds"/> (or holds it there, per frame).</summary>
	private static void Aim(PlayerController player, Vector3 at, float k)
	{
		var rig = player.CameraRig;
		Vector3 to = at - rig.Camera.GlobalPosition;
		player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, Mathf.Atan2(-to.X, -to.Z)) * k, 0));
		rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length()), k));
	}

	private async Task JumpAcross(PlayerController player, CancellationToken ct)
	{
		var rig = player.CameraRig;
		Vector3 from = player.GlobalPosition, to = _across.GlobalPosition + Vector3.Up * 0.02f;
		// a breath, a run-up of one step, and over
		double t = 0;
		while (t < 0.8)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			Aim(player, to + Vector3.Up * 0.8f, Mathf.Min(1f, dt * 5f));
		}
		Sfx("breath_out", 3, player.GlobalPosition, -4f, 3f);
		t = 0;
		const double air = 0.75;
		while (t < air)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			float u = (float)(t / air);
			player.GlobalPosition = from.Lerp(to, u) + Vector3.Up * (4f * 0.55f * u * (1f - u));
			Aim(player, to + GapDir * 2f + Vector3.Up * 0.4f, Mathf.Min(1f, dt * 4f));
		}
		player.GlobalPosition = to;
		Sfx("step_metal", 6, to, 2f, 5f);
		Sfx("body_thump", 2, to, -4f, 4f);
		var dip = player.CreateTween();
		dip.TweenProperty(rig, "EyeHeight", 1.15f, 0.15f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		dip.TweenProperty(rig, "EyeHeight", 1.62f, 0.5f).SetTrans(Tween.TransitionType.Sine);
		await Cutscene.Wait(this, 1.1, ct);
		// it holds... then it doesn't
		Sfx("stair_groan", 3, to, 2f, 6f);
		await Cutscene.Wait(this, 0.9, ct);
		Sfx("stair_break", 1, to, 4f, 8f);
		var lurch = CreateTween().SetParallel();
		lurch.TweenProperty(_across, "position", _across.Position + Vector3.Down * 0.18f, 0.15f);
		lurch.TweenProperty(_across, "rotation", new Vector3(0.12f, 0, -0.1f), 0.15f);
		player.GlobalPosition += Vector3.Down * 0.18f;
		rig.Shake = new Vector3(0.02f, 0.015f, 0);
		await Cutscene.Wait(this, 0.45, ct);
		rig.Shake = Vector3.Zero;
		AcrossFell = true;
		// the fall: the landing drops away with them on it; they look up at the shaft going away
		Vector3 fromFall = player.GlobalPosition, acrossFrom = _across.GlobalPosition;
		float ground = LandingSpotWorld.Y;
		float v = 0f, fallen = 0f;
		var fader = StoryBeat.Fader(this);
		bool fading = false;
		t = 0;
		while (fromFall.Y - fallen > ground + 1.8f)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			v += 9.8f * dt;
			fallen += v * dt;
			// tipping off it as it goes, out over the well, looking up the shaft going away
			float drift = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, (float)t / 1.2f)) * 0.75f;
			Vector3 toWell = ToGlobal(new Vector3(0, ToLocal(fromFall).Y, 0)) - fromFall;
			player.GlobalPosition = fromFall + new Vector3(toWell.X, 0, toWell.Z) * drift + Vector3.Down * fallen;
			_across.GlobalPosition = acrossFrom + Vector3.Down * (fallen * 1.08f - 0.1f);
			// looking up and across the well at the stairs going away above
			Vector2 far = -CornerXZ(GapCorner + 1);
			Aim(player, ToGlobal(new Vector3(far.X, ToLocal(fromFall).Y + 14f, far.Y)), Mathf.Min(1f, dt * 2.5f));
			rig.RollSwim = Mathf.Lerp(rig.RollSwim, 0.2f, Mathf.Min(1f, dt));
			if (!fading && fromFall.Y - fallen < ground + 9f && fader != null) { fading = true; _ = fader.Fade(1f, 0.45f, ct); }
		}
		if (fader != null) fader.SetBlack(true);
		Sfx("body_thump", 2, LandingSpotWorld, 4f, 6f);
		Sfx("stair_break", 1, LandingSpotWorld, -2f, 10f);
		_across.Visible = false;
		rig.RollSwim = 0f;
		// on the floor: black for a while, then eyes open, lying on their side among the wreckage
		Vector3 lie = ToGlobal(new Vector3(pa().X * 0.7f, BottomY + 0.05f, pa().Y * 0.7f));
		player.GlobalPosition = lie;
		rig.EyeHeight = 0.22f;
		rig.RollSwim = 0.9f;
		rig.SetPitch(Mathf.DegToRad(-5f));
		Landed = true;
		await Cutscene.Wait(this, 2.2, ct);
		Sfx("breath_in", 5, lie, -6f, 3f);
		if (fader != null) await fader.Fade(0f, 3.0f, ct);
		await Cutscene.Wait(this, 1.6, ct);
		// rolling onto their back, looking up the way they came
		Sfx("breath_out", 3, lie, -6f, 3f);
		t = 0;
		while (t < 2.4)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			float u = Mathf.SmoothStep(0f, 1f, (float)(t / 2.4));
			rig.RollSwim = Mathf.Lerp(0.9f, 0f, u);
			rig.SetPitch(Mathf.Lerp(Mathf.DegToRad(-5f), Mathf.DegToRad(60f), u));
		}
		await Cutscene.Wait(this, 1.4, ct);
		// up onto an elbow, sitting, a long breath, and up
		Sfx("cloth", 3, lie, -8f, 3f);
		var up = player.CreateTween();
		up.TweenProperty(rig, "EyeHeight", 0.8f, 2.4f).SetTrans(Tween.TransitionType.Sine);
		t = 0;
		while (t < 2.4)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.DegToRad(-12f), Mathf.Min(1f, dt * 1.5f)));
		}
		Sfx("breath_in", 5, lie, -6f, 3f);
		await Cutscene.Wait(this, 1.6, ct);
		var stand = player.CreateTween();
		stand.TweenProperty(rig, "EyeHeight", 1.45f, 1.4f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		stand.TweenProperty(rig, "EyeHeight", 1.62f, 0.8f).SetTrans(Tween.TransitionType.Sine);
		t = 0;
		while (t < 2.2)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			rig.RollSwim = 0.06f * Mathf.Sin((float)t * 2.2f) * (1f - (float)(t / 2.2));   // a small stagger
			Aim(player, ChamberExitWorld + Vector3.Up * 1.4f, Mathf.Min(1f, dt * 1.2f));
		}
		rig.RollSwim = 0f;
		rig.EyeHeight = 1.62f;
		StoodUp = true;
		await End(player, true, ct);
	}

	private Vector2 pa() => CornerXZ(GapCorner + 1);

	private async Task JumpDown(PlayerController player, CancellationToken ct)
	{
		var rig = player.CameraRig;
		// over the edge: look down into it, a step, and drop
		Vector3 edge = player.GlobalPosition + GapDir * 0.9f;
		double t = 0;
		while (t < 0.9)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			Aim(player, edge + GapDir * 0.6f + Vector3.Down * 6f, Mathf.Min(1f, dt * 4f));
		}
		Sfx("breath_in", 5, player.GlobalPosition, -4f, 3f);
		Vector3 from = player.GlobalPosition, off = from + GapDir * 0.9f;
		t = 0;
		while (t < 0.35)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			player.GlobalPosition = from.Lerp(off, (float)(t / 0.35)) + Vector3.Up * 0.25f * Mathf.Sin((float)(t / 0.35) * Mathf.Pi);
		}
		// straight down, the floor coming up out of the dark
		Vector3 top = player.GlobalPosition;
		Vector3 land = LandingSpotWorld;
		float ground = land.Y, v = 0f, fallen = 0f;
		var fader = StoryBeat.Fader(this);
		bool fading = false;
		while (top.Y - fallen > ground + 0.6f)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			v += 9.8f * dt;
			fallen += v * dt;
			float u = Mathf.Clamp(fallen / Mathf.Max(0.1f, top.Y - ground), 0f, 1f);
			Vector3 p = top.Lerp(new Vector3(land.X, top.Y, land.Z), Mathf.SmoothStep(0f, 1f, u)) + Vector3.Down * fallen;
			player.GlobalPosition = p;
			rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.DegToRad(-60f), Mathf.Min(1f, dt * 2f)));
			if (!fading && top.Y - fallen < ground + 6f && fader != null) { fading = true; _ = fader.Fade(1f, 0.25f, ct); }
		}
		if (fader != null) fader.SetBlack(true);
		player.GlobalPosition = land;
		Landed = true;
		Sfx("body_thump", 2, land, -2f, 4f);
		Sfx("step_stone", 6, land, 2f, 4f);
		// on their feet: a hard bend at the knees, straight up again, looking at the way on
		rig.EyeHeight = 0.95f;
		rig.SetPitch(Mathf.DegToRad(-20f));
		player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, Mathf.Atan2(-(ChamberExitWorld - land).X, -(ChamberExitWorld - land).Z)), 0));
		await Cutscene.Wait(this, 0.25, ct);
		var up = player.CreateTween();
		up.TweenProperty(rig, "EyeHeight", 1.62f, 0.45f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		if (fader != null) await fader.Fade(0f, 0.5f, ct);
		rig.SetPitch(0f);
		StoodUp = true;
		await End(player, false, ct);
	}

	/// <summary>Down, whichever way: Act 15 starts here. The save, and (until Act 15 exists) the end.</summary>
	private async Task End(PlayerController player, bool across, CancellationToken ct)
	{
		var s = StoryManager.Instance;
		s?.SetFlag(across ? StoryManager.Flag.Act14JumpedAcross : StoryManager.Flag.Act14JumpedDown);
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act14Finished);
		GD.Print($"[story] Act 14: down ({(across ? "across, and the landing fell" : "straight down, on their feet")}) - Act 15 begins here");
		if (StoryBeat.Atmosphere(this) is { } atmo) atmo.Underground = 1f;
		await Cutscene.Wait(this, 0.1, ct);
		// control back: the passage out of the chamber is Act 15's hallway (Act15Hallway)
	}
}
