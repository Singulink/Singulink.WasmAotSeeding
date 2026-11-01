using System;
using System.Linq;

namespace Singulink.WasmAotSeeding.Tests;

/// <summary>
/// The AOT profile writer, which replaces the ProfileWriter of Mono.Profiler.Aot that Uno.Wasm.Bootstrap writes the seed profile with.
/// </summary>
[TestClass]
public class AotProfileWriterTests
{
    // What the ProfileWriter of Uno.Wasm.Bootstrap's copy of Mono.Profiler.Aot writes for the seed profile of AotWrapperSeeds with the seed methods
    // S0, S1 and S2, as the bootstrapper's AotWrapperSeedGenerator.WriteProfile has it write them: a ModuleRecord (ID 0, no MVID), a TypeRecord (ID 1)
    // and a MethodRecord per seed method (IDs from 2), with the signature void() and no parameters.
    private const string MonoProfilerAotProfile =
        "414F5450524F46494C450000010001000000000F000000416F745772617070657253656564730000000002010000001200000000FFFFFFFF15000000416F7457"
        + "72617070657253656564732E5365656473040200000001000000FFFFFFFF0000000002000000533006000000766F69642829040300000001000000FFFFFFFF00"
        + "00000002000000533106000000766F69642829040400000001000000FFFFFFFF0000000002000000533206000000766F696428290000000000";

    [TestMethod]
    public void When_Writing_The_Seed_Profile_Then_The_Bytes_Are_Those_Of_Mono_Profiler_Aot()
    {
        var writer = new AotProfileWriter();

        foreach (string method in new[] { "S0", "S1", "S2" })
            writer.AddMethod("AotWrapperSeeds", "AotWrapperSeeds.Seeds", method, 0, "void()");

        Convert.ToHexString(writer.ToArray()).ShouldBe(MonoProfilerAotProfile);
    }

    // One image record per assembly and one type record per class, which the methods of each class share.
    [TestMethod]
    public void When_Methods_Share_Assemblies_And_Classes_Then_Each_Is_Written_Once()
    {
        var writer = new AotProfileWriter();
        writer.AddMethod("Seeds", "Seeds.Seeds", "S0", 0, "void()");
        writer.AddMethod("Seeds", "Seeds.Seeds", "S1", 0, "void()");
        writer.AddMethod("Seeds", "Seeds.Other", "Take", 2, "void(int,long)");
        writer.AddMethod("Other", "Other.Seeds", "S0", 0, "void()");

        var profile = AotProfileReader.Read(writer.ToArray());

        profile.Modules.Select(m => m.Name).ShouldBe(["Seeds", "Other"]);
        profile.Modules.ShouldAllBe(m => m.Mvid.Length == 0);
        profile.Types.Select(t => $"{t.Module.Name}:{t.Name}").ShouldBe(["Seeds:Seeds.Seeds", "Seeds:Seeds.Other", "Other:Other.Seeds"]);
        profile.Methods.Select(m => $"{m.Type.Name}::{m.Name} {m.Signature} {m.ParamCount}").ShouldBe([
            "Seeds.Seeds::S0 void() 0",
            "Seeds.Seeds::S1 void() 0",
            "Seeds.Other::Take void(int,long) 2",
            "Other.Seeds::S0 void() 0",
        ]);
    }

    // The AOT compiler splits the class name at its last dot into namespace and name, and requires one.
    [TestMethod]
    public void When_A_Class_Has_No_Namespace_Then_Its_Name_Starts_With_A_Dot()
    {
        var writer = new AotProfileWriter();
        writer.AddMethod("Seeds", "Seeds", "S0", 0, "void()");

        AotProfileReader.Read(writer.ToArray()).Types.ShouldHaveSingleItem().Name.ShouldBe(".Seeds");
    }

    [TestMethod]
    public void When_There_Are_No_Methods_Then_The_Profile_Is_Empty()
    {
        var profile = AotProfileReader.Read(new AotProfileWriter().ToArray());

        profile.Modules.ShouldBeEmpty();
        profile.Methods.ShouldBeEmpty();
    }
}
