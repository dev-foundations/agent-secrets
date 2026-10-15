#!/bin/sh
# Removes AgentSecrets for the current macOS user.
#
# One-line uninstall:
#
#     curl -fsSL https://github.com/dev-foundations/agent-secrets/releases/latest/download/uninstall.sh | sh
#
# or run the copy the installer left in ~/.local/share/agent-secrets/uninstall.sh.
#
# Removes the program, the PATH line the installer added (unless other programs now live in that
# directory) and the Claude Code / Codex skills. Your stored secrets are KEPT unless you pass
# --remove-secrets.

set -eu

usage() {
    cat <<'EOF'
Usage: uninstall.sh [options]

  --prefix <dir>     Where AgentSecrets was installed (as given to install.sh). Default: ~/.local
  --remove-secrets   Also delete every secret stored by AgentSecrets from the macOS Keychain.
  --keep-skills      Leave the Claude Code / Codex skill directories in place.
  --yes              Do not ask for confirmation.
EOF
}

fail() {
    printf 'uninstall.sh: %s\n' "$*" >&2
    exit 1
}

prefix=$HOME/.local
remove_secrets=false
keep_skills=false
assume_yes=false

while [ $# -gt 0 ]; do
    case "$1" in
        --prefix) [ $# -ge 2 ] || fail "option $1 needs a value."; prefix=$2; shift 2 ;;
        --remove-secrets) remove_secrets=true; shift ;;
        --keep-skills) keep_skills=true; shift ;;
        --yes | -y) assume_yes=true; shift ;;
        --help | -h) usage; exit 0 ;;
        *) fail "unknown option '$1'. Run with --help for the options." ;;
    esac
done

case "$prefix" in
    /*) ;;
    *) prefix=$PWD/$prefix ;;
esac
bin_dir=$prefix/bin
exe=$bin_dir/agent-secrets
share_dir=$prefix/share/agent-secrets
codex_home=${CODEX_HOME:-$HOME/.codex}

# Exactly the line install.sh writes for this prefix.
case "$bin_dir" in
    "$HOME"/*) path_entry="\$HOME${bin_dir#"$HOME"}" ;;
    *) path_entry=$bin_dir ;;
esac
path_line="export PATH=\"$path_entry:\$PATH\"  # added by the AgentSecrets installer"

if ! $assume_yes; then
    # When piped into sh, stdin is this script: ask on the terminal instead.
    (: </dev/tty) 2>/dev/null || fail "no interactive terminal to confirm in. Re-run with --yes."
    what="AgentSecrets ($exe) and its PATH line"
    if ! $keep_skills; then
        what="$what and the agent skills"
    fi
    if $remove_secrets; then
        what="$what, AND ALL SECRETS stored by AgentSecrets"
    fi
    printf 'Remove %s? [y/N] ' "$what"
    answer=
    read -r answer </dev/tty || true
    case "$answer" in
        [Yy] | [Yy][Ee][Ss]) ;;
        *) echo 'Nothing changed.'; exit 0 ;;
    esac
fi

# --- Secrets (only on request) ---------------------------------------------------------------

if $remove_secrets; then
    if [ ! -x "$exe" ]; then
        echo "$exe not found, so secrets were not removed. Delete the AgentSecrets items in Keychain Access (search for AgentSecrets)." >&2
    elif ! names=$("$exe" list 2>/dev/null); then
        echo "'agent-secrets list' failed, so secrets were not removed. Delete the AgentSecrets items in Keychain Access (search for AgentSecrets)." >&2
    elif [ -z "$names" ]; then
        echo 'No secrets were stored.'
    else
        # Secret names are letters, digits, '.', '_' and '-', so word splitting is safe here.
        for name in $names; do
            if "$exe" remove "$name" --yes >/dev/null; then
                echo "Removed secret '$name'."
            else
                echo "Could not remove secret '$name'; delete it in Keychain Access (search for AgentSecrets)." >&2
            fi
        done
    fi
else
    echo 'Secrets were kept (macOS Keychain, "AgentSecrets/<name>"). A reinstall picks them up again.'
fi

# --- Files -----------------------------------------------------------------------------------

if [ -e "$exe" ]; then
    rm -f "$exe"
    echo "Removed $exe"
fi
if [ -d "$share_dir" ]; then
    rm -rf "$share_dir"
    echo "Removed $share_dir"
fi

# --- PATH ------------------------------------------------------------------------------------

# The line puts the whole directory on PATH. Programs installed there since (for example by an
# installer that found it already on PATH) still need it, so it goes only with the last program.
for profile in "${ZDOTDIR:-$HOME}/.zprofile" "$HOME/.bash_profile" "$HOME/.bash_login" "$HOME/.profile"; do
    if [ -f "$profile" ] && grep -qxF "$path_line" "$profile"; then
        if [ -n "$(ls -A "$bin_dir" 2>/dev/null)" ]; then
            echo "PATH: kept the line in $profile because $bin_dir still contains other programs."
        else
            # grep exits with 1 when no line is left; writing back with cat keeps the file's permissions.
            grep -vxF "$path_line" "$profile" >"$profile.agent-secrets.tmp" || true
            cat "$profile.agent-secrets.tmp" >"$profile"
            rm -f "$profile.agent-secrets.tmp"
            echo "PATH: removed the line the installer added to $profile"
        fi
    fi
done

if ! $keep_skills; then
    for skill_dir in "$HOME/.claude/skills/agent-secrets" "$HOME/.agents/skills/agent-secrets" "$codex_home/skills/agent-secrets"; do
        if [ -d "$skill_dir" ]; then
            rm -rf "$skill_dir"
            echo "Removed skill: $skill_dir"
        fi
    done
fi

echo 'AgentSecrets was uninstalled.'
