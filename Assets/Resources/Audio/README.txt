Parcel Factory audio (spec 13.1)

Drop two clips into this folder (any format Unity imports: .wav, .ogg, .mp3):

  music-shift.<ext>      background music. Played on a loop for the whole session,
                         so it should loop seamlessly (no silence at the start or end).
  ambient-factory.<ext>  factory ambience (hum, rollers, distant machines). Looped;
                         faded to half at lunch and to silence on the start screen,
                         while paused and during the day summary.

The file names (without extension) must match exactly. If a file is missing the game
stays silent for that channel and the settings card shows a note under its slider.
Import settings are fixed by Assets/Editor/AudioImport.cs (Vorbis 0.6, load in background;
music streams, ambience is compressed in memory).

Only use audio you are allowed to redistribute under a free licence (for example CC0 or
CC-BY with attribution added to the credits). Do not add commercial or unlicensed tracks.
