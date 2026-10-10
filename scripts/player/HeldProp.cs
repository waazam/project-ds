using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.World;

namespace ProjectDS.Player;

/// <summary>
/// A tin can or a bottle picked up to throw (2026-10-10, see <see cref="Knockable"/>): held low in the right hand, and
/// thrown (G / B on a pad) where you're looking. It clatters down there, and whatever's listening goes to that spot: the
/// arm in the crawlspace's walls claws after the noise instead of you.
/// </summary>
public partial class HeldProp : HeldItem
{
	protected override Vector3 Rest => new(0.24f, -0.2f, -0.48f);
	protected override Vector3 Lowered => new(0.3f, -0.75f, -0.45f);
	protected override bool Wanted => Has && !CameraUp && !HeldFlare.AnyLit;

	public bool Has { get; private set; }
	public int Thrown { get; private set; }
	private string _model;
	private Dictionary<string, Material> _roles;
	private Knockable.Sound _kind;
	private Vector3 _size;
	private Node3D _holder;
	private bool _hinted;

	public static HeldProp For(PlayerController player)
	{
		var cam = player?.CameraRig?.Camera;
		if (cam == null) return null;
		return cam.GetNodeOrNull<HeldProp>(nameof(HeldProp)) ?? Attach<HeldProp>(player);
	}

	protected override void Build()
	{
		_holder = new Node3D { Name = "Holder" };
		AddChild(_holder);
		BuildFist(this, new Vector3(0, 0.0f, 0), new Color(0.09f, 0.075f, 0.065f), new Color(0.07f, 0.075f, 0.07f));
		var fill = new OmniLight3D { Name = "HandLight", LightColor = new Color(1f, 0.8f, 0.6f), LightEnergy = 0.3f, OmniRange = 0.6f, ShadowEnabled = false, LightCullMask = HeldLayer, Position = new Vector3(-0.1f, 0.1f, 0.1f) };
		AddChild(fill);
		SetLayer(this);
		if (Has) Show(_model, _roles);
	}

	public void Carry(string model, Dictionary<string, Material> roles, Knockable.Sound kind, Vector3 size)
	{
		Has = true;
		_model = model; _roles = roles; _kind = kind; _size = size;
		AudioDirector.OneShot(this, "cloth", 4, null, -16f, "Player");
		if (_holder != null) Show(model, roles);
		if (!_hinted)
		{
			_hinted = true;
			_ = Systems.StoryBeat.Caption(this, "[G] Throw it", 0.4f, 2.6f, 0.8f);
		}
	}

	private void Show(string model, Dictionary<string, Material> roles)
	{
		foreach (var c in _holder.GetChildren()) c.QueueFree();
		var mi = FurnitureKit.Clean(() => FurnitureKit.Place(_holder, model, Transform3D.Identity, roles, "Model"));
		_holder.Position = new Vector3(0, -_size.Y * 0.35f, -0.02f);
		SetLayer(_holder);
		_ = mi;
	}

	public void Throw()
	{
		if (!Has) return;
		var cam = Player.CameraRig?.Camera;
		if (cam == null) return;
		var fwd = -cam.GlobalBasis.Z;
		var k = new Knockable { Name = "ThrownProp", Model = _model, Roles = _roles, Kind = _kind, Size = _size, Thrown = true };
		(GetTree().CurrentScene ?? GetTree().Root).AddChild(k);
		k.GlobalPosition = cam.GlobalPosition + fwd * 0.45f + cam.GlobalBasis.X * 0.2f - cam.GlobalBasis.Y * 0.1f;
		k.LinearVelocity = fwd * 8.5f + Vector3.Up * 2.2f + Player.Velocity * 0.5f;
		k.AngularVelocity = new Vector3(6f, 2f, 3f);
		Has = false;
		Thrown++;
		foreach (var c in _holder.GetChildren()) c.QueueFree();
		GD.Print($"[prop] thrown ({_model})");
	}

	public override void _Process(double delta)
	{
		if (Has && !HeldFlare.AnyLit && Player?.PlayerInput is { ThrowPressed: true } && Raise > 0.6f) Throw();
		base._Process(delta);
	}

	protected override void Animate(float dt) => Hold = Rest;
}
