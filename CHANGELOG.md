# Changelog

## 1.0.0 — initial public package

Includes exploration themes, team-turn colors, turn sweeps, G-key health, gold F1–F3 AP, purple numpad Advantage, squad damage/healing feedback and the squad AP-based Space cue.

Optimization over the 0.2.0 test build:

- Refresh selection/ability object discovery once per four samples, while reading changing values every sample. Mission changes clear the cache; invalid references force rediscovery. Newly created objects may take up to approximately one second to appear.
- Reuse attribute readings within each sample, avoiding reading the selected squad member twice. No health or AP values are cached between samples.
- Precompute LED positions and numpad slot mappings at connection time; calculate the pulse waveform once per frame.
- Reduce normal logs to turn/squad status and errors.
- Package only an explicit release-file allowlist, then verify every archived file against its build hash.

Validation: 48 C# checks and 35 mocked Lua checks. In a controlled 40-sample player-turn workload, global searches fell from 120 to 60; selected-unit attribute traversals fell from 80 to 40. These are operation counts, not measured FPS or game-thread timing gains. The previous 0.2.0 build was tested in-game by the user; this optimization still merits a short in-game smoke test before publication.

Known limitation: the Advantage meter is confirmed in live logs, but the separate ability-usable flag remained unknown in the captured session. Do not treat the readiness pulse as a verified release feature.

## 0.2.0 — combat test build

Moved AP to gold F1–F3; added Advantage meter, squad health tracking across teams, turn-change sweeps and the all-living-squad AP Space cue. Fixed snapshot file sharing.

## 0.1.2 — character reference fix

Unwrapped SelectedCharacter's FWeakObjectPtr and used the selected character's own ability-system component. Confirmed health/AP reads in-game.
