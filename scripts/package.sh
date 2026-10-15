#!/bin/sh
# Builds the macOS release package: artifacts/dist/agent-secrets-<runtime>.tar.gz
#
# The archive contains everything install.sh needs:
#     agent-secrets                 self-contained, single file (no .NET runtime needed)
#     skill/agent-secrets/SKILL.md  the agent skill for Claude Code and Codex
#     uninstall.sh
#     LICENSE
# Used by the release workflow and by 'install.sh --from-source'. Requires the .NET 10 SDK and
# macOS: the SDK signs the binary there (ad hoc), and Apple Silicon refuses to run unsigned code.
#
#     scripts/package.sh --runtime osx-arm64 --version 0.2.0

set -eu

runtime=osx-arm64
version=
output_dir=

usage() {
    echo "Usage: scripts/package.sh [--runtime osx-arm64|osx-x64] [--version <version>] [--output-dir <dir>]" >&2
    exit 2
}

while [ $# -gt 0 ]; do
    case "$1" in
        --runtime) [ $# -ge 2 ] || usage; runtime=$2; shift 2 ;;
        --version) [ $# -ge 2 ] || usage; version=$2; shift 2 ;;
        --output-dir) [ $# -ge 2 ] || usage; output_dir=$2; shift 2 ;;
        *) usage ;;
    esac
done

case "$runtime" in
    osx-arm64 | osx-x64) ;;
    *) echo "package.sh: unsupported runtime '$runtime' (use osx-arm64 or osx-x64)." >&2; exit 2 ;;
esac

if [ "$(uname -s)" != Darwin ]; then
    echo "package.sh: run this on macOS, where the binary gets signed." >&2
    exit 1
fi

repo_root=$(cd "$(dirname "$0")/.." && pwd)
[ -n "$output_dir" ] || output_dir=$repo_root/artifacts/dist
publish_dir=$repo_root/artifacts/publish/$runtime
staging_dir=$repo_root/artifacts/staging/$runtime
archive=$output_dir/agent-secrets-$runtime.tar.gz

set -- publish "$repo_root/src/AgentSecrets.Cli/AgentSecrets.Cli.csproj" \
    --configuration Release --runtime "$runtime" --self-contained true \
    -p:PublishSingleFile=true -p:PublishTrimmed=true -p:DebugType=none \
    --output "$publish_dir" --nologo --verbosity quiet
if [ -n "$version" ]; then
    set -- "$@" "-p:Version=${version#v}"
fi

echo "Publishing agent-secrets ($runtime)..."
dotnet "$@"
codesign --verify --strict "$publish_dir/agent-secrets" # fails if the binary is not signed

rm -rf "$staging_dir"
mkdir -p "$staging_dir/skill" "$output_dir"
cp "$publish_dir/agent-secrets" "$staging_dir/"
cp -R "$repo_root/skill/agent-secrets" "$staging_dir/skill/"
cp "$repo_root/scripts/uninstall.sh" "$repo_root/LICENSE" "$staging_dir/"
chmod 755 "$staging_dir/agent-secrets" "$staging_dir/uninstall.sh"

rm -f "$archive"
# COPYFILE_DISABLE keeps macOS metadata (._* files) out of the archive; tar keeps the execute bits.
COPYFILE_DISABLE=1 tar -czf "$archive" -C "$staging_dir" agent-secrets skill uninstall.sh LICENSE
echo "Package: $archive"
