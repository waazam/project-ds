using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;

namespace ProjectDS.World.LodgeParts;

/// <summary>
/// A wall switch (the lodge's rooms, Act 23): a brass plate with a toggle, at the height of a hand by a door. It
/// turns a room's lights on and off: its lamps, and the glow of any shades on the circuit. The rooms' lights are
/// off when the player first comes in (the lodge was shut down for the holidays): the switch, or the lantern.
/// Built facing +Z (its plate on the wall behind it); place and turn it like any node.
/// </summary>
public partial class LightSwitch : Node3D
{
	public readonly List<Light3D> Lights = new();
	/// <summary>Glowing materials on the circuit (lamp shades), dimmed with it.</summary>
	public readonly List<BaseMaterial3D> Glows = new();
	public bool On { get; private set; }
	public PickupInteractable Use { get; private set; }
	public string Room = "the room";
	/// <summary>Fired after a flick (tests, and a room that answers its switch).</summary>
	public event System.Action<LightSwitch> Flicked;

	private readonly Dictionary<Light3D, float> _energy = new();
	private readonly Dictionary<BaseMaterial3D, float> _glow = new();
	private Node3D _toggle;

	public override void _Ready()
	{
		var k = new MeshKit();
		k.Mat(LodgeTextures.BrassMat);
		k.Color = Colors.White;
		k.Box(new Vector3(0, 0, 0.006f), new Vector3(0.08f, 0.12f, 0.012f), 1f);
		k.CommitTo(this, "Plate", false);
		_toggle = new Node3D { Name = "Toggle", Position = new Vector3(0, 0, 0.014f) };
		AddChild(_toggle);
		var t = new MeshKit();
		t.Mat(LodgeTextures.PorcelainMat);
		t.Color = Colors.White;
		t.Box(new Vector3(0, 0.012f, 0.008f), new Vector3(0.014f, 0.03f, 0.014f), 1f);
		t.CommitTo(_toggle, "Lever", false);
		Use = new PickupInteractable
		{
			Name = "Use", PickRadius = 0.16f, MaxDistance = 2.0f, Position = new Vector3(0, 0, 0.03f),
			PromptFor = _ => On ? "Turn the lights off" : "Turn the lights on", CanUse = _ => true,
		};
		Use.Interacted += _ => Flick();
		AddChild(Use);
		Callable.From(() => Set(On, true)).CallDeferred();
	}

	/// <summary>The lights on the circuit, and their energies when on.</summary>
	public void Wire(Light3D light)
	{
		if (light == null) return;
		Lights.Add(light);
		_energy[light] = light.LightEnergy;
		light.Visible = On;
	}

	public void WireGlow(BaseMaterial3D m)
	{
		if (m == null || _glow.ContainsKey(m)) return;
		Glows.Add(m);
		_glow[m] = m.EmissionEnergyMultiplier;
		m.EmissionEnergyMultiplier = On ? _glow[m] : 0f;
	}

	public void Flick()
	{
		Set(!On);
		AudioDirector.OneShot(this, "light_switch", 2, GlobalPosition, -10f, "Events", 2f, 0.06f);
		Flicked?.Invoke(this);
	}

	/// <summary>On or off, without the click (a room's own state, a Continue).</summary>
	public void Set(bool on, bool force = false)
	{
		if (on == On && !force) return;
		On = on;
		if (_toggle != null) _toggle.Rotation = new Vector3(on ? -0.45f : 0.45f, 0, 0);
		foreach (var l in Lights) if (IsInstanceValid(l)) { l.Visible = on; l.LightEnergy = _energy.GetValueOrDefault(l, l.LightEnergy); }
		foreach (var m in Glows) m.EmissionEnergyMultiplier = on ? _glow[m] : 0f;
	}
}
