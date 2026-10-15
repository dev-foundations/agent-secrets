#!/bin/sh
# Installs AgentSecrets for the current macOS user. No administrator rights needed.
#
# One-line install:
#
#     curl -fsSL https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.sh | sh
#
# What it does, after showing the plan and asking once:
#   1. Downloads the release package for your Mac (Apple silicon or Intel) and verifies its SHA-256.
#   2. Installs agent-secrets to ~/.local/bin (self-contained, no .NET needed).
#   3. If that directory is not on your PATH yet, adds it in your shell profile
#      (~/.zprofile for zsh; for bash, the first of ~/.bash_profile, ~/.bash_login, ~/.profile).
#   4. Installs the agent skill for Claude Code (~/.claude/skills) and Codex (~/.agents/skills).
#
# Running it again upgrades in place, also while agent-secrets is running.
# Your secrets (in the macOS Keychain) are never touched.
#
# To pass options with the one-liner:
#     curl -fsSL https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.sh | sh -s -- --version v0.2.0 --yes

set -eu

usage() {
    cat <<'EOF'
Usage: install.sh [options]

  --version <tag>        Release to install, e.g. v0.2.0. Default: the latest release.
  --prefix <dir>         Install to <dir>/bin and <dir>/share/agent-secrets. Default: ~/.local
  --runtime <rid>        osx-arm64 or osx-x64. Default: detected.
  --package <file>       Install from a local agent-secrets-<runtime>.tar.gz (offline installs, testing).
  --from-source          Build from this clone instead of downloading (requires the .NET 10 SDK).
  --no-path              Do not modify PATH.
  --no-skills            Do not install the Claude Code / Codex skill.
  --codex-home-skills    Install the Codex skill to $CODEX_HOME/skills (default ~/.codex/skills)
                         instead of ~/.agents/skills. Codex scans both, so only one is installed.
  --yes                  Do not ask for confirmation (unattended installs).
EOF
}

fail() {
    printf 'install.sh: %s\n' "$*" >&2
    exit 1
}

version=latest
repository=dev-foundations/agent-secrets
prefix=$HOME/.local
runtime=
package=
from_source=false
no_path=false
no_skills=false
codex_home_skills=false
assume_yes=false

while [ $# -gt 0 ]; do
    case "$1" in
        --version | --repository | --prefix | --runtime | --package)
            [ $# -ge 2 ] || fail "option $1 needs a value."
            case "$1" in
                --version) version=$2 ;;
                --repository) repository=$2 ;;
                --prefix) prefix=$2 ;;
                --runtime) runtime=$2 ;;
                --package) package=$2 ;;
            esac
            shift 2
            ;;
        --from-source) from_source=true; shift ;;
        --no-path) no_path=true; shift ;;
        --no-skills) no_skills=true; shift ;;
        --codex-home-skills) codex_home_skills=true; shift ;;
        --yes | -y) assume_yes=true; shift ;;
        --help | -h) usage; exit 0 ;;
        *) fail "unknown option '$1'. Run with --help for the options." ;;
    esac
done

if [ "$(uname -s)" != Darwin ]; then
    fail "this installer is for macOS. On Windows use install.ps1: https://github.com/$repository#3-installation"
fi

