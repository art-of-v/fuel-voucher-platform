# Supplier PDF test data

Real supplier PDFs used to verify that the import pipeline reads the **actual** document format, not
just a synthetic stand-in.

## Why this exists

Every other import test builds its PDF in code (`PdfPig` word lists in
`FuelFlow.UnitTests/Parsers/ParserTests.cs`, a generator in `VoucherImportIntegrationTests`). That
proves the parser is consistent with itself — not that it can read what the supplier actually sends.

The parsers are OCR/regex based (`OkkoVoucherParser`, `KloVoucherParser`, `WogVoucherParser`) and depend
on wording and layout: `"Дійсний" / "до" / "17.06.2025"`, fuel name wording, the litres position, and
the `<productCode>$` prefix on an OKKO QR payload. If a supplier changes any of that, the synthetic
tests stay green while the real import breaks in production.

A fixture here closes that gap: one real page per provider, imported end to end in a test.

## Required sanitisation before a file is committed

The import reads **vouchers that are live bearer instruments**. Treat the source file accordingly:

- **Destroy every QR code.** A voucher QR can be scanned at a pump and redeemed. Rendering over the
  QR modules is not enough — flatten the page to an image, or paint a solid box over each code.
- **Change every voucher number.** Voucher numbers are sequential and guessable; they map to real
  customer orders. Replace them with an obviously fake range (e.g. `99999600000000000001`).
- **Remove anything identifying the operator**: company name, tax id, contract number, account number,
  invoice totals, signatures, stamps.
- **Keep the layout.** That is the whole point — same page size, same fonts, same positions, same
  wording. Do not re-save through a tool that re-flows or re-compresses the text layer.
- One page per file is enough. Trim multi-page statements.

## Naming

```
<provider>-<n>-<what-it-covers>.pdf
```

Lowercase, hyphen-separated. Examples:

```
okko-1-basic.pdf
okko-2-expired-and-odd-fonts.pdf
wog-1-basic.pdf
klo-1-basic.pdf
```

The provider token must match what the parsers dispatch on (`OkkoVoucherParser` / `WogVoucherParser` /
`KloVoucherParser`), otherwise the fixture will be routed to the wrong strategy and the test will
fail for the wrong reason.

## How a fixture is used

A test loads the file from the output directory (the csproj copies `TestData/**`), runs the real import
pipeline, and asserts on the parsed values. Because a fixture is a fixed document, the assertions are
exact — voucher number, litres, fuel type, and the `ExpirationDate` printed on the page.

`ExpirationDate` matters twice over: it is the source of `provider_expiration_date` (the real supplier
term, the ceiling a customer's renewal can never exceed — see planning issue #166), so a fixture whose
date drifts silently changes what the renewal feature is allowed to sell.

## Adding one

1. Drop the sanitised PDF in this folder.
2. Add a test that imports it and asserts the parsed values.
3. Note in the test what the fixture protects against, so nobody "simplifies" it away later.