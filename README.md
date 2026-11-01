# VR Cat Café

The first ten minutes in a cat café, in VR. Warm wood, terracotta tiles, a bar with stools, a window seat, and nothing to win. Pick up the toy wand and a cat chases the feather. Hold a treat tube and nearby cats gather. Sit down and a cat may hop onto the table beside you.

My VR Studio final project at NYU (Spring 2026), built solo by Caroline Guo for **Meta Quest 3**.


## How the cats decide

Nothing is commanded. Each cat checks its own state (eating, being petted, on furniture, on cooldown) and whether it has a path before it reacts.

| You do this | The cats do this |
|---|---|
| Hold a treat tube | Every 0.65 s, cats within 5 m walk to one of four waiting spots about 1.15 m in front of you. Squeeze to feed. A fed cat ignores treats for 18 s. |
| Pick up the wand | Once a second, the nearest free cat is picked to chase the feather. |
| Sit down | After one second, one cat with a route and room to jump is invited to a tabletop within 2.5 m of your seat. Which cat comes is luck. |
| Nothing | Cats wander to random spots and keep a 0.8 m bubble around you. Pet one and it turns to face you and holds still. |

## Who made what

Everything in this repo that isn't a third-party asset is mine: the room layout, lighting, the pick-up and petting interactions, the cat behavior scripts and the HDRP build.

Third-party assets used: Malbers Animations (cat models and animations), a coffee-shop environment pack, AllSkyFree, and Unity's XR Interaction Toolkit samples. Their licenses stay with their authors.

## Run it

- **Unity 6000.3.6f1** (Unity 6.3), **HDRP 17.3**
- XR Interaction Toolkit 3.3, OpenXR 1.16 + Meta OpenXR 2.6, XR Hands 1.9
- Open `Assets/Scenes/CoffeeShopFinal.unity` and press Play with a Quest connected over Link, or build for Android with the Meta Quest OpenXR feature enabled.
- Before building, add `CoffeeShopFinal` to the scene list in Build Profiles.

## Where the code lives

```
Assets/Scripts/
  Cat/    roaming, perching, toy chasing, treat feeding, petting expressions
  XR/     teaser wand, treat tube, seating, petting hand, grab transformers, wrist menu
  Audio/  lo-fi playlist and output routing
```
