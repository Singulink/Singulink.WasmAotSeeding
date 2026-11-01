namespace Singulink.WasmAotSeeding.Tests;

/// <summary>
/// The command line of the generator, which the targets write.
/// </summary>
[TestClass]
public class OptionsTests
{
    [TestMethod]
    public void When_Options_Follow_The_Command_Then_Each_Has_Its_Value()
    {
        var options = new Options(["verify", "--bitcode", string.Empty, "--llvm-nm", "C:\\llvm bin\\llvm-nm.exe"]);

        options["bitcode"].ShouldBe(string.Empty);
        options["llvm-nm"].ShouldBe("C:\\llvm bin\\llvm-nm.exe");
    }

    [TestMethod]
    public void When_An_Option_Is_Missing_Then_Reading_It_Throws()
    {
        var options = new Options(["generate", "--docs", "https://docs.example"]);

        Should.Throw<OptionsException>(() => options["issues"]).Message.ShouldBe("Missing --issues.");
    }

    [TestMethod]
    [DataRow("generate", "docs", DisplayName = "An option without its leading dashes")]
    [DataRow("generate", "--docs", DisplayName = "An option without a value")]
    public void When_The_Arguments_Are_Not_Pairs_Of_Options_And_Values_Then_They_Are_Rejected(string command, string argument)
    {
        Should.Throw<OptionsException>(() => new Options([command, argument])).Message.ShouldBe($"Unexpected argument: {argument}");
    }
}
