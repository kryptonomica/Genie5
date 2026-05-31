# Genie 5 — Privacy Policy

_Last updated: 2026-05-31_

Genie 5 is an open-source desktop game client for [DragonRealms](https://www.play.net/dr).
This policy describes what data the software handles and where it goes.

## Summary

**This program will not transfer any information to other networked systems unless
specifically requested by the user or the person installing or operating it.**

The Genie 5 maintainers do not operate any servers, do not collect telemetry, and
receive no data from your use of the software.

## What Genie 5 connects to

Genie 5 makes network connections **only** to:

- **Simutronics' official servers** (`eaccess.play.net`, `*.play.net`,
  `*.simutronics.net`) — for account authentication and live gameplay. This is the
  same destination the official client uses.
- **A local [Lich 5](https://github.com/elanthia-online/lich-5) proxy** (`127.0.0.1`)
  — only if you explicitly select Lich Proxy connection mode.
- **An AI vendor API** — only if you explicitly opt in to AI advisor features (a
  roadmap item, off by default). When enabled, the outgoing context is filtered to
  remove other players' speech (whisper / talk / thoughts / familiar / tells) before
  any request is made.

Genie 5 does not "phone home," check for updates against maintainer-operated servers,
or send usage analytics.

## Data stored on your device

- **Account credentials** — your DragonRealms account name and password, if you save a
  connection profile. Passwords are encrypted at rest with **AES-256-GCM** and stored
  locally under `{AppData}/Genie5/`. They are transmitted only to Simutronics' official
  authentication servers, using the game's standard login handshake.
- **Configuration** — your scripts, rules (`.cfg`), maps, and per-character profile
  data, all stored locally on your device.
- **Session recordings** — only if you use the Session Recorder; these raw-XML captures
  are written to local disk and never uploaded.

All of this data stays on your machine. Uninstalling Genie 5 and deleting its
`{AppData}/Genie5/` folder removes it.

## Third parties

Genie 5 does not share data with any third party. Your interactions with Simutronics'
servers are governed by [Simutronics' own policies](https://www.play.net/) and the
[DragonRealms Allowed Software policy](https://elanthipedia.play.net/DragonRealms_Policy:_Allowed_Software).
If you enable the optional AI advisor, the AI vendor's terms apply to the filtered
context you choose to send.

## Contact

Questions about this policy can be raised via
[GitHub Issues](https://github.com/GenieClient/Genie5/issues) or the process described
in [SECURITY.md](SECURITY.md).
