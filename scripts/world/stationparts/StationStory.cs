using System.Collections.Generic;
using System.Text;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World.StationParts;

/// <summary>
/// (2026-10-16, STORY.md Rev 5) The forester station as Mitchell Webber's headquarters: what his story puts in it, shared
/// by the lobby, the rooms and the locked room. His handwriting (a font, and the 3D lettering in it), the texts (his
/// logbook, page by page, verbatim from STORY.md's appendix; the relief crew's notice of February 1988), the story's
/// flags, and the materials for the Blender pieces (tools/Blender/station_story.py, assets/models/furniture/ss_*.glb).
/// </summary>
public static class StationStory
{
	// ------------------------------------------------------------------ flags (saved with the story)

	/// <summary>The desk's photo has been looked at ("That's... mine.").</summary>
	public const string PhotoSeen = "station_photo_seen";
	/// <summary>The lobby was found changed on a return ("Dad... what happened here?").</summary>
	public const string LobbyChangedSaid = "station_lobby_changed_said";
	/// <summary>His logbook has been opened.</summary>
	public const string LogbookRead = "station_logbook_read";
	/// <summary>The relief crew's notice has been read.</summary>
	public const string NoticeRead = "station_notice_read";
	/// <summary>The player has stepped into the room his father locked.</summary>
	public const string LockedRoomEntered = "station_locked_room_entered";

	// ------------------------------------------------------------------ his hand

	private static Font _hand;
	/// <summary>Mitchell's handwriting: a plain, upright print-and-script hand (the Windows handwriting faces).</summary>
	public static Font Hand => _hand ??= new SystemFont { FontNames = new[] { "Segoe Print", "Ink Free", "Bradley Hand ITC", "Comic Sans MS", "serif" } };

