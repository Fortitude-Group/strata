using Strata.Core.Util;

namespace Strata.Core.Tests;

public sealed class VersionOrderEdgeCaseTests
{
    [Fact]
    public void Double_digit_minor_outranks_single_digit_minor()
    {
        Assert.True(VersionOrder.Compare("1.10.0", "1.9.0") > 0);
        Assert.True(VersionOrder.Compare("1.9.0", "1.10.0") < 0);
    }

    [Fact]
    public void Missing_trailing_zero_segment_compares_equal()
    {
        // "2.0" has no patch segment; it is treated as "2.0.0".
        Assert.Equal(0, VersionOrder.Compare("2.0", "2.0.0"));
    }

    [Fact]
    public void Openssl_style_letter_suffix_orders_after_the_bare_version()
    {
        Assert.True(VersionOrder.Compare("1.1.1k", "1.1.1") > 0);
        Assert.True(VersionOrder.Compare("1.1.1", "1.1.1k") < 0);
    }

    [Fact]
    public void Letter_suffixes_order_lexically_when_numeric_segments_are_equal()
    {
        Assert.True(VersionOrder.Compare("1.1.1k", "1.1.1a") > 0);
    }

    [Fact]
    public void Unknown_sentinel_compares_ordinally_rather_than_numerically()
    {
        Assert.Equal(0, VersionOrder.Compare("unknown", "unknown"));
        Assert.NotEqual(0, VersionOrder.Compare("unknown", "1.0.0"));
    }

    [Fact]
    public void Min_and_max_pick_the_correct_bound()
    {
        Assert.Equal("1.9.0", VersionOrder.Min("1.10.0", "1.9.0"));
        Assert.Equal("1.10.0", VersionOrder.Max("1.10.0", "1.9.0"));
    }

    [Fact]
    public void Equal_versions_are_reflexively_equal_via_min_and_max()
    {
        Assert.Equal("2.0.0", VersionOrder.Min("2.0.0", "2.0.0"));
        Assert.Equal("2.0.0", VersionOrder.Max("2.0.0", "2.0.0"));
    }
}
