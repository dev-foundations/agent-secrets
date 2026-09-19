## Secrets (AgentSecrets)

Credentials on this machine are managed by the `agent-secrets` CLI. You handle secret
*names* (aliases such as `openai`, `elevenlabs`), never secret *values*.

- Run anything that needs credentials through it:
  `agent-secrets run --env OPENAI_API_KEY=openai -- python app.py`
  With a `.agentsecrets.json` in the project, just: `agent-secrets run -- python app.py`
- `.agentsecrets.json` maps env vars to aliases and is safe to commit:
  `{ "version": 1, "bindings": { "OPENAI_API_KEY": "openai" } }`
- Check availability with `agent-secrets exists <alias>` (exit 0 = yes) or `agent-secrets status`;
  see names with `agent-secrets list`.
- If a secret is missing, stop and ask the user to run `agent-secrets set <alias>` in their own
  terminal. The human enters the credential, never you.
- Never put secret values in `.env`, source, config, scripts, logs or commits; never ask the
  user to paste a key; never print or inspect secret environment variables; never search the
  machine for keys. There is no `get`/`export` command by design.
