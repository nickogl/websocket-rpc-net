#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(dirname -- "$0")"
ROOT_DIR="$SCRIPT_DIR/../.."
SERVER_ARGS=()
NODE_PORTS=()
NODE_ARGS=()
EXECUTOR_ARGS=()
while [[ $# -gt 0 ]]; do
  case $1 in
    --node)
      [[ -z "$2" ]] && (echo 'Expected port number after --node' >&2; exit 1)
      [[ "$2" =~ ^([[:digit:]]+)(-([[:digit:]]+))?$ ]] || (echo 'Expected port to be a number or a range (e.g. 5000-5010)' >&2; exit 1)
      [[ -n "${BASH_REMATCH[3]}" && "${BASH_REMATCH[3]}" -lt "${BASH_REMATCH[1]}" ]] && (echo 'End of port range must be greater than start of port range' >&2; exit 1)
      if [[ -z "${BASH_REMATCH[2]}" ]]; then
        NODE_PORTS+=("${BASH_REMATCH[1]}")
      else
        for ((port = "${BASH_REMATCH[1]}"; port <= "${BASH_REMATCH[3]}"; port++)); do
          NODE_PORTS+=("$port")
        done
      fi
      shift 2 ;;
    --connections)
      [[ -z "$2" ]] && (echo 'Expected number after --connections' >&2; exit 1)
      CONNECTIONS="$2"
      NODE_ARGS+=('-e' "Connections=$CONNECTIONS")
      shift 2 ;;
    --duration)
      [[ -z "$2" ]] && (echo 'Expected string with .NET TimeSpan format (e.g. 00:10:00 for 10 minutes) after --duration' >&2; exit 1)
      EXECUTOR_ARGS+=('-e' "TestDuration=$2")
      shift 2 ;;
    --server-url)
       [[ -z "$2" ]] && (echo 'Expected string after --server-url' >&2; exit 1)
      SERVER_URL="$2"
      shift 2 ;;
    --server-memory)
      [[ -z "$2" ]] && (echo 'Expected string with memory format (e.g. 1024MiB) after --server-memory' >&2; exit 1)
      SERVER_ARGS+=('--memory' "$2")
      shift 2 ;;
    --server-env)
      [[ -z "$2" ]] && (echo 'Expected key-value pair (e.g. Key=Value) after --server-env' >&2; exit 1)
      SERVER_ARGS+=('-e' "$2")
      shift 2 ;;
    --node-memory)
      [[ -z "$2" ]] && (echo 'Expected string with memory format (e.g. 1024MiB) after --node-memory' >&2; exit 1)
      NODE_ARGS+=('--memory' "$2")
      shift 2 ;;
    --node-cpus)
      [[ -z "$2" ]] && (echo 'Expected decimal after --node-cpus' >&2; exit 1)
      NODE_ARGS+=('--cpus' "$2")
      shift 2 ;;
    --node-env)
      [[ -z "$2" ]] && (echo 'Expected key-value pair (e.g. Key=Value) after --node-env' >&2; exit 1)
      NODE_ARGS+=('-e' "$2")
      shift 2 ;;
    *)
      echo "Unknown option or unexpected positional argument: '$1'" >&2
      exit 1
  esac
done
[[ ${#NODE_PORTS[@]} == 0 ]] && (echo 'Must use at least one node' >&2; exit 1)
[[ $CONNECTIONS -lt 1 ]] && (echo 'Please specify more than 1 connection per node' >&2; exit 1)
[[ $CONNECTIONS -gt 32768 ]] && (echo 'Please specify less than 32768 connections per node' >&2; exit 1)

function cleanup() {
  mkdir -p "$SCRIPT_DIR/TestResults/Logs" || true
  docker stop websocket-rpc-load-test-executor >/dev/null 2>&1 || true
  for port in "${NODE_PORTS[@]}"; do
    docker logs "websocket-rpc-load-test-client-$port" --timestamps >"$SCRIPT_DIR/TestResults/Logs/client-$port.log" 2>&1 || true
    docker stop "websocket-rpc-load-test-client-$port" >/dev/null 2>&1 || true
  done
  docker logs websocket-rpc-load-test-server --timestamps >"$SCRIPT_DIR/TestResults/Logs/server.log" 2>&1 || true
  docker stop websocket-rpc-load-test-server >/dev/null 2>&1 || true
  docker network remove websocket-rpc-load-test >/dev/null 2>&1 || true
}

# Preparation
echo 'Building load test containers...'
docker build -t websocket-rpc-load-test-server -f "$SCRIPT_DIR/load-test-server/Dockerfile" "$ROOT_DIR" >/dev/null 2>&1
docker build -t websocket-rpc-load-test-client -f "$SCRIPT_DIR/load-test-client/Dockerfile" "$ROOT_DIR" >/dev/null 2>&1
docker network create websocket-rpc-load-test >/dev/null
trap cleanup EXIT

if [[ -z "$SERVER_URL" ]]; then
  SERVER_URL=http://websocket-rpc-load-test-server:8080
  echo 'Starting websocket RPC server under test...'
  docker run -d --rm \
    --network websocket-rpc-load-test \
    --name websocket-rpc-load-test-server \
    -e Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Warning \
    -e Logging__LogLevel__Microsoft.AspNetCore.Server.Kestrel.Connections=Warning \
    -e Logging__LogLevel__Microsoft.AspNetCore.Server.Kestrel=None \
    "${SERVER_ARGS[@]}" \
    websocket-rpc-load-test-server >/dev/null
fi
NODE_ARGS+=('-e' "TestServerUrl=$SERVER_URL")

echo 'Starting load test nodes...'
for port in "${NODE_PORTS[@]}"; do
  docker run -d --rm \
    --network websocket-rpc-load-test \
    --name "websocket-rpc-load-test-client-$port" \
    "${NODE_ARGS[@]}" \
    websocket-rpc-load-test-client >/dev/null
done

NODE_INDEX=0
for port in "${NODE_PORTS[@]}"; do
  EXECUTOR_ARGS+=('-e' "Nodes__${NODE_INDEX}__BaseUrl=http://websocket-rpc-load-test-client-$port:8080")
  ((++NODE_INDEX))
done
echo 'Starting load test executor...'
docker run --rm \
  --network websocket-rpc-load-test \
  --name websocket-rpc-load-test-executor \
  -v "$SCRIPT_DIR/TestResults:/app/TestResults" \
  -e Logging__LogLevel__Microsoft.Hosting.Lifetime=Warning \
  "${EXECUTOR_ARGS[@]}" \
  websocket-rpc-load-test-client
