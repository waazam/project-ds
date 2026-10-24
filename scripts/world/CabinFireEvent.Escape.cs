using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 7's way out of the fire (2026-10-24, the owner: "The cabin fire needs more steps then just breaking out of the front
/// door with an axe"). After the roof comes down over the door:
/// <list type="number">
/// <item><b>The room cut in two.</b> A second roof beam crashes down burning across the left of the room, from the wall
/// over the woodpile to the table's corner, between the front (the door, the window, the washstand and its bucket) and
/// the back (the stove and the axe over it). The room has no other way round: the table and the cot close the right.</item>
/// <item><b>The table.</b> Held E shoves the heavy table back against the chair (it goes over): a way opens between the
/// beam and the table.</item>
/// <item><b>The hearth.</b> The stove has burst into the room, the flames all round the axe on its pegs. The bucket of
/// water from beside the washstand (E), thrown on it (E): a hiss, a wall of steam, the flames gone, and the axe can be
/// taken.</item>
/// <item><b>The rag.</b> E at the washstand's basin soaks a rag (held over the mouth): the smoke's harm comes half as
/// fast.</item>
/// <item><b>The smoke.</b> It settles as a layer at head height: standing up in it, the eyes go (<see cref="UI.StyleFx.SmokeEyes"/>),
/// they cough, slow, and in the end it takes them; crouched under it they breathe and it eases off.</item>
/// <item><b>The window.</b> With the axe the front window smashes in: boards nailed across it from outside, the fire's
/// light through their gaps. Nobody gets out that way ("Boarded. From the outside.").</item>
/// <item><b>The door.</b> The beams' flames rise and fall; a blow lands only when they're down (the swing timed to the
/// dip). Swung into them while they're up, the fire roars back up the haft. Three good blows and the doorway is clear.</item>
/// </list>
/// </summary>
public partial class CabinFireEvent
{
	[ExportGroup("The way out")]
	/// <summary>The beams' flames: seconds high, seconds down (the window to swing in), and the time between.</summary>
	[Export] public float FlamesHighSeconds = 1.8f, FlamesLowSeconds = 1.25f, FlamesTurnSeconds = 0.3f;
	/// <summary>The smoke layer's underside (m over the floor) once it has settled: over a crouch, under a standing head.</summary>
	[Export] public float SmokeLayer = 1.3f;
	/// <summary>Seconds standing in the smoke to be taken by it (twice as long with the wet rag), and to clear it crouched.</summary>
	[Export] public float SmokeSeconds = 8f, ClearSeconds = 4f;
	/// <summary>With the axe and the window never tried, the beams can be chopped after this long all the same.</summary>
	[Export] public float WindowWaitSeconds = 15f;

	/// <summary>For tests: where the way out stands.</summary>
	public bool MidBeamDown { get; private set; }
	public bool TableShoved { get; private set; }
	public bool HasBucket { get; private set; }
	public bool HearthDoused { get; private set; }
	public bool HasRag { get; private set; }
	public bool WindowSmashed { get; private set; }
	public bool FlamesLow { get; private set; }
	public int FlaredBlows { get; private set; }
	/// <summary>0..1: the smoke's hold on them (1 takes them).</summary>
	public float Exposure { get; private set; }
	public float SmokeLevelLocal { get; private set; } = 99f;
	public Interactable TableUse => _tableUse;
	public Interactable BucketUse => _bucketUse;
	public Interactable HearthUse => _hearthUse;
	public Interactable BasinUse => _basinUse;
	public Interactable WindowUse => _windowUse;
	/// <summary>For tests: cabin-local points on the way (the front, the gap the table opens, by the stove).</summary>
	public static readonly Vector3 FrontSpot = new(0.4f, 0f, 0.5f), GapSpot = new(0.35f, 0f, -0.95f), StoveSpot = new(-0.95f, 0f, -0.95f);

