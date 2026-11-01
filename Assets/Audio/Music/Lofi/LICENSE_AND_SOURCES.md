# Cafe lo-fi background music

Artist: Holizna (HoliznaCC0)
Collection: Happy Lo-Fi (Lofi Collection)
Source: https://opengameart.org/content/happy-lo-fi-lofi-collection
Download: https://opengameart.org/sites/default/files/happy_lo-fi_lofi_collection.zip
License: CC0 1.0 Universal
License deed: https://creativecommons.org/publicdomain/zero/1.0/
Legal text: https://creativecommons.org/publicdomain/zero/1.0/legalcode
Retrieved and checked: 2026-10-17

The author's collection page explicitly labels these recordings CC0. CC0
permits copying, modification, distribution and commercial use without
permission or mandatory attribution. This record credits the artist voluntarily.

## Included recordings (original Ogg Vorbis files, renamed only)

| Project asset | Original collection filename |
| --- | --- |
| Holizna_PoorButHappy.ogg | 01 HoliznaCC0 - Poor, But Happy.ogg |
| Holizna_BlueSkies.ogg | 03 HoliznaCC0 - Blue Skies.mp3.ogg |
| Holizna_PuppyLove.ogg | 19 HoliznaCC0 - Puppy Love.ogg |

## Scene playback

CoffeeShopFinal contains Cafe Lofi Music with CafeLofiPlaylist and two
dedicated child AudioSources. Enter Play mode to start the three-track playlist;
the last track transitions back to the first. Playback is 2D stereo with
an adjustable volume and 3-second crossfades. Adjust Volume and Crossfade Seconds on the
playlist component in the Inspector. The initial fade-in is up to 2 seconds.
The recordings are streamed with Vorbis compression to reduce decoded audio
memory use in VR. Cat sound effects use their existing independent sources.

CafeAudioOutputRouter selects the current Windows desktop output when the
headset is not worn, and the Oculus audio endpoint when XR reports the headset
is worn. This affects Unity's output only. The standalone Quest/Android build
uses the platform's normal output. See NativePlugins/CafeAudioRouter/README.md
for the native Windows helper's source and rebuild instructions.
