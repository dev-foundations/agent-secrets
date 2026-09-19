---
name: agent-secrets
description: Use whenever a task needs an API key, token or other credential (OpenAI, ElevenLabs, GitHub, Azure, any *_API_KEY / *_TOKEN environment variable), when running or testing a program that calls an authenticated API, when a project contains .agentsecrets.json, or when you are about to create or read a .env file or ask the user for a key. Credentials are injected at run time with the agent-secrets CLI, so you never see, request or write secret values.
---

# AgentSecrets

The user's credentials are stored once per machine in Windows Credential Manager under short
names ("aliases") such as `openai` or `elevenlabs`. The `agent-secrets` CLI injects them as
environment variables into **one child process** and nowhere else. You work with alias
*names* only. You never need, see or handle a secret *value*.

## Hard rules

- Never ask the user to paste a key into the chat, source code, a config file or a command.
- Never write secret values to `.env`, source files, config files, scripts, logs or commits.
- Never print or inspect a secret: no `echo $env:OPENAI_API_KEY`, `printenv`, `set`,
  `Get-ChildItem env:`, and no debug output of the variable inside code you run. To verify
  a variable, check only that it is set.
- Never search the machine for keys (other `.env` files, shell history, Credential Manager,
  registry, environment dumps) to work around a missing secret.
- Never run `agent-secrets set` yourself and never invent or supply a value. There is no
  `get` or `export` command; do not look for one.

## Workflow

1. **Identify** the environment variables the program reads (e.g. `OPENAI_API_KEY`) and pick
   the alias for each. Conventional aliases are the lower-case provider name: `openai`,
   `elevenlabs`, `github`, `azure-openai`. Run `agent-secrets list` to see existing names.

2. **Check availability** (exit code 0 = exists, 1 = missing; prints no secret):

   ```
   agent-secrets exists openai
   ```

3. **If a secret is missing, stop and ask the human** to run this in their own terminal, then
   wait for them to confirm:

   ```
   agent-secrets set <alias>
   ```

   The human enters the credential at a hidden prompt. Do not offer alternatives that involve
   the key passing through you.

4. **Run the program through `agent-secrets run`.** Everything after `--` is the command,
   exactly as you would normally run it:

   ```
   agent-secrets run --env OPENAI_API_KEY=openai -- python app.py
   agent-secrets run --env OPENAI_API_KEY=openai --env ELEVENLABS_API_KEY=elevenlabs -- npm test
   ```

   stdin/stdout/stderr and the exit code are the child's own. Exit codes 125 (agent-secrets
   could not start the command, e.g. missing secret or bad manifest), 126 and 127 (command
   not executable / not found) come from agent-secrets itself; read its stderr message.

5. **Prefer a project manifest** when the project will be run more than once. Create
   `.agentsecrets.json` in the project root. It contains names only and is safe to commit:

   ```json
   {
     "version": 1,
     "bindings": {
       "OPENAI_API_KEY": "openai",
       "ELEVENLABS_API_KEY": "elevenlabs"
     }
   }
   ```

   Then no `--env` flags are needed:

   ```
   agent-secrets run -- python app.py
   ```

   The manifest is found in the current directory or any parent up to the Git root. An
   explicit `--env NAME=alias` overrides the manifest for that variable. Use
   `agent-secrets status` to check that every alias the manifest needs exists.

## Notes

- Write application code to read credentials from environment variables
  (`os.environ["OPENAI_API_KEY"]`, `process.env.OPENAI_API_KEY`, ...). Do not add dotenv
  loading or a `.env` file for them; if the project already has a `.env.example`, keep
  placeholders only.
- Scripts that are not executables need their interpreter: `-- python x.py`,
  `-- node x.js`, `-- pwsh -File x.ps1`. For shell built-ins or pipelines use
  `-- cmd /c "..."` or `-- pwsh -Command "..."`.
- The secret exists only in the child process. A later command in your shell will not have
  it; wrap every command that needs it in `agent-secrets run`.
- If `agent-secrets` is not found, ask the user to install it in their own PowerShell terminal
  and then restart you (it changes their PATH, so do not run it yourself):
  `irm https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.ps1 | iex`
  `agent-secrets doctor` diagnoses setup. Source: https://github.com/dev-foundations/agent-secrets
- If you ever do see a secret value (for example a program printed it), do not repeat it;
  tell the user so they can rotate the key.
