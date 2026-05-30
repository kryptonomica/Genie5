# Pre-publish checklist

Items to verify before tagging a new public release. The order is
roughly "cheapest checks first" so anything that blocks publication
shows up early.

If you're a contributor: you don't need to walk this checklist for a
normal PR. This is for the maintainer cutting a release.

## 1. Identity and display

- [ ] **Character references show as `Character-Account`** everywhere
  user-visible — not bare character name. This is the convention adopted
  in alpha for distinguishing characters that share a first name across
  accounts. Check:
  - Title bar (`MainWindow.axaml.cs` — bound to `WindowTitle`)
  - Connect dialog character dropdown (after Fetch, the list items
    show `{Name}-{Account}`)
  - Profile picker (the per-character profile dropdown on connect)
  - Profile name default (when creating a new profile, suggest
    `{Name}-{Account}` not just `{Name}`)
- [ ] **No development-machine paths** in any user-visible string. Search
  the compiled binary for your local home-directory username, your repo
  root path, and any other developer-machine markers that might have
  been baked in at build time:
  ```
  dotnet publish -c Release -r win-x64 -o publish/win-x64
  strings publish/win-x64/Genie.exe | grep -iE "<your-username>|<your-repo-name>"
  ```

## 2. AI surface gates

Verify the five release gates in `AiContextBuffer.cs` are honored in
the build that ships:

- [ ] **G1 — Default OFF.** Open `DisplaySettings` (or wherever the AI
  enable lives) on a fresh profile; AI features must require explicit
  opt-in.
- [ ] **G2 — Other-player content stripped.** The `SnapshotBuffer` (or
  whatever wrapper sends to the AI vendor) must filter `<preset id="whisper">`,
  `<preset id="speech">`, and `<pushStream id="talk|whispers|thoughts|familiar">`
  blocks before any external send. Smoke-test by enabling AI mode in a
  Replay of a session with active whispers and confirming the prompt
  payload omits them.
- [ ] **G3 — InCharacterAdvisor disabled.** The mode is feature-flagged
  off. Confirm the UI doesn't expose it.
- [ ] **G4 — AI never feeds ProcessInput.** Grep for any path from AI
  response to `Commands.ProcessInput`:
  ```
  rg "ProcessInput" --type cs -A2 -B2
  ```
  Should be zero matches in the AI pipeline.
- [ ] **G5 — Privacy notice on first AI enable.** First-time toggle must
  surface plain-language disclosure of what gets sent off-machine.

## 3. DR policy compliance

See [docs/POLICY.md](docs/POLICY.md) for the full review. Spot-check:

- [ ] **`Reconnect=true` config flag is unwired.** Set it true in a profile;
  disconnect the session manually; confirm the app does NOT auto-reconnect.
- [ ] **Auto-walk pauses on window unfocus.** Click-to-walk on a multi-step
  route; alt-tab to another app; wait 60 seconds; confirm the walk has
  paused. (Also test: Esc during walk cancels; typing any command cancels.)
- [ ] **No `--headless` / `--service` / `--daemon` CLI flag.** Run
  `Genie.exe --help`; confirm there's no flag that bypasses GUI startup.

## 4. Privacy and credentials

- [ ] **Profile passwords are encrypted on disk.** Open a saved profile's
  JSON file under `%APPDATA%/Genie5/Profiles/{Char}-{Acct}/` — the
  `EncryptedPassword` field must be a base64 blob, not plain text.
- [ ] **Profile keys are machine-bound.** Confirm a profile saved on one
  machine fails to decrypt when copied to another machine. (This is by
  design — see `ProfileCrypto.cs` for the DPAPI / machine-key derivation.)
- [ ] **No profiles ship in the public repo.** Search the published zip
  for `profiles.json` and any `Profiles/` directory; should be absent.
- [ ] **No recordings ship.** Search the published zip for any
  `raw_session_*.xml` or `_streams.txt`; should be absent. (`Logs/` and
  `test_results/` are gitignored.)

## 5. Repo cleanliness

- [ ] **No AI-vendor brand references in tracked files** of the public repo.
  Scan source comments, docs, and commit messages for common AI-tool
  brand names:
  ```
  rg -iE "anthropic|openai|gpt|copilot|gemini" -- ':!*.csproj'
  ```
  The only allowed hits are `Anthropic.SDK` vendor identifiers in
  `src/Genie.Core/AiContextBuffer.cs` — the NuGet package and its
  exported constant / method names, annotated with explanatory
  comments next to each occurrence.
- [ ] **No AI-tool context or scratch files** tracked in the public
  repo. Editor / assistant project-context files and scratch
  directories (anything matching `.claude/`, `.cursor/`,
  `.cursorrules`, `.aider*`, or a known AI-tool's project-context
  file pattern) should all be gitignored so they never accidentally
  land here.
- [ ] **No `Co-Authored-By:` AI trailers** in any public commit:
  ```
  git log --all --pretty=format:"%H %B" | grep -iE "co-authored-by:.*(anthropic|openai|copilot|gemini)"
  ```
- [ ] **Dead doc links resolved.** Grep README and CONTRIBUTING for
  every relative link and confirm the target file exists:
  ```
  grep -oE "\([^)]+\.md\)" README.md CONTRIBUTING.md | sort -u
  ```

## 6. Build artifacts

- [ ] **Self-contained publish builds cleanly** for win-x64 (and any
  other RID you're shipping):
  ```
  dotnet publish src/Genie.App/Genie.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64
  ```
- [ ] **App launches.** Double-click `Genie.exe` from the publish folder.
  No "missing .NET runtime" prompt; main window appears.
- [ ] **Smoke connect.** File → Connect → walk through SGE auth on a
  real account. Confirm character list populates, character connects,
  ready-for-input fires (`<settingsInfo/>`).
- [ ] **Alpha zip size sanity.** Expect ~40 MB for win-x64 self-contained.
  If it's drifting larger, check for accidentally-bundled debug symbols.

## 7. Release notes

- [ ] **`publish/win-x64/ALPHA-README.txt` reflects current state.**
  Update the "What works" and "What's NOT working yet" sections to
  match what's actually shipping in this build.
- [ ] **`docs/ROADMAP.md` reflects items that moved to shipped.** If
  this release graduated a 🚧 item, move it to the alpha-shipped list.
- [ ] **GitHub release page** has the zip attached and the relevant
  commit range linked.

## 8. After publish

- [ ] **Tag the commit** that was published: `git tag v5.0.0-alpha.N`,
  push tag.
- [ ] **Announce in Discord** `#announcements` channel.
- [ ] **Watch the bug-reports channel** for the next 48 hours and
  triage any showstoppers into a hotfix release.
