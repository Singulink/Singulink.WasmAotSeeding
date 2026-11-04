# Singulink.WasmAotSeeding

[![Chat on Discord](https://img.shields.io/discord/906246067773923490)](https://discord.gg/EkQhJFsBu6)
[![View nuget packages](https://img.shields.io/nuget/v/Singulink.WasmAotSeeding.svg)](https://www.nuget.org/packages/Singulink.WasmAotSeeding/)
[![Build and Test](https://github.com/Singulink/Singulink.WasmAotSeeding/workflows/build%20and%20test/badge.svg)](https://github.com/Singulink/Singulink.WasmAotSeeding/actions?query=workflow%3A%22build+and+test%22)

**Singulink.WasmAotSeeding** keeps WebAssembly apps that are AOT-compiled with an AOT profile from crashing on methods that the profile doesn't list.

It applies to WebAssembly apps on the Mono runtime, including Blazor and Uno Platform apps. It was verified on Windows with an Uno Platform app, with .NET 10.0.12 and 10.0.14.

### About Singulink

We are a small team of engineers and designers dedicated to building beautiful, functional and well-engineered software solutions. We offer very competitive rates as well as fixed-price contracts and welcome inquiries to discuss any custom development / project support needs you may have.

This package is part of our **Singulink Libraries** collection. Visit https://github.com/Singulink to see our full list of publicly available libraries and other open-source projects.

## The Problem

With an AOT profile, only the methods that the profile lists are AOT-compiled. All other methods run in the .NET runtime's interpreter. To reach them from compiled code, the runtime needs small pieces of code that were compiled ahead of time, and a build only includes some of these. When the runtime reaches a method without its piece of code, the app crashes.

This can happen to any method that the profile doesn't list:

- A method that didn't run while the profile was recorded.
- A method that was added or changed since. This includes lambdas, async methods and iterators: the names that the C# compiler generates for them change whenever the code around them changes.

A method doesn't have to be called to cause the crash. The first interface call to a class can crash on another method of that class.

For a method with more than 8 parameters, the browser's console shows:

```
* Assertion at .../interp.c:3420, condition '<disabled>' not met
```

## What It Does

In profile-guided AOT builds, the package also compiles those small pieces of code and fails the build if one of them is missing. This is the *seeding* that its settings and messages refer to. The app's profile is unchanged, so it still decides which methods are compiled for speed.

The cost is small: on an Uno Platform app with 131 AOT-compiled assemblies, the package's build steps took a few seconds and `dotnet.native.wasm` grew by about 0.5%.

For how it works, what it costs and the full details, see [HOW-IT-WORKS.md](https://github.com/Singulink/Singulink.WasmAotSeeding/blob/main/HOW-IT-WORKS.md).

## Installation

Add the package to the WebAssembly app project:

```xml
<PackageReference Include="Singulink.WasmAotSeeding" Version="1.0.0" PrivateAssets="all" />
```

With central package management, the `PackageReference` has no `Version` (NuGet fails with `NU1008` otherwise), and `Directory.Packages.props` has it:

```xml
<PackageVersion Include="Singulink.WasmAotSeeding" Version="1.0.0" />
```

### When It Acts

The package only acts in WebAssembly builds on the Mono runtime that AOT-compile with an AOT profile: `RunAOTCompilation` is `true` and `WasmAotProfilePath` is set. Uno.Sdk sets these when the project has an `aot.profile` and `WasmShellMonoRuntimeExecutionMode` is `InterpreterAndAOT`.

It does nothing in other builds and in the project's other target frameworks, so a project that targets several can reference it for all of them.

### Requirements

- A .NET 10 or later runtime. The .NET SDKs that build for WebAssembly on .NET 10 or later have one.
- AOT deduplication (`WasmShellEnableAOTDeduplication`, on by default). The build warns with `AOTSEED004` when it's off.

## Settings

| Property | Default | Meaning |
|---|---|---|
| `WasmAotWrapperSeeding` | `true` | `false` turns the seeding and its check off. |
| `WasmAotWrapperSeedingCheck` | `true` | `false` turns the check off. The seeding still applies. |
| `WasmAotWrapperSeedingAssemblyName` | `AotWrapperSeeds` | Name of the seed assembly that the package generates. It isn't published. |

The package's files are in `obj/<configuration>/<target framework>/wasm-aot-seeding/`. `dotnet clean` removes the folder (`dotnet clean -c Release` for a Release publish).

Don't publish the same project twice at once: the two publishes would share these files.

## Messages

| Code | Kind | Meaning |
|---|---|---|
| `AOTSEED001` | Error | Code that the seeding expected is missing, so the app may still crash on the methods named. Please report it. `WasmAotWrapperSeedingCheck=false` publishes anyway. |
| `AOTSEED002` | Error | The check couldn't run or finish. The message says why. `WasmAotWrapperSeedingCheck=false` skips it. |
| `AOTSEED003` | Warning | The seeding couldn't be added, so the app can still crash. Please report it. |
| `AOTSEED004` | Warning | AOT deduplication is off, so the build has no seeding. Turn it back on, or set `WasmAotWrapperSeeding` to `false`. |
| `AOTSEED005` | Warning | The warning names a method that the seeding can't protect, and says how to change it. |
| `AOTSEED006` | Warning | Large or recursive generic code wasn't fully followed, so the code named can still crash. Running it while recording the AOT profile gets it compiled. |
| `AOTSEED007` | Warning | The app's Uno.Wasm.Bootstrap has its own seeding, so the package is off. See "Removing It". |
| `AOTSEED008` | Error | The package's generator didn't run or finish. `WasmAotWrapperSeeding=false` publishes without the seeding. |

Adding `AOTSEED005` or `AOTSEED006` to `NoWarn` removes that warning. Full details are in [Message Details](https://github.com/Singulink/Singulink.WasmAotSeeding/blob/main/HOW-IT-WORKS.md#message-details).

## What Isn't Covered

The package doesn't apply to builds without an AOT profile, builds that don't AOT-compile, CoreCLR on WebAssembly (`UseMonoRuntime=false`) or projects that don't build for WebAssembly.

A few kinds of method can't be protected, and the build warns about them with `AOTSEED005`. Code that only appears at run time, such as through reflection, and some generic code are only partly covered.

Full details are in [What Isn't Covered, in Full](https://github.com/Singulink/Singulink.WasmAotSeeding/blob/main/HOW-IT-WORKS.md#what-isnt-covered-in-full).

## Removing It

1. Run `dotnet clean` with the configuration of the app's publishes (`dotnet clean -c Release`, for example). This removes the package's files in `obj/<configuration>/<target framework>/wasm-aot-seeding/`, which stay otherwise until they're deleted.
2. Remove its `PackageReference`.

If the app's Uno.Wasm.Bootstrap has its own seeding, [Uno.Wasm.Bootstrap's Own Seeding](https://github.com/Singulink/Singulink.WasmAotSeeding/blob/main/HOW-IT-WORKS.md#unowasmbootstraps-own-seeding) lists the settings to change.
