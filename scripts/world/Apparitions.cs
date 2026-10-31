using System.Collections.Generic;
using Godot;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// Something standing down there (2026-10-31, the owner: figures seen "within the environment and not just in the
/// peripheral"). Now and then, in the station, the tunnels, the bunker and the lodge, when they're looking down a long
/// view (a corridor, a tunnel, a hall), a figure is standing far down it, in plain sight, on the floor, under a lamp if
/// there's one near the line: tall, too thin, black, two embers for eyes, facing them. It doesn't come. It goes:
/// <list type="bullet">
/// <item>if they look away and back, it isn't there;</item>
/// <item>if they come toward it, before they're close;</item>
/// <item>if they just stare, the lamp over it stutters out and back, and with the light it's gone.</item>
/// </list>
/// Rare (minutes apart, a few to an area), never while a scripted scare or a cutscene has them. One director to an area
/// (RenderBudget, the bunker), working only while they're inside it.
/// </summary>
public partial class Apparitions : Node3D
{
	private Aabb _bounds;
	private readonly RandomNumberGenerator _rng = new();
	private double _now, _next, _check;
	private int _shown, _max;
	private Node3D _figure;
	private double _figureAt, _seenFor;
	private bool _seenOnce;
	private OmniLight3D _lamp;
	private float _lampEnergy;
	/// <summary>For tests: how many have stood there.</summary>
	public int Shown => _shown;

	public static void Haunt(Node3D root, Aabb bounds, int seed, int max = 3)
	{
		if (root == null || !IsInstanceValid(root) || root.HasMeta("apparitions")) return;
		root.SetMeta("apparitions", true);
		var a = new Apparitions { Name = "Apparitions", _bounds = bounds.Grow(1f), _max = max };
		a._rng.Seed = (ulong)seed;
		a._next = a._rng.RandfRange(40f, 90f);
		root.AddChild(a);
	}

	public override void _Process(double delta)
	{
		_now += delta;
		var player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
		var cam = GetViewport()?.GetCamera3D();
		if (player == null || cam == null) return;
		if (_figure != null) { Watch(player, cam, (float)delta); return; }
		if (_shown >= _max || _now < _next || (_check -= delta) > 0) return;
		_check = 0.5;
		if (!_bounds.HasPoint(player.GlobalPosition) || !player.PlayerInput.Enabled || Systems.PlayerDeath.Dying) return;
		TryStand(player, cam);
	}