	/// <summary>Lettering in his hand on a surface: <paramref name="b"/>'s Z is the way it faces (the text reads from
	/// that side), <paramref name="em"/> its em in metres.</summary>
	public static Label3D HandText(Node parent, string text, Vector3 at, Basis b, float em, Color ink, string name = "Hand",
		HorizontalAlignment align = HorizontalAlignment.Center, float lineSpacing = 0f)
	{
		const int size = 64;
		var l = new Label3D
		{
			Name = name, Text = text, Font = Hand, FontSize = size, PixelSize = em / size, Modulate = ink, OutlineSize = 0,
			Shaded = true, AlphaCut = Label3D.AlphaCutMode.Discard, DoubleSided = false, HorizontalAlignment = align,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps, LineSpacing = lineSpacing,
			Transform = new Transform3D(b, at), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		parent.AddChild(l);
		return l;
	}

	/// <summary>Pencil, faint and grey.</summary>
	public static readonly Color Pencil = new(0.36f, 0.36f, 0.38f);
	/// <summary>His pen: a blue-black gone brown.</summary>
	public static readonly Color PenInk = new(0.1f, 0.09f, 0.12f);
	/// <summary>Old blood, dried dark.</summary>
	public static readonly Color Blood = new(0.24f, 0.02f, 0.02f);
	/// <summary>The other hand's ink in the logbook (from January 2nd): a rusty brown-red, not his.</summary>
	public static readonly Color OtherInk = new(0.34f, 0.1f, 0.07f);

	// ------------------------------------------------------------------ the texts

	/// <summary>The relief crew's notice, pinned in the lobby (STORY.md: dated February 1988; the relief crew reached the
	/// station and found no one; the three rangers declared dead).</summary>
	public const string NoticeText =
		"OVERLOOK PARK\n" +
		"FORESTER STATION\n\n" +
		"NOTICE                       February 1988\n\n" +
		"The relief crew reached the station in\n" +
		"February and found no one. No one was\n" +
		"found in the station or on its grounds.\n\n" +
		"The three rangers stationed here over the\n" +
		"winter,\n\n" +
		"    TRAVIS ALEXANDER\n" +
		"    NATHAN DANIELS\n" +
		"    MITCHELL WEBBER, CHIEF RANGER\n\n" +
		"are declared dead.\n\n" +
		"The station is closed until further notice.";

	/// <summary>The logbook's twelve entries, verbatim from STORY.md's appendix (date, text).</summary>
	public static readonly (string date, string text)[] Entries =
	{
		("November 30th, 1987.", "The snow is coming down hard again. I sent Travis to gather as much firewood as he can. We will be snowed in soon. Nathan is bringing the rest of the food reserves. I will set the snares for some small game. The well has already frozen over, so we will need more buckets and an axe to break through the ice on the lake."),
		("December 11th, 1987.", "We have been snowed in for 10 days now. Food reserves are starting to run low, but the snares are catching a few rabbits. Travis and Nathan are trying to fight their boredom by playing cards, but I can see they are getting a little stir crazy. Our relief team should come to switch with us in a week. Until then we will keep what we can plowed and keep our eyes open for anyone lost out here."),
		("December 23rd, 1987.", "They didn't come."),
		("December 25th, 1987.", "Well, merry fucking Christmas to us. Our firewood is just about gone and we are drying out frozen sticks and logs we found in the woods. The snares aren't enough to sustain us anymore. The food storage is all but gone. We are going to have to survive on pemmican and water until relief arrives. Travis won't talk to us anymore. He has been wrapped in a blanket staring at the fire for days."),
		("December 26th, 1987.", "We are getting too weak. We can barely break through the ice anymore to get water. The firewood is spent. We are huddling for warmth at night. Nathan said he heard something in the woods when he checked the snares. He said it sounded like a child asking him to come look at something. I'm trying to keep everyone sane, but Travis hasn't spoken to us in days."),
		("December 28th, 1987.", "Travis is gone. Nathan said he found a half-eaten rabbit in one of the snares and Travis followed the blood trail. It was fresh. Unfortunately, after searching for Travis we found nothing. The rabbit in the snare was gone."),
		("December 29th, 1987.", "I heard the voises in the woods."),
		("December 31st, 1987.", "Nathan died last night. I had to put him into one of the rooms and lock the door. I cant bury him now. Im locking all the doors. I have to get out of here, the food and water are gone."),
		("January 1st, 1988.", "An eye.... in the lake.... watched me. The basment pipes broke and it is floding."),
		("January 2nd, 1988.", "Im starveing."),
		("January 3rd, 1988.", "So.. hungrey.. so.. coldd."),
		("January 4th, 1988.", "Come and see."),
	};

	/// <summary>
	/// His logbook, page by page. His own entries in his hand, clean at first; from December 29th the hand slips (the
	/// smudged, pressed-hard style). From January 2nd a steadier hand in a different ink, copying his slipping spelling.
	/// After January 4th every page is "Come and see." over and over, drifting, doubling, indenting and crowding the
	/// margins, more on every page, with a few typos, no date, no signature (STORY.md, like the pages in The Shining).
	/// </summary>
	public static NotePage[] LogbookPages()
	{
		var pages = new List<NotePage>();
		string E(int i) => Entries[i].date + "\n" + Entries[i].text;
		void His(Readable.NoteStyle style, params int[] at)
		{
			var sb = new StringBuilder();
			foreach (int i in at) { if (sb.Length > 0) sb.Append("\n\n"); sb.Append(E(i)); }
			pages.Add(new NotePage { Text = sb.ToString(), Style = style, Ink = style == Readable.NoteStyle.Handwritten ? PenInk : null });
		}
		His(Readable.NoteStyle.Handwritten, 0, 1);
		His(Readable.NoteStyle.Handwritten, 2, 3);
		His(Readable.NoteStyle.Handwritten, 4, 5);
		His(Readable.NoteStyle.Smudged, 6, 7);
		His(Readable.NoteStyle.Smudged, 8);
		// the other hand: steadier, a different ink
		pages.Add(new NotePage { Text = E(9) + "\n\n" + E(10), Style = Readable.NoteStyle.Handwritten, Ink = OtherInk });
		pages.Add(new NotePage { Text = E(11), Style = Readable.NoteStyle.Handwritten, Ink = OtherInk });
		// and every page after
		var rng = new RandomNumberGenerator { Seed = 1988 };
		string[] typos = { "Come and se e.", "Comeand see.", "Come and see see.", "Come  and see.", "Come and sea.", "Come and see.." };
		int[] lines = { 9, 13, 17, 22, 28, 34, 40, 48 };
		foreach (int n in lines)
		{
			var sb = new StringBuilder();
			int drift = 0;
			for (int l = 0; l < n; l++)
			{
				// the lines drift right and snap back; some double up; one in a while is misspelled
				drift = rng.Randf() < 0.2f ? 0 : Mathf.Clamp(drift + rng.RandiRange(-1, 3), 0, 18);
				sb.Append(new string(' ', drift));
				int reps = rng.Randf() < 0.35f ? rng.RandiRange(2, 3) : 1;
				if (n > 30 && rng.Randf() < 0.3f) reps++;
				for (int r = 0; r < reps; r++)
				{
					if (r > 0) sb.Append(rng.Randf() < 0.5f ? " " : "  ");
					sb.Append(rng.Randf() < 0.06f ? typos[rng.RandiRange(0, typos.Length - 1)] : "Come and see.");
				}
				sb.Append('\n');
				if (n < 20 && rng.Randf() < 0.25f) sb.Append('\n');
			}
			pages.Add(new NotePage { Text = sb.ToString().TrimEnd('\n'), Style = Readable.NoteStyle.Handwritten, Ink = OtherInk });
		}
		return pages.ToArray();
	}

	/// <summary>"Come and see." in his hand, line after line, to fill a wall <paramref name="width"/> by
	/// <paramref name="height"/> metres at an em of <paramref name="em"/>: the locked room's walls.</summary>
	public static string ComeAndSeeWall(float width, float height, float em, int seed)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)seed };
		int perLine = Mathf.Max(1, Mathf.FloorToInt(width / (em * 8.4f)));   // (a margin: a Label3D never wraps)
		int rows = Mathf.Max(1, Mathf.FloorToInt(height / (em * 1.6f)));
		var sb = new StringBuilder();
		for (int r = 0; r < rows; r++)
		{
			sb.Append(new string(' ', rng.RandiRange(0, 3)));
			int k = perLine - (rng.Randf() < 0.3f ? 1 : 0);
			for (int i = 0; i < k; i++)
			{
				if (i > 0) sb.Append(new string(' ', rng.RandiRange(1, 3)));
				sb.Append("Come and see.");
			}
			sb.Append('\n');
		}
		return sb.ToString().TrimEnd('\n');
	}

	// ------------------------------------------------------------------ materials for the Blender pieces

	private static Material M(string key, Color c, float rough = 0.85f, float spec = 0.2f) => StationTextures.Flat(key, c, rough, spec);

	/// <summary>The desk photo's roles: an old print gone to sepia, its corner rubbed pale.</summary>
	public static Dictionary<string, Material> PhotoRoles => new()
	{
		["frame"] = FixtureModels.Tint(StationTextures.CasingMat, new Color(0.62f, 0.48f, 0.34f)), ["back"] = M("ss_card", new Color(0.36f, 0.31f, 0.24f)),
		["sky"] = M("ss_sky", new Color(0.6f, 0.54f, 0.42f), 0.5f, 0.35f), ["lake"] = M("ss_lake", new Color(0.5f, 0.47f, 0.38f), 0.5f, 0.35f),
		["trees"] = M("ss_trees", new Color(0.16f, 0.14f, 0.1f), 0.5f, 0.35f), ["ground"] = M("ss_ground", new Color(0.31f, 0.26f, 0.18f), 0.5f, 0.35f),
		["man"] = M("ss_man", new Color(0.1f, 0.085f, 0.07f), 0.5f, 0.35f), ["boy"] = M("ss_boy", new Color(0.22f, 0.17f, 0.12f), 0.5f, 0.35f),
		["skin"] = M("ss_skin", new Color(0.55f, 0.45f, 0.34f), 0.5f, 0.35f),
		["rubbed"] = _rubbed ??= new StandardMaterial3D
		{
			ResourceName = "ss_rubbed", AlbedoColor = new Color(0.66f, 0.61f, 0.5f, 0.86f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			Roughness = 0.9f, MetallicSpecular = 0.1f,
		},
	};
	private static StandardMaterial3D _rubbed;

	/// <summary>Room 3's staff photos: sepia rangers, their faces scratched to the pale paper.</summary>
	public static Dictionary<string, Material> StaffRoles => new()
	{
		["frame"] = FixtureModels.Tint(StationTextures.CasingMat, new Color(0.42f, 0.34f, 0.26f)), ["mat"] = M("ss_staffmat", new Color(0.44f, 0.41f, 0.34f)),
		["print"] = M("ss_staffprint", new Color(0.42f, 0.38f, 0.3f), 0.5f, 0.3f), ["uniform"] = M("ss_uniform", new Color(0.14f, 0.14f, 0.1f), 0.5f, 0.3f),
		["hat"] = M("ss_hat", new Color(0.2f, 0.16f, 0.1f), 0.5f, 0.3f), ["face"] = M("ss_staffface", new Color(0.48f, 0.42f, 0.33f), 0.5f, 0.3f),
		["scratch"] = M("ss_scratch", new Color(0.74f, 0.72f, 0.66f), 0.95f, 0.1f), ["brass"] = ItemTextures.BrassMat,
	};

	/// <summary>Everything else's roles (the station's own woods and metals where it has them).</summary>
	public static Dictionary<string, Material> Roles
	{
		get
		{
			var r = LobbyDecor.Roles;
			r["black"] = FixtureModels.Dark;
			r["casing"] = StationTextures.CasingMat;
			r["glass"] = M("ss_night", new Color(0.015f, 0.018f, 0.022f), 0.1f, 0.6f);
			r["plank"] = FixtureModels.Tint(BuildingTextures.BoardsMat, new Color(0.62f, 0.52f, 0.4f));
			r["iron"] = StationTextures.CastIronMat;
			r["canvas"] = M("ss_canvas", new Color(0.24f, 0.26f, 0.17f), 0.95f, 0.05f);
			r["strap"] = M("ss_strap", new Color(0.1f, 0.08f, 0.06f), 0.8f, 0.2f);
			r["clothes"] = M("ss_flannel", new Color(0.33f, 0.12f, 0.09f), 0.95f, 0.05f);
			r["cover"] = M("ss_cover", new Color(0.24f, 0.09f, 0.06f), 0.85f, 0.15f);
			r["edge"] = M("ss_pageedge", new Color(0.6f, 0.56f, 0.46f), 0.95f, 0.05f);
			r["page"] = PaperKit.Material(PaperKit.Look.Ledger, 1987);
			r["lead"] = M("ss_lead", new Color(0.08f, 0.08f, 0.08f), 0.5f, 0.4f);
			r["thread"] = M("ss_thread", new Color(0.14f, 0.16f, 0.1f), 0.95f, 0.05f);
			r["face"] = M("ss_watchface", new Color(0.66f, 0.62f, 0.52f), 0.5f, 0.3f);
			r["gold"] = FixtureModels.Tint(ItemTextures.BrassMat, new Color(1.1f, 0.95f, 0.6f));
			r["blanket"] = M("ss_blanket", new Color(0.27f, 0.25f, 0.22f), 1f, 0.05f);
			r["steel"] = StationTextures.DrabSteelMat;
			return r;
		}
	}

	/// <summary>Puts a piece in (its own mesh instance, so it can be moved or swapped); warns if it isn't built.</summary>
	public static MeshInstance3D Place(Node3D parent, string model, Transform3D xf, Dictionary<string, Material> roles = null, string name = null)
	{
		var mi = FurnitureKit.Place(parent, model, xf, roles ?? Roles, name);
		if (mi == null) GD.PushWarning($"[station] {model}.glb isn't built (tools/Blender/station_story.py)");
		return mi;
	}

	/// <summary>A collision box (layer 1) under <paramref name="parent"/>.</summary>
	public static StaticBody3D Solid(Node3D parent, Vector3 c, Vector3 size, string surface = "wood", string name = "Body")
	{
		var b = new StaticBody3D { Name = name, CollisionLayer = 1, CollisionMask = 0 };
		b.SetMeta("surface", surface);
		b.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = size } });
		parent.AddChild(b);
		return b;
	}
}
