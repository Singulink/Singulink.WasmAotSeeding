# Maintaining Singulink.WasmAotSeeding

This file is for working on the package itself. For how the package works, see [HOW-IT-WORKS.md](HOW-IT-WORKS.md).

## Building and Testing

`dotnet test` in the repository's folder builds the package (`Source/Singulink.WasmAotSeeding/bin/<configuration>/`) and runs both test projects. `Singulink.WasmAotSeeding.Tests` runs the generator and the checker in the test process; most of its tests are those of the bootstrapper's seeding. `Singulink.WasmAotSeeding.PackageTests` builds mock apps that import the build files of the package, extracted, and stand in for the .NET WebAssembly SDK and `llvm-nm`, and a project that restores the package from a local feed, which needs nuget.org for reference packs that the .NET SDK doesn't have. Only a real AOT publish runs the AOT compiler, which is the only way to check the wrapper names.

## Generator Details

The generator runs with `dotnet exec`, in a process of its own, so no build node or IDE keeps it loaded. It writes its warnings and errors to files, which the build logs, so that no other line of its output can pass for one, and tells how it ended by its exit code.

The package's files are in `obj/<configuration>/<target framework>/wasm-aot-seeding/`: the seed assembly, its profile, `<name>.wrappers.txt`, which lists each expected wrapper with a method that needs it, a stamp, the list of AOT inputs, the list of the code compiled with the seeds, and the messages of the generation and the check.

## Keeping It in Sync with the Bootstrapper

The files of `Source/Singulink.WasmAotSeeding/Upstream/` are copies of `src/Uno.Wasm.Bootstrap/AOTWrapperSeeding/*.cs` from [unoplatform/Uno.Wasm.Bootstrap#1117](https://github.com/unoplatform/Uno.Wasm.Bootstrap/pull/1117), at its head `924a8dba50`, with spaces for tabs, the namespace `Singulink.WasmAotSeeding` and no license headers. Uno.Wasm.Bootstrap is under the Apache 2.0 license: syncing changes that others made there brings in code under that license, which then needs its license notice. Their `.editorconfig` turns off the style rules that would change many of their lines. `GenerateCommand.cs` and `VerifyCommand.cs` do what the bootstrapper's `GenerateAOTWrapperSeedsTask` and `ValidateAOTWrapperSeedsTask` do, and `build/Singulink.WasmAotSeeding.targets` what its `_UnoGenerateAOTWrapperSeeds`, `_UnoForgetAOTWrapperSeeds`, `_UnoValidateAOTWrapperSeeds` and related targets do. Where the package differs from the bootstrapper's seeding:

- `AotWrapperSeedGenerator.WriteProfile` writes the profile with `AotProfileWriter`, which writes the same bytes as the bootstrapper's copy of Mono.Profiler.Aot when there's at least one seed method.
- `SignatureCollector.QueueConstructedType` doesn't pass the element type of a function pointer, which is `null`, to `CecilTypes.StripModifiers`, whose parameter isn't nullable. It gives the same seeds; it only matters to the null checks of Debug builds.
- The generation and the check run in a `dotnet exec` process rather than as MSBuild tasks. The generator writes its warnings and errors to files, which the targets log, and tells how it ended by its exit code: 10 when the generation failed, after `AOTSEED003`; 11 when wrappers are missing, after `AOTSEED001` errors; 12 when the check couldn't be made, after `AOTSEED002`. Any other code means that it didn't run or didn't finish, which the targets report with `AOTSEED008` for the generation and `AOTSEED002` for the check.
- `AOTSEED007`: the package steps aside for the bootstrapper's own seeding.
- The package only acts in projects that build for WebAssembly: those with the `browser-wasm` runtime identifier or the `browserwasm` target platform.
- The generator runs with the `dotnet` of `DOTNET_HOST_PATH`, or else the one in the SDK's `NetCoreRoot`, as the SDK's Razor targets do, or else the one on the `PATH`.
- The list of AOT inputs is written to a file, which passes them to the generator and is an input of the generation. That's cheap hardening against an assembly leaving the AOT inputs while all the others stay older than the stamp, which can't happen on .NET 10, whose trimmer writes every AOT input again on each run.
- The seed assembly's name can be set (`WasmAotWrapperSeedingAssemblyName`, where the bootstrapper's is always `Uno.Wasm.AotWrapperSeeds`), and the files are in a folder of their own, `wasm-aot-seeding`. The stamp is named after the seed assembly and the package version, which `build/Singulink.WasmAotSeeding.props` sets when the package is packed, where the bootstrapper's is named after its commit.
- The messages use the `AOTSEED` codes and point to this repository.
- The check finds `llvm-nm` through `EmscriptenUpstreamBinPath`, as the bootstrapper does, or else through `_WasmLLVMPathForAOT`, or else on the `PATH`.
- The check that the SDK ran the seeding (`AOTSEED003`) doesn't run when the app has the bootstrapper's own seeding, which reports it itself.

