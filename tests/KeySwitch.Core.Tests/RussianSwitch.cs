using Xunit;

namespace KeySwitch.Core.Tests;

// Serializes the test classes that change the static TypoCorrector.RussianEnabled switch.
[CollectionDefinition(Name)]
public sealed class RussianSwitch
{
    public const string Name = "TypoCorrector.RussianEnabled";
}
