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

	/// <summary>(2026-10-29, the playtest: "used interactables turn light red") It has been used (a note read, a thing
	/// looked at, a door opened): its outline turns from white to a light red, so the player knows they've seen it. Set by
	/// <see cref="PlayerInteraction"/> on every use (<see cref="MarkUsed"/>); a note keeps it across a Continue
	/// (<see cref="Readable"/>: its read flag, or one made from its words).</summary>
	public virtual bool Used { get => _used; set => _used = value; }
	private bool _used;

	/// <summary>The player has used it: <see cref="Used"/>, and its outline told.</summary>
	public void MarkUsed()
	{
		Used = true;
		if (OutlineFor() is { } o) o.User = this;
	}

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
		if (!Engine.IsEditorHint()) Callable.From(SetupOutline).CallDeferred();
	}

	// ───────────────────────────── the white outline ─────────────────────────────

	/// <summary>The thin white line (<see cref="World.ItemOutline"/>; the owner, 2026-10-14: every interactable but the
	/// doors and windows). Null: decided for it (an opening, a door, hatch, gate, window or wardrobe, has none); false:
	/// never; true: always, even if it reads like a door.</summary>
	public bool? Outline;
	/// <summary>When the line may show, beyond being usable (a drawer only while it holds something; a thing the story
	/// hides until it's found). Null: whenever it's usable and has something to say.</summary>
	public Func<bool> OutlineWhen;
	/// <summary>For a thing that isn't a mesh of its own (a niche in the great door's one mesh): a stand-in shape, never
	/// seen, that the outline is drawn round (<see cref="OutlineProxyAt"/>: where, in this interactable's space).</summary>
	public Mesh OutlineProxy;
	public Transform3D OutlineProxyAt = Transform3D.Identity;
	private static StandardMaterial3D _proxyMat;

	private static readonly System.Text.RegularExpressions.Regex Opening =
		new(@"\b(doors?|doorway|hatch(es)?|gates?|windows?|shutters?|trapdoor|wardrobe|curtains?)\b|door(use)?\b|(Door|Hatch|Gate|Window)",
			System.Text.RegularExpressions.RegexOptions.CultureInvariant);

	/// <summary>An architectural opening (a door, a hatch, a gate, a window, a wardrobe's doors): what the player uses to
	/// get through, not a thing in the room. Read off its name, its owner's type and what its prompt says.</summary>
	public bool IsOpening()
	{
		string text = $"{Name} {Prompt} {SafePrompt(StoryBeat.Player(this))}";
		// (and what it's part of, two up: a vine door's handle is a plain node under the BunkerVineDoor)
		for (Node n = GetParent(); n != null; n = n.GetParent())
		{
			text += $" {n.GetType().Name} {n.Name}";
			if (n == GetParent()?.GetParent()) break;
		}
		return Opening.IsMatch(text) || Opening.IsMatch(text.ToLowerInvariant());
	}

	private string SafePrompt(PlayerController p)
	{
		try { return GetPrompt(p) ?? ""; }
		catch (Exception) { return ""; }
	}

	private void SetupOutline()
	{
		if (!IsInsideTree() || Outline == false) return;
		// (one set up by its owner, a pickup's: told whose use turns it red)
		if (OutlineFor() is { } own) { own.User ??= this; return; }
		if (Outline == null && IsOpening()) return;
		if (OutlineProxy != null)
		{
			// a thing with no mesh of its own (a niche carved in the door's one mesh): an unseen stand-in of its shape,
			// drawn only into the outline
			var proxy = new MeshInstance3D
			{
				Name = "OutlineProxy", Mesh = OutlineProxy, Transform = OutlineProxyAt, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				MaterialOverride = _proxyMat ??= new StandardMaterial3D
				{
					Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(0, 0, 0, 0),
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DisableReceiveShadows = true,
				},
			};
			AddChild(proxy);
			World.ItemOutline.Attach(this, proxy, OutlineActive).User = this;
			return;
		}
		var root = HighlightRoot.IsEmpty ? GetParent() as Node3D : GetNodeOrNull(HighlightRoot) as Node3D;
		if (root == null) return;
		// no highlight root of its own: its parent is the thing if that's a small thing (a valve, a cigar box, a switch);
		// a room or a level, then only the small meshes right at it (the room round it is not the thing)
		bool whole = !HighlightRoot.IsEmpty || IsSmallThing(root);
		if (World.ItemOutline.Attach(this, root, OutlineActive, null, whole ? null : () => MeshesAt(root)) is { } line) line.User = this;
	}

	/// <summary>At most a couple of dozen meshes, none of them bigger than a piece of furniture (looked at no further
	/// than that).</summary>
	private static bool IsSmallThing(Node3D root)
	{
		int n = 0;
		bool small = true;
		void Walk(Node node)
		{
			if (!small) return;
			if (node is MeshInstance3D mi && mi.Mesh != null)
			{
				Aabb b = mi.GlobalTransform * mi.GetAabb();
				if (++n > 24 || b.Size[(int)b.GetLongestAxisIndex()] > 2.5f) { small = false; return; }
			}
			foreach (var c in node.GetChildren()) Walk(c);
		}
		Walk(root);
		return small && n > 0;
	}

	/// <summary>Usable, with something to say (an empty prompt is a thing with nothing to do now: a keycard the blacklight
	/// hasn't found, a crate already open), and anything else its owner asks.</summary>
	private bool OutlineActive()
	{
		if (!Enabled || !IsVisibleInTree() || (OutlineWhen != null && !OutlineWhen())) return false;
		return SafePrompt(StoryBeat.Player(this)).Length > 0;
	}

	/// <summary>The meshes that are this thing when it has no highlight root of its own: the small ones (a few of its pick
	/// spheres across) close by its pick point, else the one bigger mesh its pick point is in (a candle stand, the font).
	/// The table a note lies on is not the note.</summary>
	private IEnumerable<MeshInstance3D> MeshesAt(Node3D root)
	{
		Vector3 p = GlobalPosition + GlobalBasis * PickOffset;
		float smallSize = Mathf.Clamp(PickRadius * 4f, 0.5f, 2.5f);
		var small = new List<MeshInstance3D>();
		MeshInstance3D best = null;
		float bestScore = float.MaxValue;
		foreach (var mi in SmallMeshes(root))
		{
			// (not a light's shaft, a flame or a glow: what casts no shadow is no thing to outline)
			if (!IsInstanceValid(mi) || !mi.IsVisibleInTree() || mi.CastShadow == GeometryInstance3D.ShadowCastingSetting.Off) continue;
			Aabb box = mi.GlobalTransform * mi.GetAabb();
			float size = box.Size[(int)box.GetLongestAxisIndex()];
			float d = p.Clamp(box.Position, box.End).DistanceTo(p);
			if (d > PickRadius + 0.3f) continue;
			if (size <= smallSize) small.Add(mi);
			// a bigger thing (a candle stand, the font, a sheet): the one mesh it's in
			// (only one the pick point is in or right at: the nearest other mesh is some other thing)
			if (d > 0.12f || (size > 4.5f && d > 0.08f)) continue;
			float score = d * 4f + size;
			if (score < bestScore) { bestScore = score; best = mi; }
		}
		if (small.Count > 0) return small;
		return best != null ? new[] { best } : Array.Empty<MeshInstance3D>();
	}

	private static readonly Dictionary<ulong, (ulong at, List<MeshInstance3D> list)> _small = new();

	/// <summary>Every small mesh under a level node, walked once and shared by all its interactables (a lodge has dozens;
	/// each walking the lodge's whole tree would be thousands of nodes apiece). Walked again after a couple of seconds.</summary>
	private static List<MeshInstance3D> SmallMeshes(Node root)
	{
		ulong now = Time.GetTicksMsec();
		ulong id = root.GetInstanceId();
		if (_small.TryGetValue(id, out var c) && now - c.at < 2000) return c.list;
		var list = new List<MeshInstance3D>();
		void Walk(Node n)
		{
			if (n is MeshInstance3D mi && mi.Mesh != null)
			{
				Aabb b = mi.GetAabb();
				if (b.Size[(int)b.GetLongestAxisIndex()] * mi.GlobalBasis.Scale.Abs().X <= 9f) list.Add(mi);
			}
			foreach (var ch in n.GetChildren()) Walk(ch);
		}
		Walk(root);
		_small[id] = (now, list);
		return list;
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
		// a pickup with its white outline (ItemOutline): the outline's own overlay is the highlight, brightened
		if (OutlineFor() is { } outline)
		{
			outline.Focused = on;
			outline.User ??= this;
			_meshes.Clear();
			_previousOverlay.Clear();
			return;
		}
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

	private World.ItemOutline OutlineFor()
	{
		if (GetNodeOrNull<World.ItemOutline>(World.ItemOutline.NodeName) is { } own) return own;
		var root = HighlightRoot.IsEmpty ? GetParent() : GetNodeOrNull(HighlightRoot);
		return World.ItemOutline.Find(root);
	}

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
