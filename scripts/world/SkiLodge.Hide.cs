using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-10b) The lodge, closer:
/// <list type="bullet">
/// <item><b>Hiding in a wardrobe</b> (rooms 202 and 204): once it's open, step in (E) and pull the door to. You look out
/// through the gap; E again to step out. The first time, in Act 23, something comes up the corridor while you're in
/// there: its weight on the boards, step by step, a stop outside the room's door, a breath, and on, and gone.</item>
/// <item><b>The sheet</b> (the dining hall): among the dust sheets over the furniture, one is standing, a person's height.
/// Every time your light goes out in that hall (the lantern put out, a flare guttering), it's a little nearer when you
/// see again. It never moves while it's lit. It never reaches you.</item>
/// </list>
/// </summary>
public partial class SkiLodge
{
	public readonly List<WardrobeHide> Hides = new();
	public Node3D SheetFigure { get; private set; }
	public int SheetMoves { get; private set; }
	private bool _sheetDarkLast;

	private void BuildHides(Searchable door, CollisionShape3D solid, Vector3 at, Basis facing, Vector3 size, int num)
	{
		var h = new WardrobeHide { Name = $"Hide{num}", Door = door, Solid = solid, Room = num, Lodge = this, Inside = at + facing * new Vector3(0, 0, -0.02f), Out = at + facing * new Vector3(0, 0, size.Z * 0.5f + 0.7f), Facing = facing };
		AddChild(h);
		h.Position = at + facing * new Vector3(0, 1.1f, size.Z * 0.5f - 0.1f);
		Hides.Add(h);
	}

	/// <summary>Where the corridor passes room <paramref name="num"/>'s door (lodge-local).</summary>
	public Vector3 CorridorByRoom(int num) => RoomDoors.TryGetValue(num, out var d) ? new Vector3(d.Position.X + 0.5f, UpperY, 0f) : new Vector3(-20f, UpperY, 0f);

	// ------------------------------------------------------------------ the sheet

	private void BuildSheetFigure()
	{
		var f = new Node3D { Name = "SheetFigure", Position = new Vector3(WingX1 - 3.5f, 0.02f, -WingHalfZ + 1.6f) };
		AddChild(f);
		var k = new MeshKit();
		k.Mat(LodgeTextures.SheetLinenMat);
		k.Color = Colors.White;
		// a head, the shoulders, the sheet falling from them to the floor in folds
		k.Blob(new Vector3(0, 1.72f, 0), new Vector3(0.13f, 0.15f, 0.13f), 7101, 0.05f, false, 1f, 0f, 2);
		k.Blob(new Vector3(0, 1.45f, 0), new Vector3(0.27f, 0.12f, 0.17f), 7102, 0.06f, false, 1f, 0f, 2);
		for (int i = 0; i < 9; i++)
		{
			float a = i / 9f * Mathf.Tau;
			var d = new Vector3(Mathf.Cos(a) * 0.24f, 0, Mathf.Sin(a) * 0.17f);
			k.Cylinder(new Vector3(0, 1.5f, 0) + d * 0.55f, d * 1.45f + Vector3.Up * 0.01f, 0.07f, 0.11f, 7, false);
		}
		k.Cylinder(new Vector3(0, 1.5f, 0), new Vector3(0, 0.02f, 0), 0.24f, 0.36f, 12, false);
		k.CommitTo(f, "Sheet", true);
		SheetFigure = f;
	}

	/// <summary>In the dining hall with the light gone out: when they see again it has come a step nearer.</summary>
	private void SheetTick(PlayerController player)
	{
		if (SheetFigure == null || player == null) return;
		var l = ToLocal(player.GlobalPosition);
		bool inHall = l.X > WingX0 + 1f && l.X < WingX1 && Mathf.Abs(l.Z) < WingHalfZ && l.Y < UpperY - 0.5f;
		bool dark = !(player.GetNodeOrNull<Lantern>("Lantern")?.IsOn ?? false) && !HeldFlare.AnyLit;
		if (inHall && dark && !_sheetDarkLast) SheetStep(l);
		_sheetDarkLast = dark;
	}

	// ------------------------------------------------------------------ the way out (Act 24)

