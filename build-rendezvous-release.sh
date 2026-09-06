#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SERVICES_DIR="$REPO_ROOT/rendezvous_services"

VERSION=""
PROTOCOL_VERSION="1"
OUTPUT_DIR=""
KEEP_STAGING="false"
TAG=""
COMMIT=""
BUILD_DATE=""
PYTHON_PATH=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      VERSION="$2"; shift 2 ;;
    --protocol-version)
      PROTOCOL_VERSION="$2"; shift 2 ;;
    --output-dir)
      OUTPUT_DIR="$2"; shift 2 ;;
    --keep-staging)
      KEEP_STAGING="true"; shift ;;
    --tag)
      TAG="$2"; shift 2 ;;
    --commit)
      COMMIT="$2"; shift 2 ;;
    --build-date)
      BUILD_DATE="$2"; shift 2 ;;
    --python)
      PYTHON_PATH="$2"; shift 2 ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1 ;;
  esac
done

if [[ -z "$OUTPUT_DIR" ]]; then
  OUTPUT_DIR="$REPO_ROOT/Releases"
fi

if [[ -z "$VERSION" ]]; then
  PYPROJECT="$SERVICES_DIR/pyproject.toml"
  if [[ -f "$PYPROJECT" ]]; then
    VERSION="$(grep -E '^version\s*=\s*"[^"]+"' "$PYPROJECT" | head -n1 | sed -E 's/^version\s*=\s*"([^"]+)"$/\1/')"
  fi
fi

if [[ -z "$PYTHON_PATH" ]]; then
  if [[ -x "$SERVICES_DIR/.venv/bin/python" ]]; then
    PYTHON_PATH="$SERVICES_DIR/.venv/bin/python"
  elif [[ -x "$SERVICES_DIR/.venv/Scripts/python.exe" ]]; then
    PYTHON_PATH="$SERVICES_DIR/.venv/Scripts/python.exe"
  else
    PYTHON_PATH="python3"
  fi
fi

if [[ -z "$COMMIT" ]]; then
  COMMIT="$(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null || true)"
fi

if [[ -z "$TAG" ]]; then
  TAG="$(git -C "$REPO_ROOT" describe --tags --exact-match 2>/dev/null || true)"
  if [[ -z "$TAG" && -n "$VERSION" ]]; then
    TAG="rs-v$VERSION"
  fi
fi

if [[ -z "$BUILD_DATE" ]]; then
  BUILD_DATE="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
fi

ARGS=(
  "release_helper.py"
  "--output-dir" "$OUTPUT_DIR"
  "--protocol-version" "$PROTOCOL_VERSION"
  "--tag" "$TAG"
  "--commit" "$COMMIT"
  "--build-date" "$BUILD_DATE"
  "--include-path" "scripts/manage-jwt-keyring.py"
)

if [[ -n "$VERSION" ]]; then
  ARGS+=("--version" "$VERSION")
fi

if [[ "$KEEP_STAGING" == "true" ]]; then
  ARGS+=("--keep-staging")
fi

echo "Running release helper with stamp values:"
echo "  Version:   $VERSION"
echo "  Protocol:  $PROTOCOL_VERSION"
echo "  Tag:       $TAG"
echo "  Commit:    $COMMIT"
echo "  Build UTC: $BUILD_DATE"
echo "  Output:    $OUTPUT_DIR"

cd "$SERVICES_DIR"
"$PYTHON_PATH" "${ARGS[@]}"
