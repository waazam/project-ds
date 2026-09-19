using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Act 5's break-in: at the boarded cabin door, an axe chops through quickly;
/// a hammer takes a slower, hold-to-pry mini-game. Either way the door opens,
/// the tool is used up, and the player can finally step inside.
/// </summary>
public partial class DoorBreakEvent : Area3D
{
	[Export] public NodePath CabinPath = "..";
	[Export] public float AxeSeconds = 2.2f;
	[Export] public float HammerSeconds = 9f;

	private Cabin _cabin;
	private PlayerController _player;
	private bool _playerInRange;
	private bool _busy;
	private float _hammerProgress;
	private bool _wasPressed;

	public override void _Ready()
	{
		_cabin = GetNode<Cabin>(CabinPath);
		BodyEntered += OnEntered;
		BodyExited += OnExited;
	}

	private void OnEntered(Node3D body) { if (body is PlayerController p) { _player = p; _playerInRange = true; } }

	private void OnExited(Node3D body)
	{
		if (body != (Node3D)_player) return;
		_playerInRange = false;
		_hammerProgress = 0f;
		if (!_busy) InteractPrompt.Instance?.HidePrompt();
	}

	public override void _Process(double delta)
	{
		if (!_playerInRange || _player == null || _busy || _cabin.IsOpen) return;
		if (!_cabin.DoorBoarded) { InteractPrompt.Instance?.HidePrompt(); return; }

		bool pressed = Input.IsActionPressed("interact");
		bool justPressed = pressed && !_wasPressed;
		_wasPressed = pressed;

		var inv = _player.GetNodeOrNull<PlayerInventory>("Inventory");
		switch (inv?.CurrentTool)
		{
			case ToolKind.Axe:
				InteractPrompt.Instance?.ShowPrompt("[E] Chop the boards with the axe");
				if (justPressed) { _busy = true; _ = ChopWithAxe(inv); }
				break;
			case ToolKind.Hammer:
				if (Input.IsActionPressed("interact"))
				{
					_hammerProgress = Mathf.Min(1f, _hammerProgress + (float)delta / HammerSeconds);
					InteractPrompt.Instance?.ShowPrompt($"Prying the boards loose... {_hammerProgress * 100f:0}%");
					if (_hammerProgress >= 1f) { _busy = true; _ = FinishWithHammer(inv); }
				}
				else
				{
					_hammerProgress = Mathf.Max(0f, _hammerProgress - (float)delta * 0.4f);
					InteractPrompt.Instance?.ShowPrompt("[Hold E] Pry the boards loose");
				}
				break;
			default:
				InteractPrompt.Instance?.ShowPrompt("The door is boarded shut.");
				break;
		}
	}

	private async Task ChopWithAxe(PlayerInventory inv)
	{
		InteractPrompt.Instance?.ShowPrompt("Chopping through the boards...");
		await ToSignal(GetTree().CreateTimer(AxeSeconds), SceneTreeTimer.SignalName.Timeout);
		inv.Consume();
		await FinishOpening();
	}

	private async Task FinishWithHammer(PlayerInventory inv)
	{
		inv.Consume();
		await FinishOpening();
	}

	private async Task FinishOpening()
	{
		InteractPrompt.Instance?.HidePrompt();
		_cabin.OpenDoor();
		if (GetTree().Root.FindChild("ScreenFader", true, false) is ScreenFader fader)
			await fader.ShowCaption("", "The door gives way.", 1.0f, 1.8f, 1.0f);
	}
}