	/// <summary>(2026-10-10b, the owner: "the exit for the lodge ... needs a lot of work") The porch's great doors, gone: their
	/// frame left standing, torn leaves hanging off the hinges either side in splinters, an iron strap bent out and down, the
	/// chain and its lock dropped on the landing; the storm already drifting in over the threshold, and the boards flung
	/// out across the snow in front.</summary>
	private void PorchWreck()
	{
		float z0 = Apothem, z1 = Apothem + 5.2f;
		var dc = new Vector3(0, 1.55f, z0 + 0.42f);
		var k = new MeshKit();
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Color = Colors.White;
		var rng = new RandomNumberGenerator { Seed = 2402 };
		// what's left of each leaf on its hinges: a jagged column of short splintered boards
		foreach (float sx in new[] { -1f, 1f })
		{
			for (int j = 0; j < 5; j++)
			{
				float y = 0.2f + j * 0.6f, w = rng.RandfRange(0.12f, 0.38f), h = rng.RandfRange(0.4f, 0.62f);
				var c = dc + new Vector3(sx * (1.3f - 0.07f - w * 0.5f), y - 1.55f + h * 0.5f, 0.06f + rng.RandfRange(0f, 0.12f));
				k.Box(c, new Vector3(w, h, 0.1f), 1f, new Basis(Vector3.Up, sx * rng.RandfRange(0.15f, 0.6f)) * new Basis(Vector3.Back, rng.RandfRange(-0.12f, 0.12f)));
				// a spike of grain off the torn edge
				k.Box(c + new Vector3(-sx * (w * 0.5f + 0.07f), rng.RandfRange(-0.1f, 0.15f), 0), new Vector3(0.16f, 0.04f, 0.04f), 1f, new Basis(Vector3.Back, sx * rng.RandfRange(0.2f, 0.7f)));
			}
		}
		// boards flung out over the landing and onto the snow beyond it
		for (int i = 0; i < 22; i++)
		{
			float x = rng.RandfRange(-3.4f, 3.4f), z = rng.RandfRange(z0 + 0.8f, z1 + 7f);
			float y = z < z1 ? 0.12f : 0.2f;
			k.Box(new Vector3(x, y, z), new Vector3(rng.RandfRange(0.1f, 0.24f), 0.05f, rng.RandfRange(0.5f, 1.5f)), 1f, new Basis(Vector3.Up, rng.RandfRange(0, Mathf.Pi)) * new Basis(Vector3.Right, rng.RandfRange(-0.2f, 0.2f)));
		}
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.35f, 0.35f, 0.38f);
		// a strap torn off one leaf, bent out and down; the chain and the padlock where they fell
		k.Box(dc + new Vector3(-1.05f, 0.75f, 0.25f), new Vector3(0.9f, 0.07f, 0.03f), 1f, new Basis(Vector3.Back, -0.9f) * new Basis(Vector3.Up, 0.5f));
		for (int i = 0; i < 9; i++)
			k.Cylinder(new Vector3(0.4f + i * 0.07f, 0.12f, z0 + 1.6f + Mathf.Sin(i * 0.9f) * 0.08f), new Vector3(0.46f + i * 0.07f, 0.12f, z0 + 1.6f + Mathf.Sin(i * 0.9f + 0.9f) * 0.08f), 0.02f, 0.02f, 4, true);
		k.Box(new Vector3(1.1f, 0.15f, z0 + 1.7f), new Vector3(0.12f, 0.06f, 0.15f), 1f);
		k.CommitTo(this, "PorchWreck", true);
		// the storm drifting in: snow banked against the jambs and blown in a tongue over the threshold
		var sn = new MeshKit();
		sn.Mat(WinterWoods.SoftSnow);
		sn.Color = Colors.White;
		sn.Blob(new Vector3(-1.5f, 0.0f, z0 + 0.9f), new Vector3(1.0f, 0.16f, 0.9f), 2460, 0.25f, false, 1f, 0f, 1);
		sn.Blob(new Vector3(1.6f, 0.0f, z0 + 1.0f), new Vector3(0.9f, 0.14f, 1.0f), 2461, 0.25f, false, 1f, 0f, 1);
		sn.Blob(new Vector3(0.2f, -0.02f, z0 + 0.7f), new Vector3(1.1f, 0.08f, 1.6f), 2462, 0.25f, false, 1f, 0f, 1);
		sn.Blob(new Vector3(-0.3f, -0.02f, z0 - 0.5f), new Vector3(0.7f, 0.05f, 1.0f), 2463, 0.25f, false, 1f, 0f, 1);
		// along the landing's open edge, the snow it drifted up against it
		sn.Blob(new Vector3(0, 0.0f, z1 - 0.6f), new Vector3(3.6f, 0.14f, 0.8f), 2470, 0.25f, false, 1f, 0f, 2);
		sn.CommitTo(this, "PorchDrift", false);
	}

	// ------------------------------------------------------------------ 204's door (wrong on a second look)

	/// <summary>(One run in four) 204's door, locked and shut, is found standing ajar, dark inside, once they've looked away
	/// from it; come near, and it eases shut, and the lock clicks.</summary>
	public int DoorAjarState { get; private set; }   // 0 not this run, 1 waiting, 2 ajar, 3 shut again
	private void AjarTick(PlayerController player, Vector3 l)
	{
		if (DoorAjarState == 0 || DoorAjarState == 3 || !RoomDoors.TryGetValue(204, out var d)) return;
		if (d.Current != LodgeParts.LodgeDoor.State.Locked) { DoorAjarState = 3; return; }
		var cam = player.CameraRig?.Camera;
		if (cam == null) return;
		bool seen = cam.IsPositionInFrustum(d.CentreWorld) && (-cam.GlobalBasis.Z).Dot((d.CentreWorld - cam.GlobalPosition).Normalized()) > 0.5f;
		float dist = player.GlobalPosition.DistanceTo(d.CentreWorld);
		bool corridor = l.Y > UpperY - 0.5f && Mathf.Abs(l.Z) < CorrHalf + 0.3f && l.X > CorrX0 && l.X < CorrX1;
		if (DoorAjarState == 1 && corridor && !seen && dist > 6f && StoryManager.Instance is { Current: >= Checkpoint.Act23Room202Done })
		{
			DoorAjarState = 2;
			d.Ajar(0.16f, 0.01f);
			GD.Print("[story] second look: 204's door is standing ajar");
		}
		else if (DoorAjarState == 2 && dist < 2.6f)
		{
			DoorAjarState = 3;
			d.Ajar(0f, 1.6f);
			var tw = CreateTween();
			tw.TweenInterval(1.6f);
			tw.TweenCallback(Callable.From(() => AudioDirector.OneShot(this, "door_slam", 2, d.CentreWorld, -16f, "Events", 2f, 0.05f)));
			PlayerBreathing.Startle(0.6f);
			GD.Print("[story] second look: 204's door eases shut as they come");
		}
	}

	public bool SheetStep(Vector3 playerLocal)
	{
		var f = SheetFigure;
		var to = (playerLocal - f.Position) with { Y = 0 };
		float d = to.Length();
		if (d < 2.6f) return false;
		var next = f.Position + to / d * Mathf.Min(0.9f, d - 2.4f);
		// (not into the tables under their sheets)
		var q = new PhysicsShapeQueryParameters3D { Shape = new SphereShape3D { Radius = 0.3f }, Transform = new Transform3D(Basis.Identity, ToGlobal(next + Vector3.Up * 1.0f)), CollisionMask = 1 };
		if (GetWorld3D().DirectSpaceState.IntersectShape(q, 1).Count > 0) return false;
		f.Position = next;
		f.Rotation = new Vector3(0, Mathf.Atan2(to.X, to.Z), 0);
		SheetMoves++;
		GD.Print($"[story] Act 23: the light went out - the standing sheet is nearer ({d:0.0} m -> {(playerLocal - next).Length():0.0} m)");
		return true;
	}
}

