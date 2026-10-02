using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>Act 19's beats: the sheet, the puzzle box, the bookmark, the bookcase.</summary>
public partial class Library
{
	/// <summary>True while the player is in the library, the passage or the round room (for the calm fog).</summary>
	public bool PlayerInside(Vector3 world)
	{
		Vector3 l = ToLocal(world);
		return l.X > -HalfW - 1f && l.X < HalfW + 1f && l.Z > -0.4f && l.Z < RoundRoomAt.Z + RoundRoom.Radius + 1f && l.Y > -2f && l.Y < 60f;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		SeekJournal();
		var player = StoryBeat.Player(this);
		if (player == null || !PlayerInside(player.GlobalPosition)) return;
		// out of the pit's red-black murk: clear, warm, still air
		if (StoryBeat.Atmosphere(this) is { } atmo)
		{
			atmo.Underground = Mathf.MoveToward(atmo.Underground, 1f, dt * 2f);
			atmo.UndergroundFogColor = atmo.UndergroundFogColor.Lerp(new Color(0.09f, 0.065f, 0.045f), Mathf.Min(1f, dt));
			atmo.UndergroundFogDensity = Mathf.MoveToward(atmo.UndergroundFogDensity, 0.006f, dt * 0.05f);
		}
	}

	// ------------------------------------------------------------------ the sheet

	private void OnSheet(PlayerController player)
	{
		if (SheetOff || player == null) return;
		SheetOff = true;
		SheetUse.Enabled = false;
		_ = Cutscene.Run(this, ct => PullSheet(player, ct), lockInput: true);
	}

