namespace MantosExtract.AddIn
{
    /// <summary>
    /// Human-visible build tag, logged at docker startup (%TEMP%\MantosExtract\docker.log) so
    /// we can instantly tell whether CorelDRAW loaded the new DLL or a cached old one.
    /// </summary>
    internal static class Build
    {
        public const string Tag = "0.3.1";
    }
}
