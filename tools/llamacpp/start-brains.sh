#!/usr/bin/env bash
# Starts the two llama.cpp brain servers (Linux/macOS). See start-brains.ps1 and README.md for details.
# Usage: PILOT_MODEL=/models/pilot.gguf STRATEGIC_MODEL=/models/strategist.gguf ./start-brains.sh
set -euo pipefail

: "${PILOT_MODEL:?set PILOT_MODEL to a .gguf file}"
: "${STRATEGIC_MODEL:?set STRATEGIC_MODEL to a .gguf file}"
SERVER="${LLAMA_SERVER:-llama-server}"
PARALLEL="${PARALLEL:-2}"
KV_CACHE_TYPE="${KV_CACHE_TYPE:-q8_0}"     # 8-bit KV cache; empty to keep f16
STRATEGIC_DRAFT_MODEL="${STRATEGIC_DRAFT_MODEL:-}"  # small same-family .gguf for speculative decoding
GPU_LAYERS="${GPU_LAYERS:-999}"
LOG_DIR="${LOG_DIR:-${XDG_STATE_HOME:-$HOME/.local/state}/llm-surviving-proxy-agent/llama-server}"
mkdir -p "$LOG_DIR"

healthy() { curl -fsS -o /dev/null "http://127.0.0.1:$1/health" 2>/dev/null; }

start_brain() { # name model alias port context extra [draft]
  local name=$1 model=$2 alias=$3 port=$4 context=$5 extra=$6 draft=${7:-}
  [[ -n "$KV_CACHE_TYPE" ]] && extra="--cache-type-k $KV_CACHE_TYPE --cache-type-v $KV_CACHE_TYPE $extra"
  [[ -n "$draft" ]] && extra="--model-draft $draft --gpu-layers-draft $GPU_LAYERS $extra"
  if healthy "$port"; then echo "$name brain already running on port $port."; return; fi
  [[ -f "$model" ]] || { echo "$name model not found: $model" >&2; exit 1; }
  # shellcheck disable=SC2086
  nohup "$SERVER" --model "$model" --alias "$alias" --host 127.0.0.1 --port "$port" \
    --ctx-size $((context * PARALLEL)) --parallel "$PARALLEL" --n-gpu-layers "$GPU_LAYERS" --jinja $extra \
    >"$LOG_DIR/$name.log" 2>&1 &
  echo "Starting $name brain on port $port (log: $LOG_DIR/$name.log)..."
}

start_brain pilot "$PILOT_MODEL" "${PILOT_ALIAS:-pilot}" "${PILOT_PORT:-8081}" "${PILOT_CONTEXT:-8192}" "${PILOT_EXTRA_ARGS:-}"
start_brain strategic "$STRATEGIC_MODEL" "${STRATEGIC_ALIAS:-strategist}" "${STRATEGIC_PORT:-8082}" \
  "${STRATEGIC_CONTEXT:-16384}" "${STRATEGIC_EXTRA_ARGS:-}" "$STRATEGIC_DRAFT_MODEL"

for port in "${PILOT_PORT:-8081}" "${STRATEGIC_PORT:-8082}"; do
  for _ in $(seq 1 150); do healthy "$port" && break; sleep 2; done
  healthy "$port" || { echo "llama-server on port $port did not become healthy; see $LOG_DIR" >&2; exit 1; }
  echo "Port $port ready."
done
