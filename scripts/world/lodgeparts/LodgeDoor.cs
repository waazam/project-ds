using System;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.LodgeParts;

/// <summary>
/// A door in the lodge: a panelled leaf on a hinge, its casing, a brass handle and (for the hotel rooms) a
/// brass number and a keycard reader beside it with a little light (red: locked; green: open; dark: dead).
/// Locked doors want a tool (a key, a keycard); a door can be slammed shut and jammed for good (the rooms, once
/// the player has what's in them). The node sits at the hinge, on the floor; the closed leaf runs along local +X
/// for <see cref="Width"/>; it swings about Y by <see cref="OpenAngle"/> (positive turns it toward -Z).
/// </summary>
public partial class LodgeDoor : Node3D
{
	public enum State { Locked, Open, Closed, Jammed }
	public float Width = 1.0f, Height = 2.2f, OpenAngle = 1.6f;
	public string Number = "";
	public bool HasReader;
	/// <summary>What opens it (None: it just opens).</summary>
	public ToolKind Needs = ToolKind.None;
	public bool ConsumeKey = true;
	/// <summary>The side the reader and the number face (local, +Z or -Z).</summary>
	public float FrontSign = 1f;
	public string LockedPrompt = "Locked.";
	public string OpenPrompt = "Open the door";
	public State Current { get; private set; } = State.Locked;
	public PickupInteractable Use { get; private set; }
	/// <summary>Raised when it opens (by the player, not on restore).</summary>
	public event Action<PlayerController> Opened;

	private Node3D _leaf;
	private StaticBody3D _body;
	private StandardMaterial3D _led;
	private Vector3 _readerPos;

	public Vector3 CentreWorld => ToGlobal(new Vector3(Width * 0.5f, 1.2f, 0));
	/// <summary>Where to stand to use it (on its front side).</summary>
	public Vector3 FrontWorld => ToGlobal(new Vector3(Width * 0.5f, 0.05f, FrontSign * 1.1f));
	public Vector3 ReaderWorld => ToGlobal(_readerPos);

