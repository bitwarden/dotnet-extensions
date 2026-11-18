#!/usr/bin/env bash
# Places the bitwarden-uniffi-server native libraries where the csproj packs and copies them:
#
#   native/runtimes/<rid>/native/<lib>
#
# Usage:
#   fetch-native.sh --from-source <sdk-internal dir>   Build for the host RID with cargo (dev loop).
#   fetch-native.sh --from-run <run-id> [rid...]       Download CI artifacts, verify sha256.
#                                                      Defaults to every RID in sdk-internal.env.
#   fetch-native.sh --from-commit <sha> [rid...]       --from-run with that commit's latest
#                                                      successful build.
#   fetch-native.sh --from-pinned [rid...]             --from-commit with SDK_INTERNAL_COMMIT (CI).
set -euo pipefail

PACKAGE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NATIVE_DIR="$PACKAGE_DIR/native/runtimes"
CRATE=bitwarden-uniffi-server

# shellcheck source=../sdk-internal.env
source "$PACKAGE_DIR/sdk-internal.env"

# Library file name per RID, e.g. osx-arm64 -> libbitwarden_uniffi_server.dylib.
lib_name() {
    case "$1" in
        win-*) echo "bitwarden_uniffi_server.dll" ;;
        osx-*) echo "libbitwarden_uniffi_server.dylib" ;;
        linux-*) echo "libbitwarden_uniffi_server.so" ;;
        *) echo "Unsupported RID: $1" >&2; exit 1 ;;
    esac
}

# RID of the machine running this script, e.g. Darwin/arm64 -> osx-arm64.
host_rid() {
    local os arch
    case "$(uname -s)" in
        Darwin) os=osx ;;
        Linux) os=linux ;;
        MINGW* | MSYS* | CYGWIN*) os=win ;;
        *) echo "Unsupported OS: $(uname -s)" >&2; exit 1 ;;
    esac
    case "$(uname -m)" in
        arm64 | aarch64) arch=arm64 ;;
        x86_64 | amd64) arch=x64 ;;
        *) echo "Unsupported architecture: $(uname -m)" >&2; exit 1 ;;
    esac
    echo "$os-$arch"
}

sha256() {
    if command -v sha256sum > /dev/null; then
        sha256sum "$1" | cut -d' ' -f1
        return
    fi
    shasum -a 256 "$1" | cut -d' ' -f1
}

from_source() {
    local sdk_dir rid lib out
    sdk_dir="$(cd "$1" && pwd)"
    rid="$(host_rid)"
    lib="$(lib_name "$rid")"
    out="$NATIVE_DIR/$rid/native"

    cargo build --manifest-path "$sdk_dir/Cargo.toml" -p "$CRATE" --release

    mkdir -p "$out"
    cp "$sdk_dir/target/release/$lib" "$out/"
    git -C "$sdk_dir" rev-parse HEAD > "$out/source-commit.txt"
    echo "Placed $rid from $sdk_dir"
}

from_run() {
    local run_id="$1"
    shift
    local rids=("$@")
    if [ ${#rids[@]} -eq 0 ]; then
        read -r -a rids <<< "$RIDS"
    fi

    local rid lib out expected actual
    for rid in "${rids[@]}"; do
        lib="$(lib_name "$rid")"
        out="$NATIVE_DIR/$rid/native"
        rm -rf "$out"
        mkdir -p "$out"

        gh run download "$run_id" --repo "$SDK_INTERNAL_REPO" \
            --name "$CRATE-$rid" --dir "$out"

        # The artifact carries the checksum computed on the build runner.
        expected="$(cut -d' ' -f1 < "$out/$lib.sha256")"
        actual="$(sha256 "$out/$lib")"
        if [ "$expected" != "$actual" ]; then
            echo "Checksum mismatch for $rid: expected $expected, got $actual" >&2
            exit 1
        fi
        rm "$out/$lib.sha256"
        echo "Placed $rid from run $run_id"
    done
}

# Latest successful SDK_INTERNAL_WORKFLOW run of an sdk-internal commit.
run_for_commit() {
    local run_id
    run_id="$(gh run list --repo "$SDK_INTERNAL_REPO" --workflow "$SDK_INTERNAL_WORKFLOW" \
        --commit "$1" --status success --limit 1 --json databaseId --jq '.[0].databaseId // empty')"

    if [ -z "$run_id" ]; then
        echo "No successful $SDK_INTERNAL_WORKFLOW run for commit $1" >&2
        exit 1
    fi

    echo "$run_id"
}

from_commit() {
    local run_id
    run_id="$(run_for_commit "$1")"
    shift

    from_run "$run_id" "$@"
}

case "${1:-}" in
    --from-source) from_source "${2:?sdk-internal dir required}" ;;
    --from-run) shift; from_run "${1:?run id required}" "${@:2}" ;;
    --from-commit) shift; from_commit "${1:?commit required}" "${@:2}" ;;
    --from-pinned) shift; from_commit "${SDK_INTERNAL_COMMIT:?SDK_INTERNAL_COMMIT unset in sdk-internal.env}" "$@" ;;
    *) sed -n '2,13p' "$0" >&2; exit 1 ;;
esac
