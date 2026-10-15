using System;
using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The thin white line round a thing the player can pick up (the owner, 2026-10-14: "add a very thin but noticeable
/// white line highlight for items you can pick up"; then: every interactable but the doors and windows). Every pickup
/// has one, set up by its owner: the <see cref="Pickup"/>s (gear, tools, keys, keycards, the cap, the chalice, the
/// crowbar), the walkie, the flares in the snow, the lodge's tins, the station's eye. Every other
/// <see cref="Systems.Interactable"/> sets up its own (<see cref="Systems.Interactable.Outline"/>): notes, valves,
/// switches, candles, the font, the niches, keypads, puzzle boxes, the crate, the boat; not the architectural openings
/// (doors, hatches, gates, windows, a wardrobe's doors), read off its name, its owner and its prompt.
///
/// <para>How it looks: about a game pixel wide (of the 360 lines, whatever the 3D's supersampling), white, drawn just
/// outside the item's silhouette. It fades in from <see cref="Far"/> to <see cref="Near"/> metres, stays while the item
/// is there to take, a little brighter and wider while the crosshair is on it, and goes with the item. Not through walls
/// (depth-tested; see item_outline.gdshader).</para>
///
/// <para>How it's drawn: each of the item's meshes gets a material_overlay (item_outline_mask.gdshader) that marks the
/// item's visible pixels in the stencil buffer and is also the focus highlight (the dim rim the
/// <see cref="Systems.Interactable"/> used to swap in); its next_pass (item_outline.gdshader) draws the grown hull
/// where the stencil isn't marked. Two extra draws a mesh, only while the line shows (beyond <see cref="Far"/> the
/// overlay is taken off). <see cref="Systems.Interactable"/> finds this by name (<see cref="NodeName"/>) under itself or
/// its highlight root and hands the focus here instead of swapping overlays.</para>
/// </summary>
public partial class ItemOutline : Node
{
	public const string NodeName = "ItemOutline";
	/// <summary>Meta on each outlined mesh (the shader warm-up draws it with the outline's overlay).</summary>
	public const string Meta = "item_outline";
	public const float Near = 5.5f, Far = 7.5f;
	public const float Width = 1.0f, FocusWidth = 1.35f;
	public const float Alpha = 0.8f;
	private const int MaskPriority = 120, LinePriority = 121;

	/// <summary>Whether the item is there to take right now (revealed, not taken); null = whenever it's visible.</summary>
	public Func<bool> Active;
	public bool Focused;

	private Node3D _meshRoot;
	private readonly List<MeshInstance3D> _meshes = new();
	private ShaderMaterial _mask, _line;
	private bool _applied;
	private float _alpha, _focus;
	private double _recollect;

	private static Shader _maskShader, _lineShader;
	private static ShaderMaterial _warm;

	/// <summary>Outlines the meshes under <paramref name="meshRoot"/> (found again until there are some: a model built a
	/// frame late). The outline node goes under <paramref name="owner"/>, which should be the interactable's highlight
	/// root (or its parent, if it has none) so a focus brightens it.</summary>
	public static ItemOutline Attach(Node owner, Node3D meshRoot, Func<bool> active = null, Func<MeshInstance3D, bool> filter = null,
		Func<IEnumerable<MeshInstance3D>> source = null)
	{
		if (owner == null || meshRoot == null) return null;
		if (owner.GetNodeOrNull<ItemOutline>(NodeName) is { } old)
		{
			old._meshRoot = meshRoot; old.Active = active; old._filter = filter; old.Source = source;
			old.Collect();
			return old;
		}
		var o = new ItemOutline { Name = NodeName, _meshRoot = meshRoot, Active = active, _filter = filter, Source = source };
		owner.AddChild(o);
		return o;
	}

	/// <summary>Which meshes under the root are the thing (an interactable with no highlight root of its own lights only
	/// the small things right at it, never the room round it). Null: all of them.</summary>
	private Func<MeshInstance3D, bool> _filter;
	/// <summary>How many meshes it outlines (the audit's log).</summary>
	public int MeshCount => _meshes.Count;

	public static ItemOutline Find(Node root) => root?.GetNodeOrNull<ItemOutline>(NodeName);

	private static ShaderMaterial NewMask() => new()
	{
		Shader = _maskShader ??= GD.Load<Shader>("res://assets/shaders/item_outline_mask.gdshader"),
		RenderPriority = MaskPriority,
	};

	private static ShaderMaterial NewLine() => new()
	{
		Shader = _lineShader ??= GD.Load<Shader>("res://assets/shaders/item_outline.gdshader"),
		RenderPriority = LinePriority,
	};

	/// <summary>The overlay the shader warm-up draws an outlined mesh with (one shared pair: the pipelines are the
	/// shaders' with the mesh's vertex layout, whichever item's copy of them it is).</summary>
	public static Material WarmOverlay(MeshInstance3D mi)
	{
		if (!mi.HasMeta(Meta)) return null;
		if (_warm == null)
		{
			_warm = NewMask();
			var line = NewLine();
			line.SetShaderParameter("alpha", 0.0f);
			_warm.NextPass = line;
		}
		return _warm;
	}

	/// <summary>Dev A/B (LookShots --look-pickups): every outline off.</summary>
	public static bool AllOff;
	public const string Group = "item_outline";
	public Node3D MeshRoot => _meshRoot;
	/// <summary>Its line is drawn now (its overlay on).</summary>
	public bool Showing => _applied;
	/// <summary>Whether the item is there to take now (what the line shows for).</summary>
	public bool IsActive => _meshRoot != null && IsInstanceValid(_meshRoot) && _meshRoot.IsVisibleInTree() && (Active?.Invoke() ?? true);

	/// <summary>The item's meshes' world bounds (false: none yet).</summary>
	public bool TryBounds(out Aabb box)
	{
		box = default;
		bool any = false;
		foreach (var m in _meshes)
		{
			if (!IsInstanceValid(m)) continue;
			Aabb b = m.GlobalTransform * m.GetAabb();
			box = any ? box.Merge(b) : b;
			any = true;
		}
		return any;
	}

	public override void _Ready()
	{
		AddToGroup(Group);
		_mask = NewMask();
		_line = NewLine();
		_mask.NextPass = _line;
		Collect();
	}

	/// <summary>A flat thing (a card, a note, a key, an envelope) is outlined by pushing straight out from its centre on
	/// the screen: its face's normal, seen at a slant, points up the screen at its near edge too, and the near edge lost
	/// its line. (A second, radial pass chained after the first broke the stencil in 4.7: one pass, chosen per thing.)</summary>
	private void ChooseGrow()
	{
		if (_line == null || !TryBounds(out Aabb b)) return;
		float min = Mathf.Min(b.Size.X, Mathf.Min(b.Size.Y, b.Size.Z)), max = Mathf.Max(b.Size.X, Mathf.Max(b.Size.Y, b.Size.Z));
		_line.SetShaderParameter("radial_only", min < 0.18f * max ? 1.0f : 0.0f);
	}

	public override void _ExitTree() => Apply(false);


	/// <summary>Where the meshes come from instead of a walk under the root (an interactable with no highlight root of its
	/// own: the small meshes right at it, from the level's shared list; see <see cref="Systems.Interactable"/>).</summary>
	public Func<IEnumerable<MeshInstance3D>> Source;

	private void Collect()
	{
		Apply(false);
		_meshes.Clear();
		if (_meshRoot == null || !IsInstanceValid(_meshRoot)) return;
		void Take(MeshInstance3D mi)
		{
			// another overlay already on it (a dust or char overlay): leave that mesh be rather than lose it
			if (mi.Mesh == null || mi.MaterialOverlay != null || (_filter != null && !_filter(mi))) return;
			mi.SetMeta(Meta, true);
			_meshes.Add(mi);
		}
		if (Source != null)
		{
			foreach (var mi in Source()) if (IsInstanceValid(mi)) Take(mi);
			ChooseGrow();
			return;
		}
		void Add(Node n)
		{
			if (n is MeshInstance3D mi) Take(mi);
			foreach (var c in n.GetChildren()) Add(c);
		}
		Add(_meshRoot);
		ChooseGrow();
	}

	private void Apply(bool on)
	{
		if (on == _applied) return;
		_applied = on;
		foreach (var m in _meshes)
			if (IsInstanceValid(m) && (on ? m.MaterialOverlay == null : m.MaterialOverlay == _mask))
				m.MaterialOverlay = on ? _mask : null;
	}

	private double _retry = 0.25, _check, _glance;
	private bool _activeNow;

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		if (_meshRoot == null || !IsInstanceValid(_meshRoot)) { Apply(false); return; }
		if (_meshes.Count == 0 || !IsInstanceValid(_meshes[0]))
		{
			// none yet (a model built a moment late): looked for again, less and less often
			_recollect -= delta;
			if (_recollect > 0) return;
			_retry = Math.Min(_retry * 2, 5.0);
			_recollect = _retry;
			Collect();
			if (_meshes.Count == 0) return;
		}
		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return;
		// far off and not showing: only a glance now and then
		if (!_applied && _alpha <= 0f)
		{
			_glance -= delta;
			if (_glance > 0) return;
			_glance = 0.25;
			if (TryBounds(out Aabb far) && far.GetCenter().DistanceSquaredTo(cam.GlobalPosition) > (Far + 6f) * (Far + 6f)) return;
			_activeNow = IsActive;
			_check = 0.2;
		}
		// the item's centre: its meshes' bounds (a tin or a flare can move)
		if (!TryBounds(out Aabb box)) { Apply(false); return; }
		Vector3 c = box.GetCenter();
		float d = cam.GlobalPosition.DistanceTo(c);
		if (d > Far + 1f && _alpha <= 0f) { Apply(false); return; }
		// whether it's usable now (a prompt may be worked out): five times a second is plenty
		_check -= delta;
		if (_check <= 0) { _check = 0.2; _activeNow = IsActive; }
		bool active = !AllOff && _activeNow;
		if (AllOff) { _alpha = 0f; _focus = 0f; }
		float target = active ? 1f - Mathf.SmoothStep(Near, Far, d) : 0f;
		_alpha = Mathf.MoveToward(_alpha, target, dt * 2.5f);
		// (the focus rim shows whenever it's aimed at, as the old highlight did; the line only while it's usable)
		_focus = Mathf.MoveToward(_focus, Focused && !AllOff ? 1f : 0f, dt * 6f);
		bool show = _alpha > 0.002f || _focus > 0.002f;
		Apply(show);
		if (!show) return;
		float a = Mathf.Clamp(_alpha * Mathf.Lerp(Alpha, 1f, _focus), 0f, 1f), w = Mathf.Lerp(Width, FocusWidth, _focus);
		var l = _line;
		{
			l.SetShaderParameter("alpha", a);
			l.SetShaderParameter("width_px", w);
			l.SetShaderParameter("center_world", c);
		}
		_mask.SetShaderParameter("strength", _focus);
	}
}
