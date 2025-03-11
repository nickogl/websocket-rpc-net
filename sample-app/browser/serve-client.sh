#!/usr/bin/env sh
set -e

SCRIPT_DIR="$(dirname -- "$0")"

if [ ! -d "$SCRIPT_DIR/node_modules" ]; then
  npm ci
fi
npx http-server "$SCRIPT_DIR"