case "$prefix" in
    /*) ;;
    *) prefix=$PWD/$prefix ;;
esac
# The bin directory is written into a shell profile, so it must not need any quoting.
case "$prefix" in
    *[\"\$\`\\]* | *'
'*) fail "--prefix must not contain quotes, '\$', backquotes, backslashes or line breaks." ;;
esac
bin_dir=$prefix/bin
share_dir=$prefix/share/agent-secrets
exe=$bin_dir/agent-secrets

# Codex scans both ~/.agents/skills (its documented user-level location) and $CODEX_HOME/skills
# (default ~/.codex/skills). It does NOT de-duplicate: a skill present in both is listed twice.
# So exactly one is installed - the documented one by default.
claude_skill=$HOME/.claude/skills/agent-secrets
agents_skill=$HOME/.agents/skills/agent-secrets
codex_home_skill=${CODEX_HOME:-$HOME/.codex}/skills/agent-secrets
if $codex_home_skills; then
    codex_skill=$codex_home_skill
    stale_skill=$agents_skill
else
    codex_skill=$agents_skill
    stale_skill=$codex_home_skill
fi

if [ -z "$runtime" ]; then
    # Under Rosetta, uname -m says x86_64 even on Apple silicon; the sysctl does not.
    if [ "$(uname -m)" = arm64 ] || [ "$(sysctl -n hw.optional.arm64 2>/dev/null || true)" = 1 ]; then
        runtime=osx-arm64
    else
        runtime=osx-x64
    fi
fi
case "$runtime" in
    osx-arm64 | osx-x64) ;;
    *) fail "unsupported runtime '$runtime' (use osx-arm64 or osx-x64)." ;;
esac
package_name=agent-secrets-$runtime.tar.gz

script_dir=$(cd "$(dirname "$0")" && pwd)
if $from_source && [ -n "$package" ]; then
    fail "--from-source and --package cannot be combined."
fi
if $from_source && [ ! -f "$script_dir/package.sh" ]; then
    fail "--from-source works only when running install.sh from a clone of the repository."
fi

if [ -n "$package" ]; then
    case "$package" in
        /*) ;;
        *) package=$PWD/$package ;;
    esac
    source_description="local package $package"
elif $from_source; then
    source_description="build from source ($(dirname "$script_dir"))"
else
    source_description="GitHub release '$version' of $repository ($runtime)"
fi

# The PATH line written to the shell profile; uninstall.sh removes exactly this line.
case "$bin_dir" in
    "$HOME"/*) path_entry="\$HOME${bin_dir#"$HOME"}" ;;
    *) path_entry=$bin_dir ;;
esac
path_line="export PATH=\"$path_entry:\$PATH\"  # added by the AgentSecrets installer"
case "$(basename "${SHELL:-}")" in
    zsh) profile=${ZDOTDIR:-$HOME}/.zprofile ;;
    bash)
        # A bash login shell reads only the first of these that exists. Add to that one, so a new
        # ~/.bash_profile never hides an existing ~/.profile.
        profile=$HOME/.bash_profile
        for candidate in "$HOME/.bash_profile" "$HOME/.bash_login" "$HOME/.profile"; do
            if [ -f "$candidate" ]; then
                profile=$candidate
                break
            fi
        done
        ;;
    *) profile= ;;
esac
case ":${PATH:-}:" in
    *":$bin_dir:"* | *":$bin_dir/:"*) path_present=true ;;
    *) path_present=false ;;
esac
if [ -n "$profile" ] && [ -f "$profile" ] && grep -qxF "$path_line" "$profile"; then
    profile_has_line=true
else
    profile_has_line=false
fi

# --- Plan ------------------------------------------------------------------------------------

echo
echo 'AgentSecrets installer'
echo "  Source  : $source_description"
echo "  Program : $exe"
if $no_path; then
    echo '  PATH    : not changed (--no-path)'
elif $path_present; then
    echo '  PATH    : already contains the install directory'
elif $profile_has_line; then
    echo "  PATH    : already added in $profile (open a new terminal)"
elif [ -n "$profile" ]; then
    echo "  PATH    : add $bin_dir in $profile"
else
    echo "  PATH    : not changed (unknown shell '${SHELL:-}'; you add $bin_dir yourself)"
fi
if $no_skills; then
    echo '  Skills  : not installed (--no-skills)'
else
    echo "  Skill   : $claude_skill   (Claude Code)"
    echo "  Skill   : $codex_skill   (Codex)"
fi
echo '  Secrets : not touched (they stay in the macOS Keychain)'
echo

if ! $assume_yes; then
    # When piped into sh, stdin is this script: ask on the terminal instead.
    (: </dev/tty) 2>/dev/null || fail "no interactive terminal to confirm in. Re-run with --yes for an unattended install."
    printf 'Continue? [Y/n] '
    answer=
    read -r answer </dev/tty || true
    case "$answer" in
        '' | [Yy] | [Yy][Ee][Ss]) ;;
        *) echo 'Nothing changed.'; exit 0 ;;
    esac
fi

# --- 1. Get the package ----------------------------------------------------------------------

work=$(mktemp -d "${TMPDIR:-/tmp}/agentsecrets-install.XXXXXX")
trap 'rm -rf "$work"' EXIT
trap 'exit 1' HUP INT TERM

if [ -n "$package" ]; then
    [ -f "$package" ] || fail "package not found: $package"
    archive=$package
elif $from_source; then
    command -v dotnet >/dev/null 2>&1 || fail "building from source needs the .NET 10 SDK: https://dotnet.microsoft.com/download"
    "$script_dir/package.sh" --runtime "$runtime" --output-dir "$work"
    archive=$work/$package_name
else
    if [ "$version" = latest ]; then
        base_url=https://github.com/$repository/releases/latest/download
    else
        case "$version" in
            v*) tag=$version ;;
            *) tag=v$version ;;
        esac
        base_url=https://github.com/$repository/releases/download/$tag
    fi

    archive=$work/$package_name
    echo "Downloading $base_url/$package_name"
    if ! curl -fsSL --proto '=https' --tlsv1.2 -o "$archive" "$base_url/$package_name" \
        || ! curl -fsSL --proto '=https' --tlsv1.2 -o "$work/SHA256SUMS.txt" "$base_url/SHA256SUMS.txt"; then
        fail "download failed. Check the version ('$version') and your connection."
    fi

    expected=$(awk -v name="$package_name" '$2 == name || $2 == "*" name { print tolower($1) }' "$work/SHA256SUMS.txt")
    [ -n "$expected" ] || fail "SHA256SUMS.txt has no entry for $package_name."
    actual=$(shasum -a 256 "$archive" | awk '{ print $1 }')
    if [ "$actual" != "$expected" ]; then
        fail "checksum mismatch for $package_name. The download is corrupt or was tampered with; nothing was installed."
    fi
    echo 'Checksum verified (SHA-256).'
fi

staging=$work/package
mkdir -p "$staging"
tar -xzf "$archive" -C "$staging"
[ -f "$staging/agent-secrets" ] || fail "the package does not contain agent-secrets: $archive"

# --- 2. Program ------------------------------------------------------------------------------

upgrading=false
[ -e "$exe" ] && upgrading=true
mkdir -p "$bin_dir" "$share_dir"
# Replace the program by renaming a new file over it: a running copy keeps its own file, and a
# signed binary is never modified in place (macOS can kill a process whose code changes).
cp "$staging/agent-secrets" "$bin_dir/.agent-secrets.new"
chmod 755 "$bin_dir/.agent-secrets.new"
mv -f "$bin_dir/.agent-secrets.new" "$exe"
cp "$staging/uninstall.sh" "$staging/LICENSE" "$share_dir/"
chmod 755 "$share_dir/uninstall.sh"
echo "Installed: $exe"

# --- 3. PATH ---------------------------------------------------------------------------------

path_added=false
if ! $no_path && ! $path_present && ! $profile_has_line && [ -n "$profile" ]; then
    # Start on a new line if the profile does not end with one.
    if [ -s "$profile" ] && [ -n "$(tail -c 1 "$profile")" ]; then
        echo >>"$profile"
    fi
    printf '%s\n' "$path_line" >>"$profile"
    path_added=true
    echo "PATH: added $bin_dir in $profile"
fi

# --- 4. Agent skill --------------------------------------------------------------------------

if ! $no_skills; then
    # Remove the copy in the other supported directory, so Codex never lists the skill twice.
    if [ -e "$stale_skill" ]; then
        rm -rf "$stale_skill"
        echo "Removed the duplicate skill copy in $stale_skill"
    fi
    for target in "$claude_skill" "$codex_skill"; do
        rm -rf "$target"
        mkdir -p "$target"
        cp -R "$staging/skill/agent-secrets/." "$target/"
    done
    echo "Skill installed for Claude Code: $claude_skill"
    echo "Skill installed for Codex: $codex_skill"
fi

installed=$("$exe" --version)
echo
echo "$installed is ready."
echo 'Next steps:'
echo '    agent-secrets doctor'
echo '    agent-secrets set openai          (you type or paste the key; it is not shown)'
echo '    agent-secrets run --env OPENAI_API_KEY=openai -- python3 app.py'
if $path_added || (! $no_path && ! $path_present && $profile_has_line); then
    echo 'Open a new terminal (and restart your agent) so they find agent-secrets.'
fi
if $upgrading; then
    echo 'After an upgrade macOS may ask once per secret whether agent-secrets may use it: choose Always Allow.'
fi
if ! $no_skills; then
    echo 'Restart Claude Code / Codex so they load the skill.'
fi
