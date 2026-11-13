# Running the brains on llama.cpp

The agent has three model "brains". With llama.cpp each runs on `llama-server` on your own machine, next to MTC,
so it can use your GPU:

| Brain | Job | Wants | Default llama.cpp endpoint |
| --- | --- | --- | --- |
| Pilot | One action per game prompt, many times a minute | Very small and fast; strict JSON | `http://127.0.0.1:8081/` |
| Reactive | Classifies game events; names new characters | Same as the Pilot (shares its server) | Pilot's endpoint |
| Strategic | Sets objectives, reviews progress, writes lessons after each life | Larger; deliberates over a long horizon | `http://127.0.0.1:8082/` |

Each brain gets its own server so both models stay loaded. Every MTC agent on the machine shares them;
`--parallel` is how many agents one server answers at once. That is why llama.cpp runs as a shared sidecar rather
than inside each agent: a model is loaded into GPU memory once, not once per agent.

## Recommended setup: RTX 3080 Ti Laptop GPU (16 GB), 1–2 agents

The design assumes both brains stay loaded on the GPU at the same time, so the Pilot never waits for the strategist.

| Piece | Model / setting | VRAM (approx.) |
| --- | --- | --- |
| Pilot + Reactive | Qwen3-4B-Instruct-2507, Q4_K_M | ~2.5 GB |
| Strategic | Qwen3-8B, Q4_K_M, thinking on (two-pass) | ~5 GB |
| Strategic draft | Qwen3-0.6B, Q8_0, for speculative decoding | ~0.6 GB |
| KV cache, 8-bit | Pilot 8k × 2 slots, Strategic 16k × 2 slots | ~3.5 GB |
| CUDA buffers, Windows desktop | | ~1.5–2 GB |

That totals about 13–14 GB of the 16 GB. A Q4_K_M Qwen3-14B strategist (~9 GB) also fits if you shorten its context
(`-StrategicContext 8192`); use the bench and harness runs to see whether it's worth the VRAM.

```powershell
.\tools\llamacpp\start-brains.ps1 -PilotModel D:\models\Qwen3-4B-Instruct-2507-Q4_K_M.gguf `
  -StrategicModel D:\models\Qwen3-8B-Q4_K_M.gguf -StrategicDraftModel D:\models\Qwen3-0.6B-Q8_0.gguf
```

Laptop notes: keep it on AC power with the Windows power mode on *Best performance*; NVIDIA laptop GPUs throttle hard
on battery. The script prints `nvidia-smi` memory use once both servers are up. If memory use is close to 16 GB, lower
`-StrategicContext` first, then drop the draft model.

What a real run already showed (a CPU-only check of the integration, not of speed): Qwen3-0.6B returned valid
answers but was too passive as a Pilot, choosing "None" in 11 of 12 decisions even with StarDock one hop away. That's
why the recommended Pilot is the 4B instruct model. The Pilot's ~4,300-token prompt is reused from the server's cache
after the first call, so only the ~500 tokens of new game state are processed each turn.

## 1. Get llama-server

Download a prebuilt release from <https://github.com/ggml-org/llama.cpp/releases>: the **CUDA** build for NVIDIA,
**Vulkan** for AMD or Intel GPUs, the macOS build for Apple Silicon (Metal). Put `llama-server` on your `PATH`, or pass
its full path to the script with `-ServerPath`.

## 2. Get models (GGUF)

Download `.gguf` files from Hugging Face; the Q4_K_M or Q8_0 quantizations are good defaults. The candidates below
were good choices at the time of writing. Newer small models appear often, so check what's current and compare them
with the bench (step 5); that's what the bench is for.

**Pilot: small and fast.** The Pilot answers in strict JSON and only picks among allowed actions, so small models do
well.

| Candidate | Approx. size | Notes |
| --- | --- | --- |
| Qwen3-0.6B (Q8_0) | ~0.6 GB | Fastest; check how often its answers are valid |
| Qwen3-1.7B (Q8_0) | ~1.8 GB | Same family as today's Ollama default (`qwen3:1.7b`) |
| Qwen3-4B-Instruct-2507 (Q4_K_M) | ~2.5 GB | Non-thinking instruct variant; strong for its size |
| Gemma 3 1B / 4B instruct | ~0.8 / ~2.5 GB | |
| Llama 3.2 3B Instruct, SmolLM3-3B, Phi-4-mini | ~2–2.5 GB | Alternatives to compare |

**Strategic: deliberating.** On llama.cpp the Strategic brain thinks first in a free-form pass, then answers in a
second pass constrained to the JSON schema (see "How thinking works").

| Candidate | Approx. size | Notes |
| --- | --- | --- |
| Qwen3-8B (Q4_K_M) | ~5 GB | Same family as today's Ollama default (`qwen3:8b`) |
| Qwen3-14B (Q4_K_M) | ~9 GB | Noticeably better planning if it fits |
| Qwen3-30B-A3B-Thinking-2507 (Q4_K_M) | ~18 GB | MoE with ~3B active parameters: fast for its quality; on a smaller GPU keep some expert layers on the CPU with `--n-cpu-moe N` |
| gpt-oss-20b | ~13 GB | MoE reasoning model |

Budget VRAM for both models plus their KV cache (context × parallel slots). For example, a 12 GB card fits a Q8 1.7B
Pilot (~2 GB) plus a Q4 8B strategist (~5 GB) with room for 4 agents' context. Add `--cache-type-k q8_0
--cache-type-v q8_0` to roughly halve KV-cache memory.

## 3. Start the servers

```powershell
.\tools\llamacpp\start-brains.ps1 -PilotModel D:\models\Qwen3-4B-Instruct-2507-Q4_K_M.gguf `
  -StrategicModel D:\models\Qwen3-8B-Q4_K_M.gguf -Parallel 2
```

