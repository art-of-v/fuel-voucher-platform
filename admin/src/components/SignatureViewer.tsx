import { useI18n } from "@/lib/i18n";

/**
 * The customer's signature, as they drew it.
 *
 * <p>
 * The signature arrives as JSON: an array of SVG path strings, which is what the mobile's
 * signature pad captures (one path per stroke). It is drawn here rather than shown as an image
 * because that is what it is - a picture of the strokes is a second rendering of the same
 * thing, and it would need a renderer that can go wrong.
 * </p>
 *
 * <p>
 * A signature that cannot be read is shown as such, not as an empty box: an operator looking at
 * a signature has to be able to tell "this customer signed nothing" from "this record is
 * damaged", and a blank panel says neither.
 * </p>
 */
export function SignatureViewer({ data }: { data: string }) {
  const { t } = useI18n();

  try {
    const paths = JSON.parse(data);
    if (!Array.isArray(paths))
      return <div className="text-red-500">{t("contracts.signatureInvalid")}</div>;

    return (
      <svg
        viewBox="0 0 400 200"
        className="w-full h-auto"
        style={{ maxHeight: "300px" }}
      >
        {paths.map((d: string, i: number) => (
          <path
            key={i}
            d={d}
            fill="none"
            stroke="#22c55e"
            strokeWidth="4"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        ))}
      </svg>
    );
  } catch {
    return (
      <div className="p-4 text-red-500 text-center">
        {t("contracts.signatureUnreadable")}
      </div>
    );
  }
}
