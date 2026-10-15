# AgentSecrets

**Store a secret once. Refer to it by name. Inject it only into the process that needs it.**

AgentSecrets is a small command-line tool for Windows and macOS, for developers who work with
coding agents such as **OpenAI Codex** and **Claude Code**. Your API keys live in Windows
Credential Manager or the macOS Keychain; agents and scripts only ever see their *names*.

```shell
agent-secrets set openai                                           # once per machine (hidden prompt)
agent-secrets run --env OPENAI_API_KEY=openai -- python demo.py    # any time, by you or your agent
```

No `.env` files. No keys in prompts, repositories, shell history or persistent environment variables.

**Install** (no admin rights; installs the tool and the skill for Claude Code and Codex):

```powershell
# Windows (PowerShell)
irm https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.ps1 | iex
```

```sh
# macOS (Terminal)
curl -fsSL https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.sh | sh
```

---

## Contents

1. [The problem](#1-the-problem)
2. [How it works](#2-how-it-works)
3. [Installation](#3-installation)
4. [Add your first secret](#4-add-your-first-secret)
5. [List, check and remove secrets](#5-list-check-and-remove-secrets)
6. [Run a command with one secret](#6-run-a-command-with-one-secret)
7. [Run a command with several secrets](#7-run-a-command-with-several-secrets)
8. [Project manifest: `.agentsecrets.json`](#8-project-manifest-agentsecretsjson)
9. [Using AgentSecrets with Codex](#9-using-agentsecrets-with-codex)
10. [Using AgentSecrets with Claude Code](#10-using-agentsecrets-with-claude-code)
11. [Installing the Agent Skill](#11-installing-the-agent-skill)
12. [Security model](#12-security-model)
13. [Troubleshooting](#13-troubleshooting)
14. [Uninstalling](#14-uninstalling)
15. [Command reference](#15-command-reference) · [Development](#16-development)

---

## 1. The problem

Every project that calls an API needs a credential, and the usual answers all leak:

- **`.env` files** get copied from project to project, committed by accident, and read by any
  agent that lists the directory.
- **Persistent environment variables** are visible to every process you ever start.
- **Pasting a key into an agent prompt** puts it in a transcript you do not control.

The result is *secret proliferation*: the same OpenAI key in a dozen folders, none of which you
remember. AgentSecrets replaces all of that with one rule:

> **Store once → refer to by name → inject only when executing a process.**

## 2. How it works

```
 you, once per machine                       you or your agent, any time
┌──────────────────────────┐                ┌─────────────────────────────────────────────┐
│ agent-secrets set openai │                │ agent-secrets run -- python app.py          │
└────────────┬─────────────┘                └──────────────────────┬──────────────────────┘
             │ hidden prompt                                       │ 1. read .agentsecrets.json
             ▼                                                     │    OPENAI_API_KEY -> "openai"
┌──────────────────────────┐   2. read "AgentSecrets/openai"       │
│ Credential Manager       │ ◄─────────────────────────────────────┤
│ (Windows) or Keychain    │                                       │ 3. start child process with
│ (macOS), per user        │                                       ▼    OPENAI_API_KEY=<secret>
└──────────────────────────┘                ┌─────────────────────────────────────────────┐
                                            │ python app.py   (only this process sees it) │
                                            └─────────────────────────────────────────────┘
```

- **Storage** – on Windows, secrets are *Generic Credentials* in Windows Credential Manager,
  named `AgentSecrets/<name>` and encrypted by Windows for your user account. On macOS, they are
  generic passwords in your login keychain (service `AgentSecrets`, account `<name>`, shown in
  Keychain Access as `AgentSecrets/<name>`). Nothing is written to plaintext files.
- **Injection** – `agent-secrets run` reads the secrets it needs, starts your command as a
  child process, and places the values **only in that child's environment block**. Your shell,
  your user environment and the agent's session are never modified.
- **Transparent** – stdin, stdout, stderr and the exit code are the child's own, so
  interactive programs, pipes and CI-style checks behave exactly as without the wrapper.
- **No plaintext output** – there is deliberately no `get` or `show` command. For moving secrets
  between machines, `export` creates a password-encrypted backup; `import` restores it.

## 3. Installation

Requirements: Windows 10/11 (x64 or ARM64) or macOS (Apple silicon or Intel). Nothing else: the
program is self-contained (no .NET runtime needed). To build from source instead, see
[Development](#16-development).

### Windows

In PowerShell (Windows PowerShell 5.1 or PowerShell 7):

```powershell
irm https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.ps1 | iex
```

The installer shows what it will do and asks once. Then it:

1. downloads the latest release for your CPU and **verifies its SHA-256 checksum**;
2. installs `agent-secrets.exe` to `%LOCALAPPDATA%\AgentSecrets\bin`;
3. adds that directory to your *user* `PATH` (no Administrator rights; the system `PATH` is
   untouched) and to the current terminal;
4. installs the [Agent Skill](#11-installing-the-agent-skill) for Claude Code (`~\.claude\skills`)
   and Codex (`~\.agents\skills`).

Your secrets are never touched. **Run the same command again to upgrade** (this works even
while an `agent-secrets run` is in progress).

Options are passed with this form of the one-liner:

```powershell
& ([scriptblock]::Create((irm https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.ps1))) -Version v0.1.0 -Yes
```

| Option | Effect |
| --- | --- |
| `-Version v0.1.0` | Install a specific release instead of the latest. |
| `-Yes` | Do not ask (unattended installs). |
| `-NoSkills` | Do not install the Claude Code / Codex skill. |
| `-CodexHomeSkills` | Put the Codex skill in `$CODEX_HOME\skills` instead of `~\.agents\skills` (see [section 11](#11-installing-the-agent-skill)). |
| `-NoPath` | Do not modify `PATH`. |
| `-InstallDir <dir>` | Install somewhere other than `%LOCALAPPDATA%\AgentSecrets`. |
| `-Runtime win-arm64` | Override CPU detection. |
| `-PackagePath <zip>` | Install from a downloaded `agent-secrets-win-x64.zip` (offline). |

Prefer to read before you run? Download
[`install.ps1`](https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.ps1),
inspect it, then run `powershell -ExecutionPolicy Bypass -File .\install.ps1`.

### macOS

In Terminal:

```sh
curl -fsSL https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.sh | sh
```

The installer shows what it will do and asks once. Then it:

1. downloads the latest release for your Mac (Apple silicon or Intel) and **verifies its SHA-256 checksum**;
2. installs `agent-secrets` to `~/.local/bin`;
3. if that directory is not on your `PATH` yet, adds it in your shell profile (`~/.zprofile` for
   zsh; for bash, whichever of `~/.bash_profile`, `~/.bash_login` and `~/.profile` it reads), on a
   line marked `# added by the AgentSecrets installer`;
4. installs the [Agent Skill](#11-installing-the-agent-skill) for Claude Code (`~/.claude/skills`)
   and Codex (`~/.agents/skills`).

Your secrets are never touched. **Run the same command again to upgrade** (this works even
while an `agent-secrets run` is in progress). Options go after `sh -s --`:

```sh
curl -fsSL https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.sh | sh -s -- --version v0.2.0 --yes
```

| Option | Effect |
| --- | --- |
| `--version v0.2.0` | Install a specific release instead of the latest. |
| `--yes` | Do not ask (unattended installs). |
| `--no-skills` | Do not install the Claude Code / Codex skill. |
| `--codex-home-skills` | Put the Codex skill in `$CODEX_HOME/skills` instead of `~/.agents/skills` (see [section 11](#11-installing-the-agent-skill)). |
| `--no-path` | Do not modify `PATH`. |
| `--prefix <dir>` | Install to `<dir>/bin` and `<dir>/share/agent-secrets` instead of `~/.local`. |
| `--runtime osx-x64` | Override CPU detection (`osx-arm64` or `osx-x64`). |
| `--package <file>` | Install from a downloaded `agent-secrets-osx-arm64.tar.gz` (offline). |

Prefer to read before you run? Download
[`install.sh`](https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.sh),
inspect it, then run `sh install.sh`.

The first time agent-secrets uses a secret after you install or upgrade it, macOS may ask
whether it may use the keychain item: choose *Always Allow* ([why](#12-security-model)).

### Verify

**Open a new terminal** (or restart the one your agent uses), then run:

```shell
agent-secrets doctor
```

## 4. Add your first secret

```shell
agent-secrets set openai
```

```
Enter the secret for 'openai' (input is hidden; paste is fine), then press Enter:
Secret 'openai' stored successfully.
```

Paste or type the key — nothing is echoed, not even asterisks — and press Enter. Then the next one:

```shell
agent-secrets set elevenlabs
```

Things worth knowing:

- The value is **never accepted as a command-line argument** (that would put it in your shell
  history and make it visible to other processes). `agent-secrets set openai <value>` is rejected.
- Names are case-insensitive, 1–64 characters: letters, digits, `.`, `_`, `-`.
  Suggested convention: the lower-case provider name (`openai`, `elevenlabs`, `github`,
  `azure-openai`), with a suffix when you have several (`openai-work`).
- Setting an existing name asks before overwriting (`--force` skips the question).
- `set` refuses to run without a real terminal. An agent that tries it gets an error telling it
  to ask *you*. For deliberate automation there is `--stdin` (for example
  `Get-Clipboard | agent-secrets set openai --stdin` on Windows, `pbpaste | agent-secrets set openai --stdin` on macOS).
- On Windows the maximum length is 1280 characters (a Credential Manager limit).

### Move secrets to another machine (Windows or macOS)

On the source machine, run this **yourself in an interactive terminal**:

```shell
agent-secrets export keys.agentsecrets-backup
```

Enter a long, unique backup passphrase twice at the hidden prompts. The file includes **all
AgentSecrets aliases and values**, encrypted together. Existing files are never overwritten;
choose a new filename for each backup. The original credentials remain in the source machine's
credential store. macOS may ask for permission to read the keychain items.

Copy the encrypted file to the other machine, install a version of AgentSecrets that supports
these commands, and run:

```shell
agent-secrets import keys.agentsecrets-backup
```

Enter the same passphrase at the hidden prompt. This works in either direction between Windows
and macOS: imported secrets go into the destination user's Credential Manager or Keychain.
An incorrect password, a modified file, an invalid entry or an existing alias stops the import
before any credentials are written. To deliberately replace existing aliases:

```shell
agent-secrets import keys.agentsecrets-backup --force
```

Windows cannot store secrets longer than 1280 characters. If a Mac backup contains one, the
whole import is refused on Windows before writing anything. Storage failures during the write
phase can leave a partial import; the error reports how many entries were imported. Resolve the
storage problem and retry with `--force` if you want to replace those entries.

Backups use AES-256-GCM with a random salt and nonce and PBKDF2-HMAC-SHA256 (600,000 iterations).
Only encrypted bytes reach disk, including any temporary file; aliases, values and passwords
are never printed. Passwords cannot be supplied through arguments, environment variables or
stdin. Keep the passphrase separately: **there is no password recovery**. Keep backups out of
Git (`*.agentsecrets-backup` is ignored), and remove copies you no longer need. See
[the backup format and security limits](SECURITY.md#encrypted-backups).

## 5. List, check and remove secrets

```shell
agent-secrets list
```

```
elevenlabs
openai
```

Only names are ever shown.

```shell
agent-secrets exists openai        # exit code 0 = exists, 1 = missing
agent-secrets exists openai -q     # same, prints nothing
agent-secrets remove openai        # asks for confirmation
agent-secrets remove openai --yes  # no question (scripts)
```

To **rotate** a key, just run `agent-secrets set openai` again. Every project picks up the new
value on its next run, because no project ever had a copy.

## 6. Run a command with one secret

```shell
agent-secrets run --env OPENAI_API_KEY=openai -- python demo.py
```

Read it as: *run `python demo.py` with the environment variable `OPENAI_API_KEY` set to the secret named `openai`.*

- Everything after `--` is the command and its arguments, passed through unchanged
  (spaces, quotes, `%`, `&` and friends are handled correctly — also for `.cmd` shims like `npm`
  on Windows and `#!` scripts on macOS).
- The exit code is the child's exit code.
- Your shell is unchanged afterwards: `echo $env:OPENAI_API_KEY` (PowerShell) or
  `echo $OPENAI_API_KEY` (macOS) still prints nothing.

## 7. Run a command with several secrets

Repeat `--env` (or `-e`):

```powershell
agent-secrets run `
  --env OPENAI_API_KEY=openai `
  --env ELEVENLABS_API_KEY=elevenlabs `
  -- python demo.py
```

On macOS, continue lines with `\` instead of PowerShell's backtick.
The same secret can feed different variable names in different projects
(`--env AZURE_OPENAI_KEY=azure-openai`, `--env GH_TOKEN=github`, …).

## 8. Project manifest: `.agentsecrets.json`

Put the bindings in a file at the root of your project so nobody has to remember them:

```json
{
  "version": 1,
  "bindings": {
    "OPENAI_API_KEY": "openai",
    "ELEVENLABS_API_KEY": "elevenlabs"
  }
}
```

Now the command is simply:

```shell
agent-secrets run -- python app.py
```

**This file contains only names and is safe to commit.** It doubles as documentation of which
credentials the project needs. Check a project's requirements at any time:

```shell
agent-secrets status
```

```
Project
  [ok] Manifest: D:\code\my-app\.agentsecrets.json
  [ok]   OPENAI_API_KEY <- openai
  [!!]   ELEVENLABS_API_KEY <- elevenlabs   MISSING. Add it with: agent-secrets set elevenlabs
```

Rules, kept deliberately simple:

| Topic | Behaviour |
| --- | --- |
| Lookup | The current directory first, then each parent directory **up to the Git root** (the first directory containing `.git`). The nearest manifest wins; manifests are not merged. |
| Outside a Git repository | Only the current directory is checked, so a stray file high up in your drive never applies by surprise. |
| Override | `--env NAME=secret` on the command line overrides the manifest's binding for `NAME`; other manifest bindings still apply. Windows ignores case in `NAME`, like its environment variables; on macOS it must match exactly. |
| Other options | `--manifest <path>` uses a specific file; `--no-manifest` ignores manifests. |
| Validation | `version` must be `1`; `bindings` maps valid environment variable names to valid secret names; unknown properties are errors (`$schema` is allowed); names that differ only in case are rejected on every platform, so a manifest also works on Windows. Comments and trailing commas are tolerated. |
| Missing secret | `run` stops *before* starting your command, exits with 125 and tells you exactly which `agent-secrets set <name>` to run. |

If a secret is missing you will see:

```
Required secret 'openai' was not found (needed for OPENAI_API_KEY).

Add it with:

    agent-secrets set openai
```

A ready-made demo lives in [`examples/`](examples/README.md).

## 9. Using AgentSecrets with Codex

1. Install the skill (the installer does this; see [section 11](#11-installing-the-agent-skill)).
   Codex discovers it in `~/.agents/skills/agent-secrets/` and activates it on its own when a task
   involves credentials. You can also invoke it explicitly with `$agent-secrets`.
2. Alternatively — or additionally — paste [`integrations/AGENTS.agent-secrets.md`](integrations/AGENTS.agent-secrets.md)
   into your global `~/.codex/AGENTS.md` or a project's `AGENTS.md`.

Then the daily flow is:

```text
You:    Run the example using my OpenAI credentials.
Codex:  agent-secrets exists openai                       → exit 0
        agent-secrets run -- python app.py                → your program's output
```

And when a secret is not there yet:

```text
Codex:  The secret 'elevenlabs' is not stored on this machine. Please run this in your own
        terminal, then tell me to continue:

            agent-secrets set elevenlabs
```

**Sandbox note:** Codex runs commands in a sandbox. If `agent-secrets run` cannot reach Credential
Manager, the macOS Keychain or the network from inside it, approve the command when Codex asks to
run it outside the sandbox.

## 10. Using AgentSecrets with Claude Code

1. Install the skill (the installer offers this). Claude Code loads it from
   `~/.claude/skills/agent-secrets/` and uses it automatically when credentials come up;
   `/agent-secrets` invokes it explicitly.
2. Alternatively — or additionally — paste [`integrations/CLAUDE.agent-secrets.md`](integrations/CLAUDE.agent-secrets.md)
   into `~/.claude/CLAUDE.md` or a project's `CLAUDE.md`.

Optional: use `/permissions` in Claude Code to always allow the read-only commands
(`agent-secrets exists`, `list`, `status`). Think twice before always allowing
`agent-secrets run`: that lets the agent run *any* command with your secrets without asking.
Keeping that one prompt is a cheap safeguard.

Run `agent-secrets set <name>` in a **separate terminal window**, not through the agent and not
with Claude Code's `!` prefix: the hidden prompt needs a real terminal.

On macOS, a Keychain dialog may appear the first time `agent-secrets run` uses a secret after an
install or upgrade; the command waits until you answer it ([section 12](#12-security-model)).

## 11. Installing the Agent Skill

The skill at [`skill/agent-secrets/SKILL.md`](skill/agent-secrets/SKILL.md) is what makes the workflow
automatic: it teaches an agent *when* credentials are involved, to use `agent-secrets run`, to
create `.agentsecrets.json`, to ask **you** to run `agent-secrets set` when something is missing,
and never to read, print, write or hunt for secret values.

| Agent | Global (all projects) | Per project (commit it for your team) |
| --- | --- | --- |
| Claude Code | `~/.claude/skills/agent-secrets/SKILL.md` | `.claude/skills/agent-secrets/SKILL.md` |
| Codex | `~/.agents/skills/agent-secrets/SKILL.md` | `.agents/skills/agent-secrets/SKILL.md` |

`~` is your home folder (`C:\Users\<you>` on Windows, `/Users/<you>` on macOS). Codex also scans
`$CODEX_HOME/skills` (by default `~/.codex/skills`), where its own `skill-installer` puts skills.
**Install the skill in one of the two, never both:** Codex does not de-duplicate, so a skill
present in both directories is offered twice. Pass `-CodexHomeSkills` (Windows) or
`--codex-home-skills` (macOS) to the installer if you prefer the `CODEX_HOME` location;
`agent-secrets doctor` reports it as a problem if both copies exist.

**Automatic:** the [installer](#3-installation) installs the skill for both agents (unless you
pass `-NoSkills` / `--no-skills`) and refreshes it on every upgrade.

**Manual**, from a clone of this repository:

```powershell
# Windows - Claude Code
New-Item -ItemType Directory -Force "$HOME\.claude\skills\agent-secrets" | Out-Null
Copy-Item .\skill\agent-secrets\* "$HOME\.claude\skills\agent-secrets\" -Recurse -Force

# Windows - Codex
New-Item -ItemType Directory -Force "$HOME\.agents\skills\agent-secrets" | Out-Null
Copy-Item .\skill\agent-secrets\* "$HOME\.agents\skills\agent-secrets\" -Recurse -Force
```

```sh
# macOS - Claude Code
mkdir -p ~/.claude/skills/agent-secrets && cp -R skill/agent-secrets/. ~/.claude/skills/agent-secrets/

# macOS - Codex
mkdir -p ~/.agents/skills/agent-secrets && cp -R skill/agent-secrets/. ~/.agents/skills/agent-secrets/
```

Restart the agent afterwards. `agent-secrets doctor` reports whether both copies are in place.

Prefer plain instruction files? Use the snippets in [`integrations/`](integrations/) instead
(sections 9 and 10). Skill and snippet can be combined.

## 12. Security model

> **AgentSecrets protects against accidental exposure and secret proliferation.**
> **It does *not* protect against malicious software running as your user.**

What it gives you:

- Secrets are not in repositories, `.env` files, prompts, agent transcripts, shell history or
  persistent environment variables.
- One copy per machine, encrypted at rest by the operating system; rotate in one place.
- The tool never prints a secret: not in output, errors, diagnostics or logs. `exists`, `list`,
  `status` and `doctor` do not even read the values.
- Text that is rejected as a name is never echoed back, in case a key was pasted in the wrong place.
- On macOS, keychain items are bound to agent-secrets: any other program that tries to read one
  (for example an agent running Apple's `security` command) makes macOS ask you first.

What it cannot do:

- **Same-user malware.** On Windows, any program running as you can ask Credential Manager for
  your credentials, exactly like AgentSecrets does. On macOS it would have to get past the
  Keychain dialog — or simply run `agent-secrets run` itself.
- **A child process that misbehaves.** The command you run receives the real key. If it prints
  its environment, logs the key or sends it somewhere, AgentSecrets cannot stop it. Only run
  code you trust — and remember a `.agentsecrets.json` in an untrusted repository decides
  which of your secrets that repository's code receives.
- **An agent that ignores its instructions.** An agent could write a program that prints the
  variable. The skill forbids it, the `run` permission prompt lets you see each command, and the
  absence of a `get` command removes the easy path — but this is guidance, not a sandbox.

**macOS Keychain dialogs.** Releases are signed ad hoc for now, not with an Apple Developer ID, so
each release is a new program to macOS. After you install or upgrade, expect a dialog once per
secret, the first time agent-secrets uses it, asking whether agent-secrets may use the keychain
item: choose *Always Allow*. Without a desktop session (SSH) the command fails instead; run it
once from a terminal on the Mac.

Details, including what to do when a key leaks: [SECURITY.md](SECURITY.md).

## 13. Troubleshooting

Start with:

```shell
agent-secrets doctor
```

| Symptom | Cause and fix |
| --- | --- |
| `agent-secrets` is not recognized (Windows) | Open a **new** terminal after installing (PATH is read at terminal start). Still failing: run the [install command](#3-installation) again, or check that `%LOCALAPPDATA%\AgentSecrets\bin` is on your user PATH. |
| `agent-secrets: command not found` (macOS) | Open a **new** terminal after installing. Still failing: check that `~/.local/bin` is on your `PATH` (`echo $PATH`); the installer adds it in `~/.zprofile` (zsh) or your bash profile. |
| `Required secret '…' was not found` | Run the `agent-secrets set <name>` command shown. Check spelling with `agent-secrets list`. |
| `Nothing to inject` | No manifest was found from the current directory and no `--env` was given. `cd` into the project, or pass `--env` / `--manifest <path>`. |
| `Command '…' was not found` (exit 127) | The program is not on `PATH`. Programs in the current directory need `.\name` (Windows) or `./name` (macOS) — the current directory is deliberately not searched, so a repository cannot shadow `python` or `git`. On macOS, Python is usually `python3`. |
| `… is not an executable` (exit 126, Windows) | Scripts need their interpreter: `-- python x.py`, `-- node x.js`, `-- pwsh -File x.ps1`. Shell built-ins and pipelines: `-- cmd /c "…"` or `-- pwsh -Command "…"`. |
| `… is not executable` (exit 126, macOS) | Make the script executable (`chmod +x`, with a `#!` line) or run it through its interpreter: `-- python3 x.py`, `-- sh x.sh`. Shell built-ins and pipelines: `-- sh -c '…'`. |
| `'set' must be run by a person in an interactive terminal` | Working as intended: run it yourself in a normal terminal window. |
| `Invalid manifest …` | The message names the file and the problem. Remember: values in `bindings` are secret *names*. |
| Win32 error 1312 (Windows) | Credential Manager is unavailable in this logon session (some SSH sessions, services, scheduled tasks). Use an interactive session. |
| A dialog asks whether agent-secrets may use a keychain item (macOS) | Expected once per secret after installing or upgrading ([section 12](#12-security-model)): choose *Always Allow*. If you did not just run agent-secrets, yourself or through your agent, choose *Deny*. |
| `The macOS Keychain could not …` (OSStatus -25308) | The login keychain is locked or there is no desktop session (SSH, background jobs). Run the command in a terminal on the Mac, or unlock the keychain first with `security unlock-keychain`. |
| `The macOS Keychain could not …` (OSStatus -128 or -25293) | Access was denied in the Keychain dialog. Run the command again and choose *Always Allow*. |
| "agent-secrets" cannot be opened because the developer cannot be verified (macOS) | It was downloaded with a browser, so macOS quarantined it. Use the `curl` installer, or clear the flag: `xattr -d com.apple.quarantine ~/.local/bin/agent-secrets`. |
| The program says the key is invalid | The stored value is wrong (partial paste?). Run `agent-secrets set <name>` again. You can also inspect the entry `AgentSecrets/<name>` in *Control Panel → Credential Manager → Windows Credentials* (Windows) or in *Keychain Access* (macOS; open it with Spotlight). |

Exit codes of `run`: the child's own code, or **125** (could not run: missing secret, bad
manifest, storage error), **126** (not executable), **127** (not found), **2** (usage error).
On macOS a child stopped by a signal reports 128 + the signal number (143 for SIGTERM), as in a
shell, and a SIGTERM sent to agent-secrets itself is passed on to the child.

## 14. Uninstalling

On Windows (PowerShell):

```powershell
irm https://github.com/dev-foundations/agent-secrets/releases/latest/download/uninstall.ps1 | iex
```

This removes the program, its `PATH` entry and the Claude Code / Codex skills, and **keeps your
secrets**. Kept secrets remain visible in Credential Manager as `AgentSecrets/<name>` and are
picked up again if you reinstall.

To also delete every AgentSecrets secret:

```powershell
& ([scriptblock]::Create((irm https://github.com/dev-foundations/agent-secrets/releases/latest/download/uninstall.ps1))) -RemoveSecrets
```

The installer also leaves a copy at `%LOCALAPPDATA%\AgentSecrets\uninstall.ps1` (options:
`-RemoveSecrets`, `-KeepSkills`, `-Yes`). If `agent-secrets` is still running, for example a long
`agent-secrets run`, the uninstaller stops without changing anything; run it again once it has finished.

On macOS (Terminal):

```sh
curl -fsSL https://github.com/dev-foundations/agent-secrets/releases/latest/download/uninstall.sh | sh
```

This removes the program, the `PATH` line the installer added (kept while other programs are in
that folder) and the Claude Code / Codex skills, and **keeps your secrets** in the Keychain
(`AgentSecrets/<name>` in Keychain Access). To also delete every AgentSecrets secret:

```sh
curl -fsSL https://github.com/dev-foundations/agent-secrets/releases/latest/download/uninstall.sh | sh -s -- --remove-secrets
```

The installer also leaves a copy at `~/.local/share/agent-secrets/uninstall.sh` (options:
`--remove-secrets`, `--keep-skills`, `--yes`, `--prefix <dir>`).

## 15. Command reference

```text
agent-secrets set <name> [--force] [--stdin]     Store a secret (prompted, never echoed)
agent-secrets list                               List secret names
agent-secrets exists <name> [--quiet]            Exit 0 if it exists, 1 if not
agent-secrets remove <name> [--yes]              Delete a secret
agent-secrets export <file>                     Export all secrets to a password-encrypted backup
agent-secrets import <file> [--force]            Restore a backup; --force replaces existing aliases
agent-secrets run [--env NAME=secret]... [--manifest <path> | --no-manifest] -- <command> [args...]
agent-secrets status                             Does this project have the secrets it needs?
agent-secrets doctor                             Installation, storage, project and skill checks
agent-secrets help | --version
```

## 16. Development

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```shell
git clone https://github.com/dev-foundations/agent-secrets.git
cd agent-secrets
dotnet build AgentSecrets.slnx
dotnet test  AgentSecrets.slnx
.\scripts\install.ps1 -FromSource     # Windows: build the release package locally and install it
./scripts/install.sh --from-source    # macOS: the same
```

```
src/AgentSecrets.Core    ISecretStore + WindowsCredentialStore + MacKeychainStore, manifest, bindings, command resolution, child process
src/AgentSecrets.Cli     the agent-secrets executable (argument parsing, commands, diagnostics)
tests/AgentSecrets.Tests unit tests (in-memory store) + integration tests (real Credential Manager or Keychain, fake values)
tests/AgentSecrets.TestProbe  child process used by tests; reports *whether* a variable is set, never its value
skill/ integrations/     agent skill and AGENTS.md / CLAUDE.md snippets
examples/                demo project
scripts/                 install.ps1/.sh, uninstall.ps1/.sh, package.ps1 (Windows zip), package.sh (macOS tar.gz)
.github/workflows/       ci.yml (build, test, package, install smoke test on Windows and macOS), release.yml
```

**Releasing:** bump `<Version>` in `Directory.Build.props`, then push a tag:

```shell
git tag v0.2.0
git push origin v0.2.0
```

The release workflow tests and packages on Windows and macOS — `agent-secrets-win-x64.zip`,
`agent-secrets-win-arm64.zip`, `agent-secrets-osx-arm64.tar.gz` and `agent-secrets-osx-x64.tar.gz` —
writes `SHA256SUMS.txt`, and publishes them together with the install and uninstall scripts as a
GitHub release. The one-line installers always fetch the latest release. A tag with a hyphen
(for example `v0.2.0-rc.1`) is published as a prerelease, which never becomes the latest release,
so the pipeline can be tried first; install it with `-Version` / `--version`. The macOS binaries
are signed ad hoc by the .NET SDK; Developer ID signing and notarization are not set up.

Storage sits behind the small `ISecretStore` interface, so another backend (Linux Secret Service,
1Password, Azure Key Vault, …) can be added without touching the commands. Windows Credential
Manager and the macOS Keychain are implemented. The integration tests use unique throw-away names
in their own namespace (a Credential Manager prefix or a keychain service), use fake values only,
and clean up after themselves; on macOS they use your login keychain (CI uses a temporary one).
Tests that apply to one platform only are reported as skipped on the others.
