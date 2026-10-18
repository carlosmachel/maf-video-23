# Ep. 23 — Harness Agent with background sub-agents

> Part of the **Microsoft Agent Framework** video series on [AzureBrasil.Cloud](https://azurebrasil.cloud) (PT-BR). All episodes: [maf-azurebrasil-hub](https://carlosmachel.github.io/maf-azurebrasil-hub/)

A **stock price researcher** built with `Microsoft.Agents.AI.Harness`. The parent agent receives a list of tickers, starts one **background web-search sub-agent per ticker** concurrently, waits for all of them and returns a summary table.

## How it works

See [diagram.md](diagram.md) for the full flow.

```
User (BAC, MSFT, BA)
  └─ StockPriceResearcher (parent)
       1. GetCurrentDateTime()
       2. fan-out → WebSearchAgent × N   (background, concurrent)
       3. await all → aggregate → markdown table
       4. clear completed tasks
```

## What it shows

- `AsHarnessAgent(...)` with context-window and output-token budgets
- Turning off harness features a sub-agent doesn't need (todo list, agent mode, file memory, file access, tool approval) to keep it focused
- Built-in hosted web search tool
- Fan-out / fan-in with background tasks driven by the instructions
- Retry policy on the `AIProjectClient`

## Stack

.NET 10 · `Microsoft.Agents.AI.Harness` · `Microsoft.Agents.AI.Foundry` · Microsoft Foundry (Responses API)

## Run

```bash
az login
cp src/Harness/.env.example src/Harness/.env   # fill in the endpoint
dotnet run --project src/Harness
```
