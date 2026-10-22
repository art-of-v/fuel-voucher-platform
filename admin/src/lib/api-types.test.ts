import { describe, expect, it } from "vitest";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

/**
 * The admin's money screens and the backend's DTOs have to agree on the field names.
 *
 * <p>
 * They used to have no way to disagree visibly: every answer was typed `any`, so a field the
 * server had renamed was simply `undefined` at runtime, and the screen that read it showed
 * nothing at all. That is not hypothetical - the voucher screen asked for `brands` and
 * `fuelTypesByBrand` while the server had been sending `providers` and `fuelTypesByProvider` for
 * months, and the expiry column asked for `providerExpirationDate` while the server sends
 * `supplierExpirationDate`. Both filters were dead and both columns were blank, and nothing
 * complained.
 *
 * <p>
 * This test is the complaint. It reads the backend's DTOs and checks that every field the
 * admin's types name is a field the backend actually sends - so a rename on the server breaks
 * the build here, where the compiler points at the screen, instead of quietly emptying a column.
 */
const adminRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "..",
  "..",
);
const backendRoot = path.resolve(
  adminRoot,
  "..",
  "backend",
  "src",
  "FuelFlow.API",
);

/** Every `.cs` file under the backend's API project, read once. */
function backendSources(): { file: string; text: string }[] {
  const out: { file: string; text: string }[] = [];
  const walk = (dir: string) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name.endsWith(".cs")) {
        out.push({
          file: path.relative(backendRoot, full),
          text: fs.readFileSync(full, "utf8"),
        });
      }
    }
  };
  walk(backendRoot);
  return out;
}

