using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>
/// Act 23, room 201 (the owner's document): the card lets them in, and it's the cleanest room in the lodge, strangely
/// perfect: every lamp lit, the bed turned down with a chocolate on each pillow, roses on the nightstand, towels
/// folded at its foot. On the bed, an envelope. Picked up, it's brought up before the eyes, its flap opens and the
/// letter slides out: "Welcome Back. We missed you as a valued guest." Put down, the door they came in by slams
/// shut behind them and won't open again. The bathroom off to the right is dark (a switch by its door, or the
/// lantern): the switch lights it a deep red, and in its back wall a doorway has been hacked through into the
/// cavity between the rooms: the crawlspace (SkiLodge.Crawlspace.cs).
/// </summary>
public partial class SkiLodge
{
	public Node3D Envelope201 { get; private set; }
	public PickupInteractable Envelope201Use { get; private set; }
	public Readable Letter201 { get; private set; }
	public bool Room201Jammed { get; private set; }
	/// <summary>The 201 bathroom's jagged doorway into the wall cavity (lodge-local, on the bathroom's west wall).</summary>
	public static readonly Vector3 Hole201 = new(CorrX0, UpperY, 1.95f);

	private Node3D _envFlap, _envLetter;
	private bool _letterOpen;

	private void BuildRoom201()
	{
		var d = RoomDoors[201];
		d.ConsumeKey = false;
		d.LockedPrompt = "Room 201. The reader wants a keycard.";
		d.Opened += _ =>
		{
			StoryManager.Instance?.SetFlag(LodgeFlag.Room201Open);
			GD.Print("[story] Act 23: 201's reader goes green; the door opens on the cleanest room in the lodge");
		};
		// the room: lit (the only room whose lights are on), and perfect
		if (RoomSwitches.TryGetValue(201, out var sw)) sw.Set(true);
		// (201 is kept perfect: not a speck of dust in it)
		FurnitureKit.Dusty = false;
		try { BuildPerfect201(); }
		finally { FurnitureKit.Dusty = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-dust") < 0; }
		BuildHole201();
		if (Has(LodgeFlag.Room201Jammed)) { Room201Jammed = true; d.SlamAndJam(instant: true); }
		else if (Has(LodgeFlag.Room201Open)) d.Open(null, instant: true);
	}

	/// <summary>The dressing of a room made ready for a guest: the bed turned down, a chocolate on each pillow, red roses
	/// on the nightstand, towels folded at the bed's foot, and the envelope on the cover.</summary>
	private void BuildPerfect201()
	{
		float x0 = CorrX0, x1 = RoomSplitX, zOut = InnerZ, y = UpperY;
		float cx = (x0 + x1) * 0.5f + 1.0f;
		var k = new MeshKit();
		k.Color = Colors.White;
		// the turned-down corner of the cover (a fold of linen back over it)
		k.Mat(LodgeTextures.LinenMat);
		k.Tri(new Vector3(cx - 0.8f, y + 0.575f, zOut - 1.6f), new Vector3(cx - 0.1f, y + 0.575f, zOut - 1.6f), new Vector3(cx - 0.8f, y + 0.575f, zOut - 2.2f), Vector3.Up, Vector2.Zero, new Vector2(0.7f, 0), new Vector2(0, 0.6f));
		// a chocolate on each pillow, in gold foil
		k.Mat(LodgeTextures.GoldMat);
		foreach (float s in new[] { -1f, 1f })
			k.Box(new Vector3(cx + s * 0.41f, y + 0.67f, zOut - 0.4f), new Vector3(0.06f, 0.02f, 0.04f), 1f, new Basis(Vector3.Up, 0.3f * s));
		// towels folded at the foot
		k.Mat(LodgeTextures.PorcelainMat);
		k.Box(new Vector3(cx + 0.35f, y + 0.6f, zOut - 2.05f), new Vector3(0.36f, 0.06f, 0.24f), 1f);
		k.Box(new Vector3(cx + 0.35f, y + 0.66f, zOut - 2.05f), new Vector3(0.34f, 0.06f, 0.22f), 1f);
		// roses on the right nightstand: a vase, stems, seven dark red heads
		var ns = new Vector3(cx + 1.6f, y + 0.6f, zOut - 0.35f);
		k.Mat(LodgeTextures.PorcelainMat);
		k.Cylinder(ns + new Vector3(0.1f, 0, 0), ns + new Vector3(0.1f, 0.22f, 0), 0.05f, 0.065f, 10, true);
		var stem = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.22f, 0.1f), Roughness = 0.8f };
		var petal = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.03f, 0.05f), Roughness = 0.55f };
		var rng = new RandomNumberGenerator { Seed = 2012 };
		for (int i = 0; i < 7; i++)
		{
			float a = i * 0.9f, r = rng.RandfRange(0.02f, 0.07f);
			Vector3 top = ns + new Vector3(0.1f + Mathf.Cos(a) * r, 0.42f + rng.RandfRange(-0.04f, 0.05f), Mathf.Sin(a) * r);
			k.Mat(stem);
			k.Cylinder(ns + new Vector3(0.1f, 0.2f, 0), top, 0.005f, 0.005f, 4, false);
			k.Mat(petal);
			k.Blob(top, new Vector3(0.035f, 0.03f, 0.035f), 2100 + i, 0.25f, false, 1f);
		}
		k.CommitTo(this, "Perfect201", true);
		// the envelope on the cover, toward the foot: cream, its flap sealed with red wax
		if (Has(LodgeFlag.Letter201)) return;
		Envelope201 = new Node3D { Name = "Envelope201", Position = new Vector3(cx - 0.25f, y + 0.585f, zOut - 1.75f), Rotation = new Vector3(0, 0.25f, 0) };
		AddChild(Envelope201);
		var paper = new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.82f, 0.72f), Roughness = 0.9f, AlbedoTexture = ProcTextures.Grime() };
		var e = new MeshKit();
		e.Mat(paper);
		e.Color = Colors.White;
		e.Box(Vector3.Zero, new Vector3(0.24f, 0.006f, 0.16f), 1f);
		e.CommitTo(Envelope201, "Body", false);
		// the letter inside (it slides out)
		_envLetter = new Node3D { Name = "Letter", Position = new Vector3(0, 0.001f, 0) };
		Envelope201.AddChild(_envLetter);
		var lk = new MeshKit();
		lk.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.93f, 0.91f, 0.85f), Roughness = 0.95f });
		lk.Color = Colors.White;
		lk.Box(Vector3.Zero, new Vector3(0.21f, 0.002f, 0.14f), 1f);
		lk.CommitTo(_envLetter, "Sheet", false);
		// the flap: hinged at the back edge (-Z), folded over the front
		_envFlap = new Node3D { Name = "Flap", Position = new Vector3(0, 0.004f, -0.08f) };
		Envelope201.AddChild(_envFlap);
		var fk = new MeshKit();
		fk.Mat(paper);
		fk.Color = Colors.White;
		fk.Tri(new Vector3(-0.12f, 0, 0), new Vector3(0.12f, 0, 0), new Vector3(0, 0, 0.1f), Vector3.Up, Vector2.Zero, Vector2.Right, Vector2.Down);
		fk.Tri(new Vector3(-0.12f, 0, 0), new Vector3(0, 0, 0.1f), new Vector3(0.12f, 0, 0), Vector3.Down, Vector2.Zero, Vector2.Down, Vector2.Right);
		fk.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.04f, 0.04f), Roughness = 0.35f, MetallicSpecular = 0.6f });
		fk.Cylinder(new Vector3(0, 0, 0.085f), new Vector3(0, 0.006f, 0.085f), 0.018f, 0.018f, 10, true);
		fk.CommitTo(_envFlap, "Flap", false);
		Envelope201Use = new PickupInteractable
		{
			Name = "Use", PickRadius = 0.25f, MaxDistance = 2.4f, Position = new Vector3(0, 0.03f, 0),
			PromptFor = _ => "Take the envelope", CanUse = _ => !_letterOpen,
		};
		Envelope201Use.Interacted += p => { if (!_letterOpen) _ = Cutscene.Run(this, ct => OpenLetter(p, ct), lockInput: true); };
		Envelope201.AddChild(Envelope201Use);
		Letter201 = new Readable { Name = "Letter201", Title = "", Text = "Welcome Back.\n\nWe missed you as a valued guest.", Style = Readable.NoteStyle.Handwritten, Enabled = false, Visible = false };
		AddChild(Letter201);
	}

	/// <summary>The envelope brought up before the eyes, its flap lifting, the letter drawn out; then the letter itself.
	/// Put down, the door slams behind them (<see cref="After201Letter"/>).</summary>
	private async Task OpenLetter(PlayerController player, CancellationToken ct)
	{
		_letterOpen = true;
		Envelope201Use.Enabled = false;
		var cam = player.CameraRig.Camera;
		AudioDirector.OneShot(this, "paper_rustle", 1, Envelope201.GlobalPosition, -8f, "Events", 2f, 0.1f);
		// up to the eyes, the letter's face toward them (its +Y to the camera)
		Vector3 fwd = -cam.GlobalBasis.Z;
		Vector3 at = cam.GlobalPosition + fwd * 0.42f + Vector3.Down * 0.06f;
		// (its local +Y to the camera, local -Z up the view)
		var cb = cam.GlobalBasis.Orthonormalized();
		var qTo = new Basis(cb.X, cb.Z, -cb.Y).GetRotationQuaternion().Normalized();
		var qFrom = Envelope201.GlobalBasis.Orthonormalized().GetRotationQuaternion().Normalized();
		var tw = CreateTween().SetParallel();
		tw.TweenProperty(Envelope201, "global_position", at, 0.7f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		tw.TweenMethod(Callable.From<float>(u => { if (IsInstanceValid(Envelope201)) Envelope201.GlobalBasis = new Basis(qFrom.Slerp(qTo, u)); }), 0f, 1f, 0.7f);
		await Cutscene.Tween(this, tw, ct);
		// the seal breaks, the flap lifts
		AudioDirector.OneShot(this, "paper_rustle", 1, Envelope201.GlobalPosition, -10f, "Events", 2f, 0.12f);
		var flap = CreateTween();
		flap.TweenProperty(_envFlap, "rotation:x", -2.6f, 0.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		await Cutscene.Tween(this, flap, ct);
		// the letter slides out, up past the flap
		var slide = CreateTween();
		slide.TweenProperty(_envLetter, "position", new Vector3(0, 0.004f, -0.13f), 0.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await Cutscene.Tween(this, slide, ct);
		await Cutscene.Wait(this, 0.25, ct);
		Envelope201.Visible = false;
		GD.Print("[story] Act 23: 201's envelope - \"Welcome Back. We missed you as a valued guest.\"");
		StoryManager.Instance?.SetFlag(LodgeFlag.Letter201);
		NoteOverlay.Instance?.Open(Letter201, player);
		_ = After201Letter(player);
	}

	/// <summary>Once the letter's put down: the door they came in by slams, and won't open again. A save.</summary>
	private async Task After201Letter(PlayerController player)
	{
		// (the letter reads while the cutscene's released; wait for it to be put down)
		await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
		while (NoteOverlay.Instance is { IsOpen: true }) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree().CreateTimer(0.9), SceneTreeTimer.SignalName.Timeout);
		if (!IsInstanceValid(this) || Room201Jammed) return;
		Room201Jammed = true;
		StoryManager.Instance?.SetFlag(LodgeFlag.Room201Jammed);
		RoomDoors[201].SlamAndJam();
		RoomDoors[201].LockedPrompt = "It won't open. Not from in here.";
		GD.Print("[story] Act 23: the letter put down - 201's door slams shut behind them, for good");
		await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
		if (IsInstanceValid(player)) StoryBeat.ReachCheckpoint(player, Checkpoint.Act23Letter201);
	}

	/// <summary>The bathroom's back wall hacked through: a jagged doorway into the dark between the walls, splintered
	/// lath and plaster round it, the tile cracked, the debris on the floor.</summary>
	private void BuildHole201()
	{
		var k = new MeshKit();
		k.Color = Colors.White;
		float x = CorrX0 + 0.075f, y = UpperY;
		const float z0 = 1.5f, z1 = 2.4f, top = 2.0f;
		// the broken edge: jagged plaster and splintered laths standing proud round the opening, on the bathroom side
		var rng = new RandomNumberGenerator { Seed = 2011 };
		k.Mat(LodgeTextures.PlasterMat);
		for (int i = 0; i < 16; i++)
		{
			float t = i / 15f;
			// round the three sides of the opening
			Vector3 p = t < 0.35f ? new Vector3(x, y + t / 0.35f * top, z0) : t < 0.65f ? new Vector3(x, y + top, Mathf.Lerp(z0, z1, (t - 0.35f) / 0.3f)) : new Vector3(x, y + (1f - (t - 0.65f) / 0.35f) * top, z1);
			k.Blob(p + new Vector3(0.01f, 0, 0), new Vector3(0.03f, rng.RandfRange(0.05f, 0.12f), rng.RandfRange(0.05f, 0.1f)), 2200 + i, 0.5f, false, 1f);
		}
		k.Mat(BuildingTextures.FreshPlankMat);
		for (int i = 0; i < 9; i++)
		{
			float zz = Mathf.Lerp(z0 - 0.06f, z1 + 0.06f, rng.Randf());
			float yy = y + rng.RandfRange(0.1f, top - 0.1f);
			bool left = zz < (z0 + z1) * 0.5f;
			float edge = left ? z0 : z1;
			// a lath snapped, its end sticking into the opening
			k.Box(new Vector3(x + 0.02f, yy, edge + (left ? 0.08f : -0.08f)), new Vector3(0.015f, 0.03f, rng.RandfRange(0.1f, 0.22f)), 1f, new Basis(Vector3.Right, rng.RandfRange(-0.4f, 0.4f)));
		}
		// debris on the tiles
		k.Mat(LodgeTextures.PlasterMat);
		for (int i = 0; i < 10; i++)
			k.Blob(new Vector3(x + rng.RandfRange(0.05f, 0.6f), y + 0.01f, rng.RandfRange(z0 - 0.2f, z1 + 0.2f)), new Vector3(0.05f, 0.025f, 0.04f), 2240 + i, 0.5f, true, 1f);
		k.CommitTo(this, "Hole201", false);
	}
}
