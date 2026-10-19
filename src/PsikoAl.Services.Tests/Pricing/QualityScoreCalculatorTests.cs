using PsikoAl.Services.Pricing;

namespace PsikoAl.Services.Tests.Pricing;

// Sözleşme: docs/REVIZE-2.md §1.1 (başlangıç ağırlıkları, budget_min 500 TL).
public sealed class QualityScoreCalculatorTests
{
    private const int BudgetMinTry = 500;

    [Fact]
    public void Listing_meeting_every_full_condition_scores_one()
    {
        var result = Calculate(new QualityInput(new string('b', 30), new string('a', 400), 2000m, "Ankara", "yüz_yüze_online", 2));

        Assert.Equal(1m, result.Score);
        Assert.All(
            new[] { result.Description, result.Title, result.Budget, result.Location, result.SessionBoth, result.Specialization },
            component => Assert.Equal(1m, component));
    }

    [Fact]
    public void Minimal_listing_scores_zero()
    {
        var result = Calculate(new QualityInput("0123456789", string.Empty, 500m, null, "online", 0));

        Assert.Equal(0m, result.Score);
    }

    // açıklama 200/400 → 0,5 · başlık 20 (10→30) → 0,5 · bütçe 1250 (500→2000) → 0,5
    // şehir → 1 · online → 0 · tek alan → 0,5
    // 0,30×0,5 + 0,10×0,5 + 0,25×0,5 + 0,15×1 + 0,10×0 + 0,10×0,5 = 0,525
    [Fact]
    public void Partial_components_are_linear_and_weighted()
    {
        var result = Calculate(new QualityInput(new string('b', 20), new string('a', 200), 1250m, "İzmir", "online", 1));

        Assert.Equal(0.5m, result.Description);
        Assert.Equal(0.5m, result.Title);
        Assert.Equal(0.5m, result.Budget);
        Assert.Equal(1m, result.Location);
        Assert.Equal(0m, result.SessionBoth);
        Assert.Equal(0.5m, result.Specialization);
        Assert.Equal(0.525m, result.Score);
    }

    [Fact]
    public void Description_length_ignores_surrounding_whitespace()
    {
        var padded = Calculate(new QualityInput(null, "   " + new string('a', 100) + "   ", 0m, null, null, 0));

        Assert.Equal(0.25m, padded.Description);
    }

    [Fact]
    public void Components_above_the_threshold_are_capped_at_one()
    {
        var result = Calculate(new QualityInput(new string('b', 100), new string('a', 2000), 100_000m, "Bursa", "yüz_yüze_online", 7));

        Assert.Equal(1m, result.Score);
    }

    [Fact]
    public void Budget_below_the_period_minimum_scores_zero()
        => Assert.Equal(0m, Calculate(new QualityInput(null, null, 100m, null, null, 0)).Budget);

    [Fact]
    public void Weights_are_normalized_so_scaling_them_does_not_change_the_score()
    {
        var input = new QualityInput(new string('b', 20), new string('a', 200), 1250m, "İzmir", "online", 1);
        var scaled = QualityWeights.Default with
        {
            Description = 30,
            Title = 10,
            Budget = 25,
            Location = 15,
            SessionBoth = 10,
            Specialization = 10,
        };

        Assert.Equal(Calculate(input).Score, QualityScoreCalculator.Calculate(input, scaled, BudgetMinTry).Score);
    }

    [Fact]
    public void Thresholds_come_from_the_period()
    {
        var weights = QualityWeights.Default with { DescriptionFullChars = 100, TitleFullChars = 20, BudgetFullMultiplier = 2 };

        var result = QualityScoreCalculator.Calculate(
            new QualityInput(new string('b', 15), new string('a', 50), 750m, null, null, 0),
            weights,
            BudgetMinTry);

        Assert.Equal(0.5m, result.Description);
        Assert.Equal(0.5m, result.Title);
        Assert.Equal(0.5m, result.Budget);
    }

    [Fact]
    public void All_zero_weights_score_zero_instead_of_dividing_by_zero()
    {
        var weights = QualityWeights.Default with { Description = 0, Title = 0, Budget = 0, Location = 0, SessionBoth = 0, Specialization = 0 };

        Assert.Equal(0m, QualityScoreCalculator.Calculate(new QualityInput("x", "y", 1m, "z", "online", 2), weights, BudgetMinTry).Score);
    }

    [Fact]
    public void Score_is_rounded_to_three_decimals()
    {
        // açıklama 133/400 = 0,3325 → 0,30 × 0,3325 = 0,09975 → 0,100
        var result = Calculate(new QualityInput(null, new string('a', 133), 0m, null, null, 0));

        Assert.Equal(0.100m, result.Score);
    }

    [Fact]
    public void Weights_round_trip_through_the_period_json()
    {
        var json = QualityWeights.Default.ToJson();

        Assert.Equal(QualityWeights.Default, QualityWeights.Parse(json));
        Assert.Contains("\"descriptionFullChars\":400", json, StringComparison.Ordinal);
        Assert.Contains("\"sessionBoth\":0.10", json, StringComparison.Ordinal);
    }

    private static QualityResult Calculate(QualityInput input)
        => QualityScoreCalculator.Calculate(input, QualityWeights.Default, BudgetMinTry);
}
