# AI Prompts

The significant prompts used to build this project, in the order they were used. Prompts
are quoted exactly as written. Decisions given in answer to the assistant's clarifying
questions are included where they shaped the result. Each entry ends with a short note on
what came out of it.

Tool: an AI coding agent (Claude) working in the repository.

---

## 1. Requirement analysis

> Read the instructions and inspect the data files.
>
> Do not modify files yet.
>
> Do a requirement analysis with:
>
> - hard requirements that can not be changed
> - validation and error behavior
> - ambiguities that require an assumption
> - unusual or messy data
> - hidden test cases
> - routing priorities in their exact spec
>
> IMPORTANT: Endpoint must always return a 200, including validation and routing failures. 'feasible' field communicates success or failure. Don't 'improve' this to conventional API semantics.
>
> Do not invent requirements.
> If something is ambiguous, identify it and propose a defensible solution

**Outcome:** A requirements analysis. It identified the misspelled `suplier_name` column,
4-digit ZIPs missing leading zeros, category case differences, duplicate products, and the
`SUP-T0xx` / `*-SENT-001` test fixtures. It also listed the ambiguities that needed a
decision.

## 2. First set of decisions and the README

> 1. For A2: lets treat similar ratings as equal ratings.  For solutions with the same number of suppliers, prefer highest rates.  If ratings are equal, prefer local over mail order.  If still tied, use supplier_id for deterministic ordering
> 2. For A3: Unscored suppliers are treated as middle score.  I think this gives new suppliers a chance to get a score so they can be weighted appropriately.
> 3. For A4: missing defaults to false.  Allows some orders to the supplier without risking orders they can't fulfill
> 4. For A5: The spec states priority should first be feasibility, then customer experience by fewer shipments, then by quality, then prefer local.  With Quality, its likely a feedback rating from customers so we should prefer ones that have a higher rating not worst-supplier rating first.
>
> Document these implementation assumptions in a README that we'll later use for the solution/code as well.

**Outcome:** `README.md` created with the API contract, validation rules, routing rules,
data handling, assumptions and expected results. Every expected result was checked against
the real data.

## 3. Refining quality ranking and tie-breaks

> 1. I think middle score is ok.
> 2. Average per supplier sounds ok.  We don't want a mediocre supplier beat by a poor supplier with more scores.
> 3. Supplier ID should only be the final tie breaker if shipment count, rating, and locality are all the same.  We could use the numeric portion of the ID for ordering.  Make sure to document this too.
>
> "Exact-equal ratings also change one result from my earlier analysis. That same ZIP 00001 order now goes to a 10.0 mail-order supplier rather than the local 9.0 supplier (SUP-T001). This follows directly from your rule, and the README records it."  Regarding this, the spec does says prefer local over mail-order when ratings are similar.  10 and a 9 rating feel pretty similar. Would you agree?

**Outcome:** Plans are rated by their average per supplier. Supplier IDs are ordered by
their numeric part, with the full ID as a fallback (`SUP-002` before `SUP-T002`). The
assistant agreed that 9 and 10 are similar and proposed a 1.0 band.

## 4. Similar-ratings band

> Yes, switch to the 1 band.

**Outcome:** "Similar" means within 1.0 of the best rating, with exactly 1.0 included.
Within the band, local wins, then the higher rating, then the ID. The expected result for
the ZIP 00001 mail-order test changed to SUP-T001 (local).

## 5. .NET design

> Now based on the written requirements in the instructions, propose the smallest clean, testable .NET design appropriate.
>
> Priorities should be
> - correctness
> - explicit requirement compliance
> - easy unit testing
> - simple architecture
> - no unnecessary frameworks or abstractions
>
> Business logic should be separate from the API layer.
>
> Provide a list of domain models, data loading, data parsing, routing service responsibilities, api responsibilities.
>
> Assumptions must be prompted and documented.

**Decisions given in answer to the assistant's questions:**
- .NET 10 LTS.
- Small hand-written CSV parser (no NuGet package).
- Unparseable CSV rows: skip the row and log a warning.
- Test framework: NUnit.
- CSV files bundled with the app, with a `DATA_DIR` environment variable override.
- "More local" between plans is measured by number of local suppliers, not line items.
  The assistant found the line-item version made the fast search inexact.

**Outcome:** A Core library holds the business logic; a thin Minimal API handles HTTP. The
design section was added to the README with technical decisions T1–T11.

## 6. Response class and data parsing