	/// <summary>The sheet comes off as cloth: a handful of its near edge taken in hand, lifted and drawn slowly back
	/// toward the player and down to the floor; let go, the rest slides off after it and settles in a heap beside the
	/// table (VerletCloth: the owner found Jolt's cloth spasming and flung across the room).</summary>
	private async Task PullSheet(PlayerController player, CancellationToken ct)
	{
		await Look(player, ToGlobal(TableAt + new Vector3(0, 0.75f, 0)), 0.8f, ct);
		Sfx("cloth", 4, _sheet.GlobalPosition, -2f, 0.85f);
		// the near edge's middle point: the grid's x runs across the table, and the player is to one side of it
		Vector3 toward = (player.GlobalPosition - _sheet.GlobalPosition) with { Y = 0 };
		Vector3 towardLocal = _sheet.GlobalBasis.Inverse() * toward.Normalized();
		int gi = towardLocal.X >= 0 ? DrapeNx : 0, gj = DrapeNz / 2;
		bool alongZ = Mathf.Abs(towardLocal.Z) > Mathf.Abs(towardLocal.X);
		if (alongZ) { gi = DrapeNx / 2; gj = towardLocal.Z >= 0 ? DrapeNz : 0; }
		var cloth = _sheet.GetNode<VerletCloth>("Drape");
		// a handful of the edge, not one point (one point only stretches the cloth)
		var grips = new List<int>();
		for (int d = -4; d <= 4; d++) grips.Add(alongZ ? cloth.IndexOf(gi + d, gj) : cloth.IndexOf(gi, gj + d));
		Vector3 gripLocal = cloth.Point(grips[4]);
		var hand = new Node3D { Name = "Hand", Position = gripLocal };
		_sheet.AddChild(hand);
		cloth.Wake();
		foreach (int g in grips) cloth.Pin(g, hand);
		Vector3 dir = alongZ ? new Vector3(0, 0, Mathf.Sign(towardLocal.Z)) : new Vector3(Mathf.Sign(towardLocal.X), 0, 0);
		// lifted off the table, drawn back over the near edge, and down to the floor beside it: slow, eased
		Vector3 p0 = gripLocal, p1 = gripLocal + Vector3.Up * 0.45f + dir * 0.2f,
			p2 = gripLocal with { Y = 0.3f } + dir * 0.8f, p3 = gripLocal with { Y = -0.5f } + dir * 1.05f;
		var tw = CreateTween();
		tw.TweenMethod(Callable.From<float>(u =>
		{
			if (!IsInstanceValid(hand)) return;
			float v = 1f - u;
			hand.Position = v * v * v * p0 + 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u * p3;
		}), 0f, 1f, 2.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		tw.TweenInterval(0.2f);
		await Cutscene.Tween(this, tw, ct);
		for (int r = 0; r < 4; r++)
		{
			cloth.Unpin(grips[r]);
			cloth.Unpin(grips[grips.Count - 1 - r]);
			await Cutscene.Wait(this, 0.06, ct);
		}
		cloth.Unpin(grips[4]);
		hand.QueueFree();
		Sfx("cloth", 4, _sheet.ToGlobal(p3), -6f, 0.7f);
		_ = Settled(cloth);
		await Look(player, Puzzle.GlobalPosition, 1.2f, ct);
		_ = StoryBeat.Caption(this, "A puzzle box. The pieces are beside it.", 0.4f, 2.4f, 1f);
		await Cutscene.Wait(this, 0.6, ct);
		BoxUse.Enabled = true;
		GD.Print("[story] Act 19: the sheet comes off - a bamboo puzzle box and five loose pieces");
	}

	/// <summary>For tests: where the pulled-off sheet came to rest (null until it has).</summary>
	public Vector3? SheetHeapWorld { get; private set; }

	private async Task Settled(VerletCloth cloth)
	{
		while (IsInstanceValid(cloth) && !cloth.Settled) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (IsInstanceValid(cloth)) SheetHeapWorld = cloth.GlobalTransform * cloth.GetAabb().GetCenter();
	}

	// ------------------------------------------------------------------ the puzzle

	private void OnBox(PlayerController player)
	{
		if (PuzzleSolved || player == null) return;
		if (PuzzleOverlay.Instance is { IsOpen: false } o) o.Open(player, Puzzle, PuzzleSound);
	}

	private void PuzzleSound(string what)
	{
		Vector3 at = Puzzle.GlobalPosition + Vector3.Up * 0.1f;
		switch (what)
		{
			case "take": Sfx("wood_take", 3, at, -8f, 1f); break;
			case "place": Sfx("wood_place", 3, at, -4f, 1f); break;
			case "bump": Sfx("wood_bump", 3, at, -6f, 1f); break;
			case "turn": Sfx("cryptex_click", 4, at, -14f, 1.3f); break;
			case "tick": Sfx("cryptex_click", 4, at, -18f, 1.6f); break;
		}
	}

	private void OnSolved()
	{
		if (PuzzleSolved) return;
		PuzzleSolved = true;
		BoxUse.Enabled = false;
		_ = Cutscene.Run(this, ct => Dissolve(ct), lockInput: true);
	}

	private async Task Dissolve(CancellationToken ct)
	{
		var player = StoryBeat.Player(this);
		GD.Print("[story] Act 19: the last piece goes in - the box is solved");
		await Cutscene.Wait(this, 0.5, ct);
		// once, softly: a warm bloom of light out of the box (never a flash; gentler still with Reduce Flashing)
		bool reduce = GameSettings.Instance?.ReduceFlashing ?? false;
		float peak = reduce ? 1.2f : 3.0f;
		Sfx("puzzle_glow", 1, Puzzle.GlobalPosition, -2f, 1f);
		var glow = CreateTween();
		glow.TweenProperty(_glow, "light_energy", peak, 1.1f).SetTrans(Tween.TransitionType.Sine);
		glow.TweenProperty(_glow, "light_energy", peak * 0.6f, 0.8f).SetTrans(Tween.TransitionType.Sine);
		if (player != null) _ = Look(player, Puzzle.GlobalPosition, 3.8f, ct);
		await Cutscene.Wait(this, 1.0, ct);
		// and it goes out of the world, burning away in a glowing edge
		double t = 0;
		const double seconds = 2.8;
		while (t < seconds)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			Puzzle.Dissolve((float)(t / seconds));
		}
		Puzzle.Dissolve(1f);
		Puzzle.Visible = false;
		var fade = CreateTween();
		fade.TweenProperty(_glow, "light_energy", 0f, 1.6f);
		// left behind where it stood: a bookmark
		Bookmark = new Pickup
		{
			Name = "Bookmark", Kind = ToolKind.Bookmark, UseSpot = false, SnapToSurface = false, TakenId = "act19_bookmark",
			TakenLine = "A silk bookmark, warm to the touch.",
			Position = Puzzle.Position + new Vector3(0, 0.005f, 0), Rotation = new Vector3(0, 0.5f, 0),
		};
		AddChild(Bookmark);
		await Cutscene.Wait(this, 1.2, ct);
		_ = StoryBeat.Caption(this, "It left something behind.", 0.4f, 2f, 1f);
		GD.Print("[story] Act 19: the box glows and dissolves - a bookmark is left behind");
	}

