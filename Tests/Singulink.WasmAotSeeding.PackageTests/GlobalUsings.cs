global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Shouldly;

// Every test builds its own project in its own folder, and most of the time is spent waiting for builds.
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