	public override void _Ready()
	{
		_leaf = new Node3D { Name = "Leaf" };
		AddChild(_leaf);
		var k = new MeshKit();
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Color = Colors.White;
		k.Box(new Vector3(Width * 0.5f, Height * 0.5f, 0), new Vector3(Width - 0.02f, Height - 0.01f, 0.05f), 1f);
		// raised panels, both faces
		foreach (float s in new[] { -1f, 1f })
		{
			k.Box(new Vector3(Width * 0.5f, Height * 0.72f, s * 0.03f), new Vector3(Width - 0.24f, Height * 0.36f, 0.012f), 1f);
			k.Box(new Vector3(Width * 0.5f, Height * 0.26f, s * 0.03f), new Vector3(Width - 0.24f, Height * 0.34f, 0.012f), 1f);
		}
		k.Mat(LodgeTextures.BrassMat);
		foreach (float s in new[] { -1f, 1f })
		{
			k.Cylinder(new Vector3(Width - 0.1f, 1.02f, s * 0.03f), new Vector3(Width - 0.1f, 1.02f, s * 0.08f), 0.012f, 0.012f, 6, false);
			k.Box(new Vector3(Width - 0.16f, 1.02f, s * 0.085f), new Vector3(0.12f, 0.02f, 0.02f), 1f);
			k.Box(new Vector3(Width - 0.1f, 1.0f, s * 0.03f), new Vector3(0.05f, 0.2f, 0.008f), 1f);
		}
		k.CommitTo(_leaf, "Mesh", true);
		if (!string.IsNullOrEmpty(Number))
			SignKit.Text(_leaf, Number, new Vector3(Width * 0.5f, 1.62f, FrontSign * 0.038f), FrontSign > 0 ? Basis.Identity : new Basis(Vector3.Up, Mathf.Pi), 0.09f, new Color(0.78f, 0.6f, 0.28f), shadow: false);
		_body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		_body.SetMeta("surface", "wood");
		_body.AddChild(new CollisionShape3D { Position = new Vector3(Width * 0.5f, Height * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(Width, Height, 0.06f) } });
		_leaf.AddChild(_body);
		// the casing round it (both faces)
		var c = new MeshKit();
		c.Mat(LodgeTextures.DarkWoodMat);
		c.Color = Colors.White;
		foreach (float s in new[] { -1f, 1f })
		{
			c.Box(new Vector3(-0.06f, Height * 0.5f + 0.03f, s * 0.1f), new Vector3(0.12f, Height + 0.06f, 0.04f), 1f);
			c.Box(new Vector3(Width + 0.06f, Height * 0.5f + 0.03f, s * 0.1f), new Vector3(0.12f, Height + 0.06f, 0.04f), 1f);
			c.Box(new Vector3(Width * 0.5f, Height + 0.07f, s * 0.1f), new Vector3(Width + 0.26f, 0.14f, 0.04f), 1f);
		}
		if (HasReader)
		{
			_readerPos = new Vector3(Width + 0.26f, 1.2f, FrontSign * 0.13f);
			c.Mat(LodgeTextures.BrassMat);
			c.Box(_readerPos, new Vector3(0.08f, 0.14f, 0.03f), 1f);
			c.Mat(LodgeTextures.BlackMat);
			c.Box(_readerPos + new Vector3(0, -0.01f, FrontSign * 0.016f), new Vector3(0.012f, 0.08f, 0.004f), 1f);   // the slot
			_led = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.05f, 0.04f), EmissionEnabled = true, Emission = new Color(1f, 0.1f, 0.05f), EmissionEnergyMultiplier = 1.2f };
			c.Mat(_led);
			c.Box(_readerPos + new Vector3(0, 0.05f, FrontSign * 0.017f), new Vector3(0.012f, 0.012f, 0.004f), 1f);
		}
		c.CommitTo(this, "Casing", true);
		Use = new PickupInteractable
		{
			Name = "Use", PickRadius = 0.55f, MaxDistance = 2.4f, Position = new Vector3(Width * 0.5f, 1.15f, 0),
			PromptFor = PromptFor, CanUse = CanUse,
		};
		Use.Interacted += OnUse;
		AddChild(Use);
		SetLed(Current == State.Locked ? 1 : Current == State.Jammed ? 0 : 2);
	}

	private string PromptFor(PlayerController p)
	{
		switch (Current)
		{
			case State.Open: return "";
			case State.Jammed: return "It's shut fast. The reader's dead.";
			case State.Closed: return OpenPrompt;
		}
		if (Needs != ToolKind.None && p?.Inventory is { } inv && inv.HasTool(Needs)) return HasReader ? "Use the keycard" : "Unlock it";
		return LockedPrompt;
	}

	private bool CanUse(PlayerController p) => Current == State.Closed || (Current == State.Locked && (Needs == ToolKind.None ? false : p?.Inventory is { } inv && inv.HasTool(Needs)));

	private void OnUse(PlayerController p)
	{
		if (Current == State.Locked && Needs != ToolKind.None && p?.Inventory is { } inv && inv.HasTool(Needs))
		{
			if (ConsumeKey) inv.Consume(Needs);
			if (HasReader)
			{
				AudioDirector.OneShot(this, "card_beep", 1, ReaderWorld, -8f, "Events", 2f, 0.01f);
				SetLed(2);
			}
			else AudioDirector.OneShot(this, "valve_clunk", 1, CentreWorld, -8f);
			Open(p);
			return;
		}
		if (Current == State.Closed) Open(p);
	}

	/// <summary>Unlocks and swings it open.</summary>
	public void Open(PlayerController p = null, bool instant = false)
	{
		if (Current == State.Open || Current == State.Jammed) return;
		Current = State.Open;
		SetCollision(false);
		if (instant) { _leaf.Rotation = new Vector3(0, OpenAngle, 0); SetLed(2); return; }
		AudioDirector.OneShot(this, "door_creak", 1, CentreWorld, -8f, "Events", 3f, 0.08f);
		CreateTween().TweenProperty(_leaf, "rotation:y", OpenAngle, 1.3f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		Opened?.Invoke(p);
	}

	/// <summary>Sets it unlocked but shut (restores; doors that simply open).</summary>
	public void Unlock() { if (Current == State.Locked) { Current = State.Closed; SetLed(2); } }

	/// <summary>Slams it and jams it shut for good: the reader sparks, its light dies.</summary>
	public void SlamAndJam(bool instant = false)
	{
		Current = State.Jammed;
		SetCollision(true);
		SetLed(0);
		if (instant) { _leaf.Rotation = Vector3.Zero; return; }
		if (HasReader)
		{
			AudioDirector.OneShot(this, "card_error", 1, ReaderWorld, -8f, "Events", 2f, 0.01f);
			AudioDirector.OneShot(this, "reader_zap", 1, ReaderWorld, -6f, "Events", 2f, 0.05f);
		}
		var tw = CreateTween();
		tw.TweenInterval(0.35f);
		tw.TweenProperty(_leaf, "rotation:y", 0f, 0.28f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tw.TweenCallback(Callable.From(() => AudioDirector.OneShot(this, "door_slam", 2, CentreWorld, 0f, "Events", 5f, 0.05f)));
	}

	public void SetLocked() { Current = State.Locked; SetCollision(true); _leaf.Rotation = Vector3.Zero; SetLed(1); }

	private void SetCollision(bool on)
	{
		foreach (var c in _body.GetChildren()) if (c is CollisionShape3D cs) cs.SetDeferred(CollisionShape3D.PropertyName.Disabled, !on);
	}

	/// <summary>0 dead, 1 red, 2 green. (Steady: a reader's light never flickers.)</summary>
	private void SetLed(int s)
	{
		if (_led == null) return;
		Color c = s == 0 ? new Color(0.05f, 0.05f, 0.05f) : s == 1 ? new Color(1f, 0.1f, 0.05f) : new Color(0.1f, 1f, 0.3f);
		_led.AlbedoColor = c * 0.5f;
		_led.Emission = c;
		_led.EmissionEnergyMultiplier = s == 0 ? 0f : 1.2f;
	}
}
