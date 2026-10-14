using Godot;

namespace ProjectDS.Player;

/// <summary>
/// What the right hand holds, seen (2026-10-13j; see <see cref="PlayerHands"/>): the chosen thing
/// (<see cref="PlayerInventory.InHand"/>) in a right hand (<see cref="IFirstPersonHand"/>), its model from
/// <see cref="HeldModels"/>, held where and how its <see cref="HeldModels.Spec"/> says. A switch lowers the old thing out of
/// the bottom of the view (rolling away a little as it goes), swaps it, and raises the new one (rolling in), a quarter of a
/// second each way, with the stride's bob and the view's lag of every held thing (<see cref="HeldItem"/>). The camera,
/// raised (right mouse), comes up to the eye and gives way to the viewfinder. Lit by the lantern's warm fill when it's
/// up in the other hand (that light reaches across), by a faint cool rim always, and by its own soft fill in the dark.
/// The flamethrower draws its own wand (<see cref="Flamethrower"/>); an empty hand draws nothing (it hangs at your side).
/// </summary>
public partial class HeldTool : HeldItem
{
	private ToolKind _shown = ToolKind.None;
	private HeldModels.Spec _spec = HeldModels.SpecOf(ToolKind.None);
	private Node3D _pivot, _model;
	private IFirstPersonHand _hand;
	private OmniLight3D _fill;
	private bool _ready;
	private CameraTool _camTool;
	private float _eye;

	/// <summary>For tests: what's built in the hand now, and whether it's up.</summary>
	public ToolKind Shown => _shown;
	public bool Ready(ToolKind kind) => _shown == kind && Raise > 0.9f;
	public IFirstPersonHand Hand => _hand;
	public Node3D Model => _model;

	protected override Vector3 Rest => _spec.HandAt;
	protected override Vector3 Lowered => _spec.HandAt + new Vector3(0.06f, -0.42f, 0.1f);
	protected override float RaiseSeconds => 0.24f;

	private ToolKind Target
	{
		get
		{
			var k = Player?.Inventory?.InHand ?? ToolKind.None;
			return k == ToolKind.Flamethrower ? ToolKind.None : k;   // (the flamethrower's wand is its own)
		}
	}

	protected override bool Wanted => _ready && _shown != ToolKind.None && _shown == Target
		&& (PlayerHands.Instance?.RightFree ?? true)
		&& Flamethrower.Instance is not { WandRaise: > 0.05f };

	protected override void Build()
	{
		_pivot = new Node3D { Name = "Pivot" };
		AddChild(_pivot);
		_fill = new OmniLight3D { Name = "HandLight", LightColor = new Color(0.9f, 0.82f, 0.72f), LightEnergy = 0.12f, OmniRange = 0.7f, OmniAttenuation = 1.4f, ShadowEnabled = false, LightCullMask = HeldLayer, Position = new Vector3(-0.12f, 0.12f, 0.12f) };
		AddChild(_fill);
		_camTool = Player.GetNodeOrNull<CameraTool>("CameraTool");
		_ready = true;
	}

	public override void _Process(double delta)
	{
		// lowered all the way (or never raised): put the new thing in the hand
		if (_ready && _shown != Target && Raise <= 0.001f) Dress(Target);
		base._Process(delta);
	}

	private void Dress(ToolKind kind)
	{
		if (_hand != null) { _hand.Root.QueueFree(); _hand = null; }
		_model = null;
		_shown = kind;
		_spec = HeldModels.SpecOf(kind);
		if (kind == ToolKind.None) return;
		_hand = FirstPersonHands.Create(HandSide.Right, _spec.Pose);
		_pivot.AddChild(_hand.Root);
		_model = HeldModels.Build(kind);
		if (_model != null)
		{
			_model.Position = _spec.ItemAt;
			_model.RotationDegrees = _spec.ItemTurn;
			_hand.Socket.AddChild(_model);
		}
		_hand.Root.RotationDegrees = _spec.HandTurn;
		SetLayer(this);
		foreach (var n in FindChildren("*", "GeometryInstance3D", true, false))
			((GeometryInstance3D)n).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		// the grip's sound as it comes up (a rustle went with the old one going down)
		if (_dressed++ > 0) PlayerHands.Sound(this, "item_take", 3, -19f);   // (not for what was in hand at the load)
	}

	private int _dressed;
	private ToolKind _lastTarget = ToolKind.None;

	protected override void Animate(float dt)
	{
		if (_hand == null) return;
		var target = Target;
		if (target != _lastTarget) { _lastTarget = target; PlayerHands.Sound(this, "cloth", 4, -20f); }
		// the switch: it rolls in from below and rolls away as it goes (the wrist turning, not a slide)
		float r = Mathf.SmoothStep(0f, 1f, Raise);
		var turn = _spec.HandTurn + new Vector3(-38f, 0f, -16f) * (1f - r);
		// the camera to the eye: up and in to the middle, level, then gone behind the viewfinder
		float eye = _shown == ToolKind.Camera && _camTool != null ? _camTool.RaiseAmount : 0f;
		_eye = eye;
		float e = Mathf.SmoothStep(0f, 1f, eye);
		Hold = Rest.Lerp(new Vector3(0.05f, -0.06f, -0.2f), e);
		turn = turn.Lerp(new Vector3(0f, 0f, 0f), e);
		_hand.Root.RotationDegrees = turn;
		_hand.Root.Visible = eye < 0.55f;
		// the walkie's LED: alive from the bunker's door on
		if (_shown == ToolKind.Radio && _model != null)
			HeldModels.SetLed(_model, Systems.StoryManager.Instance is { } s && s.Current >= Systems.Checkpoint.Act10WalkieFound);
		// its own soft fill only where the lantern's isn't lighting it
		bool lanternUp = Player.CameraRig?.Camera?.GetNodeOrNull<HeldLantern>(nameof(HeldLantern)) is { Raise: > 0.5f };
		_fill.LightEnergy = Mathf.MoveToward(_fill.LightEnergy, lanternUp ? 0.06f : 0.16f, dt * 0.5f);
	}
}
