# 2026-10-07b — The lantern in your hand; the camera's flash; the black at the stairwell's foot filled; noises in the storm; leaves and twigs underfoot; the settings' round trip

Six small things for the night, the owner's pick of the suggestions: "yes lets do all those suggestions, if you get the held lantern we can actually see to work we can then add things like the compass and the lighter so we can see them too. We are going to need detailed animations for them."

## The lantern in your hand (`HeldItem.cs`, `HeldLantern.cs`, `Lantern.cs`)
- **Seen:** the hurricane lantern held low in the left hand, a gloved fist round its bail, the wrist and forearm dropping out of the bottom of the view. Its light (the lantern's real light) comes from its glass.
- **Its animations:**
  - **Swinging:** it hangs from the bail and swings on it, a damped pendulum (fore and aft, side to side) pushed by how you move. Starting off tips it back, stopping swings it on, and turning swings it out; it settles slowly. The push is clamped and the swing damped, so it never flails.
  - **Bob:** with your stride, up and down twice a cycle and side to side once, less when crouched, none standing still.
  - **Weight:** it lags a touch behind the view's turns and eases back.
  - **Raised and lowered:** lit, it's raised into view (half a second, eased); put out, or with the camera up to your eye, it's lowered out of it.
  - **Gathering the beam** (right mouse) lifts it in toward the middle of the view, held out ahead.
  - **Blacklight:** the glass glows violet, with a violet bulb where the flame was.
- **How it's lit:** the lantern's own light, an inch from its glass, had blown the glass and the hand to white. The held things are on their own render layer, out of the lantern's light, under a soft warm fill from the flame's place. The glass is warm amber, glowing.
- **Ready for more:** `HeldItem` is the base for anything held and seen. The compass and the lighter will be the next things built on it, with their own animations.

## The camera's flash (`CameraTool.cs`)
- In the dark (the night, the menacing woods, underground), taking a picture fires a flash: a burst of light from the camera that lights the very frame it takes, held a moment, then fading over a third of a second.
- It's one soft flash, never a strobe. With "Reduce flashing" on it's half as bright and fades slower. In daylight, no flash.

## The black at the stairwell's foot (`Stairwell.Bottom.cs`)
The winter is built in the black after the fall (since this morning's load fix), which lasts several seconds. It's filled now: a slow heart, loud in the ears, playing on through the build, and a shuddering breath before and after, as if you lie out cold. Then the heart fades as the eyes open.

## Noises in the storm (`StormSurprises.cs`)
On the storm walk (the camp to the cabin), now and then, something in the dark you only hear. They come rarely (a minute or two apart at least, and sometimes nothing), never the same one twice in a walk, and in a different order every time. Most walks hear two or three of them:
- a branch cracking somewhere far off, and the rush of it coming down;
- something stepping in the mud off to one side, keeping pace, that stops when you stop (and, after a breath, is gone);
- a trunk groaning nearby, with no wind to bend it;
- something heavy dropping out of a tree onto the leaves behind you;
- a twig snapping, close, just behind;
- three knocks on wood from somewhere in the trees.

None of them is ever seen; none of them comes again.

## Leaves and twigs underfoot (`PlayerFootsteps.cs`, `tools/AudioGen/Foley.cs`)
- Dry ground (Act 1's woods, and the Hollow once the rain has stopped): the trail's own beaten earth is dirt. Off it, the leaves are crisp and papery, and here and there a small twig breaks underfoot.
- Fixed by place, as the mud is in the rain. What follows you through the woods steps in kind.
- New sounds: `step_dryleaves_01..06`, `step_twig_01..04`.

## The settings' round trip (`GameSettings.cs`)
- `--settings-test`, on the editor build or the exported game:
  - every setting the menus save is set to something other than its default;
  - saved, scrambled, and read back from the file as a restart would;
  - each compared, with PASS or FAIL printed per setting.
- Your own settings and their file are put back afterwards, and it quits with exit code 0 if all came back.
- All 16 come back.

## Tests
- **The full autotest:** 789 of 789, start to credits. The storm walk heard something in the dark (a heavy drop); there were no errors in the log.
- **Act 4 again**, with the sleeve made matte: 30 of 30. The lantern reads warm amber in the lower left, held in a dark glove.
- **The continue test:** 1058 of 1058, 40 scenarios.
- **The settings' round trip:** 16 of 16, on the editor build and on the exported game.
