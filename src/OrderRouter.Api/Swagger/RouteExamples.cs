namespace OrderRouter.Api.Swagger;

/// <summary>A ready-to-send request shown in Swagger UI, with the result it should produce.</summary>
/// <param name="Key">Identifier in the OpenAPI document.</param>
/// <param name="Summary">Shown in the Swagger UI examples drop-down.</param>
/// <param name="Description">What the example demonstrates.</param>
/// <param name="Body">The JSON request body.</param>
/// <param name="ExpectedFeasible">Expected "feasible" value.</param>
/// <param name="ExpectedSupplierId">Expected single supplier when feasible.</param>
/// <param name="ExpectedMode">Expected fulfillment_mode when feasible.</param>
/// <param name="ExpectedErrors">Expected errors, in order, when not feasible.</param>
public sealed record RouteExample(
    string Key,
    string Summary,
    string Description,
    string Body,
    bool ExpectedFeasible,
    string? ExpectedSupplierId = null,
    string? ExpectedMode = null,
    string[]? ExpectedErrors = null);

/// <summary>
/// Request samples for manual testing. Each one is also sent by the endpoint tests to prove it
/// produces the documented result against the real data.
/// </summary>
public static class RouteExamples
{
    public static readonly IReadOnlyList<RouteExample> All =
    [
        new("ord-001",
            "ORD-001: spec example (local wheelchair + oxygen)",
            "Sample order ORD-001, the same as the spec's example request. Four suppliers can take the whole order locally; SUP-0636 has the highest rating (6.8).",
            """
            {
              "order_id": "ORD-001",
              "customer_zip": "10015",
              "mail_order": false,
              "items": [
                { "product_code": "WC-STD-001", "quantity": 1 },
                { "product_code": "OX-PORT-024", "quantity": 1 }
              ]
            }
            """,
            ExpectedFeasible: true, ExpectedSupplierId: "SUP-0636", ExpectedMode: "local"),

        new("ord-002",
            "ORD-002: four categories, one local shipment",
            "Sample order ORD-002. Only two suppliers can take all four items; one shipment beats higher-rated splits. SUP-0928 (6.8) beats SUP-0460 (5.3).",
            """
            {
              "order_id": "ORD-002",
              "customer_zip": "77059",
              "mail_order": false,
              "items": [
                { "product_code": "HB-FUL-018", "quantity": 1 },
                { "product_code": "PL-ELEC-043", "quantity": 1 },
                { "product_code": "CM-BED-048", "quantity": 1 },
                { "product_code": "BP-AUTO-077", "quantity": 1 }
              ],
              "priority": "rush"
            }
            """,
            ExpectedFeasible: true, ExpectedSupplierId: "SUP-0928", ExpectedMode: "local"),

        new("ord-003",
            "ORD-003: mail order, ZIP with a leading zero",
            "Sample order ORD-003. No local supplier covers everything, so the best mail-order supplier wins: SUP-023 and SUP-065 tie at 10.0, and SUP-023 has the lower ID.",
            """
            {
              "order_id": "ORD-003",
              "customer_zip": "02130",
              "mail_order": true,
              "items": [
                { "product_code": "CP-STD-031", "quantity": 1 },
                { "product_code": "CP-MSK-FF-035", "quantity": 2 },
                { "product_code": "NB-COMP-039", "quantity": 1 }
              ]
            }
            """,
            ExpectedFeasible: true, ExpectedSupplierId: "SUP-023", ExpectedMode: "mail_order"),

        new("numeric-zip",
            "ZIP sent as a number (leading zero restored)",
            "customer_zip may be a JSON integer. 2130 is padded to 02130, the same as ORD-003, so the result matches ORD-003.",
            """
            {
              "customer_zip": 2130,
              "mail_order": true,
              "items": [
                { "product_code": "CP-STD-031", "quantity": 1 },
                { "product_code": "CP-MSK-FF-035", "quantity": 2 },
                { "product_code": "NB-COMP-039", "quantity": 1 }
              ]
            }
            """,
            ExpectedFeasible: true, ExpectedSupplierId: "SUP-023", ExpectedMode: "mail_order"),

        new("mail-order-missing",
            "mail_order omitted (defaults to false)",
            "mail_order is optional; missing or null means false, so only local suppliers are used. Product codes are matched ignoring case, and quantity is echoed back.",
            """
            {
              "customer_zip": "10015",
              "items": [
                { "product_code": "wc-std-001", "quantity": 3 },
                { "product_code": "OX-PORT-024", "quantity": 1 }
              ]
            }
            """,
            ExpectedFeasible: true, ExpectedSupplierId: "SUP-0636", ExpectedMode: "local"),

        new("local-only",
            "Mail order off: only local suppliers",
            "At test ZIP 00001 only SUP-T001 (9.0) is local. Higher-rated mail-order suppliers are ignored because mail_order is false.",
            """{ "customer_zip": "00001", "mail_order": false, "items": [ { "product_code": "WC-SENT-001", "quantity": 1 } ] }""",
            ExpectedFeasible: true, ExpectedSupplierId: "SUP-T001", ExpectedMode: "local"),

        new("local-preferred",
            "Similar ratings: local beats mail order",
            "Mail order is allowed and several 10.0 suppliers can mail, but local SUP-T001 (9.0) is within 1.0 of the best rating, so local wins.",
            """{ "customer_zip": "00001", "mail_order": true, "items": [ { "product_code": "WC-SENT-001", "quantity": 1 } ] }""",
            ExpectedFeasible: true, ExpectedSupplierId: "SUP-T001", ExpectedMode: "local"),

        new("fewest-shipments",
            "Fewest shipments beats higher ratings",
            "At test ZIP 00007, generalist SUP-T007 (6.0) carries all three items, so it beats three 10.0 specialists that would need three shipments.",
            """
            {
              "customer_zip": "00007",
              "mail_order": false,
              "items": [
                { "product_code": "WC-SENT-001", "quantity": 1 },
                { "product_code": "CN-SENT-001", "quantity": 1 },
                { "product_code": "WK-SENT-001", "quantity": 1 }
              ]
            }
            """,
            ExpectedFeasible: true, ExpectedSupplierId: "SUP-T007", ExpectedMode: "local"),

        new("exact-tie",
            "Exact tie: lowest supplier ID wins",
            "SUP-T003 and SUP-T004 are identical (8.0, local). The lower ID wins, so the result is always the same.",
            """{ "customer_zip": "00003", "mail_order": false, "items": [ { "product_code": "CN-SENT-001", "quantity": 1 } ] }""",
            ExpectedFeasible: true, ExpectedSupplierId: "SUP-T003", ExpectedMode: "local"),

        new("unrated-local",
            "Unrated suppliers (scored 5.5)",
            "Both local suppliers have \"no ratings yet\" and are scored 5.5, so the tie goes to the lower ID.",
            """{ "customer_zip": "00040", "mail_order": false, "items": [ { "product_code": "RL-SENT-001", "quantity": 1 } ] }""",
            ExpectedFeasible: true, ExpectedSupplierId: "SUP-T011", ExpectedMode: "local"),

        new("rated-mail-beats-unrated",
            "Much higher rating beats local",
            "Mail order is allowed. The unrated local suppliers (5.5) are more than 1.0 below the 10.0 mail-order suppliers, so quality wins over locality.",
            """{ "customer_zip": "00040", "mail_order": true, "items": [ { "product_code": "RL-SENT-001", "quantity": 1 } ] }""",
            ExpectedFeasible: true, ExpectedSupplierId: "SUP-021", ExpectedMode: "mail_order"),

        new("no-supplier",
            "Infeasible: no supplier serves the ZIP",
            "No supplier serves test ZIP 00099 (the every-ZIP range starts at 00100), and mail order is off.",
            """{ "customer_zip": "00099", "mail_order": false, "items": [ { "product_code": "WC-SENT-001", "quantity": 1 } ] }""",
            ExpectedFeasible: false,
            ExpectedErrors: ["No eligible supplier for product_code WC-SENT-001 (category: wheelchair)."]),

        new("unknown-product",
            "Infeasible: unknown product code",
            "The whole order fails if any product code isn't in products.csv. No partial routing is returned.",
            """
            {
              "customer_zip": "10015",
              "mail_order": false,
              "items": [
                { "product_code": "WC-STD-001", "quantity": 1 },
                { "product_code": "NOT-A-PRODUCT", "quantity": 1 }
              ]
            }
            """,
            ExpectedFeasible: false,
            ExpectedErrors: ["Unknown product_code: NOT-A-PRODUCT."]),

        new("spec-validation-errors",
            "Validation: the spec's failure example",
            "No line items and an invalid ZIP. Produces exactly the two errors in the spec's unsuccessful routing example.",
            """{ "customer_zip": "ABC", "items": [] }""",
            ExpectedFeasible: false,
            ExpectedErrors: ["Order must include at least one line item.", "Order must include a valid customer_zip."]),

        new("wrong-types",
            "Validation: wrong value types",
            "The ZIP contains a letter, mail_order is a string instead of true/false, and quantity is a string. All errors are reported together, still with HTTP 200.",
            """
            {
              "customer_zip": "1001A",
              "mail_order": "yes",
              "items": [ { "product_code": "WC-STD-001", "quantity": "1" } ]
            }
            """,
            ExpectedFeasible: false,
            ExpectedErrors:
            [
                "Item 1 must include a quantity that is a positive integer.",
                "Order must include a valid customer_zip.",
                "Order must include a valid mail_order flag.",
            ]),
    ];

    /// <summary>Response samples shown for the 200 response.</summary>
    public const string SuccessResponse = """
        {
          "feasible": true,
          "routing": [
            {
              "supplier_id": "SUP-0636",
              "supplier_name": "Care Supply Corp #636",
              "items": [
                { "product_code": "WC-STD-001", "quantity": 1, "category": "wheelchair", "fulfillment_mode": "local" },
                { "product_code": "OX-PORT-024", "quantity": 1, "category": "oxygen", "fulfillment_mode": "local" }
              ]
            }
          ]
        }
        """;

    public const string FailureResponse = """
        {
          "feasible": false,
          "errors": [
            "Order must include at least one line item.",
            "Order must include a valid customer_zip."
          ]
        }
        """;
}
