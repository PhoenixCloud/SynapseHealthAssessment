# Multi-Supplier Order Router

A service that routes a multi-item order to one or more suppliers, using the supplier
and product data in `suppliers.csv` and `products.csv`.

## Quick start

```bash
docker build -t order-router .
docker run --rm -p 8080:8080 order-router
# Open http://localhost:8080/swagger and pick a sample from the Examples list.
```

- **Tests:** `dotnet test` (needs the .NET 10 SDK), or
  `docker build --target test -t order-router-tests . && docker run --rm order-router-tests`.
- **Details:** [Build and Run](#8-build-and-run) and [Testing](#9-testing).
- **`POST /api/route` always returns HTTP 200.** The `feasible` field says whether routing
  succeeded.

---

## Table of Contents

1. [API](#1-api)
2. [Validation and Error Behavior](#2-validation-and-error-behavior)
3. [Routing Rules](#3-routing-rules)
4. [Data Handling](#4-data-handling)
5. [Implementation Assumptions](#5-implementation-assumptions)
6. [Expected Results for Known Orders](#6-expected-results-for-known-orders)
7. [Design](#7-design)
8. [Build and Run](#8-build-and-run)
9. [Testing](#9-testing)

---

## 1. API

### `POST /api/route`

**This endpoint always returns HTTP 200.** That includes validation failures, routing
failures, malformed JSON and unexpected server errors. The `feasible` field says whether
routing succeeded. This is intentional and must not be changed to conventional
400/422/500 status codes.

#### Request

```json
{
  "order_id": "ORD-EXAMPLE",
  "customer_zip": "10015",
  "mail_order": false,
  "items": [
    { "product_code": "WC-STD-001", "quantity": 1 },
    { "product_code": "OX-PORT-024", "quantity": 1 }
  ]
}
```

| Field | Type | Required | Notes |
|---|---|---|---|
| `order_id` | string | No | Not used for routing and not validated. |
| `customer_zip` | string or integer | Yes | A string of exactly 5 digits (`"02130"`), or a JSON integer from 0 to 99999, which is padded to 5 digits (`2130` → `"02130"`). |
| `mail_order` | boolean | No | Defaults to `false` when missing or `null`. |
| `items` | array | Yes | At least one item. |
| `items[].product_code` | string | Yes | Must exist in `products.csv`. |
| `items[].quantity` | integer | Yes | Whole number ≥ 1. |

Unknown extra fields (for example `priority` and `notes` in `sample_orders.json`) are ignored.

#### Success response

```json
{
  "feasible": true,
  "routing": [
    {
      "supplier_id": "SUP-0636",
      "supplier_name": "Care Supply Corp #636",
      "items": [
        {
          "product_code": "WC-STD-001",
          "quantity": 1,
          "category": "wheelchair",
          "fulfillment_mode": "local"
        }
      ]
    }
  ]
}
```

- `fulfillment_mode` is `"local"` or `"mail_order"`.
- `product_code` is the catalog's code (uppercase), so a request for `wc-std-001` returns
  `WC-STD-001`.
- `category` is the value from `products.csv` as written (trimmed, with repeated spaces
  collapsed), e.g. `"CPAP"`, not the lowercased matching key.

#### Failure response

```json
{
  "feasible": false,
  "errors": [
    "Order must include at least one line item.",
    "Order must include a valid customer_zip."
  ]
}
```

A failure response contains no `routing` field.

---

## 2. Validation and Error Behavior

- All validation errors are collected and returned together. Validation does not stop at
  the first error.
- Routing only runs if validation passes.
- Every outcome returns **HTTP 200**. The framework's default 400/422 handlers and its
  500 handling must be overridden for this endpoint.

| Condition | Error message |
|---|---|
| Body is empty, not valid JSON, not a JSON object (e.g. an array), or repeats a property name | `Request body must be a JSON object.` |
| `items` missing, not an array, or empty | `Order must include at least one line item.` *(from the spec)* |
| `customer_zip` missing or `null`; a string that isn't exactly 5 digits; a number that isn't a whole number from 0 to 99999 (e.g. `100000`, `-1`, `10015.0`, `1e4`); or any other type | `Order must include a valid customer_zip.` *(from the spec)* |
| `mail_order` present, not `null`, and not a JSON boolean (`"true"`, `1`, `"yes"`) | `Order must include a valid mail_order flag.` |
| An item is not an object | `Item {n} must be an object.` |
| `product_code` missing or not a string | `Item {n} must include a valid product_code.` |
| `product_code` not in `products.csv` | `Unknown product_code: {code}.` |
| `quantity` missing, not an integer, a boolean, or < 1 | `Item {n} must include a quantity that is a positive integer.` |
| No eligible supplier for an item | `No eligible supplier for product_code {code} (category: {category}).` |
| The request body is larger than the server limit (Kestrel's default, 30,000,000 bytes) | `Request body is too large.` |
| The request body can't be read for any other reason (e.g. the connection drops) | `Request body could not be read.` |
| Any method other than `POST` on `/api/route` | `Use POST to route an order.` |
| Unexpected internal error | `Internal error while routing order.` |

Notes:
- `{n}` is the item's 1-based position in `items`.
- Only the two messages marked *(from the spec)* are specified word for word. The others
  are our own wording.
- ZIP validation only checks the format. Any 5-digit string is valid, including `00001`.
  Real-ZIP lists are never used.
- **Numeric ZIPs** are accepted and padded to 5 digits: `2130` means `02130`, and `1`
  means `00001`. A JSON number can't keep a leading zero, and this is the same rule used
  for supplier ZIPs in `suppliers.csv`. **String ZIPs are not padded**: a string can keep
  its leading zero, so `"2130"` is treated as a mistake. ZIP+4 (`"02130-1234"`) is
  rejected.
- `mail_order: null` is treated the same as a missing `mail_order` (false).
- A JSON boolean must not be accepted as a quantity. In some languages (e.g. Python)
  `true` behaves like the number `1`.
- If any item cannot be routed, the **whole order fails** (`feasible: false`). Every
  unroutable item is listed, and no partial routing is returned.
- Unknown product codes and "no eligible supplier" errors come from routing. So they are
  only reported once the request itself is valid. They are listed in line order, and a
  repeated message appears once.
- Property names must match exactly (`customer_zip`, not `Customer_Zip`). The names in
  the spec and in `sample_orders.json` agree, and a test sends each order from the real
  `sample_orders.json` file as written. Where the spec and the data files disagree (the CSV
  headers, see [Column names](#column-names)), the data's spelling is accepted. A property
  repeated in the body is rejected, rather than guessing which value to use.
- When a request has validation errors (e.g. a bad ZIP), only those are reported. Product
  and supplier checks run once the request is valid. This keeps processing minimal: the
  fix usually happens at the source, and fixing one error reveals the next.
- Validation errors are listed in this order: `items`, then each item's own errors, then
  `customer_zip`, then `mail_order`. This matches the spec's example.

---

## 3. Routing Rules

### Priorities from the spec (in order of importance)

> 1. **Feasibility**: Only route to suppliers who can actually fulfill the items
> 2. **Customer experience**: Prefer fewer shipments (consolidate with one supplier when possible)
> 3. **Quality**: When multiple options exist, prefer higher-rated suppliers
> 4. **Geographic preference**: Prefer local suppliers over mail-order when ratings are similar

### Step 1: Eligibility (feasibility)

A supplier is eligible for an item when **both** of these are true:

1. The supplier's `product_categories` include the item's category. The category is
   looked up in `products.csv` and compared without regard to case.
2. **Either** the supplier serves `customer_zip` (**local**), **or** `mail_order` is
   `true` and the supplier's `can_mail_order?` is `y` (**mail order**).

When `mail_order` is true, suppliers that serve the ZIP locally are still eligible.
`fulfillment_mode` is `local` whenever the supplier serves the customer's ZIP, even if it
can also mail. Otherwise it is `mail_order`.

If any item has no eligible supplier, the order fails.

### Step 2: Fewest shipments (customer experience)

Find the smallest number of suppliers that together cover every item. Only plans with
that minimum number are considered further. A single supplier that can take the whole
order always beats splitting it, even if the split would use higher-rated suppliers.

A line item is never split across suppliers. Quantity is echoed back only; there is no
inventory or capacity data.

### Step 3: Choosing among plans with the same number of suppliers

#### Similar ratings

Two ratings are **similar** when they are within **1.0** of each other, with a gap of
exactly 1.0 included. The band is always measured from the **best** rating among the
options being compared, never from one option to the next. This prevents chaining (8.0
near 9.0, 9.0 near 10.0, so 8.0 treated as near 10.0).

Ratings are stored as `decimal`, not floating-point, so `best - rating <= 1.0m` is exact
and needs no rounding tolerance.

#### Comparing plans

A plan's rating is the **average rating of its suppliers**. Each supplier counts once,
however many items it handles, so a supplier with a poor rating can't outweigh a better
one just by taking more items.

Plans are compared in this order:

1. **Similar rating (quality).** Find the best plan rating. Only plans within 1.0 of it
   stay in the running. Plans outside the band are dropped, however local they are.
2. **Local over mail order (geographic preference).** Among the remaining plans, the one
   with more **local suppliers** wins. A supplier is local if it serves the customer's
   ZIP. This matches the plan rating, which is also measured per supplier.
3. **Higher rating.** If locality is equal, the higher plan rating wins.
4. **`supplier_id` (deterministic, final tie-breaker).** Only if shipment count, rating
   and locality are all equal. See [supplier ID ordering](#supplier-id-ordering).

#### Assigning items within a plan

When more than one supplier in a plan can handle an item, the same rules pick one:
suppliers within 1.0 of the best rating for that item, then local, then higher rating,
then `supplier_id`.

This only decides which supplier ships an item that several suppliers in the plan can
handle. It never affects which plan wins, because plans are scored on their suppliers
(average rating and number of local suppliers). In a smallest plan every supplier ends up
with at least one item. Otherwise it would be redundant and a smaller plan would exist.

#### Example

For one item, where Local A is 9.0, Mail B is 10.0 and Local C is 8.5:

- The best rating is 10.0, so the band is 9.0–10.0.
- A and B are in the band. C (8.5) is dropped.
- A is local, so **A wins**.

### Supplier ID ordering

IDs are ordered by their **numeric part**, lowest first:
`SUP-001` → 1, `SUP-0199` → 199, `SUP-T003` → 3. This avoids the mixed ID widths giving a
misleading order (as plain text, `SUP-0199` would come before `SUP-021`).

The test suppliers share numbers with regular suppliers (`SUP-T001` and `SUP-001` are
both 1, and so on up to 14). When the numbers match, the **full ID compared as text**
decides, so `SUP-002` comes before `SUP-T002`.

To compare two plans by ID, sort each plan's suppliers by this order, then compare the
two lists one position at a time. The first difference decides.

### Output ordering

- Suppliers in `routing` appear in the order their first item appears in the request.
- Items within a supplier keep their request order.
- Duplicate lines of the same `product_code` stay as separate lines, as submitted.

---

## 4. Data Handling

The data files are never edited. The parsers cope with the data as delivered.

Every problem found while loading becomes a **warning** with file, line and reason. The
API logs these at startup. Only two things stop the service from starting: a missing file,
or a missing or ambiguous required column, because then nothing can be read reliably. An
unclosed quote in a CSV file also stops startup, since every row after it would be
misread.

### General CSV handling

| Case | Handling |
|---|---|
| Quoted fields containing commas; `""` inside quotes; line breaks inside quotes | Supported (standard CSV). Warnings use the line where the row starts. |
| Spaces or tabs before an opening quote (`a, "b, c"`), common in hand-edited files | Still read as a quoted field, and the leading spaces are discarded. Otherwise the inner comma would split the field. |
| Byte-order mark; CRLF, LF or CR line endings; blank lines; trailing newline | Supported. Blank lines are ignored. |
| Spaces around values | Trimmed. |
| A row with too few columns | Row skipped with a warning. |
| A row with extra columns | Allowed if the extra values are all blank (e.g. a trailing comma). Otherwise the row is skipped with a warning. |

### Column names

Headers are matched after **normalizing**: lowercase, keeping only letters and digits. So
`Supplier ID`, `supplier_id` and `SUPPLIER-ID` all match. Column order doesn't matter, and
unknown columns are ignored.

The misspelled headers are accepted as they appear in the file. Each also has its correct
spelling as an alias, so fixing the file later won't break loading:

| Field | Accepted headers (after normalizing) |
|---|---|
| Supplier ID | `supplierid`, `suplierid` |
| Supplier name | `suppliername`, `supliername` *(the file has `suplier_name`)* |
| Service ZIPs | `servicezips` |
| Categories | `productcategories` |
| Rating | `customersatisfactionscore` |
| Mail order | `canmailorder` *(the file has `can_mail_order?`)* |
| Product code | `productcode` |
| Product name | `productname` *(optional column)* |
| Product category | `category` |

If two columns match the same field (e.g. both `supplier_name` and `suplier_name`), the
file is rejected as ambiguous. The only aliases are the typos actually seen, plus their
correct spellings. Other names are not guessed.

### `suppliers.csv` (1,100 rows)

| Case | Handling |
|---|---|
| ZIPs as a list (`"10001, 10002"`), a range (`"10001-10100"`), or both mixed in one field | Entries are separated by commas, semicolons or whitespace. Each entry is a single ZIP or an inclusive range. |
| Spaces around a range dash (`10001 - 10100`); en/em dashes (`10001–10100`) | Treated as a normal range. |
| ZIPs missing leading zeros (`2130`, `2164-2213`) | ZIPs of 1–5 digits are read as numbers, which is the same as padding them to 5 digits (`02130`, `02164-02213`). This restores zeros lost by spreadsheets. The 4-digit case is common in the file (New England ZIPs). Shorter values are legitimate too (Puerto Rico `006xx`–`009xx`, the test ZIPs `00001`–`00098`), though none appear in the file. **Known risk:** a *truncated* ZIP (e.g. `100` meant as `10001`) can't be told apart and would be read as `00100`. |
| A range whose end has **fewer digits** than its start (`01234-5678`) | Rejected as ambiguous, since it's the shape of a ZIP+4 code. The reverse (`100-99999`) is allowed, since only the start lost zeros. |
| A reversed range (`10100-10001`), more than 5 digits, or anything that isn't digits | That entry is dropped with a warning. The supplier keeps its valid ZIPs. Dropping an entry only narrows coverage, so the service can never route to a ZIP a supplier doesn't serve. |
| Overlapping ranges or repeated ZIPs | Merged. |
| `00100-99999` ("every ZIP") | A normal range. It does **not** cover `00001`–`00099`. |
| Blank `service_zips`, or no valid entries | The supplier is kept with no local coverage and a warning is logged. It can still be used for mail order if `can_mail_order?` allows it. |
| Every-ZIP suppliers with `can_mail_order? = n` | Local for every ZIP they cover. |
| Category case and spacing (`CPAP`/`cpap`, `CPM  Machine`) | Trimmed, inner spaces collapsed, lowercased. Duplicates and blank entries removed. |
| No valid categories | Row skipped with a warning, since the supplier can't serve anything. |
| Scores written as `10`, `10.0`, or decimals | Parsed as `decimal` using invariant culture (`.` as decimal point). |
| `no ratings yet` (any letter case or spacing) | Unrated, treated as **5.5** (see A3). No warning. |
| Score blank, not a number (`N/A`, `8,5`, `1e1`), or outside 1–10 (`0`, `11`, `85`) | Unrated (5.5) with a warning. A bad score only affects ranking, never whether an order can be routed. |
| `can_mail_order?` values | `y`/`yes`/`true`/`1` and `n`/`no`/`false`/`0`, in any letter case. Anything else, including blank, is treated as **`n`** with a warning. That's safe because it never adds mail-order eligibility. |
| Blank `supplier_id` or supplier name | Row skipped with a warning. Every response entry needs both. |
| The same business name used by different suppliers | Allowed. Suppliers are identified by `supplier_id` only. |
| Mixed ID formats: `SUP-001`, `SUP-0115`, `SUP-T001` | Any non-empty ID is accepted. IDs are kept as written in responses and ordered by numeric part, then full ID (see [supplier ID ordering](#supplier-id-ordering)). |
| Repeated `supplier_id` (ignoring case and spaces) with identical data | Later copies dropped with a warning. |
| Repeated `supplier_id` with **different** data | Not in the current data. Every row with that ID is dropped with a warning until a real case needs a rule. Different ZIPs might mean several warehouses; a different rating or mail-order flag isn't safe to guess. |

### `products.csv` (1,200 rows; the spec says 200)

| Case | Handling |
|---|---|
| Product code case and spacing | Trimmed and uppercased as the lookup key. Requests match without regard to case. |
| Category case and spacing | Normalized the same way as supplier categories for matching. For responses, the first row's value is kept as written, only trimmed with repeated spaces collapsed (`CPAP` stays `CPAP`). |
| Code prefix doesn't match the category (e.g. `CM-SENT-001` is a **cpm machine**, not a commode) | The category always comes from the `category` column. It is never guessed from the code. |
| Blank product code or category | Row skipped with a warning. |
| Blank product name | Allowed. The name isn't used for routing or in responses. |
| Repeated code with identical data (5 in the current file) | Later copies dropped with a warning. |
| Repeated code where only the **name** differs | First row kept (deterministic) with a warning. The name doesn't affect routing. |
| Repeated code with a **different category** (after normalizing) | An unsafe mismatch. **Every** row for that code is dropped with a warning, so orders for it fail as an unknown product instead of being routed on a guess. |

---

## 5. Implementation Assumptions

These are decisions on points the spec leaves open. They were reviewed and approved
before implementation.

| # | Topic | Decision | Rationale |
|---|---|---|---|
| A1 | Local suppliers when `mail_order` is true | Still eligible. They are preferred over mail order when ratings are similar (see A2). | Priority 4 only makes sense if both local and mail-order suppliers are eligible. |
| A2 | "Ratings are similar" | Within **1.0** of the best rating, with exactly 1.0 included. Within the band: local first, then higher rating, then `supplier_id`. | Ratings are customer feedback, so a 1-point gap is within normal variation. Measuring from the best rating keeps the band from chaining. One side effect: an unrated local supplier (5.5) beats a 6.5 mail-order supplier, which also helps new suppliers build up a rating (see A3). |
| A3 | Suppliers with `no ratings yet` | Treated as **5.5**, the middle of the 1–10 scale. | Gives new suppliers a fair chance to win orders and build up a rating, without ranking them above suppliers with good ratings. |
| A4 | `mail_order` missing or `null` | Defaults to **`false`**. `null` is treated as missing (decided in review). | Only local suppliers that serve the ZIP are used. The order can still be routed, with no risk of sending it to a supplier that can't deliver to the customer. |
| A5 | Comparing plans with the same number of suppliers | Plans whose **average rating per supplier** (each supplier counts once) is within 1.0 of the best, then **more local suppliers**, then higher rating, then `supplier_id`. | Ratings reflect customer feedback, so the plan with higher-rated suppliers overall is preferred. The worst supplier doesn't decide on its own, and a poor supplier can't outweigh a better one by handling more items. |
| A6 | Ordering by `supplier_id` | Used **only** when shipment count, rating and locality are all equal. Ordered by numeric part, lowest first. If the numbers match, the full ID compared as text decides (`SUP-002` before `SUP-T002`). | Deterministic. The mixed ID widths would give a misleading order as plain text. |
| A7 | Some items can't be routed | The whole order fails, and every unroutable item is listed. | `feasible` is a single yes/no for the whole order. |
| A8 | The second `fulfillment_mode` value | `"mail_order"`. | The spec only shows `"local"`. This matches the request field name. |
| A9 | `category` in the response | The value from `products.csv` as written: trimmed, repeated spaces collapsed, case unchanged. | Matches the spec's example. |
| A10 | ZIP+4 or numeric ZIP | ZIP+4 is rejected with the spec's ZIP error. A **numeric ZIP** (JSON integer 0–99999) is accepted and padded to 5 digits; a 4-digit *string* is not (decided in review). | A number can't keep a leading zero, so padding restores it, the same as for supplier ZIPs. A string can keep one, so a short string is treated as a mistake. |
| A11 | Duplicate product lines in one order | Kept as separate lines and routed together. | The customer's order is not changed. |
| A12 | Quantity | Echoed back only. A line is never split. | There is no inventory or capacity data. |
| A13 | `order_id` | Optional and not validated. Not included in the response. | The spec's response doesn't include it. |
| A14 | Order of the `routing` array | Order in which each supplier's first item appears in the request. | Deterministic and easy to read. |
| A15 | Supplier names vs. their data | **Follow the data, never the name.** `SUP-T002 "Sentinel Mail-Only High"` serves ZIP `00002` locally, and `SUP-T013 "Sentinel Mail-Only CPM"` covers `00100-99999`, which is every real ZIP, so both are local there (decided in review). | The names are likely meant to mislead. Only `service_zips` and `can_mail_order?` decide eligibility and fulfillment mode. |
| A16 | Errors reported together | Validation errors stop processing; unknown-product and supplier errors are only reported for a valid request (decided in review). | Minimal processing: fixing the request at the source usually reveals any remaining problems. |

**Note:** the spec's success example (`SUP-005` for `WC-STD-001` at ZIP `10015`) is only
an illustration. `SUP-005` doesn't carry wheelchairs and doesn't serve ZIP `10015`, so it
is not a result to reproduce.

---

## 6. Expected Results for Known Orders

These results were worked out from the provided data using the rules above. They will be
used as test cases.

### Sample orders (`sample_orders.json`)

| Order | ZIP / mail order | Expected result |
|---|---|---|
| ORD-001 | `10015` / no | All items to **SUP-0636** (6.8), local. 4 suppliers can take the whole order; this one has the highest rating. |
| ORD-002 | `77059` / no | All items to **SUP-0928** (6.8), local. Only 2 suppliers can take the whole order. One shipment beats higher-rated splits. |
| ORD-003 | `02130` / yes | All items to **SUP-023** (10.0), mail order. It ties with SUP-065 on rating and mode, and wins on `supplier_id`. This relies on padding 4-digit ZIPs and on case-insensitive category matching. |

### Test fixtures (`SUP-T0xx` suppliers, `*-SENT-001` products)

| ZIP / mail order | Items | Checks | Expected result |
|---|---|---|---|
| `00001` / no | WC-SENT-001 | Mail-order suppliers excluded when mail order is off | **SUP-T001**, local |
| `00001` / yes | WC-SENT-001 | Local preference when ratings are similar (9.0 vs 10.0) | **SUP-T001** (9.0), local. It is within 1.0 of the 10.0 mail-order suppliers, and local wins. |
| `00003` / no | CN-SENT-001 | Tie on rating and mode | **SUP-T003** (`supplier_id`) |
| `00005` / yes | WK-SENT-001 | Local preferred when ratings are equal | **SUP-T005** (10.0, local), ahead of 10.0 mail-order suppliers |
| `00007` / no | WC-, CN-, WK-SENT-001 | Fewest shipments beats rating | **SUP-T007** (6.0), one shipment, ahead of three 10.0 specialists |
| `00040` / no | RL-SENT-001 | Unrated suppliers (5.5), tie | **SUP-T011** |
| `00040` / yes | RL-SENT-001 | Rated mail-order supplier vs. unrated local | **SUP-021** (10.0), mail order |
| `00040` / yes | CM-SENT-001 | Category comes from the column, not the code prefix | **SUP-021** (10.0), mail order |
| `00040` / no | CM-SENT-001 | No supplier covers the ZIP | `feasible: false` |
| `00098` / no | WC-SENT-001 | Exact ZIP match | **SUP-T014**, local |
| `00099` / no | WC-SENT-001 | `00100-99999` doesn't cover `00099` | `feasible: false` |

---

## 7. Design

### Goals

In priority order: correctness, explicit compliance with the spec, easy unit testing,
simple architecture, and no unnecessary frameworks or abstractions.

### Solution layout

```
OrderRouter.sln
Dockerfile
suppliers.csv, products.csv        # reference data (copied to the build output)
src/
  OrderRouter.Core/                # class library: all business logic, no ASP.NET reference
    Domain/                        # models and value types
    Data/                          # CSV reading and parsing into domain models
    Routing/                       # request validation and routing
  OrderRouter.Api/                 # ASP.NET Core Minimal API: HTTP only
tests/
  OrderRouter.Tests/               # NUnit: Core unit tests + API endpoint tests
```

- **Two production projects.** Core holds every rule and depends only on the .NET base
  library, including `System.Text.Json`. Api is a thin HTTP layer.
- **No interfaces, mediator, repository pattern or DI abstractions in Core.** Core
  classes are plain and are built directly in tests.
- **No external NuGet packages in production code.** The test project uses NUnit and
  `Microsoft.AspNetCore.Mvc.Testing`.

### Domain models (`Core/Domain`)

All models are immutable `record`s. `Supplier` and `Product` check their values when
created and throw if a value is invalid (blank ID, name or category; no categories; rating
outside 1–10). Parsers report bad data as warnings before building these objects. The
checks catch mistakes in code and tests that create models directly.

| Type | Contents | Notes |
|---|---|---|
| `Product` | `Code` (normalized), `Name`, `Category` (as written), `CategoryKey` (normalized) | `Category` is returned in responses. `CategoryKey` is used for matching. |
| `Supplier` | `Id`, `Name`, `ServiceArea`, `CategoryKeys` (set), `Rating` (`decimal?`, null when unrated), `EffectiveRating` (`decimal`, 5.5 when unrated), `CanMailOrder` | Owns the rating constants `MinRating = 1`, `MaxRating = 10`, `UnratedScore = 5.5`. Keeping them here means the domain doesn't depend on routing code. |
| `ZipCoverage` | Sorted, merged, inclusive `ZipRange(Start, End)` integer ranges; `Covers(int zip)` | A single ZIP is a range where Start equals End. |
| `ReferenceData` | `Products` (dictionary by code, ignoring case), `Suppliers` (list) | Loaded once and shared read-only. |
| `Order` | `OrderId?`, `CustomerZip` (string), `Zip` (int), `MailOrder` (bool), `Lines` | Only created by the request parser after validation passes. |
| `OrderLine` | `Position` (1-based), `ProductCode`, `Quantity` | |
| `FulfillmentMode` | enum: `Local`, `MailOrder` | Serialized as `"local"` / `"mail_order"`. |
| `RoutedItem` | `ProductCode`, `Quantity`, `Category`, `FulfillmentMode` | |
| `Shipment` | `SupplierId`, `SupplierName`, `Items` | One entry in `routing`. |
| `RoutingDecision` | `Feasible`, `Shipments`, `Errors` | The routing outcome in domain terms. Built only by `Success(...)` / `Failure(...)`, so it can never carry both shipments and errors. Converted to the API's `RoutingResult` response class. |
| `SupplierIdComparer` | `IComparer<string>`: numeric part, then full ID compared as text | See [supplier ID ordering](#supplier-id-ordering). |

### Data loading and parsing (`Core/Data`)

| Class | Responsibility |
|---|---|
All rules are listed in [section 4](#4-data-handling).

| Class | Responsibility |
|---|---|
| `CsvReader` | Splits CSV text into rows of raw fields, each with its starting line number. Handles quotes, `""`, line breaks inside quotes, byte-order mark and all line endings. Throws `DataFileException` on an unclosed quote. No trimming or interpretation. |
| `CsvColumns` | Finds required and optional columns by normalized header name and accepted aliases. Throws `DataFileException` if a required column is missing or ambiguous. |
| `Normalize` | Shared text rules: header names, category keys (trim, collapse spaces, lowercase), product codes (trim, uppercase), supplier ID keys. |
| `ZipCoverageParser` | Parses a `service_zips` value into a `ZipCoverage` plus a problem message for each dropped entry. |
| `RatingParser` | Turns a score into `decimal?` (null = unrated) plus an optional problem message. |
| `MailOrderParser` | Turns a `can_mail_order?` value into `bool` plus an optional problem message. |
| `SupplierCsvParser` | CSV text → suppliers + warnings. Applies the row, field and duplicate rules. |
| `ProductCsvParser` | CSV text → products + warnings. Applies the row and duplicate rules. |
| `ReferenceDataLoader` | Reads both files from a directory and returns `ReferenceData` plus all warnings. Throws `DataFileException` for a missing file. Core only returns warnings; logging them is the Api's job. |
| `DataWarning` | `File`, `Line`, `Message`. |
| `DataFileException` | A file-level problem that stops startup. |

### Validation and routing (`Core/Routing`)

| Class | Responsibility |
|---|---|
| `OrderRequestParser` | Takes the **raw request body as a string** and returns either an `Order` or a list of errors. It parses with `JsonDocument` and checks each field by hand. Automatic binding to typed classes would throw on type mismatches (e.g. `"quantity": "abc"`) instead of collecting errors. It applies every rule in [section 2](#2-validation-and-error-behavior) and collects all errors. ZIPs are checked with `^[0-9]{5}$`, not `\d`, because `\d` also matches non-ASCII digits in .NET. A quantity must be a JSON integer literal from 1 up to `int.MaxValue`. |
| `SupplierRanking` | One comparison rule used both for supplier choice and plan choice: stay within 1.0 of the best rating, then local, then higher rating, then `SupplierIdComparer`. Pure static functions. |
| `RoutingService` | `RoutingDecision Route(Order order)`. Stateless and thread-safe; holds `ReferenceData`. The steps are listed below. |
| `RoutingRules` | Constant: `SimilarityBand = 1.0m`. (`UnratedScore` is on `Supplier`.) |

**`RoutingService.Route` steps:**

1. **Look up products.** Each line gets its `Product`. Unknown codes are collected as
   errors.
2. **Find eligible suppliers.** For each distinct category in the order, a supplier is
   eligible if it covers the category and either serves the ZIP (local) or can mail when
   mail order is allowed. Categories with no eligible supplier are collected as errors.
3. **Stop on errors.** If there are any, return `Failure` with all errors from steps 1
   and 2.
4. **Narrow down the candidates.** Represent each supplier's covered categories (within
   this order) as a bitmask. Discard supplier A when some other supplier B:
   - covers **the same categories or more** (A's bitmask is contained in B's),
   - has a rating **at least as high**,
   - is **at least as local** (if A is local, B is local), and
   - is **strictly better** on rating or locality, or tied on both with a **lower ID**
     (`SupplierIdComparer`).

   **Why this never changes the result:** take any smallest plan containing A and swap
   in B. The plan still covers every category and still has k suppliers. B can't already
   be in the plan, because then A would be redundant and a smaller plan would exist. The
   plan's rating, local-supplier count and ID order all stay the same or improve. So A can
   never be in the winning plan.
5. **Find the fewest shipments.** For k = 1, 2, … check every combination of k
   remaining candidates. The first k with any combination covering every category is the
   minimum. There is no search-size limit (see T12).
6. **Pick the best plan.** Score each covering combination by its average
   `EffectiveRating` per supplier and its number of local suppliers. Choose the winner
   with the plan comparison rules in [section 3](#comparing-plans). Then assign each line
   to a supplier in the winning plan using `SupplierRanking`.
7. **Build the response.** Group lines into `Shipment`s in the order each supplier's
   first line appears, keeping request order within each shipment, and return `Success`.

### API layer (`OrderRouter.Api`)

`Program.cs`, the endpoint class `RouteOrderEndpoint`, the response classes in `Contracts/`,
and the Swagger samples in `Swagger/`. Its responsibilities:

1. **Startup:** work out the data directory. Use the `DATA_DIR` environment variable if
   it is set, otherwise the app's own folder, since the CSVs are copied there at build.
   Load `ReferenceData`, log a summary and each skipped-row warning, and register
   `ReferenceData` and `RoutingService` as singletons.
2. **`POST /api/route`** (`RouteOrderEndpoint.HandleAsync`):
   1. The handler takes only `HttpContext`. Nothing is bound from the request, so the
      framework never answers 400 or 415 by itself. It is mapped with a `(Delegate)` cast,
      because a handler taking only `HttpContext` would otherwise be treated as a raw
      `RequestDelegate`, which discards the returned result and sends an empty body.
   2. Read the body as a string, whatever the `Content-Type`. If it can't be read,
      respond with `Request body could not be read.`
   3. Call `OrderRequestParser`. If it fails, return a failure result.
   4. Otherwise call `RoutingService.Route`.
   5. Return `TypedResults.Ok(...)`: **always HTTP 200**. No `BadRequest`, `NotFound`,
      `Problem` or other status is ever used.
   6. The whole handler is wrapped in `try/catch`. Any exception is logged and returned
      as 200 with `feasible: false` and `Internal error while routing order.`
3. **Safety net:** `RouteOrderEndpoint.AlwaysOkMiddleware` handles every request to
   `/api/route` and `/api/route/` (ASP.NET routing accepts the trailing slash too), in any
   letter case. It leaves other paths alone.
   - **Any method other than POST:** it answers 200 with "Use POST to route an order."
     (headers only for HEAD) before routing can produce a 405.
   - **POST:** if anything throws outside the handler's own `try/catch` before the
     response starts, it writes the same 200 failure body.
4. **No internal details in responses.** Error messages are short, fixed, human-friendly
   sentences like the spec's examples. Exception messages and stack traces go to the
   server log only. Tests check this in the Development environment, where ASP.NET would
   otherwise show its detailed exception page.
5. **The request body isn't declared with `.Accepts<T>()`.** That metadata makes routing
   reject non-matching content types with **415**. The Swagger request schema and samples
   are added by `RouteOperationFilter` instead, which doesn't affect routing.
3. **Response class:** `RoutingResult` is the HTTP response contract, taken directly from
   the spec's examples. Every JSON name is set explicitly with `[JsonPropertyName]`, so
   the contract is visible in one place and doesn't depend on naming policies.
   `RoutingResult.From(RoutingDecision)` converts the Core result, and
   `RoutingResult.Failure(errors)` covers parse errors and the catch-all. Null properties
   are omitted, so a failure has no `routing` field and a success has no `errors` field.

   ```csharp
   public sealed class RoutingResult
   {
       [JsonPropertyName("feasible")] public bool Feasible { get; init; }
       [JsonPropertyName("routing")]  public IReadOnlyList<SupplierRouting>? Routing { get; init; }
       [JsonPropertyName("errors")]   public IReadOnlyList<string>? Errors { get; init; }
   }
   public sealed class SupplierRouting
   {
       [JsonPropertyName("supplier_id")]   public string SupplierId { get; init; }
       [JsonPropertyName("supplier_name")] public string SupplierName { get; init; }
       [JsonPropertyName("items")]         public IReadOnlyList<RoutedItemResult> Items { get; init; }
   }
   public sealed class RoutedItemResult
   {
       [JsonPropertyName("product_code")]     public string ProductCode { get; init; }
       [JsonPropertyName("quantity")]         public int Quantity { get; init; }
       [JsonPropertyName("category")]         public string Category { get; init; }
       [JsonPropertyName("fulfillment_mode")] public string FulfillmentMode { get; init; } // "local" | "mail_order"
   }
   ```
4. **Swagger:** Swashbuckle generates the OpenAPI document (`/swagger/v1/swagger.json`)
   and serves Swagger UI (`/swagger`) in every environment. `RouteOperationFilter` adds the
   request schema (`RouteRequest`, used for documentation only), the request samples from
   `RouteExamples`, and success and failure response samples. Only the 200 response is
   documented. See [Using Swagger](#using-swagger).
5. **No business logic in the Api project.**

`RoutingService.Route` is `virtual`. Endpoint tests can then swap in a subclass that
throws, to prove the catch-all returns 200, without adding an interface only for testing.

### Tests (`tests/OrderRouter.Tests`, NUnit)

| Area | What is covered |
|---|---|
| `CsvReader` | Quoted commas, escaped quotes, line breaks inside quotes, CRLF/LF/CR, byte-order mark, blank lines, empty fields, unclosed quote. |
| `CsvColumns` / `Normalize` | Header case, spacing and punctuation; typo aliases; missing and ambiguous columns; category and code normalization. |
| `ZipCoverageParser` | Single ZIPs, ranges, mixed lists, separators, dash variants, leading-zero padding, ZIP+4 shape, reversed and invalid entries, merging, `00100-99999` excluding `00098`. |
| `RatingParser` / `MailOrderParser` | Every accepted and rejected form listed in section 4. |
| `SupplierCsvParser` / `ProductCsvParser` | Real header names, row and field rules, identical and conflicting duplicates, warnings with line numbers. |
| `ReferenceDataLoader` (real CSVs) | Row counts, expected warnings, and spot checks from the data (4-digit ZIPs, unrated suppliers, test suppliers, category case). |
| `SupplierIdComparer` | `SUP-021` before `SUP-0199`; `SUP-002` before `SUP-T002`; `SUP-T003` before `SUP-T004`. |
| `OrderRequestParser` | Every rule in section 2, several errors together, spec messages word for word, `true` rejected as a quantity, `1.0` rejected, missing `mail_order` becoming `false`, extra fields ignored, arrays and invalid JSON rejected. |
| `SupplierRanking` / `RoutingService` (small in-memory data) | Each priority on its own: eligibility, fewest shipments beating rating, the 1.0 band (edge included), local within the band, per-supplier averaging, unrated as 5.5, ID tie-break, partial failure listing every error, response ordering. |
| `RoutingService` (real CSVs) | Every row in [section 6](#6-expected-results-for-known-orders). |
| API endpoint (`WebApplicationFactory`) | Always 200 with a JSON body: every Swagger sample, malformed bodies (empty, invalid JSON, XML, arrays, `sample_orders.json` itself, no body), any or no `Content-Type`, path variants, validation and routing failures, and a service that throws. Also checks the exact field names and order, that the body matches the spec's failure example and the Swagger success sample exactly, and that no internal details leak. |
| API unit tests | An unreadable body, invalid UTF-8, the safety-net middleware, and serialization of `RoutingResult`. |
| Swagger | The UI is served; the document lists every sample, only a 200 response, and the request fields. |

### Technical decisions

| # | Topic | Decision | Rationale |
|---|---|---|---|
| T1 | Runtime | **.NET 10 (LTS)**, ASP.NET Core Minimal API. Docker images `mcr.microsoft.com/dotnet/sdk:10.0` (build) and `aspnet:10.0` (run). | Current LTS release, and already installed locally. Minimal API avoids controller boilerplate for a single endpoint. |
| T2 | CSV parsing | **Small hand-written parser**, no NuGet package. | The data only uses quoted fields with commas. A small tested parser is easier to follow than adding a library. |
| T3 | Unparseable CSV data | **Skip the row (or only the bad value, where that's safe) and log a warning** with file, line and reason. A missing file, a missing or ambiguous required column, or an unclosed quote still fails at startup. Field-level rules are in [section 4](#4-data-handling). | Chosen during design review. One bad row shouldn't take the service down. File-level problems mean nothing can be read reliably. |
| T4 | Test tooling | **NUnit** plus `Microsoft.AspNetCore.Mvc.Testing`. No mocking or assertion libraries. | Chosen during design review. Tests use real objects and small in-memory data. |
| T5 | Data file location | CSVs are **copied to the build output** and so into the Docker image. The **`DATA_DIR`** environment variable overrides the location. | Works with no setup, while allowing different data. |
| T6 | Rating type | `decimal`. | Exact band comparison (`<= 1.0m`) with no floating-point rounding. |
| T7 | Request parsing | Raw body string → `JsonDocument` → validated by hand in Core. | Type errors become collected validation errors instead of framework 400s, and validation is unit-testable with plain JSON strings. |
| T8 | Response contract | An explicit `RoutingResult` response class in the Api project, with `[JsonPropertyName]` on every field, converted from Core's `RoutingDecision`. Chosen during design review. | The HTTP contract is visible in one place and taken from the spec. Core stays free of HTTP and JSON concerns. |
| T9 | Testing the catch-all | `RoutingService.Route` is `virtual`. | Lets a test inject a failure without an interface created only for testing. |
| T10 | Search approach | Exact search over candidates narrowed by dominance (see routing steps 4–5). | Always finds the true best plan. Cost is about C(P,1) + … + C(P,k) for P remaining candidates and k suppliers needed. The general problem (set cover) can grow exponentially, but pruning keeps it small in practice. |
| T11 | Measuring "more local" between plans | Number of **local suppliers** in the plan, not local line items. Chosen during design review. | Matches the per-supplier rating average. With line items, a better-rated supplier could make a plan less local by shifting the rating band, which makes an exact fast search impossible. No known result changes. |
| T12 | Search-size limit | **None for now.** Chosen during design review: add one only if the data makes it a real issue. | Measured on the current data, no search needs more than one supplier. After normalizing case there are 24 categories, and every real ZIP (00100 and up) has a local every-ZIP supplier covering all 24. The worst case is an all-24-category mail-order order: 493 eligible suppliers, 102 after pruning, 102 plans checked. Only data where ZIPs are served solely by specialist suppliers would need a limit. At that point, the recommended option is to fail safely (`feasible: false`, logged) rather than fall back to a greedy plan that could break the priority order. |
| T13 | Categories per order | At most **64 distinct categories** in one order, since categories are tracked as bits in a 64-bit number. More would be an internal error. | The catalog has 24 categories, so no order can reach the limit. Raising it would only mean a wider bit set. |
| T14 | API documentation | **Swashbuckle.AspNetCore 10** for the OpenAPI document and Swagger UI, enabled in every environment. Request and response samples live in code (`RouteExamples`), and every sample is also run as a test. | Requested for easier manual testing. One package covers both document and UI. Testing the samples keeps the documentation from drifting away from the real behavior. |
| T15 | Error detail in responses | Only fixed, human-friendly messages (section 2). Exceptions are logged, never returned. | Matches the spec's error examples, and never exposes stack traces or internal details. |
| T16 | Other HTTP methods on `/api/route` | **200** with `feasible: false` and "Use POST to route an order." for every non-POST method (headers only for HEAD). Handled in middleware, so it covers non-standard methods too. Chosen during review. | Keeps every response on the endpoint's path at 200, so clients never see a 405. |

---

## 8. Build and Run

### Run locally

Requires the .NET 10 SDK. From the repository root:

```bash
dotnet run --project src/OrderRouter.Api
```

The service listens on `http://localhost:5176` and opens Swagger UI in your browser. To use
another port: `dotnet run --project src/OrderRouter.Api --no-launch-profile --urls http://localhost:8080`.

At startup the log shows how many suppliers and products were loaded, plus one warning per
skipped or corrected data row (the 5 duplicate products in `products.csv`). To load
different data files, set `DATA_DIR` to a folder containing `suppliers.csv` and
`products.csv`.

### Run in Docker

Only Docker is needed; the .NET SDK is not. From the repository root:

```bash
docker build -t order-router .
docker run --rm -p 8080:8080 order-router
```

Then open **http://localhost:8080/swagger**, or call the endpoint directly:

```bash
curl -X POST http://localhost:8080/api/route \
  -H "Content-Type: application/json" \
  -d '{"order_id":"ORD-001","customer_zip":"10015","mail_order":false,"items":[{"product_code":"WC-STD-001","quantity":1},{"product_code":"OX-PORT-024","quantity":1}]}'
```

Run the full test suite in Docker:

```bash
docker build --target test -t order-router-tests .
docker run --rm order-router-tests
```

To use different data files, mount a folder containing `suppliers.csv` and
`products.csv` and point `DATA_DIR` at it:

```bash
docker run --rm -p 8080:8080 -e DATA_DIR=/data -v "$PWD/my-data":/data:ro order-router
```

About the image:

| Stage | Purpose |
|---|---|
| `build` | .NET 10 SDK. Restores (cached separately from source changes) and builds the whole solution in Release. |
| `test` | Runs `dotnet test` on the built solution. Only used with `--target test`. |
| `publish` | Publishes the API; the CSV files are copied next to it. |
| `final` | The small `aspnet:10.0` runtime image. Listens on port **8080** and runs as the image's non-root `app` user. Swagger is included. |

### Using Swagger

Swagger UI is enabled in every environment for manual testing.

| URL | What it is |
|---|---|
| `http://localhost:5176/swagger` | Swagger UI |
| `http://localhost:5176/swagger/v1/swagger.json` | The OpenAPI document (import into Postman, Insomnia, etc.) |

To send a request:

1. Open `/swagger` and expand **POST /api/route**. "Try it out" is already on.
2. Open the **Examples** drop-down above the request body and pick a sample. Its
   description says what it demonstrates and what result to expect. You can also edit the
   JSON freely.
3. Click **Execute**. The response appears below with **HTTP 200** every time. Check the
   `feasible` field, then `routing` or `errors`.

The samples:

| Sample | Shows | Expected result |
|---|---|---|
| ORD-001: spec example | The spec's request / sample order ORD-001 | `SUP-0636`, local |
| ORD-002 | Four categories in one local shipment | `SUP-0928`, local |
| ORD-003 | Mail order; ZIP with a leading zero (`02130`) | `SUP-023`, mail order |
| ZIP sent as a number | `2130` padded to `02130` (same order as ORD-003) | `SUP-023`, mail order |
| mail_order omitted | `mail_order` missing (or `null`) means false; product codes ignore case; quantity echoed | `SUP-0636`, local |
| Mail order off | Only local suppliers are used | `SUP-T001`, local |
| Similar ratings | Local 9.0 beats mail-order 10.0 (within 1.0) | `SUP-T001`, local |
| Fewest shipments | One 6.0 supplier beats three 10.0 specialists | `SUP-T007`, local |
| Exact tie | Identical suppliers: lowest ID wins | `SUP-T003`, local |
| Unrated suppliers | "no ratings yet" scored 5.5 | `SUP-T011`, local |
| Much higher rating | 10.0 mail order beats unrated local (more than 1.0 apart) | `SUP-021`, mail order |
| No supplier | Nobody serves ZIP 00099 | `feasible: false` |
| Unknown product | The whole order fails; no partial routing | `feasible: false` |
| Spec failure example | Empty items and a bad ZIP | The spec's two error messages |
| Wrong value types | ZIP with a letter, string `mail_order`, string quantity | Three errors |

Every sample is also sent by the automated tests, so the expected results above are
checked on each test run.

The response section in Swagger lists only **200**, with a *success* and a *failure*
sample. Invalid JSON can't be picked from the Examples list (samples must be valid JSON),
but you can type it into the request body, or use `curl`:

```bash
curl -i -X POST http://localhost:5176/api/route -H "Content-Type: application/json" -d '{oops'
# HTTP/1.1 200 OK
# {"feasible":false,"errors":["Request body must be a JSON object."]}
```

**Other HTTP methods also get 200.** `POST` is the only method that routes an order. Any
other method on `/api/route` or `/api/route/` (`GET`, `PUT`, `DELETE`, even non-standard ones) returns
**200** with `{"feasible":false,"errors":["Use POST to route an order."]}`, never a 405.
`HEAD` returns 200 with headers only, since HTTP doesn't allow a body on HEAD responses.
These aren't listed in Swagger, which documents only the POST operation.

**What the app can't control.** The web server (Kestrel) rejects some requests at the
protocol level, before any application code runs. Examples: a malformed HTTP request
line, a missing `Host` header on HTTP/1.1, or headers or a URL larger than its limits.
These get Kestrel's own 4xx response. A request body over the size limit *is* handled by
the app and still gets 200 ("Request body is too large.").

## 9. Testing

Requires the .NET 10 SDK. From the repository root:

```bash
dotnet test
```

The data-driven tests read the real `suppliers.csv` and `products.csv`. These are copied
unchanged into the test output folder.

| Test area | What it covers |
|---|---|
| Data parsing (`Data/`) | CSV format, headers, ZIPs, ratings, mail-order flags, duplicates, and loading the real files. |
| `OrderRequestParserTests` | Every request field with good, bad and missing values; body-level problems; collecting several errors; the spec messages word for word. |
| `RoutingServiceTests` | Each spec priority on its own: feasibility, mail-order and local rules, fewest shipments, quality, the 1.0 band (including the edge and no chaining), unrated suppliers, tie-breaks, item assignment, and output order. |
| `RoutingPruningTests` | 500 random data sets, each routed with and without candidate narrowing. The results must be identical. |
| `RoutingOracleTests` | An independent brute-force check that uses no routing code: 2,000 random made-up orders and 500 random real-data orders. For each feasible order it checks that every requested line appears exactly once with its quantity, every item goes to an eligible supplier with the right mode and category, the shipment count is the true minimum, and the chosen plan wins under the rating-band and local rules. |
| `RealDataRoutingTests` | Every expected result in [section 6](#6-expected-results-for-known-orders), on the real CSV files. Each order in the real `sample_orders.json` file is also sent exactly as written, with its own property names and extra fields. |
| `SupplierRankingTests`, domain tests | The ranking rule, supplier ID ordering, and the checks each model makes when created. |
| `RouteOrderEndpointTests` | The real HTTP pipeline, in memory: always 200, the exact response contract, every Swagger sample, malformed bodies, any content type, other HTTP methods (including HEAD and non-standard ones), a crashing service with no internal details leaked, and the Swagger document. |
| `RouteOrderEndpointUnitTests` | An unreadable body, invalid UTF-8, the safety-net middleware, and serialization of the response class. |

To run the tests without the .NET SDK, use the Docker test image (see
[Run in Docker](#run-in-docker)).
