# 2026-10-04b — The look: local contrast, each place's colour, drifting fog, cold rims, light shafts, heat haze, wax and damp; the monsters made imposing

Commit: this entry's commit on `main`.

The owner: "I think the crtv filter is great for the game and makes it match the vibe I was going for but I still think we can bake in some other shaders to make everything stand out more." Seven suggestions were approved ("do your recommendations in your preferred order prioritizing efficiency and our aesthetic"). Then: "focus on figuring out incredible shaders for our monsters (bosses) so they look imposing."

Everything stays in the game's PS2-era horror look: nothing bright, no glare, nothing that flashes, nothing that shakes.

## The finish (`ps2_post.gdshader`, `AreaGrade.cs`)
- **Local contrast:**
  - The fine detail in the brightness (a texture's grain, a model's creases) is drawn out a little before the CRT softness and the dither eat it.
  - It works on brightness only, so it never shifts a colour, and it is clamped, so no halo forms round an edge.
  - It eases off toward the frame's soft edges.
- **The red that matters:** blood, the beasts' eyes, the lodge's carpet and a warning sign keep their colour, a little richer, while the grade pulls everything else toward grey.
- **Each place's own colour:** the split-tone and saturation follow where the story is, eased over a few seconds and never cut.

  | Place | Colour |
  |---|---|
  | The woods | as designed |
  | The bunker | sickly green |
  | The lake | a cold cyan dawn |
  | The station | dusty green-grey |
  | The stairwell | cold grey-blue |
  | The sewer | sodium yellow-green |
  | The pit | rust and blood |
  | The library and the round room | candle-warm |
  | The church | deep gold over cold stone |
  | The winter woods | blue |
  | The lodge | amber lamplight against blue snow light |

  Act 15's hall keeps its own red grade.

## Fog that moves (`FogBanks.cs`, `fog_banks.gdshader`)
- **How it works:** a fog volume rides with the camera. Its density comes from two octaves of 3D noise in world space, scrolled by a slow wind, so the banks and wisps drift past instead of moving with the player.
- **Where they lie:** low (thinning with height above the ground), fading out at the volume's sides.
- **Where they show:** outdoors only (none indoors or underground).
  - They thicken as Act 1's fog closes in and again by the stairs.
  - They are thinner in the winter woods' colder air.
- **Light:** they have no light of their own. They show where light passes through them: the sun between the trunks, the lantern's beam.

## The creatures' cold rim (`rim_fresnel.gdshader`, `CreatureRim.cs`)
- **The rim:** a faint cold edge on a creature's silhouette, drawn as an overlay, so it stands out of the dark and the fog without being lit up. It fades close up (in the face it would read as a halo) and into the fog.
- **Who gets it:**
  - the wendigo, its arm in the crawlspace, the crawler, the shadow man;
  - the lake creature and the pit beast, which are seen from much further.
- **Exclusions:**
  - The giant's rim is built into its skin, faint, out to 180 m.
  - The stalker stays true darkness, a hole in the scene (its design: "it must not reflect light"), so its rim is zero. Its rim would also have been built into its skin, so it fades with it: an overlay would have shown it while unseen.
  - See-through cards (breath, glows, the shadow man's blur) are skipped.

## Shafts of daylight (`WindowShafts.cs`, `window_shaft.gdshader`)
- **What they are:** a few crossed cards from a window along the light's way. They are soft at their sides, die away along their length, and fade where they pass into a floor or a wall (no hard line). A handful of dust motes turn slowly in each.
- **The church:** a pale shaft through each of the south clerestory's paired lancets, slanting down across the nave.
- **The lodge's dining hall:** the noon light through the south row's high windows, down across the tables. It goes as the storm comes up, and is gone with the freeze.
- Both are dim: a haze of light.

## Heat haze and wax (`heat_haze.gdshader`, `FireVfx.cs`, `WaxMaterial.cs`)
- **Heat haze:**
  - Over every fire (the library's hearth, the burning cabin, the webs burning) the air shimmers: a card turned to the camera draws what's behind it a pixel or two out of place, the waver rising, thinning upward, scaled with the fire.
  - It starts at the flames' tips and is drawn before the flames and the smoke, so it never rubs them out.
- **Wax:** the candles in the church and the round room and the lodge's candles take the light into their wax, with a warm glow through their thin edges.

## Damp (`ProcTextures.Damp`, `rock_moss.gdshader`)
- **The Hollow's rocks:** the bare stone is wet in patches and down its lower faces, darker there and catching a glint. The moss stays dry and matte.
- **The Hollow's building stone** (the stairs above all) and **the sewer's brick:** wet in soft blotches, glinting where they are wet and dull where they are dry.
- **The bunker's concrete** already had its wet patches.

## The monsters, imposing
- **The wendigo's hide** (`wendigo_skin.gdshader`, over its baked maps):
  - **Hard light:** a lit side and a shadowed side with a sharp edge between, wrapped a little, so the gaunt body reads as ribs and hollows carved out of the dark.
  - **Hoarfrost:** settled on everything facing up (its shoulders, the tops of its arms, its back) in a broken pale crust.
  - **Frostbite:** its hands and feet black and wet, ragged at the edge, creeping up the limbs.
  - **A cold wet glint** where light catches the stretched skin.
  - **Its heart of ice beats:** a slow swell and fade, about every three seconds, faint.
- **The lake thing and the pit beast** (`octopus_flesh.gdshader`):
  - **The slime slides down them,** always, so the whole hide glistens and moves like something alive.
  - **The veins throb** in a slow double beat (lub-dub, rest), swelling dark, with a deep red warmth just showing through the thin flesh over them on the beat (faint).

## Tests
See [2026-10-04c](2026-10-04c-playtest-fixes-and-surprises.md#tests); all three went up together.
