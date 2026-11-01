# Singulink.WasmAotSeeding

[![Chat on Discord](https://img.shields.io/discord/906246067773923490)](https://discord.gg/EkQhJFsBu6)
[![View nuget packages](https://img.shields.io/nuget/v/Singulink.WasmAotSeeding.svg)](https://www.nuget.org/packages/Singulink.WasmAotSeeding/)
[![Build and Test](https://github.com/Singulink/Singulink.WasmAotSeeding/workflows/build%20and%20test/badge.svg)](https://github.com/Singulink/Singulink.WasmAotSeeding/actions?query=workflow%3A%22build+and+test%22)

**Singulink.WasmAotSeeding** keeps WebAssembly apps that are AOT-compiled with an AOT profile from crashing on methods that the profile doesn't list. Add the package to the app project, and its profile-guided AOT builds also compile the small pieces of code that the .NET runtime needs to run those methods in its interpreter, and check that the build has them.

It applies to WebAssembly apps on the Mono runtime: projects with the `browser-wasm` runtime identifier, as those of the .NET WebAssembly SDK (`Microsoft.NET.Sdk.WebAssembly`) and Blazor are, and projects of Uno Platform's `browserwasm` target platform. It brings the AOT wrapper seeding that [unoplatform/Uno.Wasm.Bootstrap#1117](https://github.com/unoplatform/Uno.Wasm.Bootstrap/pull/1117) proposes to add to Uno.Wasm.Bootstrap to apps whose bootstrapper doesn't have it. Once the app's bootstrapper has it, the package turns itself off and says so in the build (`AOTSEED007`), and it can be removed. It was verified on Windows with an Uno Platform app, with .NET 10.0.12 and 10.0.14.

### About Singulink

We are a small team of engineers and designers dedicated to building beautiful, functional and well-engineered software solutions. We offer very competitive rates as well as fixed-price contracts and welcome inquiries to discuss any custom development / project support needs you may have.

This package is part of our **Singulink Libraries** collection. Visit https://github.com/Singulink to see our full list of publicly available libraries and other open-source projects.

## The Problem

With an AOT profile, the AOT compiler only compiles the methods that the profile lists, and the other methods run in the .NET runtime's interpreter. To go from compiled code to an interpreted method, the runtime needs a small piece of code compiled ahead of time for the method's signature, an interpreter entry wrapper, as WebAssembly can't compile code while the app runs. The AOT compiler only emits these wrappers for the signatures that compiled code calls directly. The runtime also reaches interpreted methods in other ways: through interface calls, through delegates, and through the virtual methods of generic types. When it reaches one of them without its wrapper, the app crashes: for a method with more than 8 parameters, the runtime stops with `* Assertion at .../interp.c:3420, condition '<disabled>' not met` in the browser's console.

This can happen to any method that the AOT profile doesn't list: one that didn't run while the profile was recorded, or one that was added or changed since. The names of the methods that the C# compiler generates, for lambdas, async methods and iterators, change whenever the code around them changes. And a method doesn't have to be called to cause the crash: the first interface call to a class can crash on another method of the class, whose signature no compiled code calls.

The package makes the AOT compiler emit the wrappers of every signature that the runtime may need without a compiled call that names it, and fails the build if one of them is missing. The app's profile is unchanged, so it still decides which methods are compiled for speed.

## Installation

Add the package to the WebAssembly app project:

```xml
<PackageReference Include="Singulink.WasmAotSeeding" Version="1.0.0" PrivateAssets="all" />
```

It only acts in projects that build for WebAssembly, and in their builds that AOT-compile with an AOT profile on the Mono runtime: `RunAOTCompilation` is `true` and `WasmAotProfilePath` is set. A project builds for WebAssembly when its runtime identifier is `browser-wasm`, as the .NET WebAssembly SDK and Blazor set it, or its target platform is Uno.Sdk's `browserwasm` (`net10.0-browserwasm`, and the same platform of later .NET versions). Uno.Sdk sets `WasmAotProfilePath` when the project has an `aot.profile`, and `RunAOTCompilation` when `WasmShellMonoRuntimeExecutionMode` is `InterpreterAndAOT`. The package does nothing in other builds and in the project's other target frameworks, so a project that targets several can reference it for all of them, or only for `browserwasm`:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'browserwasm'">
  <PackageReference Include="Singulink.WasmAotSeeding" Version="1.0.0" PrivateAssets="all" />
</ItemGroup>
```

With central package management, the `PackageReference` has no `Version` (NuGet fails with `NU1008` otherwise), and `Directory.Packages.props` has the version: `<PackageVersion Include="Singulink.WasmAotSeeding" Version="1.0.0" />`.

The build runs the package's generator with the `dotnet` of the .NET SDK that builds the app, or with the `dotnet` on the `PATH` when the build doesn't say which SDK it is. The generator needs a .NET 10 or later runtime; the .NET SDKs that build for WebAssembly on .NET 10 or later have one. The package doesn't need a particular Uno.Wasm.Bootstrap. It requires AOT deduplication (`WasmShellEnableAOTDeduplication`, on by default), and warns with `AOTSEED004` when it's off.

## Settings

| Property | Default | Meaning |
|---|---|---|
| `WasmAotWrapperSeeding` | `true` | `false` turns the seeding and its check off. |
| `WasmAotWrapperSeedingCheck` | `true` | `false` turns the check off; the seeding still applies. |
| `WasmAotWrapperSeedingAssemblyName` | `AotWrapperSeeds` | Name of the seed assembly. |

The package's files are in `obj/<configuration>/<target framework>/wasm-aot-seeding/`: the seed assembly, its profile, `<name>.wrappers.txt`, which lists each expected wrapper with a method that needs it, a stamp, the list of AOT inputs, the list of the code compiled with the seeds, and the messages of the generation and the check. The seed assembly isn't published. `dotnet clean` removes the folder (`dotnet clean -c Release` for a Release publish). Don't publish the same project twice at once: the two publishes would share these files.

## Messages

| Code | Kind | Meaning |
|---|---|---|
| `AOTSEED001` | Error | Wrappers that the seeding expected are missing from the build, so the app may still crash when it reaches the methods that the errors name. The build lists up to 50, then counts the others. A .NET runtime update can cause it (see "Maintenance"): please report it. Setting `WasmAotWrapperSeedingCheck` to `false` publishes anyway, with the seeding. |
| `AOTSEED002` | Error | The check couldn't read the compiled code (`llvm-nm` is missing, fails or takes more than 5 minutes, or the compiled code or the expected wrappers can't be read), or the checker didn't run or didn't finish; the message says why. `WasmAotWrapperSeedingCheck=false` skips the check. |
| `AOTSEED003` | Warning | The seeds couldn't be generated (the message says why, and a normal build log has the stack trace), or the .NET SDK didn't run the targets that the seeding hooks (see "Maintenance"). The build continues without the seeding, so the app can still crash. Please report it. |
| `AOTSEED004` | Warning | AOT deduplication is off (`WasmShellEnableAOTDeduplication` or `WasmDedup` is `false`), so the build continues without the seeding. Turn deduplication back on, or set `WasmAotWrapperSeeding` to `false` to remove the warning. |
| `AOTSEED005` | Warning | A method that the seeding can't protect (see "What Isn't Covered"): the warning names it, the parameter and its type, or the struct and a method that takes it. Add `AOTSEED005` to `NoWarn` to remove it. |
| `AOTSEED006` | Warning | The generator stopped following large or recursive generic code at its bounds, so the app can still crash in the code that the warning names. Run that code while recording the AOT profile, so that it's compiled. Add `AOTSEED006` to `NoWarn` to remove it. |
| `AOTSEED007` | Warning | The app's Uno.Wasm.Bootstrap has its own seeding, so the package is off: remove its `PackageReference` (see "Removing It"). |
| `AOTSEED008` | Error | The generator didn't run or didn't finish: `dotnet` wasn't found or couldn't start it (no .NET 10 or later runtime was found, or the package's files are incomplete), or the generator stopped, which the messages before the error say. `WasmAotWrapperSeeding=false` publishes without the seeding. |

The codes match those of the bootstrapper's own seeding, five apart: `AOTSEED001` is its `UNOWASM006`, and so on to `AOTSEED006` and `UNOWASM011`. `AOTSEED007` and `AOTSEED008` are the package's own.

`AOTSEED005` and `AOTSEED006` come from the generation, and a publish without changes doesn't generate the seeds again, so it doesn't repeat them. A normal build log (`-v:n`) also lists what the generation skipped (inputs, types, methods or seeds that couldn't be read or written), the seeds whose wrapper names it couldn't predict, which are seeded but not checked, and the methods it couldn't check for `AOTSEED005`.

## Cost

Measured on an Uno Platform app with 131 AOT-compiled assemblies and .NET 10.0.14: the generation takes about 3 seconds for about 4,150 seeds, with 1,763 `gsharedvt_in_sig` and 2 `interp_in` wrappers expected, and runs again only when an AOT input changes. The check takes about 0.6 seconds, on every publish. The seed methods compile to almost nothing, and the wrappers they add make `dotnet.native.wasm` about 0.5% larger, and less than that once compressed.

## What Isn't Covered

- Code that only appears at run time is partly covered: for classes created through reflection, as dependency injection containers do, and for generic types created with `MakeGenericType`, only the interfaces that compiled code calls are covered; delegates created with `Delegate.CreateDelegate` aren't.
- The code of generic classes and structs, and of the classes and structs nested in them, that implements generic methods (overrides of generic virtual methods, implementations of generic interface methods and of generic static abstract members). Their instances' signatures are seeded, but the generic instances that their bodies create only where other code names them. Implementations whose name differs from the method they implement (VB's `Implements`, an IL `.override`) aren't followed either.
- Methods whose parameters include a `TypedReference`, an `ArgIterator` or a `RuntimeArgumentHandle`.
- Custom modifiers: the runtime keeps them in the wrapper signatures of function pointers (the calling convention of `delegate* unmanaged[SuppressGCTransition]<...>`, for example) and of `void` returns (the `modreq` of `init` accessors), but the seeds don't, and the check can't tell, as the wrapper names leave them out. It only matters above 8 parameters or for code compiled once for any value type argument.
- The AOT compiler escapes non-ASCII bytes in type names according to the build machine's locale. With a single-byte or double-byte locale, the check can fail with `AOTSEED001` for signatures with such types; UTF-8 and C locales are fine.
- Builds without an AOT profile (full AOT compiles every method), builds that don't AOT-compile, CoreCLR on WebAssembly (`UseMonoRuntime=false`), where none of this applies, and projects that don't build for WebAssembly (neither the `browser-wasm` runtime identifier nor the `browserwasm` target platform).

### Methods the Seeding Can't Protect

Some methods can't be protected, whatever the seeding does, and the generator warns about them with `AOTSEED005`:

- A method one of whose parameters is a struct with an explicit layout or many fields, such as `Matrix4x4`, or a function pointer, and has a default value or an `[Optional]`, `[In]`, `[Out]` or `[MarshalAs]` attribute. Unlike other structs, the runtime passes such a parameter as it is between compiled and interpreted code, along with what the default value or attribute adds to it, and then can't find the wrapper that does it. Compiled code that calls the method crashes the app when it makes the call or, with more than 8 parameters, as soon as that code starts running; with more than 8 parameters, a virtual method also breaks interface calls to the other methods of its class. Listing the method in the AOT profile only moves the crash to the interpreted code that calls it. Remove the default value or the attribute, or pass the parameter with `in`.
- A struct that contains itself through a field such as a `ReadOnlyMemory<T>` or an `ArraySegment<T>` of its own type, as `struct Node { ReadOnlyMemory<Node> Children; }` does, or a generic struct with more than 15 type arguments. The runtime can't make the wrapper of a method that takes or returns it, or a struct that holds it, by value, and the AOT compiler fails on such seeds, which would fail the build, so they are left out. Recording such calls in the profile doesn't help either. Pass the struct with `in`, hold its items in a class or an array, or give it at most 15 type arguments.

## Removing It

Once the app's Uno.Wasm.Bootstrap has its own seeding, the build warns with `AOTSEED007` and the package does nothing. Run `dotnet clean` with the configuration of the app's publishes first (`dotnet clean -c Release`, for example): the package's targets remove its files in `obj/<configuration>/<target framework>/wasm-aot-seeding/`, which stay otherwise until they're deleted. Then remove its `PackageReference`; move any `WasmAotWrapperSeeding` or `WasmAotWrapperSeedingCheck` setting to `WasmShellEnableAOTWrapperSeeding` or `WasmShellEnableAOTWrapperSeedingCheck`, and any `AOTSEED005` or `AOTSEED006` in `NoWarn` to `UNOWASM010` or `UNOWASM011`; and drop any `WasmAotWrapperSeedingAssemblyName` setting.

The package finds the bootstrapper's own seeding by the `WasmShellEnableAOTWrapperSeeding` property that its targets define, by the property that its generation sets when it runs first (`_UnoAOTWrapperSeedingRan`), and by its seed assembly, `Uno.Wasm.AotWrapperSeeds`, among the AOT inputs, so a build is never seeded twice. These are the names of [unoplatform/Uno.Wasm.Bootstrap#1117](https://github.com/unoplatform/Uno.Wasm.Bootstrap/pull/1117) at `924a8dba50`, which is still open. A project that sets `WasmShellEnableAOTWrapperSeeding` itself turns the package off too.

Mono on WebAssembly only gets servicing fixes: .NET 11 has experimental CoreCLR WebAssembly support, and CoreCLR becomes the standard in .NET 12 ([dotnet/runtime#134337](https://github.com/dotnet/runtime/issues/134337)), where none of this applies.

## How It Works

With an AOT profile (`WasmAotProfilePath`), the Mono AOT compiler runs in `profile-only` mode: it only compiles the methods the profile lists, and every other method stays interpreted. In llvmonly mode, which browser-wasm uses, the runtime can't JIT anything. To enter the interpreter from compiled code, it needs an interpreter entry wrapper for the method's signature, compiled ahead of time: a `gsharedvt_in_sig` wrapper for up to 8 parameters, an `interp_in` wrapper above that. The compiler only emits these wrappers for the signatures that compiled code names at its call sites. When a `gsharedvt_in_sig` wrapper is missing, the runtime puts in a stub that fails if it's called. When an `interp_in` wrapper is missing, `interp_create_method_pointer_llvmonly` (`mini/interp/interp.c`) aborts the runtime, which release runtimes print as `* Assertion at .../interp.c:3420, condition '<disabled>' not met`.

Several paths reach interpreted methods without a compiled call site that names their signature:

- **IMT slots.** The first interface call through an IMT slot builds that slot. `mini_llvmonly_get_imt_trampoline` (`mini/llvmonly-runtime.c`) then resolves *every* interface method of the class that hashes to it, and there are only 19 slots. If one of those siblings isn't compiled and no compiled code names its signature, the call can abort, even though the method it calls is compiled.
- **Delegates.** When compiled code creates a delegate, `mini_llvmonly_init_delegate` resolves the target right away. That's the target's own signature, which no call site names.
- **Virtual methods of generic types.** Code compiled once for any value type argument (`gsharedvt`), which profile-guided builds have, names signatures with the generic parameters instead of the value types, so no call site names the signature of, for example, the `Compare (T, T)` override of a `Comparer<MyStruct>`.

Before the AOT compilation, the package's generator reads the assemblies about to be AOT-compiled with Mono.Cecil and collects the signatures of the methods that the runtime may enter the interpreter for without a compiled call site that names them:

- the virtual instance methods of every interface each type implements, of every interface definition, and of every interface instance the code names (the class that implements an interface can be created where the generator can't see it);
- every method loaded by `ldftn` or `ldvirtftn` (delegate targets);
- the instances of generic virtual methods that the code calls;
- the virtual methods declared with the generic parameters of their type, of generic instances and of their generic base types.

The generic instances include those that the code and these signatures name, over value types and reference types alike, and those that generic code constructs, read from its bodies with its generic arguments. That covers the generic methods and methods of generic types that closed code calls, and the methods of generic instances that the runtime reaches without a call that names them: virtual methods (overrides, interface and default implementations, the `MoveNext` methods of async methods and iterators), static constructors (static field initializers), parameterless constructors (which `new T()` runs), and the static methods that implement static abstract interface members. The implementations of generic methods (methods with type parameters of their own) in classes and structs that aren't generic are followed too, paired by name with the calls to the methods they override or implement. Types of different assemblies are told apart even when they have the same name, and signatures that the runtime passes alike are merged (`bool` with `byte`, `char` with `ushort`, enums with their underlying types, references with each other, generic structs that only differ in reference type arguments). Recursive generic code can make ever more or ever larger instances, so the generator follows no instance nested more than 8 deep (where the AOT compiler stops too), none whose generic arguments hold more than 64 types, at most 20,000 instances of one generic type or method, and at most 500,000 in all; `AOTSEED006` reports what it stopped following.

It writes a seed assembly with one target method per distinct signature (`M0`, `M1`, ...) and seed methods (`S0`, `S1`, ...) that each call 16 targets directly, behind a branch that is always taken, so the calls never run. It also writes an AOT profile that lists only the seed methods. The seed assembly joins the AOT inputs, and its profile is added to `WasmAotProfilePath`. The compiler merges all the profiles it's given, so it compiles the seed methods and leaves the targets interpreted. A direct call from compiled code into an interpreted method of the same assembly may enter the interpreter, so the compiler records the callee's signature (`can_enter_interp` and `mini_emit_method_call_full` in `mini/calls.c`) and emits its wrapper (`compile_method` in `mini/aot-compiler.c`). It still converts the calls behind the branch, which records their signatures, then removes them as dead code, so the seed methods compile to almost nothing. With AOT deduplication (`WasmDedup`, on by default), the wrappers go to the `aot-instances` module, where the runtime looks them up by signature (`find_aot_method` in `mini/aot-runtime.c`); without it, they would only be in the seed assembly's module, which the runtime never loads, so the seeding requires it. Nothing references the seed assembly and it isn't published.

After the AOT compilation, a check lists the functions of `aot-instances.dll.bc` with `llvm-nm` and fails the build if an expected `gsharedvt_in_sig` or `interp_in` wrapper is missing. The expected names come from the generator's reproduction of the runtime's signature normalization and name mangling (`WrapperSignature.cs`), so the check also catches a runtime update that changes how wrappers are generated or named.

The generator runs with `dotnet exec`, in a process of its own, so no build node or IDE keeps it loaded. It writes its warnings and errors to files, which the build logs, so that no other line of its output can pass for one, and tells how it ended by its exit code. Publishing again without changes doesn't generate the seeds again, nor make the AOT compiler compile anything again: the generator writes its files only when their content changes, and identical inputs give identical files. The generation runs again only when an AOT input, the list of them or the package's files are newer than its stamp, when one of its files is gone, or when the package version or the seed assembly's name (`WasmAotWrapperSeedingAssemblyName`) changes, which the stamp's name holds. The check runs on every AOT compilation. When the seeding stops applying to an AOT compilation after it applied to one (the generation failed, the seeding or deduplication was turned off, or the bootstrapper's own seeding took over), the build deletes the `aot-instances` code that was compiled with the seeds, so that the AOT compiler compiles it again without them. Builds that don't AOT-compile, such as the Release builds of Uno.Sdk's apps with `InterpreterAndAOT`, which build native code without AOT-compiling it, leave that code alone, so the next publish doesn't compile everything again.

## Maintenance

**Keeping it in sync with the bootstrapper.** The files of `Source/Singulink.WasmAotSeeding/Upstream/` are copies of `src/Uno.Wasm.Bootstrap/AOTWrapperSeeding/*.cs` from [unoplatform/Uno.Wasm.Bootstrap#1117](https://github.com/unoplatform/Uno.Wasm.Bootstrap/pull/1117), at its head `924a8dba50`, with spaces for tabs, the namespace `Singulink.WasmAotSeeding` and no license headers. Uno.Wasm.Bootstrap is under the Apache 2.0 license: syncing changes that others made there brings in code under that license, which then needs its license notice. Their `.editorconfig` turns off the style rules that would change many of their lines. `GenerateCommand.cs` and `VerifyCommand.cs` do what the bootstrapper's `GenerateAOTWrapperSeedsTask` and `ValidateAOTWrapperSeedsTask` do, and `build/Singulink.WasmAotSeeding.targets` what its `_UnoGenerateAOTWrapperSeeds`, `_UnoForgetAOTWrapperSeeds`, `_UnoValidateAOTWrapperSeeds` and related targets do. Where the package differs from the bootstrapper's seeding:

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

**Runtime updates.** This relies on unsupported internals of the Mono runtime: the compiler's wrapper rules, the storage of the wrappers in `aot-instances` and their lookup by normalized signature, and the normalization and mangling that `WrapperSignature.cs` reproduces (`mini_get_underlying_signature`, `get_wrapper_shared_type_full` and `get_wrapper_shared_vtype` in `mini-generic-sharing.c`, `append_mangled_type` and `get_debug_sym` in `aot-compiler.c`, `mono_class_layout_fields` in `class-init.c`). The bootstrapper's seeding checked them against .NET 10.0.12 (dotnet/runtime at tag `v10.0.12`, `src/mono/mono/`), and the check passed on a large Uno Platform app with 10.0.12 and 10.0.14; .NET 11 RC2 only adds extended layouts, which the generator already handles. If the check fails with `AOTSEED001` after a runtime update, compare the names it reports with those that `llvm-nm --defined-only obj/Release/<target framework>/wasm/for-publish/aot-instances.dll.bc` lists, find the change in those functions, and mirror it in `WrapperSignature.cs` (and `PrimitiveTypes.cs` or `TypeNames.cs`), preferably in the bootstrapper first. Until then, `WasmAotWrapperSeedingCheck=false` publishes with the seeding.

**SDK names.** The seeding also relies on names of the .NET WebAssembly SDK (`WasmApp.Common.targets` and `BrowserWasmApp.targets`) that aren't public. If an SDK renames one, the build shows it:

| Name | Use | If an SDK renames it |
|---|---|---|
| `_WasmAotCompileApp` | The seeding runs before it, the check after it | The seeding doesn't run: `AOTSEED003` after `WasmNestedPublishApp` |
| `_WasmShouldAOT` | Whether the SDK AOT-compiles the build | The same |
| `_WasmAssembliesInternal` | The AOT inputs, which the seed assembly joins | `AOTSEED003`: `System.Private.CoreLib` isn't among the AOT inputs |
| `_BitcodeFile` | The code that the check reads, which the targets delete when the seeding stops applying | `AOTSEED002`: the AOT compilation didn't write `aot-instances.dll.bc` |
| `WasmNestedPublishApp` | The check that the seeding ran, after it | That check doesn't run, and nothing reports it |
| `WasmDedup` | Whether AOT deduplication is on | `AOTSEED004` |

The check that the seeding ran only covers publishes: a build that AOT-compiles (`RunAOTCompilationAfterBuild`) doesn't run `WasmNestedPublishApp`.

**Building and testing.** `dotnet test` in the repository's folder builds the package (`Source/Singulink.WasmAotSeeding/bin/<configuration>/`) and runs both test projects. `Singulink.WasmAotSeeding.Tests` runs the generator and the checker in the test process; most of its tests are those of the bootstrapper's seeding. `Singulink.WasmAotSeeding.PackageTests` builds mock apps that import the build files of the package, extracted, and stand in for the .NET WebAssembly SDK and `llvm-nm`, and a project that restores the package from a local feed, which needs nuget.org for reference packs that the .NET SDK doesn't have. Only a real AOT publish runs the AOT compiler, which is the only way to check the wrapper names.
