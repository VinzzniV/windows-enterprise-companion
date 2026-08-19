# Wec.ContractGenerator

Generates the TypeScript wire declarations used by the frontend from all
`IActionHandler<TPayload, TResult>` implementations. Event payloads that do not
flow through an action handler opt in with `[BridgeContract]`.

```powershell
# Rewrite the committed declarations after a C# contract change
dotnet run --project tools/Wec.ContractGenerator --configuration Release

# Read-only drift check used by CI
dotnet run --project tools/Wec.ContractGenerator --configuration Release -- --check
```

The output is deterministic UTF-8/LF. Unsupported name collisions fail the run
instead of silently renaming a wire type. `frontend/src/shared/api-types.ts`
remains a small facade only for intentional, stable UI aliases.
