using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// A world item the player can take with [E]. Lantern and Compass equip
/// permanently; Axe, Key and Hammer occupy the single tool slot until used.
/// Builds its own small placeholder mesh and collision; no scene file needed.
/// If <see cref="RequiredCheckpoint"/> is set, the item stays hidden and
/// untouchable until the story reaches it (the porch lantern shouldn't be
/// there to grab on the way out at the very start of the game).
/// </summary>
[Tool]
[GlobalClass]
public partial class Pickup : Area3D
{
	[Export] public ToolKind Kind = ToolKind.Axe;
	[Export] public string Label = "Axe";
	/// <summary>Generous on purpose: sloped ground near a prop can put a couple of vertical metres
	/// between a player who's "there" and the item's exact point, and this is a 3D sphere check.</summary>
	[Export] public float Radius = 2.5f;
	[Export] public Checkpoint RequiredCheckpoint = Checkpoint.None;
	/// <summary>If set, the player must be holding this tool to take the item; it's consumed (a locked shed).</summary>
	[Export] public ToolKind RequiredKey = ToolKind.None;

	private bool _playerInRange;
	private bool _revealed;
	private bool _wasPressed;
	private PlayerController _player;

	public override void _Ready()
	{
		CollisionLayer = 0;
		CollisionMask = 2;
		Monitorable = false;
		// Always monitoring, even while hidden: Godot's Area3D never retroactively fires
		// BodyEntered for a body already inside when Monitoring flips on later, so a
		// reveal-on-checkpoint item that only started monitoring once revealed could stand
		// right next to the player and never register. Gate the checkpoint instead.
		Monitoring = true;
		AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = Radius } });
		BuildVisual();
		if (Engine.IsEditorHint()) return;
		BodyEntered += OnEntered;
		BodyExited += OnExited;
		_revealed = RequiredCheckpoint == Checkpoint.None
			|| (StoryManager.Instance != null && StoryManager.Instance.Current >= RequiredCheckpoint);
		Visible = _revealed;
	}

	/// <summary>
	/// Polling with our own press-edge tracking (not _UnhandledInput or the global
	/// IsActionJustPressed, whose one-frame pulse a scripted key tap can miss depending
	/// on node processing order) so a simulated tap behaves the same as a real one.
	/// </summary>
	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint()) return;
		if (!_revealed)
		{
			if (StoryManager.Instance == null || StoryManager.Instance.Current < RequiredCheckpoint) return;
			_revealed = true;
			Visible = true;
			if (_playerInRange) ShowInteractPrompt();
		}
		bool pressed = Input.IsActionPressed("interact");
		bool justPressed = pressed && !_wasPressed;
		_wasPressed = pressed;
		if (!_revealed || !_playerInRange || _player == null || !justPressed) return;
		var inv = _player.GetNodeOrNull<PlayerInventory>("Inventory");
		if (inv == null) return;
		if (RequiredKey != ToolKind.None)
		{
			if (inv.CurrentTool != RequiredKey) { InteractPrompt.Instance?.ShowPrompt($"Locked. Needs the {RequiredKey}."); return; }
			inv.Consume();
		}
		if (!inv.TryPickup(Kind)) return;
		if (Kind == ToolKind.NewelPost) StoryManager.Instance?.MarkNewelPostTaken();
		InteractPrompt.Instance?.HidePrompt();
		QueueFree();
	}

	private void OnEntered(Node3D body)
	{
		if (body is not PlayerController p) return;
		_player = p;
		_playerInRange = true;
		if (_revealed) ShowInteractPrompt();
	}

	private void OnExited(Node3D body)
	{
		if (body != (Node3D)_player) return;
		_playerInRange = false;
		InteractPrompt.Instance?.HidePrompt();
	}

	private void ShowInteractPrompt()
	{
		string hint = RequiredKey != ToolKind.None ? $" (locked, needs the {RequiredKey})" : "";
		InteractPrompt.Instance?.ShowPrompt($"[E] Take {Label}{hint}");
	}

	private void BuildVisual()
	{
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		var gen = new Node3D { Name = "Generated" };
		AddChild(gen);
		var k = new MeshKit();
		switch (Kind)
		{
			case ToolKind.Lantern: BuildLantern(k); break;
			case ToolKind.Compass: BuildCompass(k); break;
			case ToolKind.Axe: BuildAxe(k); break;
			case ToolKind.Key: BuildKey(k); break;
			case ToolKind.Hammer: BuildHammer(k); break;
			case ToolKind.Camera: BuildCamera(k); break;
			case ToolKind.NewelPost: BuildNewelPost(k); break;
		}
		k.CommitTo(gen, "Mesh");
	}

	private static void BuildLantern(MeshKit k)
	{
		var metal = ProcTextures.MetalMat;
		var glass = ProcTextures.Flat("lantern_glass", new Color(0.75f, 0.68f, 0.42f), 0.3f);
		k.Color = new Color(0.3f, 0.28f, 0.24f);
		k.Mat(metal);
		k.Box(new Vector3(0, 0.09f, 0), new Vector3(0.1f, 0.02f, 0.1f));         // base
		k.Cylinder(new Vector3(0, 0.1f, 0), new Vector3(0, 0.22f, 0), 0.035f, 0.045f, 6);
		k.Box(new Vector3(0, 0.24f, 0), new Vector3(0.11f, 0.02f, 0.11f));       // cap
		k.Cylinder(new Vector3(0, 0.24f, 0), new Vector3(0, 0.3f, 0), 0.012f, 0.012f, 5);
		k.Color = Colors.White;
		k.Mat(glass).Cylinder(new Vector3(0, 0.1f, 0), new Vector3(0, 0.22f, 0), 0.03f, 0.04f, 6);
	}

	private static void BuildCompass(MeshKit k)
	{
		var brass = ProcTextures.Flat("compass_brass", new Color(0.55f, 0.42f, 0.2f), 0.4f);
		var glass = ProcTextures.Flat("compass_glass", new Color(0.6f, 0.65f, 0.6f), 0.1f);
		k.Color = Colors.White;
		k.Mat(brass).Cylinder(new Vector3(0, 0.015f, 0), new Vector3(0, 0.03f, 0), 0.09f, 0.09f, 10);
		k.Mat(glass).Cylinder(new Vector3(0, 0.03f, 0), new Vector3(0, 0.033f, 0), 0.075f, 0.075f, 10);
	}

	private static void BuildAxe(MeshKit k)
	{
		var wood = ProcTextures.WoodMat;
		var metal = ProcTextures.MetalMat;
		k.Color = new Color(0.5f, 0.38f, 0.24f);
		k.Mat(wood).Cylinder(new Vector3(0, 0.02f, 0), new Vector3(0.02f, 0.55f, 0.05f), 0.018f, 0.022f, 6);
		k.Color = new Color(0.35f, 0.36f, 0.37f);
		var head = new Basis(Vector3.Forward, Mathf.DegToRad(20f));
		k.Mat(metal).Box(new Vector3(0.02f, 0.55f, 0.05f), new Vector3(0.22f, 0.1f, 0.03f), 1f, head);
	}

	private static void BuildKey(MeshKit k)
	{
		var metal = ProcTextures.Flat("key_iron", new Color(0.25f, 0.24f, 0.22f), 0.6f);
		k.Color = Colors.White;
		k.Mat(metal);
		k.Cylinder(new Vector3(0, 0.02f, 0), new Vector3(0, 0.16f, 0), 0.012f, 0.012f, 6);
		k.Cylinder(new Vector3(0, -0.01f, 0), new Vector3(0, 0.02f, 0), 0.035f, 0.035f, 8, true, 1f);
		k.Box(new Vector3(0.02f, 0.17f, 0), new Vector3(0.05f, 0.015f, 0.015f));
		k.Box(new Vector3(0.02f, 0.2f, 0), new Vector3(0.015f, 0.05f, 0.015f));
	}

	private static void BuildHammer(MeshKit k)
	{
		var wood = ProcTextures.WoodMat;
		var metal = ProcTextures.MetalMat;
		k.Color = new Color(0.5f, 0.38f, 0.24f);
		k.Mat(wood).Cylinder(new Vector3(0, 0.02f, 0), new Vector3(0, 0.42f, 0), 0.018f, 0.022f, 6);
		k.Color = new Color(0.32f, 0.33f, 0.34f);
		k.Mat(metal).Box(new Vector3(0, 0.42f, 0), new Vector3(0.2f, 0.06f, 0.06f));
	}

	private static void BuildCamera(MeshKit k)
	{
		var body = ProcTextures.Flat("camera_body", new Color(0.12f, 0.12f, 0.13f), 0.75f);
		var lens = ProcTextures.Flat("camera_lens", new Color(0.08f, 0.09f, 0.1f), 0.15f);
		var metal = ProcTextures.MetalMat;
		k.Color = Colors.White;
		k.Mat(body).Box(new Vector3(0, 0.05f, 0), new Vector3(0.14f, 0.09f, 0.06f));
		k.Mat(body).Box(new Vector3(0, 0.105f, -0.005f), new Vector3(0.06f, 0.02f, 0.03f));
		k.Mat(lens).Cylinder(new Vector3(0, 0.05f, 0.035f), new Vector3(0, 0.05f, 0.065f), 0.032f, 0.026f, 8);
		k.Color = new Color(0.7f, 0.68f, 0.6f);
		k.Mat(metal).Cylinder(new Vector3(0.04f, 0.1f, 0), new Vector3(0.04f, 0.1f, 0.002f), 0.014f, 0.014f, 6);
	}

	private static void BuildNewelPost(MeshKit k)
	{
		var wood = ProcTextures.WoodMat;
		k.Color = new Color(0.42f, 0.33f, 0.22f);
		k.Mat(wood);
		k.Cylinder(new Vector3(0, 0f, 0), new Vector3(0, 0.05f, 0), 0.045f, 0.05f, 8);
		k.Cylinder(new Vector3(0, 0.05f, 0), new Vector3(0, 0.16f, 0), 0.038f, 0.038f, 8);
		k.Color = new Color(0.48f, 0.38f, 0.26f);
		k.Blob(new Vector3(0, 0.2f, 0), new Vector3(0.05f, 0.055f, 0.05f), 41, 0.12f, false, 0.9f);
	}
}
