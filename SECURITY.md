# Security

## In one sentence

AgentSecrets protects against **accidental exposure and secret proliferation**. It does **not**
protect against **malicious software running with your user account's permissions**.

## What it protects

| Risk | How AgentSecrets helps |
| --- | --- |
| Keys committed to Git | No plaintext key is written to the working tree. `.agentsecrets.json` holds names only. Encrypted backups should also stay out of Git. |
| Keys copied into many `.env` files | One copy per machine, in Windows Credential Manager or the macOS Keychain. Rotate in one place. |
| Keys pasted into agent prompts and transcripts | Agents work with names (`openai`), never values. |
| Keys in shell history | `set` never accepts the value as an argument; it is read from a hidden prompt. |
| Keys in persistent environment variables | The value exists only in the environment of one child process, for its lifetime. |
| Keys in tool output | AgentSecrets never prints a value: not in output, errors, diagnostics or logs. `list`, `exists`, `status` and `doctor` do not read values at all. Rejected input is not echoed back, in case a key was typed where a name belongs. |
| Keys at rest | The operating system encrypts them: Windows for your user account, macOS in your login keychain (protected by your login password). Backups use a separate password; no plaintext credential files or plaintext temporary files are written. |

## What it does not protect

- **Same-user malware or untrusted programs.** On Windows, Credential Manager's Generic
  Credentials are readable by any process running as you. On macOS, the Keychain asks you
  before a program other than agent-secrets reads one of its items (see
  [below](#macos-keychain-access-dialogs)), but malware running as you can still get you to
  allow it, or simply run `agent-secrets run` with a command of its choosing. AgentSecrets adds
  no real barrier there and does not claim to.
- **Administrators and anyone who can sign in as you.**
- **The child process.** The program you run gets the real key and can do anything with it.
- **Memory inspection.** The value is briefly held in AgentSecrets' memory and in the child's
  environment block. The MVP avoids needless copies but does not attempt memory hardening.
- **Backups and roaming.** On Windows, entries are stored with local-machine persistence (they
  do not roam), but whatever backs up your Windows profile may include the encrypted vault. On
  macOS, items live in the login keychain of this Mac (not in iCloud Keychain); Time Machine
  backs up that keychain file, still encrypted.

### Same-user threat model

The trust boundary is your user account. Everything running as you is inside it: your shell,
your editor, your coding agent, and every program they start. AgentSecrets is a *hygiene* tool
inside that boundary — it makes the safe path the easy path — not an isolation mechanism. If
you need a boundary between an agent and your credentials, run the agent under a different user
account, in a VM or container, and give that environment only scoped, low-privilege keys.

### macOS: Keychain access dialogs

macOS remembers which program created each keychain item and lets that program use it without
asking. When any other program wants the value — Apple's `security` command, an agent running
it, or a different build of agent-secrets — macOS shows a dialog that names the program and asks
whether it may use the item (*Always Allow*, *Deny*, *Allow*).

- This is a useful safeguard: an agent that tries to read a key directly cannot do it silently.
  Do not allow access for a program you did not expect.
- Releases are currently signed ad hoc, not with an Apple Developer ID, so each release is a new
  program to macOS. After installing or upgrading, expect the dialog once per secret, the first
  time agent-secrets uses it (it can also appear when a new build overwrites or removes a
  secret). Choose *Always Allow* for agent-secrets.
- Without a desktop session (SSH, background jobs) there is nowhere to show the dialog: the
  command fails with a Keychain error instead. Run it once from a terminal on the Mac itself.

## Why there is no plaintext `get` or `show`

A command that prints a secret turns every one of the leaks above back into a one-liner: an
agent "helpfully" runs it and the key is in the transcript; a script captures it into a file.
Leaving it out keeps plaintext values out of terminal output. The separate `export` command
writes only password-encrypted backups and requires a human at a hidden terminal prompt.
This is a guard-rail against accidents, not a cryptographic guarantee: a determined
same-user program can read the credential store itself or run a child that prints its
environment. To look at or edit a stored value yourself, use *Control Panel → Credential
Manager → Windows Credentials* on Windows, or *Keychain Access* on macOS (open it with
Spotlight and search for `AgentSecrets`). Entries are named `AgentSecrets/<name>`.

## Encrypted backups

`agent-secrets export <file>` copies every AgentSecrets alias and value to a portable encrypted
file. `agent-secrets import <file> [--force]` restores them into the destination machine's native
credential store. Both require an interactive terminal; passwords are never accepted through
arguments, environment variables or stdin. Export asks twice and never overwrites an existing
file. Import refuses existing aliases unless the user passes `--force`.

Version 1 has this binary layout, independent of OS and CPU:

| Field | Size | Meaning |
| --- | --- | --- |
| Magic/version | 8 bytes | ASCII `AGSBKP01` |
| Salt | 16 bytes | Cryptographically random, fresh for every export |
| Nonce | 12 bytes | Cryptographically random, fresh for every export |
| Authentication tag | 16 bytes | AES-GCM tag |
| Ciphertext | Remaining bytes | UTF-8 JSON object mapping aliases to values |

The 256-bit key is derived from the exact password (UTF-8, without trimming or normalization)
with PBKDF2-HMAC-SHA256 and 600,000 iterations. AES-256-GCM authenticates the ciphertext and the
36-byte header (magic, salt and nonce). The password and derived key are not stored in the file.
The file is limited to 16 MiB; its size and format are checked before key derivation. PBKDF2's
work factor is fixed by version 1, so a malicious header cannot request unbounded iterations.
See [.NET AES-GCM](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm?view=net-10.0)
and [OWASP's PBKDF2 guidance](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html#pbkdf2).

Aliases and values are encrypted together. The header, file size and secret count printed at
export/import are not confidential. The file can be attacked offline: **use a long, unique
passphrase and keep it separately**. There is no password recovery. A backup remains valid
after deleting credentials from the source machine; rotating a key does not change old backups.

Export builds the encrypted archive in memory and writes only ciphertext to a sibling temporary
file, then moves it into place without replacing an existing file. On macOS the backup is
created with owner read/write permissions (0600); Windows uses inherited directory permissions.
The plaintext payload byte buffer and derived key are cleared after use, but managed strings
and runtime buffers cannot all be reliably erased. The existing memory-inspection limitation
still applies.

Import authenticates and validates the whole payload and checks all conflicts before writing.
On Windows it also checks every value against Credential Manager's 1280-character limit, so
oversized Mac entries cannot cause a partial import. Native stores do not offer a transaction
across entries: a later storage failure can leave a partial import, which is reported with a
count. `--force` explicitly permits replacing existing values, including entries already
imported by a previous partial attempt. Do not change the store concurrently during import.

Backups are intended for human-operated migration. Agents must ask the user to run these
commands themselves, never request the password or decrypt a backup. Do not commit backups or
leave unnecessary copies on shared storage.

## Why secrets are injected into child processes

Environment variables are what virtually every SDK already reads (`OPENAI_API_KEY`,
`ELEVENLABS_API_KEY`, …), so no code changes are needed. Setting them on a **single child
process** gives the narrowest practical scope: the parent shell, the agent's own process, other
terminals and future sessions never have the value, and nothing persists when the child exits.
The values go directly from the credential store into the child's environment block; they are
not written to disk, not placed on a command line, and not set in AgentSecrets' own environment.

## Risks of child processes printing environment variables

The child — and any process *it* starts, since environments are inherited — can read the
variable. Be careful with:

- commands that dump the environment: `set`, `env`, `printenv`, `Get-ChildItem env:`,
  `echo %OPENAI_API_KEY%`, `echo $OPENAI_API_KEY`, `python -c "import os; print(os.environ)"`;
- debug or verbose modes, crash reporters and test runners that log the environment;
- CI-style scripts that echo their configuration.

If a command run under `agent-secrets run` prints a key, the key is now in your terminal
scrollback and, if an agent ran it, in the agent's transcript. Treat it as compromised
([rotate it](#if-a-key-is-exposed-revoke-and-rotate)).

**Untrusted repositories:** `agent-secrets run` injects whatever the nearest
`.agentsecrets.json` asks for into whatever command you run. In a repository you do not trust,
that means its code can receive any secret the manifest names. Read the manifest (it is tiny)
and the code before running it with your credentials — exactly as you would before running it
with a `.env` file. Bare command names are resolved from `PATH` only, never from the current
directory, so a repository cannot substitute its own `python.cmd`, `git.exe` or `python`.

## Safe agent usage

- Install the skill (`skill/agent-secrets/SKILL.md`) or one of the `integrations/` snippets so
  the agent knows the rules: names only, `agent-secrets run`, never print or persist values,
  never search for keys, and ask **you** to run `agent-secrets set`.
- **You** enter credentials, in your own terminal. `set` refuses to run without one.
- Keep the permission prompt for `agent-secrets run` in your agent, at least in projects you
  have not reviewed. It shows you exactly which command is about to receive your secrets.
- Prefer scoped, low-privilege, spend-limited keys for agent work (project-scoped OpenAI keys,
  fine-grained GitHub tokens). Use separate names for them (`openai-agents` vs `openai-prod`).
- If an agent ever shows you a secret value, do not continue the session as if nothing
  happened: rotate the key.

## If a key is exposed: revoke and rotate

1. **Revoke first.** Delete or disable the key in the provider's dashboard (OpenAI: *API keys*;
   ElevenLabs: *Profile → API keys*; GitHub: *Settings → Developer settings → Tokens*;
   Azure: regenerate the key or reset the app's client secret). Revocation is the only step
   that actually ends the exposure.
2. **Create a new key**, as narrowly scoped as the provider allows.
3. **Store it under the same name:** `agent-secrets set openai` (confirm the overwrite). Every
   project keeps working, because none of them had a copy.
4. **Check the provider's usage and audit logs** for activity you do not recognise.
5. **Find where it leaked** and clean up: the agent transcript, terminal scrollback, log files,
   and Git history if it was committed (rewriting history does not un-leak a pushed key;
   step 1 does).

Rotating periodically, even without an incident, is cheap with AgentSecrets for the same reason.

## Reporting a vulnerability

Please report security problems privately to the maintainer rather than in a public issue, and
never include real credentials in a report.
