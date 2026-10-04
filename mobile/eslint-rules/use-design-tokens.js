/**
 * Forbids hard-coded style values in screens, where a design token already owns
 * the number.
 *
 * The app has a real design system — `DESIGN.md` plus the token scales in
 * `src/core/design/` — and screens were ignoring it. At the time this rule was
 * added, `app/` carried roughly 600 hard-coded spacing, radius, font-size and
 * z-index numbers against 41 uses of `tokens.*`. Nothing went wrong visually;
 * what went wrong was that no spacing change could ever be made in one place,
 * because the spacing was 600 separate decisions.
 *
 * Two things this rule deliberately does not do:
 *
 * - **It does not suggest a token that does not exist.** A `paddingVertical: 14`
 *   has no entry in the spacing scale, and telling a developer to write
 *   `tokens.spacing.md` would be a lie that lands in review as a wrong number.
 *   Those report separately, naming both honest options: extend the scale, or
 *   use the nearest token and say why in a comment.
 * - **It does not touch layout values the system does not own.** `width`,
 *   `height`, `lineHeight`, `borderWidth` and `opacity` are left alone; a card's
 *   height is not a design decision, and `lineHeight` has to track its own
 *   `fontSize` ratio.
 *
 * Existing violations are baselined in `eslint-suppressions.json` (see
 * `npm run lint:baseline`), so the rule blocks new ones without turning CI red
 * on day one. It gets stricter as files are cleaned up.
 */

/**
 * Mirrors the token scales. Kept as literal data rather than imported so that
 * linting never executes app code, and so a token renamed in the design system
 * shows up here as a diff to reconcile.
 */
const SCALES = {
  spacing: {
    accessor: 'tokens.spacing',
    source: 'src/core/design/layout.ts',
    values: {
      xxs: 2,
      xs: 4,
      sm: 8,
      md: 12,
      lg: 16,
      xl: 20,
      '2xl': 24,
      '3xl': 32,
      '4xl': 40,
      '5xl': 56,
    },
  },
  radius: {
    accessor: 'tokens.radius',
    source: 'src/core/design/layout.ts',
    values: { none: 0, sm: 6, md: 10, lg: 14, xl: 20, full: 999 },
  },
  fontSize: {
    accessor: 'tokens.type',
    source: 'src/core/design/typography.ts',
    values: {
      display: 34,
      title: 26,
      heading: 20,
      sectionTitle: 13,
      body: 15,
      bodyStrong: 15,
      secondary: 13,
      caption: 12,
      label: 12,
      numericLarge: 32,
      numeric: 22,
    },
  },
  zIndex: {
    accessor: 'tokens.zIndex',
    source: 'src/core/design/layout.ts',
    values: {
      background: 0,
      content: 1,
      sticky: 10,
      header: 20,
      footer: 30,
      tabBar: 40,
      sheet: 50,
      dialog: 60,
      toast: 70,
    },
  },
};

const SPACING_PROPERTIES = [
  'padding',
  'paddingTop',
  'paddingRight',
  'paddingBottom',
  'paddingLeft',
  'paddingHorizontal',
  'paddingVertical',
  'margin',
  'marginTop',
  'marginRight',
  'marginBottom',
  'marginLeft',
  'marginHorizontal',
  'marginVertical',
  'gap',
  'rowGap',
  'columnGap',
];

/** Style property -> which scale owns its value. */
const PROPERTY_SCALE = {
  ...Object.fromEntries(SPACING_PROPERTIES.map((name) => [name, 'spacing'])),
  borderRadius: 'radius',
  fontSize: 'fontSize',
  zIndex: 'zIndex',
};

/**
 * `0` and `1` are not magic numbers: they are "none" and "one", they cannot be
 * given a token name a reader would understand, and they are already idiomatic.
 */
const ALLOWED_VALUES = new Set([0, 1]);

/** `'padding'` and `padding` both name the same property. */
function propertyName(node) {
  if (!node.computed && node.key.type === 'Identifier') return node.key.name;
  if (node.key.type === 'Literal') return String(node.key.value);
  return null;
}

module.exports = {
  meta: {
    type: 'problem',
    docs: {
      description:
        'Require design tokens instead of hard-coded spacing, radius, font-size and z-index values in screens.',
    },
    schema: [],
    messages: {
      useToken:
        '`{{property}}: {{value}}` is hard-coded and {{accessor}} already owns that number — use `{{accessor}}.{{token}}` instead.',
      noToken:
        '`{{property}}: {{value}}` is hard-coded and no token in {{accessor}} has that value. Either add it to the scale in {{source}}, or use the nearest existing token and leave a comment saying why this one differs.',
    },
  },
  create(context) {
    const sourceCode = context.sourceCode ?? context.getSourceCode();

    return {
      ObjectExpression(node) {
        for (const property of node.properties) {
          if (property.type !== 'Property') continue;

          const name = propertyName(property);
          if (!name) continue;

          const scaleKey = PROPERTY_SCALE[name];
          if (!scaleKey) continue;

          const value = property.value;
          if (!value || value.type !== 'Literal' || typeof value.value !== 'number') continue;
          if (ALLOWED_VALUES.has(value.value)) continue;

          const scale = SCALES[scaleKey];
          const match = Object.keys(scale.values).find(
            (token) => scale.values[token] === value.value,
          );

          if (match) {
            context.report({
              node: property,
              messageId: 'useToken',
              data: {
                property: name,
                value: value.value,
                accessor: scale.accessor,
                token: match,
              },
            });
          } else {
            context.report({
              node: property,
              // Point at the value rather than the whole property, so the report
              // lands on the number a reader needs to change.
              loc: sourceCode.getLoc(property.value),
              messageId: 'noToken',
              data: {
                property: name,
                value: value.value,
                accessor: scale.accessor,
                source: scale.source,
              },
            });
          }
        }
      },
    };
  },
};
