# SGE protocol notes

Wire-level documentation for the Simutronics Game Entry (SGE) login flow at
`eaccess.play.net:7900`. Verified against the [Genie 4 source][genie4] and
confirmed via live testing on DragonRealms.

[genie4]: https://github.com/GenieClient

If you touch `SgeAuthClient.cs`, read this file first. Small mistakes here
silently break auth in ways that are hard to debug — a missing tab field,
an off-by-one in the password byte transformation, or assuming a field
index that the server occasionally leaves empty, and you get back a
response that *looks* fine until login mysteriously fails.

## Transport

- Endpoint: `eaccess.play.net:7900` — plain TCP, **no TLS**. Port 7900 does
  not respond to a TLS ClientHello; do not wrap the connection in `SslStream`.
- Server sends **nothing** on connect. The client must initiate.

## Handshake sequence

| Step | Client sends | Server responds |
|---|---|---|
| 1 | `K\n` | 32 raw key bytes + trailing `\n` (33 bytes total over plain TCP) |
| 2 | `A\t{ACCOUNT_UPPER}\t` + encrypted-password-bytes + `\n` | `A\tMONIL\tKEY\t{hash}\t{full name}` on success; `A\t\tPASSWORD` or `A\t\tUNKNOWN…` on failure |
| 3 | `M\n` | game list |
| 4 | `G\tDR\n` | game selected (read and discard the `PREMIUM`/`FREE_TO_PLAY` + URL fields) |
| 5 | `C\n` | `C\t{count}\t{?}\t{?}\t{?}\t{CODE}\t{Name}\t{CODE}\t{Name}\t…` |
| 6 | `L\t{code}\tSTORM\n` | `KEY=…`, `GAMEHOST=…`, `GAMEPORT=…` (game server: `dr.simutronics.net:11024`) |

## Password encoding (from `Genie4/Utility/Utility.cs EncryptText`)

For each byte `i` of the password:

```
encryptedByte[i] = ((password[i] - 32) XOR keyByte[i]) + 32
```

- `keyByte[i]` is used **raw** — do NOT subtract 32 from it.
- Result is sent as **raw bytes** directly in the stream (not hex-encoded).
- The auth message is built as a byte array, not a string:
  `"A\t{ACCOUNT_UPPER}\t"` + raw encrypted bytes + `\n`.

## Response-parsing gotchas

### Character list field layout

```
C\t{count}\t{?}\t{?}\t{?}\t{CODE}\t{Name}\t{CODE}\t{Name}\t…
```

Character data starts at tab-field index 5, alternating code/name pairs.
The format is **not** `CODE=Name` despite some older docs suggesting so.

### PROBLEM responses have empty middle fields

```
L\t\tPROBLEM 2
```

Note the empty middle tab field. **Scan ALL fields for "PROBLEM"** — never
assume the message is at `parts[1]`. Known codes:

| Code | Meaning |
|---|---|
| `PROBLEM 1` | Billing |
| `PROBLEM 2` | Already logged in **or** mode not supported |
| `PROBLEM 3` | Already in game |
| `PROBLEM 4` | Unavailable |

### Wizard (plain-text) mode

DR's SGE has deprecated `L\t{code}\tWIZ` — it returns `L\t\tPROBLEM 2`.
Plain text is achieved by:

1. Always authenticating with `L\t{code}\tSTORM` (which DR supports).
2. NOT sending the `FE:GENIE /VERSION:… /XML\n` announcement after
   connecting to the game server.

Without the FE announcement the server sends plain text. With it, the
server switches to StormFront XML mode.

### Character list without login

Steps 1–5 only. Stop after `C\n`; do not send `L\t…`.
`SgeAuthClient.ListCharactersAsync()` does this — useful for the
account-management UI before the user picks a character.

## Successful auth response shape

```
A\tMONIL\tKEY\t{hash}\t{full name}
```

The account name appears in field 1 on success. If field 1 is empty (e.g.
`A\t\tPASSWORD`), it's a failure — the message tells you why.

## After auth

`<settingsInfo/>` is the authoritative "server ready for input" signal in
StormFront mode. `GenieCore` subscribes to `SettingsInfoEvent` once
(`.Take(1)`) and sends `look` when it fires. Do **not** use a timer or
hardcoded delay — `<settingsInfo/>` arrives in all three connection modes
(DirectSGE, LichProxy, DevReplay).

For Wizard (plain-text) mode, no `<settingsInfo/>` ever arrives. `GenieCore`
fires `look` on the `Connected` event instead.