/** The C# property names declared by a type, camelCased the way the wire carries them. */
function propertiesOf(
  typeName: string,
  sources: { file: string; text: string }[],
): Set<string> {
  const camel = (name: string) => name.charAt(0).toLowerCase() + name.slice(1);

  for (const { file, text } of sources) {
    // The type's declaration: a record's primary constructor, or a class body.
    const declaration = new RegExp(
      `public\\s+(?:(?:sealed|abstract|static|partial)\\s+)*(?:record|class|struct)\\s+${typeName}\\b`,
    );
    const match = declaration.exec(text);
    if (!match) continue;

    const names = new Set<string>();

    // A record's primary constructor: the positional parameters are its properties.
    const record = new RegExp(
      `public\\s+(?:(?:sealed|abstract|static|partial)\\s+)*record\\s+${typeName}\\s*\\(`,
    ).exec(text);
    if (record) {
      const params = readBalanced(
        text,
        record.index + record[0].length - 1,
        "(",
        ")",
      );
      for (const param of splitTopLevel(params)) {
        // A parameter may carry a default (`int Page = 1`); the name is what comes before it.
        const name = param.split("=")[0].trim().split(/\s+/).pop();
        if (name && /^\w+$/.test(name)) names.add(camel(name));
      }
    }

    // The body: `{ get; set; }` properties and expression-bodied ones (`=> `).
    const body = readBalanced(
      text,
      match.index + match[0].length - 1,
      "{",
      "}",
    );
    const property =
      /public\s+(?:(?:static|virtual|override|new|required|readonly|const|async)\s+)*[\w.]+(?:<[^<>]*(?:<[^<>]*>[^<>]*)*>)?(?:\[\])*\??\s+(\w+)\s*(?=\{|=>)/g;
    let found: RegExpExecArray | null;
    while ((found = property.exec(body)) !== null) names.add(camel(found[1]));

    void file;
    return names;
  }
  return new Set<string>();
}

/** Reads a balanced `(...)` or `{...}` block starting at `start`, and returns its inside. */
function readBalanced(
  text: string,
  start: number,
  open: string,
  close: string,
): string {
  let depth = 0;
  for (let i = start; i < text.length; i++) {
    if (text[i] === open) depth++;
    else if (text[i] === close) {
      depth--;
      if (depth === 0) return text.slice(start + 1, i);
    }
  }
  return "";
}

/** Splits a parameter list on the commas that are not inside brackets. */
function splitTopLevel(text: string): string[] {
  const parts: string[] = [];
  let depth = 0;
  let current = "";
  for (const ch of text) {
    if (ch === "<" || ch === "(" || ch === "[") depth++;
    else if (ch === ">" || ch === ")" || ch === "]") depth--;
    if (ch === "," && depth === 0) {
      parts.push(current);
      current = "";
      continue;
    }
    current += ch;
  }
  parts.push(current);
  return parts;
}

/** The field names of the TypeScript interfaces in `api-types.ts`. */
function adminInterfaces(): Map<string, Map<string, string[]>> {
  const source = fs.readFileSync(
    path.join(adminRoot, "src", "lib", "api-types.ts"),
    "utf8",
  );
  // The comments are stripped first: a line of prose that ends in a colon is not a field.
  const withoutComments = source
    .replace(/\/\*[\s\S]*?\*\//g, "")
    .replace(/\/\/.*$/gm, "");

  const interfaces = new Map<string, Map<string, string[]>>();
  const declaration = /export interface (\w+)(?:<[^>]*>)?\s*\{/g;
  let match: RegExpExecArray | null;
  while ((match = declaration.exec(withoutComments)) !== null) {
    const body = readBalanced(
      withoutComments,
      match.index + match[0].length - 1,
      "{",
      "}",
    );
    interfaces.set(match[1], fieldsOf(body));
  }
  return interfaces;
}

/**
 * The fields of one interface body: the field name, and - for a field whose type is an inline
 * object - the members of that object.
 *
 * <p>
 * The members are kept apart because they are a different contract: `CatalogImportResult.errors`
 * is a list of the backend's `ImportErrorLine`, so its members have to be checked against that
 * type and not against the one the list itself comes from.
 */
function fieldsOf(body: string): Map<string, string[]> {
  const fields = new Map<string, string[]>();
  const field = /(\w+)\??\s*:\s*/g;
  let match: RegExpExecArray | null;
  while ((match = field.exec(body)) !== null) {
    // The field's own text: up to the `;` that ends it, at the body's own depth.
    const rest = body.slice(match.index + match[0].length);
    const end = readUntilSemicolon(rest);
    const text = rest.slice(0, end);
    const members: string[] = [];
    const object = /\{([\s\S]*?)\}/.exec(text);
    if (object) {
      const member = /(\w+)\??\s*:/g;
      let found: RegExpExecArray | null;
      while ((found = member.exec(object[1])) !== null) members.push(found[1]);
    }
    fields.set(match[1], members);
    field.lastIndex = match.index + match[0].length + end;
  }
  return fields;
}

/** The length of the text up to the `;` that ends a field, ignoring the ones inside an object. */
function readUntilSemicolon(text: string): number {
  let depth = 0;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (ch === "{" || ch === "<" || ch === "(") depth++;
    else if (ch === "}" || ch === ">" || ch === ")") depth--;
    else if (ch === ";" && depth === 0) return i;
  }
  return text.length;
}

/**
 * The two sides of the contract, one row per DTO the admin's money screens read.
 *
 * <p>
 * The right-hand column is the C# type the response is built from. The test finds it by name
 * anywhere under the API project, so a DTO that moves to another file does not break the test -
 * only a DTO that is renamed or deleted does, and that is exactly when the admin's mirror needs
 * a human.
 */
const CONTRACT: { admin: string; backend: string }[] = [
  { admin: "PagedResult", backend: "PagedResult" },
  { admin: "AdminUserOption", backend: "AdminUserOptionDto" },
  { admin: "AdminUser", backend: "AdminUserDto" },
  { admin: "AdminOrder", backend: "AdminPurchaseDto" },
  { admin: "AdminOrderLine", backend: "OrderLineItemDto" },
  { admin: "AdminVoucher", backend: "AdminVoucherListItemDto" },
  { admin: "AdminVoucherList", backend: "AdminVoucherListResponse" },
  { admin: "AdminVoucherDetail", backend: "AdminVoucherDetailDto" },
  { admin: "ImportBatch", backend: "ImportBatchDto" },
  { admin: "ImportBatchVoucher", backend: "ImportBatchVoucherDto" },
  { admin: "ImportBatchVouchersPage", backend: "ImportBatchVouchersPage" },
  { admin: "ImportBatchCost", backend: "ImportBatchCostDto" },
  { admin: "ImportBatchPnl", backend: "ImportBatchPnlDto" },
  { admin: "ReconciliationSummary", backend: "SummaryData" },
  { admin: "ReconciliationThreeWayMatchItem", backend: "ThreeWayMatchItem" },
  { admin: "ReconciliationException", backend: "ExceptionItem" },
  { admin: "ReconciliationVoucherFunnelItem", backend: "VoucherFunnelItem" },
  { admin: "ReconciliationRevenueSummaryItem", backend: "RevenueSummaryItem" },
  { admin: "ReconciliationResponse", backend: "GetReconciliationResponse" },
  { admin: "ReportPeriod", backend: "ReportPeriod" },
  { admin: "ReportSummary", backend: "ReportSummary" },
  { admin: "ReportPayment", backend: "PaymentEntry" },
  { admin: "ReportRedemption", backend: "RedemptionEntry" },
  { admin: "ReportMonthlyBreakdown", backend: "MonthlyBreakdown" },
  { admin: "AdminReport", backend: "GetReportResponse" },
  { admin: "CatalogImportResult", backend: "ImportVouchersResponse" },
  { admin: "CatalogImportResult.errors", backend: "ImportErrorLine" },
  { admin: "ParsedInvoiceLine", backend: "ParsedInvoiceLineDto" },
  { admin: "ParsedInvoiceResponse", backend: "ParseInvoiceResultDto" },
];

describe("the admin's API types and the backend's DTOs", () => {
  const sources = backendSources();
  const interfaces = adminInterfaces();

  it("has a backend to read", () => {
    expect(sources.length).toBeGreaterThan(50);
  });

  for (const { admin, backend } of CONTRACT) {
    it(`${admin} names only fields the backend's ${backend} sends`, () => {
      // A dotted name is a nested type: `Interface.field` is an object of its own, and its
      // members are checked against the C# type that object comes from.
      const [interfaceName, fieldName] = admin.split(".");
      const fields = interfaces.get(interfaceName);
      expect(
        fields,
        `${interfaceName} is not declared in src/lib/api-types.ts`,
      ).toBeDefined();

      const names = fieldName ? fields!.get(fieldName) : [...fields!.keys()];
      expect(
        names,
        `${admin} is not a field of ${interfaceName}`,
      ).toBeDefined();

      const serverFields = propertiesOf(backend, sources);
      expect(
        serverFields.size,
        `the backend's ${backend} was not found under ${backendRoot}`,
      ).toBeGreaterThan(0);

      const missing = names!.filter((field) => !serverFields.has(field));
      expect(
        missing,
        `${admin} reads ${missing.join(", ")} - the backend's ${backend} does not send ${missing.join(", ")}`,
      ).toEqual([]);
    });
  }
});
