namespace Singulink.WasmAotSeeding;

/// <summary>
/// The exit codes of the generator, which the targets act on. They differ from those that dotnet exec and the runtime exit with when the generator
/// can't start or stops on an unhandled exception, which the targets report as the generator not having run or finished: AOTSEED008 for "generate"
/// and AOTSEED002 for "verify".
/// </summary>
internal static class ExitCodes
{
    public const int Success = 0;

    public const int UsageError = 2;

    /// <summary>"generate" couldn't write the files, after an AOTSEED003 warning. The targets then delete them and the stamp.</summary>
    public const int GenerationFailed = 10;

    /// <summary>"verify" found wrappers missing, after AOTSEED001 errors.</summary>
    public const int MissingWrappers = 11;

    /// <summary>"verify" couldn't make the check, after an AOTSEED002 error.</summary>
    public const int CheckFailed = 12;
}
