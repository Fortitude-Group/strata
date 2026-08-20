using Strata.Core.Model;

namespace Strata.Core.Tests;

public sealed class ModelInvariantTests
{
    [Fact]
    public void Component_without_evidence_is_forbidden()
    {
        // FR-014 / SC-007: the evidence invariant is enforced at construction.
        VersionResolution v = VersionResolution.OfExact("1.0.0", []);
        Assert.Throws<ArgumentException>(() =>
            new IdentifiedComponent("zlib", v, 0.9, evidence: []));
    }

    [Fact]
    public void Exact_version_requires_a_value()
    {
        Assert.Throws<ArgumentException>(() => VersionResolution.OfExact("", []));
    }

    [Fact]
    public void Range_display_uses_en_dash()
    {
        VersionResolution v = VersionResolution.OfRange("1.2.8", "1.2.11", []);
        Assert.Equal("1.2.8–1.2.11", v.Display);
    }
}
