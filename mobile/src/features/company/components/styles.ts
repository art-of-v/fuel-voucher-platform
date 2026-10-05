import { StyleSheet } from 'react-native';

/**
 * Presentation styles shared by the company screen and its extracted sections.
 *
 * Five of these (`card`, `sectionHeader`, `sectionTitle`, `smallBtn`, `emptyText`)
 * are used by five of the seven components, so they live here rather than being
 * copied into each one — a repeated copy of a style is how two sections end up
 * looking subtly different. The roster ones (`rowHeader`, `drillDown`, `searchField`,
 * ...) came in with the hub's workers branch and are grouped with the rest for the
 * same reason.
 */
export const styles = StyleSheet.create({
  actionRow: {
    flexDirection: 'row',
    gap: 8,
  },
  btnLabel: {
    fontFamily: 'Inter-Black',
    fontSize: 11,
    letterSpacing: 0.8,
  },
  card: {
    padding: 18,
    borderRadius: 12,
    borderWidth: 1,
    marginBottom: 18,
  },
  checkbox: {
    width: 22,
    height: 22,
    borderRadius: 6,
    borderWidth: 2,
    alignItems: 'center',
    justifyContent: 'center',
  },
  confirmBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    paddingVertical: 16,
    borderRadius: 12,
  },
  drillDown: {
    // A roster row's vouchers, opened in place under it.
    gap: 12,
    paddingTop: 12,
    borderTopWidth: 1,
  },
  emptyText: {
    fontFamily: 'Inter-Medium',
    fontSize: 13,
    textAlign: 'center',
    paddingVertical: 8,
  },
  groupHeader: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 13,
    letterSpacing: 2,
    textTransform: 'uppercase',
    marginTop: 4,
  },
  iconBtn: {
    width: 48,
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
  },
  input: {
    flex: 1,
    borderRadius: 10,
    paddingHorizontal: 14,
    paddingVertical: 12,
    fontFamily: 'Inter-Bold',
    fontSize: 14,
    borderWidth: 1,
  },
  memberRow: {
    // Worker name + joined/issued line on their own full-width row, with the
    // action buttons on a row below. On a phone two labelled buttons never fit
    // beside the text; the old side-by-side row squeezed the subtitle down to
    // one glyph per line.
    flexDirection: 'column',
    alignItems: 'stretch',
    gap: 12,
    paddingVertical: 12,
    paddingHorizontal: 12,
    borderRadius: 10,
    borderWidth: 1,
  },
  metaText: {
    fontFamily: 'Inter-Medium',
    fontSize: 12,
  },
  modalHeader: {
    flexDirection: 'row',
    alignItems: 'flex-start',
  },
  modalOverlay: {
    flex: 1,
    justifyContent: 'flex-end',
    // Scrim colour comes from `tokens.colors.overlay` at the call site. Five
    // screens each picked their own black alpha (0.6 / 0.7 / 0.8 / 0.92); the
    // scrim is now one value that also lightens correctly on the light themes.
  },
  modalSheet: {
    borderTopLeftRadius: 20,
    borderTopRightRadius: 20,
    borderWidth: 1,
    padding: 20,
    paddingBottom: 40,
    gap: 16,
  },
  rosterBody: {
    gap: 12,
    paddingHorizontal: 12,
    paddingBottom: 14,
    borderTopWidth: 1,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    paddingVertical: 10,
    paddingHorizontal: 12,
    borderRadius: 10,
    borderWidth: 1,
  },
  rowHeader: {
    // A roster row's own tap target: name + counters, with the expand chevron.
    // Kept as a full-width `Pressable` so the whole header opens the drill-down,
    // not just the name.
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    paddingVertical: 4,
  },
  rowTitle: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 16,
  },
  searchField: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    paddingHorizontal: 12,
    borderRadius: 10,
    borderWidth: 1,
  },
  searchInput: {
    flex: 1,
    paddingVertical: 10,
    fontFamily: 'Inter-Bold',
    fontSize: 14,
  },
  sectionHeader: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 10,
    marginBottom: 16,
  },
  sectionTitle: {
    fontFamily: 'Rajdhani-SemiBold',
    fontSize: 12,
    letterSpacing: 3,
    textTransform: 'uppercase',
  },
  smallBtn: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 5,
    paddingHorizontal: 10,
    paddingVertical: 7,
    borderRadius: 8,
    borderWidth: 1,
  },
  statCard: {
    flex: 1,
    alignItems: 'center',
    paddingVertical: 14,
    borderRadius: 12,
    borderWidth: 1,
    gap: 4,
  },
  statLabel: {
    fontFamily: 'Inter-Medium',
    fontSize: 10,
    letterSpacing: 1,
    textTransform: 'uppercase',
  },
  statValue: {
    fontFamily: 'Rajdhani-Bold',
    fontSize: 22,
  },
  statsRow: {
    flexDirection: 'row',
    gap: 10,
    marginBottom: 18,
  },
  voucherPick: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    padding: 12,
    borderRadius: 10,
    borderWidth: 1,
  },
});
