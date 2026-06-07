using Argentini.Umbraco.Search.Qdrant.Extensions;

namespace Umbraco.Search.Qdrant.Tests;

public sealed class SemanticSearchHelpersTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(8, 5)]
    public void ApplyFieldWeight_ClampsWeightAndRepeatsText(int weight, int expectedCount)
    {
        var result = "syntax".ApplyFieldWeight(weight);

        Assert.Equal(expectedCount, result.Split("syntax").Length - 1);
    }

    [Fact]
    public void SplitMarkdownSections_KeepsHeadingsWithTheirBody()
    {
        var markdown = """
        Intro

        ## Filters
        Blur syntax

        ## Layout
        Grid syntax
        """;

        var sections = markdown.SplitMarkdownSections();

        Assert.Equal(3, sections.Count);
        Assert.Equal("Intro", sections[0]);
        Assert.StartsWith("## Filters", sections[1]);
        Assert.Contains("Blur syntax", sections[1]);
        Assert.StartsWith("## Layout", sections[2]);
    }

}
