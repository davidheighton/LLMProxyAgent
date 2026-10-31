# twxproxy 3.0 beta1

twxproxy 3.0 beta1 is the current C#/.NET rewrite of the classic TWX Proxy helper for Trade Wars 2002.

Current source version: `3.0 beta1`

The active code lives under `Source/` and includes:

- `TWXProxy`: shared Core runtime, compiler support, script execution, proxy logic, and database code
- `TWXC`: command-line compiler for `.ts -> .cts`
- `TWXD`: command-line decompiler for `.cts -> .ts`
- `TWXDecrypt`: standalone recovery CLI for `.inc` and `.cts` files
- `Source/Tools/twx-recover.js`: non-destructive legacy `.inc -> .ts` extractor
- `MTC`: Avalonia desktop client with an embedded proxy
- `TWXP`: Avalonia based multi threaded proxy

To recover a legacy encrypted include, write to a scratch path and omit `--force`
unless replacing an intentional prior output:

```powershell
node Source/Tools/twx-recover.js --input path/to/script.inc --output scratch/script.ts
```

The C# recovery CLI handles both formats and uses the TWXD decoder directly:

```powershell
dotnet run --project Source/TWXDecrypt/TWXDecrypt.csproj -- --input path/to/script.cts --output scratch/script.ts
```

Compiled `.cts` files are bytecode containers and must be decompiled with `TWXD`;
they are not processed by the legacy `.inc` extractor.

For build details, project layout, and tooling notes, see [`Source/README.md`](Source/README.md).