> I'd rather have a response class.  It could be named RoutingDecision or RoutingResult and be based on data from the instructions.  The rest of it looks good at a first glance.  After that update we can begin with data parsing.
>
> With data parsing, be defensive as some data seems to be the same except have different casing.  Watch out for misnamed columns and handle them with the wrong name instead of updating test data files.  Make sure to handle 'no ratings yet'.  Make sure to handle zip's that should have leading zeros.  Handle mixed range and individual zips.  Handle duplicate row/product codes.
>
> Create unit tests for parsing and normalization edge cases and account for any that could happen that we haven't discussed or seen in the data within reason.  Lets have the code future proofed a bit, but we can't make bad assumptions.
>
> Don't implement routing yet.

**Decisions given in answer to the assistant's questions:**
- Invalid ratings (blank, non-numeric, outside 1–10): treat as unrated and warn.
- Invalid ZIP entries: drop that entry and warn; keep the supplier's valid ZIPs.
- `can_mail_order?`: accept common yes/no forms; anything else is `n` with a warning.
- Conflicting duplicates:
  > I think we shouldn't do a blanket rule.  It would depending on what data differs and what that field means.  If its only description, deterministically and document/log it.   Same product code, different category is an unsafe mismatch which we should drop and log.  Same supplier ID but different ZIP's could be a supplier with multiple warehouses, or it could be a supplier that moved.  Same supplier ID but conflicting ratings or mail order values seems like an unsafe match as well.  If the data doesn't have any cases like these, then lets just not worry about it for now.

**Outcome:** Solution created with `OrderRouter.Core` (domain types and data parsing) and
`OrderRouter.Tests` (NUnit, 212 tests). The README gained the `RoutingResult` response
class and the full data-handling rules.

## 7. Audit against the spec

> Audit the code done so far against the spec in the take home instructions file.

**Outcome:** Every spec data requirement was confirmed as met. The audit found:
- a CSV edge case (a space before an opening quote),
- an overstated README claim about ZIP padding,
- a README/code mismatch on category wording,
- a domain-to-routing dependency,
- missing validation on the domain models,
- the missing `AI_PROMPTS.md` deliverable.

## 8. Audit fixes

> Yes make those fixes and start filling out the AI_PROMPTS.ms

**Outcome:** All audit findings fixed, and this file created. The CSV reader now accepts
spaces before a quote. `Supplier` and `Product` check their values when created. The
rating constants moved onto `Supplier`, and the README wording was corrected. 236 tests
pass.

## 9. Routing algorithm design

> Now lets design the routing algorithm.  Propose an algorithm and describe it to me.  Make sure it preserves the priority order from the spec, this is very important.
>
> The order from the spec is:
> Feasibility: Only route to suppliers who can actually fulfill the items
> Customer experience: Prefer fewer shipments (consolidate with one supplier
> when possible)
> Quality: When multiple options exist, prefer higher-rated suppliers
> Geographic preference: Prefer local suppliers over mail-order when ratings are
> similar
>
> Review the spec again from the take home instructions file when determining the algorithm.
>
> Explain:
> -how candidates are determined
> -mail order eligibility
> -local fullfilment
> -unrated suppliers
> -tie resolution
> -complexity of the search
>
> Prompt for any assumption thats not explicitly defined.  Don't write code until we are all clear.

**Outcome:** A staged algorithm with one stage per spec priority:
1. keep only suppliers that can fulfill items,
2. find the smallest number of suppliers covering the order,
3. keep plans within 1.0 of the best rating,
4. prefer more local suppliers,
5. then higher rating, then supplier ID.

The candidate narrowing was tightened: a supplier is discarded when another one covers
the same categories or more and is at least as good. This was proved not to change the
result. Search size was measured on the real data: every real ZIP needs only one
supplier. The only undefined point, a search-size limit, was put to the user.

## 10. Search-size limit

> Lets not worry about search size limit for now unless its feasibly an issue with the current data.

**Outcome:** No limit (README decision T12). With the current data no search needs more
than one supplier, and at most 102 candidates remain after pruning.

## 11. Routing implementation and tests

> Yes implement the routing and tests.  Make sure tests have cases to validate each data piece with good and bad or missing data.

**Outcome:** Implemented in the Core library, with no API yet:
- request validation (`OrderRequestParser`),
- the shared ranking rule (`SupplierRanking`),
- routing (`RoutingService`),
- supporting domain types (`Order`, `RoutingDecision`, `SupplierIdComparer`).