	private Interactable _tableUse, _bucketUse, _hearthUse, _basinUse, _windowUse;
	private Node3D _escape, _bucket, _heldBucket, _windowBoardsOut;
	private FireVfx _hearthFire, _midFire;
	private StaticBody3D _escapeBody;
	private CollisionShape3D _gapPlug;
	private double _axeAt = -1, _cycle, _nextCough, _flareUntil = -1;
	private bool _stayLowSaid, _flaredSaid;

	private static readonly Vector3 WashAt = new(1.72f, 0f, -0.05f), BucketAt = new(1.62f, 0f, 0.55f);

	// ------------------------------------------------------------------ built at the cave-in

	private void BuildEscape()
	{
		_escape = new Node3D { Name = "Escape" };
		_cabin.AddChild(_escape);
		_escapeBody = new StaticBody3D { Name = "EscapeBody", CollisionLayer = 1, CollisionMask = 0 };
		_escapeBody.SetMeta("surface", "wood");
		_escape.AddChild(_escapeBody);
		// the gap between the table's end and the cot's foot: too narrow to pass already; made sure of
		_gapPlug = new CollisionShape3D { Position = new Vector3(1.0f, 0.5f, -0.85f), Shape = new BoxShape3D { Size = new Vector3(0.32f, 1f, 0.7f) } };
		_escapeBody.AddChild(_gapPlug);
		BuildWashstand();
		// the hearth: the stove burst into the room, the flames up the wall round the axe
		_hearthFire = AddFire(new Vector3(-1.62f, 1.05f, -1.0f), new Vector3(0.55f, 1.5f, 0.75f), 0.95f, 0.6f, true, 4f, 2.2f, 30, _escape);
		Ramp(_hearthFire, 0.95f, 1.2f);
		Sound("flame_ignite", 2, new Vector3(-1.6f, 0.8f, -1.0f), 2f, 4f);
		_hearthUse = Use("HearthUse", new Vector3(-1.45f, 1.1f, -1.0f), 0.6f, p => HasBucket ? "Throw the water on the stove" : HearthDoused ? "" : "The fire's all round the axe.",
			p => { if (HasBucket) Douse(); });
		_tableUse = Use("TableUse", new Vector3(0.3f, 0.6f, -0.5f), 0.55f, p => TableShoved ? "" : "Shove the table", p => ShoveTable(), hold: 1.6f);
		_windowUse = Use("WindowUse", new Vector3(1.395f, 1.25f, _cabin.Depth * 0.5f - 0.2f), 0.45f,
			p => WindowSmashed ? "Boarded over. From the outside." : p.Inventory?.HasTool(ToolKind.Axe) == true ? "Smash the window" : "The window. Something to break it with.",
			p => { if (!WindowSmashed && p.Inventory?.HasTool(ToolKind.Axe) == true) SmashWindow(p); });
		// (the second beam comes down behind them once they're past it, toward the door: UpdateEscape)
	}

	/// <summary>An E-point in the cabin's frame whose prompt and use are given (an empty prompt: nothing to do there now).</summary>
	private Interactable Use(string name, Vector3 local, float radius, System.Func<PlayerController, string> prompt, System.Action<PlayerController> use, float hold = 0f)
	{
		var u = new EscapeUse { Name = name, PromptFn = prompt, UseFn = use, HoldSeconds = hold, MaxDistance = 2.4f, PickRadius = radius, Position = local, HighlightRoot = new NodePath("NoHighlight") };
		_escape.AddChild(u);
		return u;
	}

	/// <summary>Done with: off, so it never stands in front of the next thing (the axe behind the stove's).</summary>
	private static void Spent(Interactable u) { if (u != null && IsInstanceValid(u)) u.Enabled = false; }

	private sealed partial class EscapeUse : Interactable
	{
		public System.Func<PlayerController, string> PromptFn;
		public System.Action<PlayerController> UseFn;
		public override string GetPrompt(PlayerController player) => PromptFn?.Invoke(player) ?? Prompt;
		public override bool CanInteract(PlayerController player) => base.CanInteract(player) && !string.IsNullOrEmpty(GetPrompt(player));
		public override void Interact(PlayerController player)
		{
			UseFn?.Invoke(player);
			base.Interact(player);
		}
	}