	/// <summary>Down the view they're looking along, if it's long: a spot on the floor three-quarters of the way, under a
	/// lamp if one's near the line.</summary>
	private void TryStand(PlayerController player, Camera3D cam)
	{
		var space = GetWorld3D().DirectSpaceState;
		Vector3 eye = cam.GlobalPosition, fwd = (-cam.GlobalBasis.Z with { Y = 0 }).Normalized();
		if (fwd.LengthSquared() < 0.5f) return;
		var q = PhysicsRayQueryParameters3D.Create(eye, eye + fwd * 45f, 1);
		q.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
		var hit = space.IntersectRay(q);
		float reach = hit.Count > 0 ? eye.DistanceTo((Vector3)hit["position"]) : 45f;
		if (reach < 16f) return;
		float d = Mathf.Min(reach - 2.5f, _rng.RandfRange(14f, 26f));
		Vector3 at = eye + fwd * d;
		// a lamp near the line: stand under it
		_lamp = null;
		foreach (var n in GetParent().FindChildren("*", "OmniLight3D", true, false))
		{
			if (n is not OmniLight3D l || !l.Visible || l.LightEnergy < 0.2f) continue;
			Vector3 lp = l.GlobalPosition;
			float along = (lp - eye).Dot(fwd);
			if (along < 12f || along > reach - 1.5f) continue;
			Vector3 onLine = eye + fwd * along;
			if ((lp with { Y = 0 }).DistanceTo(onLine with { Y = 0 }) > 3f) continue;
			if (_lamp == null || Mathf.Abs(along - d) < Mathf.Abs((_lamp.GlobalPosition - eye).Dot(fwd) - d)) _lamp = l;
		}
		if (_lamp != null) at = eye + fwd * (_lamp.GlobalPosition - eye).Dot(fwd);
		// the floor under it, open to them
		var down = space.IntersectRay(PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 1.5f, at + Vector3.Down * 6f, 1));
		if (down.Count == 0) return;
		Vector3 floor = (Vector3)down["position"];
		if (((Vector3)down["normal"]).Y < 0.7f || Mathf.Abs(floor.Y - player.GlobalPosition.Y) > 3f) return;
		var see = PhysicsRayQueryParameters3D.Create(eye, floor + Vector3.Up * 1.6f, 1);
		see.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
		if (space.IntersectRay(see).Count > 0) return;
		_figure = Figure();
		AddChild(_figure);
		_figure.GlobalPosition = floor;
		var face = (eye - floor) with { Y = 0 };
		if (face.LengthSquared() > 0.01f) _figure.GlobalBasis = Basis.LookingAt(face.Normalized(), Vector3.Up);   // (its -Z, its face, to them)
		_figureAt = _now;
		_seenFor = 0;
		_seenOnce = false;
		_lampEnergy = _lamp?.LightEnergy ?? 0f;
		_shown++;
		GD.Print($"[story] something standing down there: {GetParent().Name}, {eye.DistanceTo(floor):0} m off{(_lamp != null ? ", under a lamp" : "")}");
	}

	private void Watch(PlayerController player, Camera3D cam, float dt)
	{
		Vector3 eye = cam.GlobalPosition, to = _figure.GlobalPosition + Vector3.Up * 1.5f - eye;
		float dist = to.Length();
		bool looking = (-cam.GlobalBasis.Z).Dot(to / Mathf.Max(dist, 0.01f)) > 0.8f;
		if (looking) { _seenOnce = true; _seenFor += dt; }
		// looked away and back, or came toward it: not there
		bool gone = (_seenOnce && !looking && _seenFor > 0.4) || dist < 9f || _now - _figureAt > 25;
		if (gone) { Vanish(false); return; }
		// stared at: the lamp over it stutters out, and with the light it's gone
		if (_seenFor > 3.5) Vanish(true);
	}

	private async void Vanish(bool flicker)
	{
		var f = _figure;
		_figure = null;
		_next = _now + _rng.RandfRange(120f, 240f);
		if (flicker && _lamp != null && IsInstanceValid(_lamp))
		{
			var l = _lamp;
			float e = _lampEnergy;
			foreach (var (on, wait) in new[] { (false, 0.08), (true, 0.06), (false, 0.22) })
			{
				l.LightEnergy = on ? e : 0f;
				await ToSignal(GetTree().CreateTimer(wait), SceneTreeTimer.SignalName.Timeout);
				if (!IsInstanceValid(l)) break;
			}
			if (IsInstanceValid(f)) f.QueueFree();
			if (IsInstanceValid(l)) l.LightEnergy = e;
			return;
		}
		if (flicker) Audio.AudioDirector.OneShot(this, "breath_out", 5, f.GlobalPosition, -18f, "Unnatural", 6f, 0.05f);
		if (IsInstanceValid(f)) f.QueueFree();
	}

	/// <summary>Tall, too thin, black (it takes the light and gives none back), arms hanging past its knees, two embers.</summary>
	private static Node3D Figure()
	{
		var k = new MeshKit();
		var black = new StandardMaterial3D { AlbedoColor = new Color(0.012f, 0.011f, 0.011f), Roughness = 1f };
		var ember = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 0.35f, 0.1f), EmissionEnabled = true, Emission = new Color(1f, 0.3f, 0.05f), EmissionEnergyMultiplier = 2.2f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		k.Mat(black);
		k.Cylinder(new Vector3(0.08f, 0f, 0f), new Vector3(0.07f, 1.25f, 0f), 0.045f, 0.06f, 6);
		k.Cylinder(new Vector3(-0.08f, 0f, 0f), new Vector3(-0.07f, 1.25f, 0f), 0.045f, 0.06f, 6);
		k.Cylinder(new Vector3(0f, 1.2f, 0f), new Vector3(0f, 2.15f, 0.03f), 0.13f, 0.15f, 8);
		k.Cylinder(new Vector3(0f, 2.15f, 0.03f), new Vector3(0f, 2.34f, 0.07f), 0.045f, 0.04f, 6);
		k.Cylinder(new Vector3(0.17f, 2.08f, 0f), new Vector3(0.24f, 0.7f, 0.08f), 0.04f, 0.028f, 6);
		k.Cylinder(new Vector3(-0.17f, 2.08f, 0f), new Vector3(-0.24f, 0.7f, 0.08f), 0.04f, 0.028f, 6);
		k.Cylinder(new Vector3(0f, 2.3f, 0.06f), new Vector3(0f, 2.55f, 0.08f), 0.085f, 0.07f, 8, true);
		k.Mat(ember);
		foreach (float x in new[] { -0.03f, 0.03f })
			k.Cylinder(new Vector3(x, 2.45f, -0.01f), new Vector3(x, 2.45f, -0.04f), 0.011f, 0.011f, 6);
		var n = new Node3D { Name = "Apparition" };
		var mi = k.CommitTo(n, "Mesh", true);
		return n;
	}
}
