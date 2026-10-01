using System.Collections.Generic;
using System.Linq;

namespace ProjectDS.Systems;

/// <summary>
/// The owner: sixty important pictures to take across the game, each scored for clarity, focus,
/// framing and zoom; the monsters are the hard ones, and worth the most. This is the list: every
/// subject's id (its <see cref="World.PhotoSubject.Id"/> and save flag), its pencil caption on the
/// print, how big it is (a radius in metres, for judging the zoom and the framing) and whether it's a
/// monster (its points count double). In the order the game meets them.
/// </summary>
public static class PhotoCatalog
{
	public sealed record Entry(string Id, string Caption, float Radius, bool Monster = false);

	public static readonly IReadOnlyList<Entry> All = new List<Entry>
	{
		// Act 1: the trail
		new("bird_red", "a red bird", 0.15f), new("bird_blue", "a blue bird", 0.15f), new("bird_purple", "a purple bird", 0.15f),
		new("bird_black", "a black bird", 0.15f), new("deer", "a deer", 0.8f), new("frog", "a frog", 0.06f),
		new("wildflowers", "wildflowers", 1.1f), new("mushrooms", "mushrooms", 0.2f), new("waterfall", "the waterfall", 3.5f),
		new("weird_stone", "a strange stone", 1.2f), new("stairs", "stairs?", 1.6f),
		// the Hollow
		new("camp", "R.H.'s camp", 2.2f), new("cabin", "the cabin", 4f), new("shed", "the shed", 2f), new("woodpile", "a woodpile", 1.2f),
		new("signpost", "a trail sign", 0.7f), new("tree_number", "a number on a tree", 0.6f), new("footbridge", "the footbridge", 3f),
		new("fallen_tree", "the fallen tree", 5f), new("friend", "him", 0.9f), new("bunker_door", "the bunker door", 1.6f),
		new("walkie", "the walkie", 0.12f), new("crt_room", "the screens", 1.5f), new("vine_door", "the vine door", 1.2f),
		new("the_lake", "the lake", 40f), new("forester_station", "the forester station", 5f),
		// the station, and down
		new("lobby", "the lobby", 3f), new("writing_on_wall", "the writing", 2f), new("red_room", "the red room", 2.2f),
		new("cryptex", "the cryptex", 0.25f), new("flooded_basement", "the basement", 3f), new("the_well", "the stairwell", 5f),
		new("hallway", "the long hallway", 2f), new("closet", "the closet", 1.3f), new("sewer", "the sewer", 2f),
		new("the_hole", "the hole", 1.4f), new("the_pit", "the pit", 8f), new("valve", "a valve", 0.45f),
		new("puzzle_box", "the puzzle box", 0.35f), new("fireplace", "the fireplace", 1f), new("secret_bookcase", "the bookcase", 1.3f),
		new("journal", "the journal", 0.15f), new("the_ring", "the ring", 3f), new("bricked_windows", "bricked windows", 4f),
		new("switches", "the switches", 0.45f), new("room_above", "the room above", 3f),
		// the church (Act 21)
		new("church_nave", "the church", 12f), new("font", "the font", 0.8f), new("altar", "the altar", 2f),
		new("crypt", "the crypt", 3f), new("church_door", "the great door", 2.5f),
		// the winter woods (Act 22)
		new("snowman", "a snowman", 1.2f), new("plow", "the snowplow", 3f), new("antler_tree", "antlers in a tree", 3f),
		new("ski_tracks", "tracks that stop", 1.5f), new("buried_arm", "an arm in the snow", 1.4f), new("frozen_deer", "a frozen deer", 1f),
		new("lodge_sign", "the lodge sign", 1f), new("the_lodge", "the ski lodge", 20f),
		// the things that hunt you: hard to get, worth double
		new("stalker", "IT", 1.2f, true), new("giant", "the giant", 12f, true), new("room_thing", "the thing in the room", 1f, true),
		new("bunker_creature", "it followed me", 1.2f, true), new("leviathan", "the lake thing", 9f, true), new("dead_eye", "the eye", 0.7f, true),
		new("shadowman", "the shadow man", 1.1f, true), new("pit_beast", "the beast in the pit", 7f, true),
		new("lake_creature", "something in the water", 2f, true),
		new("wendigo", "it wears antlers", 1.8f, true),
	};

	public const int Total = 69;   // sixty, and the winter woods' nine (Act 22)

	private static readonly Dictionary<string, Entry> _byId = All.ToDictionary(e => e.Id);

	public static Entry Get(string id) => id != null && _byId.TryGetValue(id, out var e) ? e : null;
	public static bool Contains(string id) => Get(id) != null;
}