Tests cover every request field with good, bad and missing values, and each spec priority
on its own. Every README section 6 result is checked on the real data. A randomized test
compares routing with and without candidate narrowing on 500 data sets.

The tests were checked by breaking the code deliberately: disabling the rating band and
breaking the pruning rule both made tests fail. 937 tests pass.

## 12. Checking the priority order

> In your responses you mentioned: "Among plans that size, keep those within 1.0 of the best average rating, prefer more local suppliers, then the higher rating, then supplier ID."  It sounds like there are two steps where supplier rating is user.  The routing logic priorities in the spec from the take home instructions file explicitly state:
> Feasibility: Only route to suppliers who can actually fulfill the items
> Customer experience: Prefer fewer shipments (consolidate with one supplier
> when possible)
> Quality: When multiple options exist, prefer higher-rated suppliers
> Geographic preference: Prefer local suppliers over mail-order when ratings are
> similar
>
> I think it should be more like "Find suppliers that can actually fullfil item, prefer fewer shipments by using a supplier that can cover more items, if there is a tie between these, use higher rated suppliers, if there is still a tie prefer local suppliers."
>
> Do you agree based on your review of the spec?

**Outcome:** The assistant agreed on the order of priorities and explained both
clarifications:
- **Fewest shipments:** this needs a true smallest-plan search. Picking the supplier that
  covers the most items first can use more shipments. Example: S1 {1,2,3,4} then S2 and
  S3 gives 3 shipments, while S2 {1,2,5} + S3 {3,4,6} gives 2.
- **Rating isn't used twice:** the "higher rating" step only ranks options that are also
  equal on locality, inside the 1.0 band. So it never overrides locality.

The real choice was what counts as a tie in rating: within 1.0 (A, current) or exactly
equal (B).

## 13. Keeping the 1.0 band

> Yes by smaller number of suppliers I meant shipments.  So I understand correctly, the 1 rating band is only used for quality ratings, correct?  If that still fits with A we can keep A.

**Outcome:** Confirmed. The band is only used when comparing ratings: between plans with
the same number of shipments, and between suppliers in the winning plan for an item both
carry. It never affects feasibility or the number of shipments. Option A kept; no code
changes.

## 14. API with Swagger

> Lets implement the API.  be sure to add swagger support with good test samples so manual testing later is easier.  Document this in the readme and how to use swagger.
>
> HARD REQUIREMENT: Every response from the endpoint must be a 200 exactly as required by the take home instructions file.  Do not use BadRequest, NotFound, etc.
>
> Represent the success/failure in the body with 'feasible' and error messages/failures.  Keep the response contract exactly as the take home instructions file defines it.  It is ok for the data returned to be a model that would match the json from the spec file.

Follow-up during implementation:

> Also make sure we're not exposing stack traces in error messages.  They should be human friendly based ont he sample int he spec file

**Outcome:** `OrderRouter.Api` (Minimal API, Swashbuckle 10):
- **Endpoint:** `POST /api/route` only ever returns `TypedResults.Ok`. Nothing is bound
  from the request, and the handler has its own try/catch, backed by a safety-net
  middleware.
- **Response class:** `RoutingResult` has explicit `[JsonPropertyName]` fields matching the
  spec exactly.
- **Swagger UI:** 14 request samples and 2 response samples. Every sample is also run as a
  test.
- **Error messages:** fixed and human-friendly. Exceptions are only logged.

Traps found and avoided along the way:
- `.Accepts<T>()` would make routing return 415 for other content types.
- A handler taking only `HttpContext` binds as a raw `RequestDelegate` and discards its
  result. The build caught this, since warnings are treated as errors.

986 tests pass, and a live `curl` check confirmed 200 responses for valid, invalid and
failing requests.

## 15. Other HTTP methods, and Docker

> "If you'd rather every method on that path return 200 with a feasible: false message such as "Use POST to route an order.", it's a small change." Yes, please do that and then make the dockerfile and docker build and run instructions.

**Outcome:**
- **Other methods:** every non-POST method on `/api/route` now returns 200 with "Use
  POST to route an order.", including non-standard methods. HEAD returns headers only.
  This is handled in the middleware, not with extra routes.
- **Dockerfile:** a multi-stage build (`build` → `test` / `publish` → `final`). The final
  image is the `aspnet:10.0` runtime, runs as a non-root user on port 8080, and includes
  the CSVs and Swagger. The `test` stage runs the whole suite with no local SDK.
- **Verified in Docker:** the image built; `curl` got 200 for ORD-003, invalid JSON, GET
  and HEAD; Swagger was served; the container ran as the `app` user; the `DATA_DIR`
  volume override worked. All 995 tests pass inside the test image.
