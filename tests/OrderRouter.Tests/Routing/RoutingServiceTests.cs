using OrderRouter.Core.Domain;
using OrderRouter.Core.Routing;
using static OrderRouter.Tests.TestSupport.Fixture;

namespace OrderRouter.Tests.Routing;

/// <summary>Each spec priority on its own, using small in-memory data.</summary>
public class RoutingServiceTests
{
    // ---- Priority 1: feasibility (products and eligibility) ----

    [Test]
    public void UnknownProductFailsWithItsCode()
    {
        var decision = Route(Order(false, "NOPE-1"), Local("SUP-1", 9m, "wheelchair"));

        Assert.That(decision.Feasible, Is.False);
        Assert.That(decision.Errors, Is.EqualTo(new[] { "Unknown product_code: NOPE-1." }));
        Assert.That(decision.Shipments, Is.Empty);
    }

    [Test]
    public void ProductCodeLookupIgnoresCaseAndReturnsTheCatalogCode()
    {
        var decision = Route(Order(false, "wc-1"), Local("SUP-1", 9m, "wheelchair"));

        Assert.That(decision.Shipments.Single().Items.Single().ProductCode, Is.EqualTo("WC-1"));
    }

    [Test]
    public void CategoryMatchingIgnoresCase()
    {
        // Product category "CPAP"; supplier lists "cpap".
        var decision = Route(Order(false, "CP-1"), Local("SUP-1", 9m, "cpap"));

        Assert.That(decision.Feasible, Is.True);
        Assert.That(decision.Shipments.Single().Items.Single().Category, Is.EqualTo("CPAP"));
    }

    [Test]
    public void SupplierWithoutTheCategoryIsNotEligible()
    {
        var decision = Route(Order(false, "WC-1"), Local("SUP-1", 10m, "cane"));

        Assert.That(decision.Errors, Is.EqualTo(new[] { "No eligible supplier for product_code WC-1 (category: wheelchair)." }));
    }

    [Test]
    public void EveryFailingItemIsListedInLineOrderWithNoPartialRouting()
    {
        var decision = Route(Order(false, "WC-1", "NOPE-1", "CN-1", "CP-1"), Local("SUP-1", 9m, "wheelchair"));

        Assert.That(decision.Feasible, Is.False);
        Assert.That(decision.Errors, Is.EqualTo(new[]
        {
            "Unknown product_code: NOPE-1.",
            "No eligible supplier for product_code CN-1 (category: cane).",
            "No eligible supplier for product_code CP-1 (category: CPAP).",
        }));
        Assert.That(decision.Shipments, Is.Empty);
    }

    [Test]
    public void RepeatedFailingLineIsReportedOnce()
    {
        var decision = Route(Order(false, "NOPE-1", "NOPE-1"), Local("SUP-1", 9m, "wheelchair"));

        Assert.That(decision.Errors, Is.EqualTo(new[] { "Unknown product_code: NOPE-1." }));
    }

