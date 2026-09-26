using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.Systems;

/// <summary>
/// Anything the player can use with E: pickups, doors, the CRT switch, the radio.
/// Add it as a child of the object (its meshes are highlighted), set the prompt,
/// and subscribe to <see cref="Interacted"/> or override <see cref="Interact"/>.
///
/// It is found by <see cref="PlayerInteraction"/>, which looks straight out from
/// the centre of the screen: only the thing under the crosshair, within
/// <see cref="MaxDistance"/> and not behind a wall, is focused. Focus shows a dim
/// rim highlight on the object's meshes and the prompt on the HUD. Input comes
/// through PlayerInput, so cutscenes and the pause menu block it automatically.
///
/// Pick volume: an Area3D sphere on the Interactables physics layer (layer 4),
/// created automatically from <see cref="PickRadius"/>/<see cref="PickOffset"/>.
/// </summary>
[GlobalClass]
public partial class Interactable : Node3D
{
	public const uint PickLayer = 1u << 3;   // physics layer 4 "Interactables"

	[Export] public string Prompt = "Interact";
	[Export] public bool Enabled = true;
	[Export] public float MaxDistance = 3f;
	[Export] public float PickRadius = 0.5f;
	[Export] public Vector3 PickOffset = Vector3.Zero;
	/// <summary>0 = a single press; otherwise E must be held this long (HoldProgress 0..1 drives the prompt).</summary>
	[Export] public float HoldSeconds = 0f;
	/// <summary>Node whose meshes get the highlight overlay. Empty = the parent object.</summary>
	[Export] public NodePath HighlightRoot = new();

	/// <summary>Raised when the player uses it (after a press, or once the hold completes).</summary>
	public event Action<PlayerController> Interacted;

	/// <summary>0..1 while E is being held on a hold-to-use interactable.</summary>
	public float HoldProgress { get; internal set; }
	public bool Focused { get; private set; }

	private static ShaderMaterial _highlight;
	private readonly List<GeometryInstance3D> _meshes = new();
	private readonly Dictionary<GeometryInstance3D, Material> _previousOverlay = new();

	public override void _Ready()
	{
		AddToGroup("interactables");
		var area = new Area3D
		{
			Name = "PickArea",
			CollisionLayer = PickLayer,
			CollisionMask = 0,
			Monitoring = false,
			Monitorable = true,
			Position = PickOffset,
		};
		area.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = PickRadius } });
		AddChild(area);
	}

	/// <summary>Whether it can be used right now (e.g. a pickup that needs a free hand). Disabled ones aren't focusable.</summary>
	public virtual bool CanInteract(PlayerController player) => Enabled && IsVisibleInTree();

	/// <summary>Prompt text shown while focused. Override to explain why it can't be used ("Hands full").</summary>
	public virtual string GetPrompt(PlayerController player) => Prompt;

	public virtual void Interact(PlayerController player) => Interacted?.Invoke(player);

	internal void SetFocused(bool on)
	{
		if (on == Focused) return;
		Focused = on;
		if (!on) HoldProgress = 0f;
		if (on) CollectMeshes();
		foreach (var m in _meshes)
		{
			if (!IsInstanceValid(m)) continue;
			if (on)
			{
				_previousOverlay[m] = m.MaterialOverlay;
				m.MaterialOverlay = HighlightMaterial;
			}
			else if (_previousOverlay.TryGetValue(m, out var prev))
				m.MaterialOverlay = prev;
		}
		if (!on) _previousOverlay.Clear();
	}

	public override void _ExitTree() => SetFocused(false);

	private void CollectMeshes()
	{
		_meshes.Clear();
		bool implicitRoot = HighlightRoot.IsEmpty;
		var root = implicitRoot ? GetParent() : GetNodeOrNull(HighlightRoot);
		if (root == null) return;
		if (root is GeometryInstance3D self) _meshes.Add(self);
		foreach (var n in root.FindChildren("*", "GeometryInstance3D", true, false))
		{
			var g = (GeometryInstance3D)n;
			// With no highlight root given, the parent may be a whole level (a room, the sewer): only
			// light up the small things right by this interactable, never the walls round it.
			if (implicitRoot && !Near(g)) continue;
			_meshes.Add(g);
		}
	}

	private bool Near(GeometryInstance3D g)
	{
		Aabb box = g.GlobalTransform * g.GetAabb();
		if (box.Size[(int)box.GetLongestAxisIndex()] > MaxImplicitSize) return false;
		Vector3 p = GlobalPosition + GlobalBasis * PickOffset;
		Vector3 closest = p.Clamp(box.Position, box.End);
		return closest.DistanceTo(p) <= PickRadius + 1.5f;
	}

	/// <summary>The biggest mesh an interactable with no <see cref="HighlightRoot"/> will light up (metres).</summary>
	private const float MaxImplicitSize = 6f;

	private static ShaderMaterial HighlightMaterial => _highlight ??= new ShaderMaterial
	{
		Shader = GD.Load<Shader>("res://assets/shaders/interact_highlight.gdshader"),
	};
}