To compare with a later version, `diff -r -w <bootstrapper>/src/Uno.Wasm.Bootstrap/AOTWrapperSeeding Source/Singulink.WasmAotSeeding/Upstream` (Git Bash has `diff`) shows the license headers, the namespace, the local changes above and the `.editorconfig`. After a sync, or any other change to the generator or the targets, change the package version.

## Runtime Updates

The seeding relies on unsupported internals of the Mono runtime: the compiler's wrapper rules, the storage of the wrappers in `aot-instances` and their lookup by normalized signature, and the normalization and mangling that `WrapperSignature.cs` reproduces (`mini_get_underlying_signature`, `get_wrapper_shared_type_full` and `get_wrapper_shared_vtype` in `mini-generic-sharing.c`, `append_mangled_type` and `get_debug_sym` in `aot-compiler.c`, `mono_class_layout_fields` in `class-init.c`). The bootstrapper's seeding checked them against .NET 10.0.12 (dotnet/runtime at tag `v10.0.12`, `src/mono/mono/`), and the check passed on a large Uno Platform app with 10.0.12 and 10.0.14; .NET 11 RC2 only adds extended layouts, which the generator already handles. If the check fails with `AOTSEED001` after a runtime update, compare the names it reports with those that `llvm-nm --defined-only obj/Release/<target framework>/wasm/for-publish/aot-instances.dll.bc` lists, find the change in those functions, and mirror it in `WrapperSignature.cs` (and `PrimitiveTypes.cs` or `TypeNames.cs`), preferably in the bootstrapper first. Until then, `WasmAotWrapperSeedingCheck=false` publishes with the seeding.

## SDK Names

The seeding also relies on names of the .NET WebAssembly SDK (`WasmApp.Common.targets` and `BrowserWasmApp.targets`) that aren't public. If an SDK renames one, the build shows it:

| Name | Use | If an SDK renames it |
|---|---|---|
| `_WasmAotCompileApp` | The seeding runs before it, the check after it | The seeding doesn't run: `AOTSEED003` after `WasmNestedPublishApp` |
| `_WasmShouldAOT` | Whether the SDK AOT-compiles the build | The same |
| `_WasmAssembliesInternal` | The AOT inputs, which the seed assembly joins | `AOTSEED003`: `System.Private.CoreLib` isn't among the AOT inputs |
| `_BitcodeFile` | The code that the check reads, which the targets delete when the seeding stops applying | `AOTSEED002`: the AOT compilation didn't write `aot-instances.dll.bc` |
| `WasmNestedPublishApp` | The check that the seeding ran, after it | That check doesn't run, and nothing reports it |
| `WasmDedup` | Whether AOT deduplication is on | `AOTSEED004` |

The check that the seeding ran only covers publishes: a build that AOT-compiles (`RunAOTCompilationAfterBuild`) doesn't run `WasmNestedPublishApp`.