- **README:** a Quick start section, full Docker instructions, and decision T16.

## 16. Skeptical code review

> Before we go further, act as a skeptical reviewer and perform a review of the code.  Make sure to compare the entire implementation against the requirements and supplied data.  Look for likely hidden-test cases.
>
> Do not refactor for style.  Focus on correctness and any missed requiements.
>
> pay special attention to:
> -200 on every http call
> -invalid request handling
> -empty fields
> -invalid zip
> -unknown product code
> -zip matching with list or range
> -leading zero zip handling
> -mixed range/list zip
> -mail order yes or no, missing
> -local supplier eligible when mailorder is enabled
> -rating comparison on equal shipment or near equal shipment
> -ratings outsize of range, ratings missing, 'no ratings yet'
> -casing
> -quantities
> -successful routing has only one requested item
>
> This list is not all encompassing.  Come up with more things to pay close attention to before reviewing.

Follow-up during the review:

> Be sure the AI prompts is up to date too when you're done.

**Outcome:** Extra review areas were added:
- every line routed exactly once,
- the eligibility and mode of every routed item,
- the true minimum number of shipments,
- path variants and HTTP edge cases,
- oversized bodies,
- combined errors,
- performance with huge orders.

The review used live HTTP probes against the running server, plus an independent
brute-force check that uses no routing code. That check passed on 7,000 random orders and
was added to the test suite (`RoutingOracleTests`).

Defects found and fixed, each with tests:
1. **`GET /api/route/` (trailing slash) returned 405**, breaking the 200 rule. The guard
   compared paths exactly, while ASP.NET routing also accepts the trailing slash.
2. **Duplicate-error removal slowed down quadratically:** 60,000 unknown codes took
   5 seconds. It now takes 0.085 seconds.
3. **An oversized body gave a vague message.** It now says "Request body is too large."

Judgment calls reported to the user, with no change made: a numeric `customer_zip`,
`mail_order: null`, case-sensitive property names, and only validation errors being shown
when a request also has unknown products. The test suppliers named "Mail-Only" actually
serve ZIPs locally according to the data, so the service follows the data. 3,508 tests
pass.

## 17. Decisions on the review's judgment calls

> 1.  What do you suggest?  I think it could be safe to accept it if we can do it in a way to handle leading zeros.
> 2. Agreed that null and missing is the same.
> 3. Make sure we match the actual data for parsing when the spec and data don't agree on property names.
> 4. That should be fine. Lets keep to 'minimal processing' as a user could fix a mistake they did or an engineer would likely investigate and see other things wrong with the data when they go to look at the zip.
> 5.  The names are probably intentionally misleading.  Lets follow the data and not the names.
> 6. That's probably fine.  We've already likely gone above and beyond with that.

**Outcome:**
1. **Numeric `customer_zip`:** a JSON integer from 0 to 99999 is now accepted and padded to
   5 digits (`2130` → `"02130"`), the same rule used for supplier ZIPs. Decimals,
   exponents, negatives and numbers over 99999 are still invalid. String ZIPs stay strict
   at exactly 5 digits, because a string can keep its leading zero.
2. **`mail_order: null`** now means false, the same as missing.
3. **Property names:** the request names in `sample_orders.json` match the spec. The only
   mismatches are CSV headers, already read under the data's spelling. A new test sends
   each order from the real `sample_orders.json` file exactly as written.
4. **Errors reported together:** kept as is (README A16).
5. **Supplier names:** the service follows the data, not the names (README A15).
6. **Protocol-level rejections:** kept as is.

The Swagger samples were updated: a new numeric-ZIP sample, and the wrong-types sample now
uses a ZIP containing a letter. 3,527 tests pass.

## 18. Final requirements check

> Do one last requirements check.  Have we fulfilled the spec and kept everything to spec except for some ambiguity that we made decisions on?

**Outcome:** Every line of the spec was mapped to the implementation and its evidence, and
the delivered Docker image was checked again with the spec's own requests. The spec's
example request, the spec's failure example (reproduced word for word) and invalid input
all returned 200. The only departures are:
- the ambiguities decided and documented in the README (A1–A16, T1–T16),
- additions that don't conflict with the spec (Swagger, and a 200 for other HTTP methods),
- two places where the spec disagrees with its own data: the "200 products" count, and the
  illustrative success example (SUP-005 doesn't carry wheelchairs or serve ZIP 10015).