    [Test, CancelAfter(5000)]
    public void HugeOrderFullOfUnknownCodesStaysFast()
    {
        var lines = Enumerable.Range(1, 100_000).Select(i => new OrderLine(i, $"NOPE-{i}", 1)).ToList();
        var order = new Order(null, Zip, false, lines);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var decision = Route(order, Local("SUP-1", 9m, "wheelchair"));

        Assert.That(decision.Errors, Has.Count.EqualTo(100_000));
        Assert.That(decision.Errors[0], Is.EqualTo("Unknown product_code: NOPE-1."));
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void NoSuppliersAtAllIsInfeasible()
    {
        Assert.That(Route(Order(true, "WC-1")).Feasible, Is.False);
    }

    // ---- Mail-order eligibility and local fulfillment ----

    [Test]
    public void WithoutMailOrderOnlyLocalSuppliersAreUsed()
    {
        var decision = Route(Order(false, "WC-1"),
            Mailer("SUP-1", 10m, "wheelchair"),
            Local("SUP-2", 5m, "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-2[WC-1:local]"));
    }

    [Test]
    public void WithoutMailOrderAMailerThatDoesNotServeTheZipIsNotEligible()
    {
        var decision = Route(Order(false, "WC-1"), Mailer("SUP-1", 10m, "wheelchair"));

        Assert.That(decision.Errors.Single(), Does.StartWith("No eligible supplier for product_code WC-1"));
    }

    [Test]
    public void WithMailOrderAMailerAnywhereIsEligible()
    {
        var decision = Route(Order(true, "WC-1"), Mailer("SUP-1", 8m, "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WC-1:mail]"));
    }

    [Test]
    public void WithMailOrderASupplierThatCannotMailAndIsNotLocalIsNotEligible()
    {
        var decision = Route(Order(true, "WC-1"), Elsewhere("SUP-1", 10m, "wheelchair"));

        Assert.That(decision.Feasible, Is.False);
    }

    [Test]
    public void WithMailOrderLocalSuppliersThatCannotMailAreStillEligible()
    {
        var decision = Route(Order(true, "WC-1"), Local("SUP-1", 7m, "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WC-1:local]"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SupplierThatServesTheZipAndCanMailIsLocal(bool mailOrder)
    {
        var decision = Route(Order(mailOrder, "WC-1"), LocalMailer("SUP-1", 7m, "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WC-1:local]"));
    }

    [Test]
    public void SupplierWithNoServiceZipsCanOnlyMail()
    {
        var mailOnly = Supplier("SUP-1", 9m, "", canMail: true, "wheelchair");

        Assert.That(Route(Order(false, "WC-1"), mailOnly).Feasible, Is.False);
        Assert.That(Describe(Route(Order(true, "WC-1"), mailOnly)), Is.EqualTo("SUP-1[WC-1:mail]"));
    }

    [Test]
    public void ZipMatchingUsesTheNumericValueWithLeadingZeros()
    {
        var boston = Supplier("SUP-1", 9m, "2130", canMail: false, "wheelchair"); // file value "2130" = 02130

        Assert.That(Route(OrderAt("02130", false, "WC-1"), boston).Feasible, Is.True);
        Assert.That(Route(OrderAt("21300", false, "WC-1"), boston).Feasible, Is.False);
    }

    [Test]
    public void EveryZipRangeDoesNotCoverZipsBelow100()
    {
        var everyZip = Supplier("SUP-1", 9m, "00100-99999", canMail: false, "wheelchair");

        Assert.That(Route(OrderAt("00099", false, "WC-1"), everyZip).Feasible, Is.False);
        Assert.That(Route(OrderAt("00100", false, "WC-1"), everyZip).Feasible, Is.True);
    }

    // ---- Priority 2: fewest shipments ----

    [Test]
    public void OneGeneralistBeatsHigherRatedSpecialists()
    {
        var decision = Route(Order(false, "WC-1", "CN-1", "WK-1"),
            Local("SUP-1", 6m, "wheelchair", "cane", "walker"),
            Local("SUP-2", 10m, "wheelchair"),
            Local("SUP-3", 10m, "cane"),
            Local("SUP-4", 10m, "walker"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WC-1:local,CN-1:local,WK-1:local]"));
    }

    [Test]
    public void OneMailOrderShipmentBeatsTwoLocalShipments()
    {
        var decision = Route(Order(true, "WC-1", "CN-1"),
            Local("SUP-1", 10m, "wheelchair"),
            Local("SUP-2", 10m, "cane"),
            Mailer("SUP-3", 5m, "wheelchair", "cane"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-3[WC-1:mail,CN-1:mail]"));
    }

    [Test]
    public void SplitsOnlyWhenNoSingleSupplierCanTakeEverything()
    {
        var decision = Route(Order(false, "WC-1", "CN-1", "WK-1"),
            Local("SUP-1", 8m, "wheelchair", "cane"),
            Local("SUP-2", 8m, "walker"),
            Local("SUP-3", 8m, "cane"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WC-1:local,CN-1:local] SUP-2[WK-1:local]"));
    }

    [Test]
    public void ThreeSupplierPlanWhenNothingSmallerCovers()
    {
        var decision = Route(Order(false, "WC-1", "CN-1", "WK-1"),
            Local("SUP-1", 7m, "wheelchair"),
            Local("SUP-2", 7m, "cane"),
            Local("SUP-3", 7m, "walker"));

        Assert.That(decision.Shipments, Has.Count.EqualTo(3));
    }

    [Test]
    public void SameCategoryTwiceNeedsOneShipment()
    {
        var decision = Route(Order(false, "WC-1", "WC-2"), Local("SUP-1", 7m, "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WC-1:local,WC-2:local]"));
    }

    // ---- Priority 3: quality ----

    [Test]
    public void HigherRatingWinsWhenEverythingElseIsEqual()
    {
        var decision = Route(Order(false, "WC-1"),
            Local("SUP-1", 7m, "wheelchair"),
            Local("SUP-2", 9m, "wheelchair"),
            Local("SUP-3", 8m, "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-2[WC-1:local]"));
    }

    [Test]
    public void PlanRatingIsTheAveragePerSupplierNotPerItem()
    {
        // Only two plans cover all four categories: {SUP-1, SUP-2} and {SUP-3, SUP-4}.
        // Per supplier: (9 + 5) / 2 = 7.0 vs 7.6, so SUP-3 + SUP-4 wins.
        // Per item it would be (9 x 4 + 5 x 2) / 6 = 7.67, and SUP-1 + SUP-2 would win instead.
        Product[] products =
        [
            Product("A-1", "a"), Product("A-2", "a"), Product("A-3", "a"),
            Product("B-1", "b"), Product("C-1", "c"), Product("D-1", "d"),
        ];
        var data = new ReferenceData(products,
        [
            Local("SUP-1", 9m, "a", "b"),
            Local("SUP-2", 5m, "c", "d"),
            Local("SUP-3", 7.6m, "a", "c"),
            Local("SUP-4", 7.6m, "b", "d"),
        ]);

        var decision = new RoutingService(data).Route(Order(false, "A-1", "A-2", "A-3", "B-1", "C-1", "D-1"));

        Assert.That(decision.Shipments.Select(s => s.SupplierId), Is.EquivalentTo(new[] { "SUP-3", "SUP-4" }));
    }

    [Test]
    public void BestTwoSupplierPlanIsChosenByRating()
    {
        var decision = Route(Order(false, "WC-1", "CN-1"),
            Local("SUP-1", 6m, "wheelchair"),
            Local("SUP-2", 9m, "wheelchair"),
            Local("SUP-3", 6m, "cane"),
            Local("SUP-4", 9m, "cane"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-2[WC-1:local] SUP-4[CN-1:local]"));
    }

    // ---- Priority 4: local preference when ratings are similar (1.0 band) ----

    [TestCase(9.5, TestName = "Gap 0.5")]
    [TestCase(9.0, TestName = "Gap exactly 1.0 is similar")]
    public void LocalWinsWhenWithinOnePointOfTheBest(double localRating)
    {
        var decision = Route(Order(true, "WC-1"),
            Mailer("SUP-1", 10m, "wheelchair"),
            Local("SUP-2", (decimal)localRating, "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-2[WC-1:local]"));
    }

    [TestCase(8.9, TestName = "Gap 1.1")]
    [TestCase(5.0, TestName = "Gap 5.0")]
    public void HigherRatedMailOrderWinsWhenMoreThanOnePointBetter(double localRating)
    {
        var decision = Route(Order(true, "WC-1"),
            Mailer("SUP-1", 10m, "wheelchair"),
            Local("SUP-2", (decimal)localRating, "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WC-1:mail]"));
    }

    [Test]
    public void BandIsMeasuredFromTheBestAndDoesNotChain()
    {
        // 8.5 is within 1.0 of 9.4, and 9.4 is within 1.0 of 10.0, but 8.5 is not within 1.0 of 10.0.
        var decision = Route(Order(true, "WC-1"),
            Mailer("SUP-1", 10m, "wheelchair"),
            Mailer("SUP-2", 9.4m, "wheelchair"),
            Local("SUP-3", 8.5m, "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WC-1:mail]"));
    }

    [Test]
    public void ReadmeBandExample()
    {
        // README section 3: Local A 9.0, Mail B 10.0, Local C 8.5 -> A wins.
        var decision = Route(Order(true, "WC-1"),
            Local("SUP-1", 9m, "wheelchair"),
            Mailer("SUP-2", 10m, "wheelchair"),
            Local("SUP-3", 8.5m, "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WC-1:local]"));
    }

    [Test]
    public void AmongLocalSuppliersInTheBandTheHigherRatingWins()
    {
        var decision = Route(Order(true, "WC-1"),
            Mailer("SUP-1", 10m, "wheelchair"),
            Local("SUP-2", 9.1m, "wheelchair"),
            Local("SUP-3", 9.6m, "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-3[WC-1:local]"));
    }

    [Test]
    public void PlanWithMoreLocalSuppliersWinsWithinTheBand()
    {
        // Plans: SUP-1 + SUP-2 (2 local, 8.5), SUP-1 + SUP-4 and SUP-3 + SUP-2 (1 local, 8.95),
        // SUP-3 + SUP-4 (0 local, 9.4). All are within 1.0 of 9.4, so the most local plan wins.
        var decision = Route(Order(true, "WC-1", "CN-1"),
            Local("SUP-1", 8.5m, "wheelchair"),
            Local("SUP-2", 8.5m, "cane"),
            Mailer("SUP-3", 9.4m, "wheelchair"),
            Mailer("SUP-4", 9.4m, "cane"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WC-1:local] SUP-2[CN-1:local]"));
    }

    // ---- Unrated suppliers (5.5) ----

    [Test]
    public void UnratedLocalBeatsMailOrderWithinOnePoint()
    {
        var decision = Route(Order(true, "WC-1"),
            Mailer("SUP-1", 6.5m, "wheelchair"),
            Local("SUP-2", null, "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-2[WC-1:local]"));
    }

    [Test]
    public void MailOrderMoreThanOnePointAboveUnratedWins()
    {
        var decision = Route(Order(true, "WC-1"),
            Mailer("SUP-1", 7m, "wheelchair"),
            Local("SUP-2", null, "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WC-1:mail]"));
    }

    [Test]
    public void UnratedRanksAboveLowRatingsAndBelowHighOnes()
    {
        var decision = Route(Order(false, "WC-1"),
            Local("SUP-1", 5m, "wheelchair"),
            Local("SUP-2", null, "wheelchair"));
        Assert.That(Describe(decision), Is.EqualTo("SUP-2[WC-1:local]"));

        decision = Route(Order(false, "WC-1"),
            Local("SUP-1", 6m, "wheelchair"),
            Local("SUP-2", null, "wheelchair"));
        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WC-1:local]"));
    }

    // ---- Tie resolution ----

    [Test]
    public void FullTieGoesToTheLowestNumericId()
    {
        var decision = Route(Order(false, "WC-1"),
            Local("SUP-0199", 8m, "wheelchair"),
            Local("SUP-021", 8m, "wheelchair"));

        Assert.That(decision.Shipments.Single().SupplierId, Is.EqualTo("SUP-021"));
    }

    [Test]
    public void SameNumberFallsBackToTheFullId()
    {
        var decision = Route(Order(false, "WC-1"),
            Local("SUP-T002", 8m, "wheelchair"),
            Local("SUP-002", 8m, "wheelchair"));

        Assert.That(decision.Shipments.Single().SupplierId, Is.EqualTo("SUP-002"));
    }

    [Test]
    public void IdOnlyBreaksTiesAfterRatingAndLocality()
    {
        var decision = Route(Order(true, "WC-1"),
            Mailer("SUP-1", 9m, "wheelchair"),     // lowest ID, but mail order
            Local("SUP-9", 9m, "wheelchair"));

        Assert.That(decision.Shipments.Single().SupplierId, Is.EqualTo("SUP-9"));
    }

    [Test]
    public void ResultDoesNotDependOnSupplierFileOrder()
    {
        Supplier[] suppliers =
        [
            Local("SUP-3", 8m, "wheelchair"), Local("SUP-1", 8m, "cane"),
            Local("SUP-2", 8m, "wheelchair", "cane"), Local("SUP-4", 8m, "wheelchair", "cane"),
        ];
        var order = Order(false, "WC-1", "CN-1");

        var forwards = Describe(Route(order, suppliers));
        var backwards = Describe(Route(order, suppliers.Reverse().ToArray()));

        Assert.That(forwards, Is.EqualTo("SUP-2[WC-1:local,CN-1:local]"));
        Assert.That(backwards, Is.EqualTo(forwards));
    }

    // ---- Item assignment and output shape ----

    [Test]
    public void OverlappingItemGoesToTheBetterSupplierInThePlan()
    {
        // Only SUP-1 has walkers and only SUP-2 has oxygen, so the plan is SUP-1 + SUP-2.
        // Both have wheelchairs; SUP-2 is more than 1.0 better, so it ships the wheelchair.
        var decision = Route(Order(false, "WK-1", "WC-1", "OX-1"),
            Local("SUP-1", 7m, "walker", "wheelchair"),
            Local("SUP-2", 9m, "oxygen", "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WK-1:local] SUP-2[WC-1:local,OX-1:local]"));
    }

    [Test]
    public void OverlappingItemPrefersTheLocalSupplierWithinTheBand()
    {
        var decision = Route(Order(true, "WK-1", "WC-1", "OX-1"),
            Local("SUP-1", 8.5m, "walker", "wheelchair"),
            Mailer("SUP-2", 9m, "oxygen", "wheelchair"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-1[WK-1:local,WC-1:local] SUP-2[OX-1:mail]"));
    }

    [Test]
    public void ShipmentsFollowFirstItemOrderAndItemsKeepRequestOrder()
    {
        var decision = Route(Order(false, "CN-1", "WC-1", "WC-2", "OX-1"),
            Local("SUP-1", 8m, "wheelchair", "oxygen"),
            Local("SUP-2", 8m, "cane"));

        Assert.That(Describe(decision), Is.EqualTo("SUP-2[CN-1:local] SUP-1[WC-1:local,WC-2:local,OX-1:local]"));
    }

    [Test]
    public void DuplicateLinesAreKeptWithTheirQuantities()
    {
        var order = new Order(null, Zip, false, [new OrderLine(1, "WC-1", 2), new OrderLine(2, "WC-1", 5)]);

        var items = Route(order, Local("SUP-1", 8m, "wheelchair")).Shipments.Single().Items;

        Assert.That(items.Select(i => (i.ProductCode, i.Quantity)), Is.EqualTo(new[] { ("WC-1", 2), ("WC-1", 5) }));
    }

    [Test]
    public void ShipmentCarriesSupplierNameAndItemDetails()
    {
        var shipment = Route(Order(false, "CP-1"), Local("SUP-7", 8m, "CPAP")).Shipments.Single();

        Assert.Multiple(() =>
        {
            Assert.That(shipment.SupplierId, Is.EqualTo("SUP-7"));
            Assert.That(shipment.SupplierName, Is.EqualTo("SUP-7 Name"));
            var item = shipment.Items.Single();
            Assert.That(item.ProductCode, Is.EqualTo("CP-1"));
            Assert.That(item.Quantity, Is.EqualTo(1));
            Assert.That(item.Category, Is.EqualTo("CPAP"));
            Assert.That(item.FulfillmentMode, Is.EqualTo(FulfillmentMode.Local));
        });
    }

    [Test]
    public void NullOrderIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => new RoutingService(DataWith()).Route(null!));
}
