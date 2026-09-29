using System;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.LodgeParts;

/// <summary>
/// Something in the lodge to search: a drawer that slides out, or a cabinet, wardrobe or closet door that swings
/// open. Its moving part is the node <see cref="Part"/> (build the drawer front or the door into it); what's
/// inside is put there when it opens (<see cref="OnOpened"/>). Once opened it stays open; an empty one says so.
/// </summary>
public partial class Searchable : Node3D
{
	public enum Kind { Drawer, Door }
	public Kind Type = Kind.Drawer;
	/// <summary>Drawer: how far it slides out along local +Z. Door: the swing (radians about local Y).</summary>
	public float Travel = 0.32f;
	public string What = "drawer";
	public bool IsOpen { get; private set; }
	/// <summary>Set when something's inside (a key, a note); the Searchable doesn't know what.</summary>
	public bool HasSomething;
	public Node3D Part { get; private set; }
	public PickupInteractable Use { get; private set; }
	/// <summary>Called as it opens, with the part (put the find in it).</summary>
	public Action<Searchable> OnOpened;
	/// <summary>A flag that marks it opened in the save (so a Continue restores it open, its find taken or not).</summary>
	public string Flag = "";

	/// <summary>The pick point, local (the drawer's pull or the door's handle).</summary>
	public Vector3 Grip = new(0, 0, 0.02f);
	public float PickRadius = 0.22f;

	public override void _EnterTree()
	{
		if (Part == null) { Part = new Node3D { Name = "Part" }; AddChild(Part); }
	}

	public override void _Ready()
	{
		Use = new PickupInteractable
		{
			Name = "Use", PickRadius = PickRadius, MaxDistance = 2.2f, Position = Grip,
			PromptFor = _ => IsOpen ? "" : $"Search the {What}", CanUse = _ => !IsOpen,
		};
		Use.Interacted += p => Open(p);
		AddChild(Use);
		if (!string.IsNullOrEmpty(Flag) && StoryManager.Instance?.HasFlag(Flag) == true) Open(null, instant: true);
	}

	public void Open(PlayerController p, bool instant = false)
	{
		if (IsOpen) return;
		IsOpen = true;
		Use.Enabled = false;
		if (!string.IsNullOrEmpty(Flag)) StoryManager.Instance?.SetFlag(Flag);
		string prop = Type == Kind.Drawer ? "position:z" : "rotation:y";
		float to = Type == Kind.Drawer ? Travel : Travel;
		if (instant) { if (Type == Kind.Drawer) Part.Position = Part.Position with { Z = to }; else Part.Rotation = Part.Rotation with { Y = to }; }
		else
		{
			AudioDirector.OneShot(this, Type == Kind.Drawer ? "wood_take" : "door_creak", Type == Kind.Drawer ? 3 : 1, GlobalPosition, Type == Kind.Drawer ? -10f : -14f, "Events", 2f, 0.1f);
			CreateTween().TweenProperty(Part, prop, to, Type == Kind.Drawer ? 0.35f : 0.7f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		}
		OnOpened?.Invoke(this);
		if (!instant && !HasSomething && p != null) _ = StoryBeat.Caption(this, "Nothing.", 0.2f, 0.9f, 0.5f);
	}
}
