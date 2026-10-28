import { View, Text, StyleSheet } from 'react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import type { VoucherStampTone } from '../lib/display';

type VoucherStampProps = {
  tone: VoucherStampTone;
  testID?: string;
};

/**
 * The "used" stamp over a spent voucher, borrowing the look of a GTA "WASTED" overlay: heavy
 * condensed capitals, wide tracking, double frame and a slight tilt.
 *
 * The word is not jargon of its own — it is the same `codes.used` the pill shows, so the stamp
 * speaks the reader's language. What the colour carries is the tense: yellow means the customer
 * reported the use and the brand has not confirmed it, blue means the brand did and the voucher can
 * no longer go back to being spendable.
 */
export function VoucherStamp({ tone, testID }: VoucherStampProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const role = tone === 'marked' ? tokens.colors.status.warning : tokens.colors.status.info;

  return (
    <View pointerEvents="none" style={styles.container} testID={testID ?? `voucher-stamp-${tone}`}>
      <View style={[styles.outer, { borderColor: role.base }]}>
        <View style={[styles.inner, { borderColor: role.base, backgroundColor: role.subtle }]}>
          <Text
            allowFontScaling={false}
            numberOfLines={1}
            style={[styles.text, { color: role.base }]}
          >
            {t('codes.used')}
          </Text>
        </View>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    position: 'absolute',
    top: 0,
    bottom: 0,
    left: 0,
    right: 0,
    alignItems: 'center',
    justifyContent: 'center',
    overflow: 'hidden',
  },
  outer: {
    borderWidth: 1,
    padding: 2,
    transform: [{ rotate: '-12deg' }],
  },
  inner: {
    borderWidth: 2,
    paddingHorizontal: 20,
    paddingVertical: 6,
  },
  text: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 22,
    letterSpacing: 6,
    textTransform: 'uppercase',
    textAlign: 'center',
  },
});