/// <summary>A wardrobe to hide in (see <see cref="SkiLodge"/>).</summary>
public partial class WardrobeHide : Node3D
{
	public Searchable Door;
	public CollisionShape3D Solid;
	public SkiLodge Lodge;
	public int Room;
	public Vector3 Inside, Out;
	public Basis Facing;
	public bool Hidden { get; private set; }
	public bool Passed { get; private set; }
	public static int PassesHeard { get; private set; }
	private PickupInteractable _use;
	private PlayerController _player;
	private float _openAngle;

	public override void _Ready()
	{
		_use = new PickupInteractable
		{
			Name = "HideUse", PickRadius = 0.45f, MaxDistance = 2.0f,
			PromptFor = _ => Hidden ? "Step out" : Door is { IsOpen: true } ? "Hide in the wardrobe" : "",
			CanUse = _ => Hidden || Door is { IsOpen: true },
		};
		_use.Interacted += p => { if (Hidden) StepOut(); else StepIn(p); };
		AddChild(_use);
	}

	public void StepIn(PlayerController p)
	{
		if (Hidden || p == null || Door == null) return;
		_player = p;
		Hidden = true;
		Cutscene.Lock(p, input: false, body: true);
		if (Solid != null) Solid.Disabled = true;
		p.GlobalPosition = Lodge.ToGlobal(Inside);
		var fwd = Lodge.GlobalBasis * (Facing * Vector3.Back);
		p.CameraRig?.SnapBehind(Mathf.Atan2(-fwd.X, -fwd.Z));
		// the door pulled to, a crack left to see through
		_openAngle = Door.Part.Rotation.Y;
		Door.CreateTween().TweenProperty(Door.Part, "rotation:y", -0.075f, 0.5f).SetTrans(Tween.TransitionType.Sine);
		AudioDirector.OneShot(this, "door_creak", 1, GlobalPosition, -18f, "Events", 2f, 0.1f);
		GD.Print($"[story] Act 23: hiding in room {Room}'s wardrobe");
		if (!Passed && StoryManager.Instance is { Current: >= Checkpoint.Act22Finished and < Checkpoint.Act23Finished }) _ = Pass();
	}