On Linux or macOS: `PILOT_MODEL=... STRATEGIC_MODEL=... ./tools/llamacpp/start-brains.sh`. Logs go to
`%LOCALAPPDATA%\LLMSurvivingProxyAgent\llama-server\`. Extra flags go through `-PilotExtraArgs` or
`-StrategicExtraArgs`. By default the script uses 2 slots per brain and an 8-bit KV cache (`-KvCacheType q8_0`,
which relies on flash attention; CUDA builds turn that on automatically), and `-StrategicDraftModel` adds speculative
decoding. Flag spelling varies between llama.cpp releases; `llama-server --help` lists yours.

Alternatively, let the agent host launch them when they aren't running by setting `Brains:{brain}:LlamaCpp:ModelPath`
(and optionally `ServerPath`, `ContextSize`, `Parallel`, `GpuLayers`, `KvCacheType`, `DraftModelPath`,
`ExtraArguments`). The first MTC process to
start launches the server; the others share it.

## 4. Point the agents at them

Environment variables (set them before starting MTC), or the same keys in the dashboard's `appsettings.json`:

```powershell
$env:Brains__Pilot__Backend = "LlamaCpp";     $env:Brains__Pilot__Model = "pilot"
$env:Brains__Reactive__Backend = "LlamaCpp";  $env:Brains__Reactive__Model = "pilot"
$env:Brains__Strategic__Backend = "LlamaCpp"; $env:Brains__Strategic__Model = "strategist"
```

The model name is the server's `--alias`. Other settings per brain: `Endpoint`, `Thinking` (true/false),
`MaxTokens`, and `Temperature`. On llama.cpp the Pilot and Reactive brains default to temperature 0.2, so the same
situation gets the same decision; the strategist keeps the model's default. `Reasoning__Backend=LlamaCpp` switches every brain at once. Ollama keeps working; you can mix backends,
for example a llama.cpp Pilot with an Ollama strategist. Endpoints must be loopback (`127.0.0.1` or `localhost`) so
game observations never leave the machine.

## 5. Compare models on your own games

The harness bench replays prompts recorded from your agents' real games (stored in `trajectories.db`) against each
candidate. It reports how often each model gives a valid answer, how often it agrees with the decision made at the
time, latency (median and p95), and tokens per second:

```powershell
dotnet run --project LLMSurvivingProxyAgent\LLMSurvivingProxyAgent.Harness -- bench --brain pilot `
  --models "llamacpp:pilot@http://127.0.0.1:8081/,ollama:qwen3:1.7b" --samples 60
dotnet run --project LLMSurvivingProxyAgent\LLMSurvivingProxyAgent.Harness -- bench --brain strategic `
  --models "llamacpp:strategist@http://127.0.0.1:8082/,llamacpp:strategist-think@http://127.0.0.1:8082/" --samples 15
```

A spec is `[backend:]model[@endpoint][+think|-think]`. Prompts recorded from now on also store the exact JSON schema
used live, and the bench replays with it; older recordings fall back to a looser generic schema, and the report
says how many of each it used. To try several Pilot models, restart the Pilot server with
each one (or run them on different ports) and list them all. Agreement is a consistency signal, not a correctness
score: the recorded model may have been wrong. Use the harness `run` command to compare what actually matters,
which is milestone turns and reward across lives.

## How thinking works

llama.cpp constrains the whole generation to the JSON-schema grammar, so a model can't reason and then answer in
a single call. When a brain's `Thinking` is on, the agent makes two calls: first an unconstrained call with thinking
enabled, asking the model to reason about the decision and the moves ahead, then a schema-constrained call (thinking
off) that sees that reasoning and returns the JSON. The Strategic brain does this by default on llama.cpp, with a
4,096-token budget (`Brains:Strategic:MaxTokens`). The Pilot and Reactive brains answer directly unless configured
otherwise. Ollama handles thinking and the schema together in one call.

The Pilot's long system prompt is the same every turn, so llama-server's prompt cache reuses it and only processes
the new game state. Keep `--parallel` at least the number of agents, so each agent keeps its own cached slot.

## Verifying on your GPU

With both servers running, the opt-in live tests exercise the real Pilot, the strategist's two-pass deliberation,
and the lesson distiller, and print timings:

```powershell
$env:LIVE_LLAMA_PILOT = "http://127.0.0.1:8081/"; $env:LIVE_LLAMA_STRATEGIC = "http://127.0.0.1:8082/"
dotnet test LLMSurvivingProxyAgent\LLMSurvivingProxyAgent.TradeWars.Tests --filter LiveLlamaCppTests `
  --logger "console;verbosity=detailed"
```
