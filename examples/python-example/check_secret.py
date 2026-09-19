"""AgentSecrets demo: proves the secret reached this process without ever revealing it.

Run from the 'examples' directory (where .agentsecrets.json lives):

    agent-secrets set demo-key
    agent-secrets run -- python python-example/check_secret.py
"""

import os
import sys

available = bool(os.environ.get("DEMO_API_KEY"))
print(f"DEMO_API_KEY available: {'yes' if available else 'no'}")
sys.exit(0 if available else 1)