	public void StepOut()
	{
		if (!Hidden || _player == null) return;
		Hidden = false;
		Door.CreateTween().TweenProperty(Door.Part, "rotation:y", _openAngle, 0.5f).SetTrans(Tween.TransitionType.Sine);
		AudioDirector.OneShot(this, "door_creak", 1, GlobalPosition, -16f, "Events", 2f, 0.1f);
		_player.GlobalPosition = Lodge.ToGlobal(Out);
		if (Solid != null) Solid.Disabled = false;
		Cutscene.Unlock(_player, input: false, body: true);
		_player = null;
	}

	/// <summary>Something up the corridor while they're in there: step by step, a stop at the door, a breath, and on.</summary>
	private async Task Pass()
	{
		Passed = true;
		var ct = CancellationToken.None;
		await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
		if (!Hidden) return;
		PassesHeard++;
		GD.Print("[story] Act 23: something comes up the corridor past the wardrobe");
		var by = Lodge.CorridorByRoom(Room);
		for (float x = SkiLodge.CorrX1 - 0.5f; x > SkiLodge.CorrX0 + 0.5f; x -= 1.1f)
		{
			var at = Lodge.ToGlobal(new Vector3(x, SkiLodge.UpperY, 0f));
			AudioDirector.OneShot(this, "wendigo_step", 6, at, -6f, "Events", 4f, 0.08f);
			if (GD.Randf() < 0.4f) AudioDirector.OneShot(this, "stair_groan", 3, at, -12f, "Events", 3f, 0.08f);
			bool atDoor = Mathf.Abs(x - by.X) < 0.6f;
			await ToSignal(GetTree().CreateTimer(atDoor ? 0.2 : 0.85), SceneTreeTimer.SignalName.Timeout);
			if (atDoor)
			{
				// it stops at the door. A breath, a long one. Then on
				AudioDirector.OneShot(this, "wendigo_mumble", 4, Lodge.ToGlobal(by + Vector3.Up * 2.2f), -8f, "Voice", 4f, 0.05f);
				PlayerBreathing.Startle(0.9f);
				await ToSignal(GetTree().CreateTimer(3.2), SceneTreeTimer.SignalName.Timeout);
			}
		}
		_ = ct;
	}
}