	/// <summary>A washstand against the right wall by the door: a basin of water on it, a bucket of water beside it.</summary>
	private void BuildWashstand()
	{
		var k = new MeshKit();
		k.Color = Colors.White;
		k.Mat(PropTextures.PostMat);
		var c = WashAt;
		k.BevelBox(c + new Vector3(0, 0.74f, 0), new Vector3(0.42f, 0.04f, 0.56f), 0.01f, 1.4f);
		foreach (var (dx, dz) in new[] { (-0.17f, -0.24f), (0.17f, -0.24f), (-0.17f, 0.24f), (0.17f, 0.24f) })
			k.Box(c + new Vector3(dx, 0.36f, dz), new Vector3(0.04f, 0.72f, 0.04f));
		k.Box(c + new Vector3(0, 0.12f, 0), new Vector3(0.38f, 0.02f, 0.52f));
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.86f, 0.82f), Roughness = 0.25f });
		k.Cylinder(c + new Vector3(0, 0.76f, 0), c + new Vector3(0, 0.86f, 0), 0.12f, 0.17f, 16, false);
		k.Cylinder(c + new Vector3(0, 0.76f, 0), c + new Vector3(0, 0.765f, 0), 0.12f, 0.12f, 16, true);
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.3f, 0.3f), Roughness = 0.05f, MetallicSpecular = 0.9f });
		k.Cylinder(c + new Vector3(0, 0.83f, 0), c + new Vector3(0, 0.835f, 0), 0.15f, 0.15f, 16, true);
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.7f, 0.62f), Roughness = 0.95f });
		k.Box(c + new Vector3(-0.12f, 0.79f, 0.22f), new Vector3(0.18f, 0.012f, 0.12f), 1f, new Basis(Vector3.Up, 0.3f));   // (the rag, folded on the edge)
		k.CommitTo(_escape, "Washstand", true);
		_escapeBody.AddChild(new CollisionShape3D { Position = c + new Vector3(0, 0.4f, 0), Shape = new BoxShape3D { Size = new Vector3(0.42f, 0.8f, 0.56f) } });
		_basinUse = Use("BasinUse", c + new Vector3(0, 0.85f, 0), 0.3f, p => HasRag ? "" : "Soak the rag in the basin", p => SoakRag());
		_bucket = BucketMesh();
		_bucket.Name = "WaterBucket";
		_bucket.Position = BucketAt;
		_escape.AddChild(_bucket);
		_bucketUse = Use("BucketUse", BucketAt + new Vector3(0, 0.3f, 0), 0.3f, p => HasBucket || HearthDoused ? "" : "Take the bucket of water", p => TakeBucket());
	}

	private static Node3D BucketMesh()
	{
		var root = new Node3D();
		var k = new MeshKit();
		k.Color = Colors.White;
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.42f, 0.4f), Metallic = 0.7f, Roughness = 0.45f, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
		k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0.3f, 0), 0.12f, 0.15f, 14, false);
		k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0.005f, 0), 0.12f, 0.12f, 14, true);
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.26f, 0.27f), Roughness = 0.05f, MetallicSpecular = 0.9f });
		k.Cylinder(new Vector3(0, 0.25f, 0), new Vector3(0, 0.255f, 0), 0.14f, 0.14f, 14, true);
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.3f, 0.3f), Metallic = 0.8f, Roughness = 0.4f });
		for (int i = 0; i < 8; i++)
		{
			float a0 = Mathf.Pi * i / 8f, a1 = Mathf.Pi * (i + 1) / 8f;
			k.Cylinder(new Vector3(Mathf.Cos(a0) * 0.15f, 0.3f + Mathf.Sin(a0) * 0.12f, 0), new Vector3(Mathf.Cos(a1) * 0.15f, 0.3f + Mathf.Sin(a1) * 0.12f, 0), 0.005f, 0.005f, 5, false);
		}
		k.CommitTo(root, "Mesh", true);
		return root;
	}

	// ------------------------------------------------------------------ the steps

	/// <summary>The second beam down across the left of the room: burning, solid, from the wall over the woodpile to the
	/// table's corner.</summary>
	private void DropMidBeam()
	{
		if (MidBeamDown) return;
		MidBeamDown = true;
		var a = new Vector3(-1.95f, 1.1f, -0.3f);
		var b = new Vector3(-0.3f, 0.25f, -0.45f);
		var pivot = new Node3D { Name = "MidBeam" };
		_escape.AddChild(pivot);
		var k = new MeshKit();
		k.Color = Colors.White;
		k.Mat(Cabin.FirePaint("char"));
		k.Cylinder(a, b, 0.12f, 0.11f, 9, true);
		k.CommitTo(pivot, "Mesh", true);
		// down from the roof: swung from where it hung at the wall
		pivot.Transform = new Transform3D(new Basis(Vector3.Back, -0.9f), new Vector3(0.6f, 1.2f, 0f));
		var tw = pivot.CreateTween();
		tw.TweenProperty(pivot, "transform", Transform3D.Identity, 0.45f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		_escapeBody.AddChild(new CollisionShape3D { Name = "MidBeamBlock", Position = new Vector3(-1.1f, 0.7f, -0.36f), Shape = new BoxShape3D { Size = new Vector3(1.78f, 1.4f, 0.3f) } });
		_midFire = AddFire((a + b) * 0.5f + Vector3.Up * 0.1f, new Vector3(1.4f, 0.45f, 0.3f), 0.7f, 0.4f, true, 3.5f, 1.6f, 31, _escape);
		Ramp(_midFire, 0.85f, 0.8f);
		Sound("wall_burst", 3, (a + b) * 0.5f, 2f, 5f);
		Sound("log_wall_shudder", 4, (a + b) * 0.5f, -2f, 5f);
		if (_player?.CameraRig is { } rig)
		{
			var shake = rig.CreateTween();
			shake.TweenMethod(Callable.From<float>(v => { if (IsInstanceValid(rig)) rig.Shake = new Vector3(0.02f * v, -0.03f * v, 0f); }), 1f, 0f, 0.5f);
		}
		GD.Print("[story] Act 7: a second beam down across the room - the stove cut off");
	}

	/// <summary>The table shoved back (held E), the chair going over behind it: a way opens past the beam.</summary>
	private void ShoveTable()
	{
		if (TableShoved) return;
		TableShoved = true;
		Spent(_tableUse);
		var off = new Vector3(0, 0, -0.8f);
		if (_cabin.GetNodeOrNull<Node3D>("HisTable") is { } his)
		{
			his.CreateTween().TweenProperty(his, "position", his.Position + off, 0.7f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			if (his.GetNodeOrNull<Node3D>("Chair") is { } chair)
			{
				var tw = chair.CreateTween();
				tw.TweenProperty(chair, "rotation", new Vector3(0, 0.3f, Mathf.Pi * 0.5f), 0.45f).SetDelay(0.35f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
				tw.Parallel().TweenProperty(chair, "position", chair.Position + new Vector3(0.15f, 0.22f, 0.49f), 0.45f).SetDelay(0.35f);
			}
			// the chair's collision goes with it (it lies flat behind the table now)
			foreach (var n in his.FindChildren("*", "CollisionShape3D", true, false))
				if (n is CollisionShape3D cs && cs.Shape is BoxShape3D bx && bx.Size.Y > 0.9f) cs.SetDeferred("disabled", true);
		}
		// the cabin's things on the table go with it
		foreach (string n in new[] { "AssignmentFolder", "AssignmentSheet", "Pencil", "PictureFrame", "FramedPhoto" })
			if (_cabin.FindChild(n, true, false) is Node3D t)
				t.CreateTween().TweenProperty(t, "position", t.Position + off, 0.7f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		if (_gapPlug != null && IsInstanceValid(_gapPlug)) _gapPlug.SetDeferred("disabled", true);
		Sound("chair_scrape", 2, new Vector3(0.3f, 0.3f, -1.2f), 2f, 4f);
		_ = Cutscene.Run(this, async ct => { await Cutscene.Wait(this, 0.55, ct); Sound("wood_bump", 3, new Vector3(0.4f, 0.2f, -2.0f), 0f, 3f); });
		if (_player?.CameraRig is { } rig)
		{
			var shake = rig.CreateTween();
			shake.TweenMethod(Callable.From<float>(v => { if (IsInstanceValid(rig)) rig.Shake = new Vector3(0f, 0f, -0.04f * v); }), 1f, 0f, 0.5f);
		}
		GD.Print("[story] Act 7: the table shoved back - a way past the beam");
	}

	private void TakeBucket()
	{
		if (HasBucket || HearthDoused) return;
		HasBucket = true;
		Spent(_bucketUse);
		if (_bucket != null && IsInstanceValid(_bucket)) _bucket.Visible = false;
		// carried low in the right hand, in view
		if (_player?.CameraRig?.Camera is { } cam)
		{
			_heldBucket = BucketMesh();
			_heldBucket.Name = "HeldBucket";
			_heldBucket.Position = new Vector3(0.24f, -0.52f, -0.42f);
			_heldBucket.Rotation = new Vector3(0.25f, 0.4f, 0f);
			cam.AddChild(_heldBucket);
		}
		Audio.AudioDirector.OneShot(this, "bilge_slosh", 8, _player?.GlobalPosition, -8f, "Player", 2f, 0.06f);
		GD.Print("[story] Act 7: the bucket of water taken");
	}

	/// <summary>The water on the stove: a hiss, a wall of steam, the flames round the axe gone; the axe can be taken.</summary>
	private void Douse()
	{
		if (HearthDoused) return;
		HearthDoused = true;
		HasBucket = false;
		Spent(_hearthUse);
		Spent(_bucketUse);
		if (_heldBucket != null && IsInstanceValid(_heldBucket))
		{
			var hb = _heldBucket;
			var tw = hb.CreateTween();
			tw.TweenProperty(hb, "position", new Vector3(0.05f, -0.1f, -0.65f), 0.18f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
			tw.TweenProperty(hb, "rotation", new Vector3(-1.6f, 0.2f, 0f), 0.12f);
			tw.TweenInterval(0.25f);
			tw.TweenCallback(Callable.From(() => { if (IsInstanceValid(hb)) hb.QueueFree(); }));
		}
		_heldBucket = null;
		var at = new Vector3(-1.6f, 1.0f, -1.0f);
		Sound("bilge_slosh", 8, at, 2f, 4f);
		Sound("flame_stop", 2, at, 4f, 5f);
		Sound("flame_gutter", 3, at + Vector3.Up * 0.6f, 0f, 4f);
		if (_hearthFire != null) Ramp(_hearthFire, 0f, 0.6f);
		Steam(_cabin.ToGlobal(at));
		_cabin.SetHearthLit(false);
		// the axe: E takes it now
		if (_cabin.HearthAxe is { } axe && IsInstanceValid(axe))
		{
			_axeUse = new PickupInteractable { Name = "TakeAxe", Prompt = "Take the axe", MaxDistance = 2.8f, PickRadius = 0.35f };
			_axeUse.Interacted += TakeAxe;
			axe.AddChild(_axeUse);
		}
		GD.Print("[story] Act 7: the water thrown on the stove - the axe can be taken");
	}

	private void Steam(Vector3 world)
	{
		var p = new CpuParticles3D
		{
			Name = "Steam", Amount = 60, Lifetime = 2.4f, OneShot = true, Explosiveness = 0.85f, EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(0.3f, 0.3f, 0.35f), Direction = Vector3.Up, Spread = 50f, InitialVelocityMin = 0.6f, InitialVelocityMax = 1.6f,
			Gravity = new Vector3(0, 0.4f, 0), ScaleAmountMin = 1.5f, ScaleAmountMax = 4f, Mesh = new QuadMesh { Size = new Vector2(0.25f, 0.25f) },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.92f, 0.92f, 0.9f, 0.22f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, AlbedoTexture = SoftPuff.Texture, VertexColorUseAsAlbedo = true,
			},
		};
		AddChild(p);
		p.GlobalPosition = world;
		p.Emitting = true;
		GetTree().CreateTimer(3.0).Timeout += () => { if (IsInstanceValid(p)) p.QueueFree(); };
	}

	private void SoakRag()
	{
		if (HasRag) return;
		HasRag = true;
		Spent(_basinUse);
		Audio.AudioDirector.OneShot(this, "bilge_slosh", 8, _cabin.ToGlobal(WashAt + Vector3.Up * 0.8f), -10f, "Events", 2f, 0.06f);
		Audio.AudioDirector.OneShot(this, "cloth", 4, null, -10f, "Player", 2f, 0.06f);
		if (UI.Subtitle.Instance is { } sub) _ = sub.Show("Over the mouth.", 0.3f, 2.0f, 0.8f);
		GD.Print("[story] Act 7: a wet rag over the mouth");
	}

	/// <summary>The window smashed in with the axe: boards nailed across it from outside, the fire's light through the gaps.</summary>
	private void SmashWindow(PlayerController p)
	{
		WindowSmashed = true;
		if (PlayerHands.Instance?.Tool is { Shown: ToolKind.Axe } tool) tool.Strike();
		var at = new Vector3(1.395f, 1.25f, _cabin.Depth * 0.5f);
		GetTree().CreateTimer(0.13).Timeout += () =>
		{
			if (!IsInstanceValid(this) || !IsInstanceValid(_cabin)) return;
			Sound("glass_shatter", 1, at, 2f, 4f);
			Sound("window_burst", 6, at, -2f, 4f);
			// the pane gone (the right-hand one's two faces)
			if (_cabin.FindChild("Panes", true, false) is Node3D panes)
				foreach (var n in panes.GetChildren())
					if (n is MeshInstance3D mi && mi.GetAabb().GetCenter().X > 0.3f) mi.Visible = false;
			BuildOutsideBoards(at);
			Chips(_cabin.ToGlobal(at));
			if (UI.Subtitle.Instance is { } sub) _ = sub.Show("Boarded. From the outside.", 0.3f, 2.6f, 1.0f);
			GD.Print("[story] Act 7: the window smashed - boarded from outside");
		};
		if (p.Inventory is { } inv && inv.InHand != ToolKind.Axe) inv.Select(ToolKind.Axe);
	}

	private void BuildOutsideBoards(Vector3 at)
	{
		var k = new MeshKit();
		k.Color = new Color(0.7f, 0.68f, 0.64f);
		k.Mat(PropTextures.PostMat);
		float z = _cabin.Depth * 0.5f + 0.08f;
		for (int i = 0; i < 4; i++)
		{
			float y = 0.95f + i * 0.2f;
			k.Box(new Vector3(at.X, y, z), new Vector3(0.95f, 0.15f, 0.03f), 1f, new Basis(Vector3.Back, (i % 2 == 0 ? 0.06f : -0.05f)));
		}
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.4f, 0.42f), Metallic = 0.8f });
		for (int i = 0; i < 4; i++)
			foreach (float dx in new[] { -0.4f, 0.4f })
				k.Box(new Vector3(at.X + dx, 0.95f + i * 0.2f, z - 0.02f), new Vector3(0.012f, 0.012f, 0.012f));
		_windowBoardsOut = k.CommitTo(_escape, "OutsideBoards", true);
		_escapeBody.AddChild(new CollisionShape3D { Position = new Vector3(at.X, 1.25f, z), Shape = new BoxShape3D { Size = new Vector3(0.95f, 0.9f, 0.05f) } });
		// the fire outside the boards: its light through the gaps
		var glow = new OmniLight3D { Name = "GapGlow", LightColor = new Color(1f, 0.5f, 0.2f), LightEnergy = 1.4f, OmniRange = 2.5f, ShadowEnabled = true, Position = new Vector3(at.X, 1.25f, z + 0.4f) };
		_escape.AddChild(glow);
	}

	// ------------------------------------------------------------------ every frame, from the cave-in on

	private void UpdateEscape(float dt)
	{
		bool axe = _player.Inventory?.HasTool(ToolKind.Axe) == true;
		if (axe && _axeAt < 0)
		{
			_axeAt = _clock;
			if (!WindowSmashed && UI.Subtitle.Instance is { } sub) _ = sub.Show("The window.", 0.3f, 1.8f, 0.8f);
		}
		// the second beam: down across the room behind them once they're toward the door (never with them at the back:
		// that would shut them away from the water and the door)
		if (!MidBeamDown && _escape != null && _cabin.ToLocal(_player.GlobalPosition).Z > 0.1f && _clock - _caveAt > 1.3) DropMidBeam();
		UpdateBeamFlames(dt);
		UpdateExposure(dt);
		if (_chop == null || !IsInstanceValid(_chop)) return;
		_chop.HoldSeconds = axe ? BlowSeconds : 0f;
		if (axe && _chop.HoldProgress > 0f && _player.Inventory is { } inv && inv.InHand != ToolKind.Axe) inv.Select(ToolKind.Axe);
		if (PlayerHands.Instance?.Tool is { } tool) tool.WindUp = axe && tool.Shown == ToolKind.Axe && _chop.HoldProgress > 0f ? _chop.HoldProgress : 0f;
	}

	/// <summary>The beams' flames, rising and falling: high, down for a while (the blow's window), up again.</summary>
	private void UpdateBeamFlames(float dt)
	{
		_cycle += dt;
		float period = FlamesHighSeconds + FlamesLowSeconds + FlamesTurnSeconds * 2f;
		float t = (float)(_cycle % period);
		float lvl;
		if (t < FlamesHighSeconds) lvl = 1f;
		else if (t < FlamesHighSeconds + FlamesTurnSeconds) lvl = Mathf.Lerp(1f, 0.15f, (t - FlamesHighSeconds) / FlamesTurnSeconds);
		else if (t < FlamesHighSeconds + FlamesTurnSeconds + FlamesLowSeconds) lvl = 0.15f;
		else lvl = Mathf.Lerp(0.15f, 1f, (t - FlamesHighSeconds - FlamesTurnSeconds - FlamesLowSeconds) / FlamesTurnSeconds);
		if (_clock < _flareUntil) lvl = 1f;
		bool low = lvl < 0.45f;
		if (FlamesLow && !low && _stage == Stage.CavedIn && _clock >= _flareUntil) Sound("flame_ignite", 2, _cabin.ToLocal(_cabin.DoorwayInside), -6f, 4f, 0.1f);
		FlamesLow = low;
		foreach (var b in _cabin.Beams)
			if (IsInstanceValid(b) && !b.HasMeta("chopped") && _beamFires.TryGetValue(b, out var f) && IsInstanceValid(f)) f.Intensity = lvl;
	}

	/// <summary>The smoke's hold: standing up into the layer it grows (half as fast through the wet rag), crouched under it
	/// it eases; the eyes, the coughing and the slowing follow it, and at 1 it takes them.</summary>
	private void UpdateExposure(float dt)
	{
		float floorY = _cabin.GlobalPosition.Y;
		var cam = _player.CameraRig?.Camera;
		float eyes = (cam?.GlobalPosition.Y ?? _player.HeadWorld.Y) - floorY;
		bool inSmoke = eyes > SmokeLevelLocal;
		Exposure = Mathf.Clamp(Exposure + (inSmoke ? dt / (SmokeSeconds * (HasRag ? 2f : 1f)) : -dt / ClearSeconds), 0f, 1f);
		UI.StyleFx.SmokeEyes(Exposure * 0.95f);
		_player.WadeScale = 1f - 0.45f * Exposure;
		if (Exposure > 0.22f && _clock >= _nextCough)
		{
			_nextCough = _clock + _rng.RandfRange(1.1f, 2.4f) * (1.4f - Exposure);
			Audio.AudioDirector.OneShot(this, "pbreath_winded_out", 10, null, -6f + 6f * Exposure, "Player", 2f, 0.1f);
			if (_player.CameraRig is { } rig && rig.Shake == Vector3.Zero)
			{
				var tw = rig.CreateTween();
				tw.TweenMethod(Callable.From<float>(v => { if (IsInstanceValid(rig)) rig.Shake = new Vector3(0f, -0.02f * v * Exposure, 0f); }), 1f, 0f, 0.3f);
			}
		}
		if (Exposure > 0.3f && !_stayLowSaid && UI.Subtitle.Instance is { CurrentlyShowing: false } sub)
		{
			_stayLowSaid = true;
			_ = sub.Show("Stay low. Under it.", 0.3f, 2.4f, 0.9f);
		}
		if (Exposure >= 1f && !PlayerDeath.Dying && _deadline != double.MaxValue)
		{
			GD.Print("[story] Act 7: stood up in the smoke too long");
			_deadline = double.MaxValue;
			_ = Cutscene.Run(this, Die, lockInput: true);
		}
	}

	/// <summary>One blow at the beams: it lands if the flames are down; swung into them while they're up, the fire roars
	/// back up the haft and it does nothing.</summary>
	private void Blow(PlayerController p)
	{
		if (_stage != Stage.CavedIn || p.Inventory?.HasTool(ToolKind.Axe) != true) return;
		if (PlayerHands.Instance?.Tool is { Shown: ToolKind.Axe } tool) { tool.WindUp = 0f; tool.Strike(); }
		else HeldUse.Do(p, ToolKind.Axe, _cabin.DoorwayInside, 0.7f);
		GetTree().CreateTimer(0.13).Timeout += () =>
		{
			if (!IsInstanceValid(this)) return;
			if (FlamesLow) { Blows++; Bite(p); }
			else Flare();
		};
	}

	private void Flare()
	{
		FlaredBlows++;
		_flareUntil = _clock + 0.6;
		Exposure = Mathf.Min(0.95f, Exposure + 0.18f);
		var at = _cabin.ToLocal(_cabin.DoorwayInside);
		Sound("flame_ignite", 2, at, 4f, 4f);
		Sound("door_frame_thud", 4, at, -4f, 4f);
		Chips(_cabin.DoorwayInside + Vector3.Up * 0.3f);
		if (_player?.CameraRig is { } rig)
		{
			var tw = rig.CreateTween();
			tw.TweenMethod(Callable.From<float>(v => { if (IsInstanceValid(rig)) rig.Shake = new Vector3(0.03f * v, 0.02f * v, 0.05f * v); }), 1f, 0f, 0.45f);
		}
		UI.StyleFx.RedBeat(0.7f);
		if (!_flaredSaid && UI.Subtitle.Instance is { } sub) { _flaredSaid = true; _ = sub.Show("Too hot. Wait for it to drop.", 0.2f, 2.4f, 0.8f); }
		GD.Print("[story] Act 7: swung into the flames while they were up - the fire roared back");
	}

	/// <summary>The beams' E-point: chop only once the window's been tried (or a while after the axe); then held E is a blow.</summary>
	private bool BeamsChoppable => WindowSmashed || (_axeAt >= 0 && _clock - _axeAt > WindowWaitSeconds);

	private void EndEscape()
	{
		UI.StyleFx.SmokeEyes(0f);
		if (_player != null && IsInstanceValid(_player)) _player.WadeScale = 1f;
		if (_heldBucket != null && IsInstanceValid(_heldBucket)) _heldBucket.QueueFree();
		_heldBucket = null;
		foreach (var u in new[] { _tableUse, _bucketUse, _hearthUse, _basinUse, _windowUse })
			if (u != null && IsInstanceValid(u)) u.QueueFree();
		_tableUse = _bucketUse = _hearthUse = _basinUse = _windowUse = null;
	}
}
