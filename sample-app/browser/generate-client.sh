#!/usr/bin/env sh
set -e

SCRIPT_DIR="$(dirname -- "$0")"
ROOT_DIR="$SCRIPT_DIR/../.."

cd "$ROOT_DIR/src/websocket-rpc-net-client"
dotnet run -f net8.0 -- --source "$ROOT_DIR/sample-app" --output "$ROOT_DIR/sample-app/browser/generated"
cd -
