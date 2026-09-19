# AgentSecrets demo

`.agentsecrets.json` in this directory maps `DEMO_API_KEY` to the secret named `demo-key`.

```powershell
# 1. Store a throw-away value (type anything at the hidden prompt)
agent-secrets set demo-key

# 2. From this directory (or any subdirectory), run the demo
cd examples
agent-secrets run -- python python-example/check_secret.py
# -> DEMO_API_KEY available: yes

# 3. Without agent-secrets the variable is not there
python python-example/check_secret.py
# -> DEMO_API_KEY available: no

# 4. Clean up
agent-secrets remove demo-key
```

The script only reports whether the variable is set. It never prints the value.
