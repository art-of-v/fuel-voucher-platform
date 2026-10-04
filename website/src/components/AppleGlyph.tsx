/**
 * Apple logo glyph. Inlined rather than pulled from an icon package: the site
 * ships no icon library, and the App Store badge needs the mark at a weight
 * that matches the badge text rather than a generic UI icon.
 */
export default function AppleGlyph({
  size = 24,
  className,
}: {
  size?: number;
  className?: string;
}) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="currentColor"
      className={className}
      aria-hidden="true"
      focusable="false"
    >
      <path d="M17.05 12.04c-.03-2.75 2.24-4.07 2.34-4.13-1.28-1.87-3.27-2.13-3.98-2.16-1.69-.17-3.31 1-4.17 1-.86 0-2.19-.98-3.6-.95-1.85.03-3.56 1.08-4.51 2.73-1.93 3.35-.49 8.3 1.38 11.01.92 1.33 2 2.82 3.42 2.76 1.37-.05 1.89-.88 3.55-.88s2.13.88 3.58.85c1.48-.02 2.42-1.34 3.32-2.68 1.05-1.53 1.48-3.02 1.5-3.1-.03-.01-2.87-1.1-2.9-4.35zM14.6 3.9c.76-.92 1.27-2.2 1.13-3.48-1.09.04-2.42.73-3.2 1.64-.7.8-1.31 2.09-1.15 3.32 1.22.09 2.46-.62 3.22-1.48z" />
    </svg>
  );
}