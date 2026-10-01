using KeySwitch.Core;
using Xunit;

namespace KeySwitch.Core.Tests;

public sealed class PairContextTests
{
    [Theory]
    [InlineData("учится", "учиться", RealWordKinds.Tsya)]
    [InlineData("учиться", "учится", RealWordKinds.Tsya)]
    [InlineData("напишете", "напишите", RealWordKinds.SecondPlural)]
    [InlineData("сделаите", "сделаете", RealWordKinds.SecondPlural)]
    [InlineData("стаей", "статей", RealWordKinds.OtherEdit)]
    [InlineData("Учится", "учиться", RealWordKinds.Tsya)]
    [InlineData("статей", "статей", RealWordKinds.None)]
    [InlineData("стаями", "статей", RealWordKinds.None)]
    public void ConfusionKinds(string typed, string intended, RealWordKinds kind) =>
        Assert.Equal(kind, TypoCorrector.Confusion(typed, intended));
}
