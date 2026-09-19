# Security

## In one sentence

AgentSecrets protects against **accidental exposure and secret proliferation**. It does **not**
protect against **malicious software running with your Windows user's permissions**.

## What it protects

| Risk | How AgentSecrets helps |
| --- | --- |
| Keys committed to Git | No key is ever in the working tree. `.agentsecrets.json` holds names only. |
| Keys copied into many `.env` files | One copy per machine, in Windows Credential Manager. Rotate in one place. |
| Keys pasted into agent prompts and transcripts | Agents work with names (`openai`), never values. |
| Keys in shell history | `set` never accepts the value as an argument; it is read from a hidden prompt. |
| Keys in persistent environment variables | The value exists only in the environment of one child process, for its lifetime. |
| Keys in tool output | AgentSecrets never prints a value: not in output, errors, diagnostics or logs. `list`, `exists`, `status` and `doctor` do not read values at all. Rejected input is not echoed back, in case a key was typed where a name belongs. |
| Keys at rest | Windows encrypts Credential Manager entries for your user account. No plaintext files, no temp files. |

## What it does not protect

- **Same-user malware or untrusted programs.** Credential Manager's Generic Credentials are
  readable by any process running as you. Malware running as you could read them directly,
  with or without AgentSecrets. AgentSecrets adds no barrier there and does not claim to.
- **Administrators and anyone who can sign in as you.**
- **The child process.** The program you run gets the real key and can do anything with it.
- **Memory inspection.** The value is briefly held in AgentSecrets' memory and in the child's
  environment block. The MVP avoids needless copies but does not attempt memory hardening.
- **Backups and roaming.** Entries are stored with local-machine persistence (they do not roam),
  but whatever backs up your Windows profile may include the encrypted vault.

### Same-user threat model

The trust boundary is your Windows user account. Everything running as you is inside it:
your shell, your editor, your coding agent, and every program they start. AgentSecrets is a
*hygiene* tool inside that boundary — it makes the safe path the easy path — not an isolation
mechanism. If you need a boundary between an agent and your credentials, run the agent under a
different Windows account, in a VM or container, and give that environment only scoped,
low-privilege keys.

## Why there is no `get` or `export`

A command that prints a secret turns every one of the leaks above back into a one-liner: an
agent "helpfully" runs it and the key is in the transcript; a script captures it into a file.
Leaving it out means the *only* thing the tool can do with a value is hand it to a child
process. This is a guard-rail against accidents, not a cryptographic guarantee: a determined
same-user program can read Credential Manager itself or run a child that prints its
environment. To look at or edit a stored value yourself, use *Control Panel → Credential
Manager → Windows Credentials* (entries are named `AgentSecrets/<name>`).

## Why secrets are injected into child processes

Environment variables are what virtually every SDK already reads (`OPENAI_API_KEY`,
`ELEVENLABS_API_KEY`, …), so no code changes are needed. Setting them on a **single child
process** gives the narrowest practical scope: the parent shell, the agent's own process, other
terminals and future sessions never have the value, and nothing persists when the child exits.
The values go directly from Credential Manager into the child's environment block; they are not
written to disk, not placed on a command line, and not set in AgentSecrets' own environment.

## Risks of child processes printing environment variables

The child — and any process *it* starts, since environments are inherited — can read the
variable. Be careful with:

- commands that dump the environment: `set`, `env`, `printenv`, `Get-ChildItem env:`,
  `echo %OPENAI_API_KEY%`, `python -c "import os; print(os.environ)"`;
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
directory, so a repository cannot substitute its own `python.cmd` or `git.exe`.

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
