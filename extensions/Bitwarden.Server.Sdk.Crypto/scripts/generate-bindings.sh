#!/usr/bin/env bash
# Generates src/Generated/bitwarden_uniffi_server.cs from a native library placed by
# fetch-native.sh, using the uniffi-bindgen-cs version pinned in sdk-internal.env.
#
# Library mode reads the API from the metadata embedded in the library, so the bindings match the
# exact binary that gets shipped. Every RID carries the same metadata; the first one found is used.
#
# Usage:
#   generate-bindings.sh            Regenerate the committed bindings.
#   generate-bindings.sh --check    Fail if the committed bindings differ (CI).
set -euo pipefail

PACKAGE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOLS_DIR="$PACKAGE_DIR/.tools"
OUT_DIR="$PACKAGE_DIR/src/Generated"
GENERATED="bitwarden_uniffi_server.cs"

# shellcheck source=../sdk-internal.env
source "$PACKAGE_DIR/sdk-internal.env"

BINDGEN="$TOOLS_DIR/bin/uniffi-bindgen-cs"

# Install the pinned generator once; reinstall when the pin changes.
if ! "$BINDGEN" --version 2> /dev/null | grep -qF "${UNIFFI_BINDGEN_CS_TAG#v}"; then
    cargo install uniffi-bindgen-cs \
        --git https://github.com/NordSecurity/uniffi-bindgen-cs \
        --tag "$UNIFFI_BINDGEN_CS_TAG" --locked --force --root "$TOOLS_DIR"
fi

LIBRARY="$(find "$PACKAGE_DIR/native/runtimes" -type f \
    \( -name '*.so' -o -name '*.dylib' -o -name '*.dll' \) 2> /dev/null | sort | head -n 1)"
if [ -z "$LIBRARY" ]; then
    echo "No native library found. Run scripts/fetch-native.sh first." >&2
    exit 1
fi

TARGET_DIR="$OUT_DIR"
if [ "${1:-}" = "--check" ]; then
    TARGET_DIR="$(mktemp -d)"
fi

# Library mode runs `cargo metadata` to locate per-crate configs and fails outside a Cargo project.
# An empty package satisfies it; the config comes from --config instead.
SHIM_DIR="$(mktemp -d)"
trap 'rm -rf "$SHIM_DIR"' EXIT
mkdir -p "$SHIM_DIR/src"
touch "$SHIM_DIR/src/lib.rs"
printf '[package]\nname = "bindgen-shim"\nversion = "0.0.0"\nedition = "2021"\n' > "$SHIM_DIR/Cargo.toml"

(cd "$SHIM_DIR" && "$BINDGEN" --library "$LIBRARY" \
    --config "$PACKAGE_DIR/uniffi.toml" \
    --no-format \
    --out-dir "$TARGET_DIR")

if [ "${1:-}" != "--check" ]; then
    echo "Generated $OUT_DIR/$GENERATED from $LIBRARY"
    exit 0
fi

if ! diff -q "$OUT_DIR/$GENERATED" "$TARGET_DIR/$GENERATED" > /dev/null; then
    echo "Committed bindings are stale. Run scripts/generate-bindings.sh and commit." >&2
    diff "$OUT_DIR/$GENERATED" "$TARGET_DIR/$GENERATED" | head -n 40 >&2
    exit 1
fi
echo "Bindings up to date."