	// ------------------------------------------------------------------ the bookcase

	private void OnBook(PlayerController player)
	{
		if (BookcaseOpen || player?.Inventory is not { } inv || !inv.HasTool(ToolKind.Bookmark)) return;
		inv.Consume(ToolKind.Bookmark);
		BookcaseOpen = true;
		BookUse.Enabled = false;
		_ = Cutscene.Run(this, ct => OpenBookcase(player, ct), lockInput: true);
	}

	private async Task OpenBookcase(PlayerController player, CancellationToken ct)
	{
		await Look(player, _jutting.GlobalPosition, 0.8f, ct);
		// the bookmark slides into the book, and the book slides home
		var mark = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.035f, 0.16f, 0.003f) },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.06f, 0.07f), Roughness = 0.5f },
			Position = new Vector3(0, 0.3f, -0.03f), Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0),
		};
		_jutting.AddChild(mark);
		var tw = CreateTween();
		tw.TweenProperty(mark, "position", new Vector3(0, 0.12f, -0.02f), 0.9f).SetTrans(Tween.TransitionType.Sine);
		tw.Parallel().TweenProperty(mark, "rotation", new Vector3(0, 0, 0), 0.9f);
		await Cutscene.Tween(this, tw, ct);
		Sfx("cloth", 4, _jutting.GlobalPosition, -14f, 1.4f);
		await Cutscene.Wait(this, 0.3, ct);
		var push = CreateTween();
		push.TweenProperty(_jutting, "position", _jutting.Position + new Vector3(0, 0, 0.07f), 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		await Cutscene.Tween(this, push, ct);
		Sfx("cryptex_click", 4, _jutting.GlobalPosition, -2f, 0.7f);
		await Cutscene.Wait(this, 0.5, ct);
		Sfx("relay_clunk", 1, _bookcase.GlobalPosition + Vector3.Up, 0f, 0.8f);
		await Cutscene.Wait(this, 0.4, ct);
		Sfx("bookcase_swing", 1, _bookcase.GlobalPosition + Vector3.Up, 0f, 1f);
		SetBookcaseOpen(true, instant: false);
		await Look(player, ToGlobal(new Vector3(0, 1.4f, Depth + 1.5f)), 2.6f, ct);
		GD.Print("[story] Act 19: the bookmark goes in, the book slides home - the bookcase swings open");
	}

	/// <summary>The bookcase on its hinge (its right edge), swung into the passage behind.</summary>
	private void SetBookcaseOpen(bool open, bool instant)
	{
		BookcaseOpen = open;
		if (BookUse != null) BookUse.Enabled = !open;
		float yaw = open ? Mathf.DegToRad(90f) : 0f;
		if (instant) { _bookcase.Rotation = new Vector3(0, yaw, 0); _jutting.Position += open ? new Vector3(0, 0, 0.07f) : Vector3.Zero; return; }
		var tw = CreateTween();
		tw.TweenProperty(_bookcase, "rotation", new Vector3(0, yaw, 0), 2.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
	}

	// ------------------------------------------------------------------ helpers

	private async Task Look(PlayerController player, Vector3 at, float seconds, CancellationToken ct)
	{
		var rig = player.CameraRig;
		double t = 0;
		while (t < seconds)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			Vector3 to = at - rig.Camera.GlobalPosition;
			player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, Mathf.Atan2(-to.X, -to.Z)) * Mathf.Min(1f, dt * 3f), 0));
			rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length()), Mathf.Min(1f, dt * 3f)));
		}
	}

	private void Sfx(string name, int variants, Vector3 at, float db, float pitch)
	{
		string path = $"res://assets/audio/sfx/{name}_{GD.RandRange(1, variants):00}.wav";
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}_01.wav";
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}.wav";
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = db, PitchScale = pitch, UnitSize = 3f, MaxDistance = 30f };
		AddChild(s);
		s.GlobalPosition = at;
		s.Finished += s.QueueFree;
		s.Play();
	}
}
