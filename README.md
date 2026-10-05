# Hackathon-BetterBrain-Sprout 🌱

![header](Film_1a_Orbit_sprout_betterbrain_2x_trim.gif)

**A brain-controlled short (horror) film about brain-computer interfaces.**

You put on an EEG headset, sit back and relax. While you calm down, a small sprout pushes
out of the ground and grows into a tree.

*But might something go wrong?*

Made for the [g.tec IEEE SMC 2026 BCI Hackathon](https://gtec.at/hackathon/ieee-smc-2026/).

![trailer](GIF-trailer.gif)

## How it works

The film uses **passive control**. You don't steer anything on purpose. The tree responds to
your brain state, read from the EEG.

1. **Band power.** The g.tec Unicorn records 8 EEG channels at 250 Hz. Ten times a second we
   take the last 2 s of each channel, remove the trend, apply a Hann window and an FFT, and add
   up the power in the standard bands: θ (4–8 Hz), α (8–12 Hz) and β (12–30 Hz). The result is
   averaged over the channels.
2. **Relaxation ratio.** **Alpha** activity gets stronger when you are awake but relaxed,
   especially with your eyes closed. **Beta** activity goes with alertness, active thinking
   and tension. The ratio **α/β** therefore rises as you relax and falls as you concentrate or
   tense up. By default the session uses **(α+θ)/β**, a common neurofeedback variant that
   also counts theta, which increases in deep relaxation and meditation. Eight metrics are
   built in; switch between them live with keys 1–8.
3. **Personal baseline.** Everyone's EEG looks different, so we first record 30 s of resting
   signal. After that, the ratio is shown as a z-score: how far you are from your own
   baseline.
4. **Growth.** The smoothed z-score is mapped to a 0–1 relaxation value, and that value sets
   the tree's *growth rate*. When you are relaxed the tree grows; when you are tense it
   pauses.

## Running it

**You need:**

- Unity **6000.5.6f1** (Unity 6)
- A **g.tec Unicorn Hybrid Black** headset
- **g.tec's Unity package for the Unicorn** (included in `Assets/g.tec/Unity Interface`)
- [Git LFS](https://git-lfs.com/), to get the videos, audio and textures

**Steps:**

1. Clone the repo with Git LFS installed. The videos and textures are stored in LFS.
2. Open the repo root in Unity Hub. The first import rebuilds `Library/` and takes a while.
3. Open `Assets/Session/oak_session.unity` and press Play.
4. Connect the Unicorn from the g.tec bar and follow the on-screen steps: calibration,
   baseline, then relax and grow your sprout.

**No headset?** Press **F9** for debug mode. It uses a simulated EEG signal and speeds up
growth; `[` and `]` change the speed. Press **N** to skip the current step.

## Credits

Built at the g.tec IEEE SMC 2026 hackathon. EEG acquisition uses g.tec's Unicorn Unity
package.
