import { useMemo, useRef, useState } from 'react';
import { Animated, Pressable, Text, View } from 'react-native';
import { ChevronDown, ChevronRight, Receipt, Ticket } from 'lucide-react-native';
import type { useCompany } from '../hooks/useCompany';
import type { OrderBranchEntry } from '../lib/stock';

import { OrderCard } from '../../vouchers/components/OrderCard';
import { VoucherCard } from '../../vouchers/components';
import { brandColorFor } from '../../vouchers/lib/display';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Card, ListItem } from '../../../core/ui';
import { Haptics } from '../../../core/utils/haptics';
import { groupOrdersByBrand, orderBranchEntry } from '../lib/stock';
import { styles } from './styles';

type Data = ReturnType<typeof useCompany>;

type HubOrdersBranchProps = Pick<Data, 'companyOrders' | 'giftable'> & {
  /**
   * Opens the screen's issue-voucher modal on these vouchers, with no recipient
   * named yet. Fuel nobody holds is the company's, so unlike the roster's
   * per-worker issue the modal has to be told who it is for — that choice happens
   * inside the sheet, not here.
   */
  onIssue: (voucherIds: string[]) => void;
};

interface BrandOrdersRowProps {
  brand: string;
  orders: OrderBranchEntry[];
  /** The ids the gift modal can actually send, so the branch never offers a dead one. */
  giftableIds: Set<string>;
  expanded: boolean;
  /** Order ids opened inside the branch — the cards expand one at a time. */
  expandedOrders: Set<string>;
  onToggle: () => void;
  onToggleOrder: (orderId: string) => void;
  onIssue: (voucherIds: string[]) => void;
  pulseAnim: Animated.Value;
}

/**
 * One brand in the orders branch: how many orders it holds and — expanded — those
 * orders, each carrying the fuel it delivered that no worker holds.
 *
 * The undistributed block is the point of the branch. Fuel bought into the company
 * is a purchase the owner made, and the litres nobody has been handed yet are exactly
 * what they can still issue; they are shown inside the order they arrived with rather
 * than as a pool total, because the owner reconciles against the order, not against a
 * sum. A brand whose orders hold no undistributed fuel says nothing extra about it —
 * the block simply is not there.
 *
 * `orderBranchEntry` calls a voucher undistributed when no worker holds it, and a
 * redeemed company voucher has no worker either. So the ISSUE button is gated on the
 * ids the modal can really send (`giftableIds`) rather than on the branch's count: a
 * button that can only come back as a conflict is the same dead end the roster's
 * voucher actions used to offer.
 */
function BrandOrdersRow({
  brand,
  orders,
  giftableIds,
  expanded,
  expandedOrders,
  onToggle,
  onToggleOrder,
  onIssue,
  pulseAnim,
}: BrandOrdersRowProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  return (
    <View style={[styles.memberRow, { borderColor: tokens.colors.borderLight }]}>
      <Pressable
        onPress={onToggle}
        accessibilityRole="button"
        accessibilityState={{ expanded }}
        style={styles.rowHeader}
      >
        <View style={{ flex: 1, gap: 2 }}>
          <Text style={[styles.rowTitle, { color: tokens.colors.text.primary }]} numberOfLines={1}>
            {brand}
          </Text>
          <Text style={[styles.metaText, { color: tokens.colors.text.dim }]}>
            {`${t('codes.orders')} · ${orders.length}`}
          </Text>
        </View>
        {expanded ? (
          <ChevronDown size={18} color={tokens.colors.primary} />
        ) : (
          <ChevronRight size={18} color={tokens.colors.text.dim} />
        )}
      </Pressable>

      {expanded &&
        orders.map((entry) => (
          <OrderBlock
            key={`${brand}:${entry.order.id}`}
            entry={entry}
            giftableIds={giftableIds}
            isExpanded={expandedOrders.has(entry.order.id)}
            onToggleOrder={onToggleOrder}
            onIssue={onIssue}
            pulseAnim={pulseAnim}
          />
        ))}
    </View>
  );
}

interface OrderBlockProps {
  entry: OrderBranchEntry;
  giftableIds: Set<string>;
  isExpanded: boolean;
  onToggleOrder: (orderId: string) => void;
  onIssue: (voucherIds: string[]) => void;
  pulseAnim: Animated.Value;
}

/**
 * One order inside a brand, and — when it is open and delivered fuel nobody holds —
 * that fuel with the button that issues it.
 *
 * The card itself is the wallet's, so a purchase reads here exactly as it does there.
 * Payment and deletion stay in the wallet: `onPay`/`onDelete` are left off on
 * purpose, since the hub is not where an owner settles or throws away a purchase.
 */
function OrderBlock({
  entry,
  giftableIds,
  isExpanded,
  onToggleOrder,
  onIssue,
  pulseAnim,
}: OrderBlockProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  const issueableIds = entry.undistributed.filter((v) => giftableIds.has(v.id)).map((v) => v.id);
  const undistributedLabel = `${entry.undistributedLiters} ${t('common.liter')}`;

  // A tap on a voucher means "issue this one" — the same modal, pre-selected with that
  // single voucher. Fuel a worker already holds, or fuel already spent, has nothing to
  // issue, so its tap does nothing instead of opening a modal that could only fail.
  const issueVoucher = (voucherId: string) => {
    if (giftableIds.has(voucherId)) onIssue([voucherId]);
  };

  return (
    <View style={{ gap: 12 }}>
      <OrderCard
        order={entry.order}
        isExpanded={isExpanded}
        onToggle={onToggleOrder}
        onVoucherPress={(v) => issueVoucher(v.id)}
        brandColor={brandColorFor(entry.order.provider, tokens)}
      />

      {isExpanded && entry.undistributed.length > 0 && (
        <View
          testID="hub-orders-undistributed"
          style={[styles.drillDown, { borderColor: tokens.colors.borderLight }]}
        >
          <View style={styles.actionRow}>
            <Text
              style={[styles.groupHeader, { flex: 1, color: tokens.colors.text.dim }]}
              numberOfLines={1}
            >
              {t('company.hub.undistributedShort')} · {undistributedLabel}
            </Text>
            {issueableIds.length > 0 && (
              <Pressable
                onPress={() => onIssue(issueableIds)}
                style={[styles.smallBtn, { borderColor: tokens.colors.primary }]}
              >
                <Ticket size={14} color={tokens.colors.primary} />
                <Text style={[styles.btnLabel, { color: tokens.colors.primary }]}>
                  {t('company.members.gift')}
                </Text>
              </Pressable>
            )}
          </View>
          {entry.undistributed.map((v) => (
            <VoucherCard
              key={v.id}
              voucher={v}
              pulseAnim={pulseAnim}
              onSelect={() => issueVoucher(v.id)}
            />
          ))}
        </View>
      )}
    </View>
  );
}

/**
 * The hub's orders branch: the company's own purchases, by brand, and inside each order
 * the fuel it delivered that no worker holds — the stock the owner can still issue.
 *
 * The wallet keeps receipts only, so this is the one place a purchase is reconciled
 * against what is left of it. Grouping is by brand rather than by the order's own
 * provider (`groupOrdersByBrand`), because an order that bought from two brands would
 * otherwise hide half the fuel behind the wrong row.
 *
 * It holds only its own open/expanded state; the issue-voucher modal, the mutation and
 * the selection stay in the screen, which is where they already live for the roster.
 */
export function HubOrdersBranch({ companyOrders, giftable, onIssue }: HubOrdersBranchProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  // Which branch is open and what is drilled into. Expansion is keyed by brand and by
  // order id, so collapsing one row never disturbs the next refetch's state (mirrors
  // the roster's expanded workers).
  const [ordersOpen, setOrdersOpen] = useState(false);
  const [expandedBrands, setExpandedBrands] = useState<Set<string>>(new Set());
  const [expandedOrders, setExpandedOrders] = useState<Set<string>>(new Set());
  // Shared by every undistributed voucher card in the branch, the way the wallet shares
  // one across its lists. Held still: the hub is a management screen and its cards do
  // not need to breathe.
  const pulseAnim = useRef(new Animated.Value(1)).current;

  const toggleOrders = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    setOrdersOpen((prev) => !prev);
  };

  const toggleBrand = (brand: string) => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    setExpandedBrands((prev) => {
      const next = new Set(prev);
      if (next.has(brand)) next.delete(brand);
      else next.add(brand);
      return next;
    });
  };

  const toggleOrder = (orderId: string) => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    setExpandedOrders((prev) => {
      const next = new Set(prev);
      if (next.has(orderId)) next.delete(orderId);
      else next.add(orderId);
      return next;
    });
  };

  // The company's purchases by brand. Grouping reads every brand an order touched, not
  // just the provider of its first line item, so a two-brand order cannot hide half the
  // fuel the owner is looking for.
  const orderBrands = useMemo(
    () => groupOrdersByBrand(companyOrders, orderBranchEntry),
    [companyOrders],
  );
  // What the gift modal can really send. Kept as a set so a branch row can ask about
  // one voucher without scanning the pool, and so the branch and the modal can never
  // disagree about what is issuable.
  const giftableIds = useMemo(() => new Set(giftable.map((v) => v.id)), [giftable]);

  return (
    <View style={{ gap: tokens.spacing.xs }}>
      <Card padding="none" style={{ backgroundColor: tokens.colors.surface }}>
        <ListItem
          leading={<Receipt size={20} color={tokens.colors.primary} />}
          title={t('codes.orders')}
          subtitle={String(companyOrders.length)}
          onPress={toggleOrders}
          showChevron={false}
          trailing={
            ordersOpen ? (
              <ChevronDown size={20} color={tokens.colors.primary} />
            ) : (
              <ChevronRight size={20} color={tokens.colors.text.muted} />
            )
          }
        />

        {ordersOpen && (
          <View style={[styles.rosterBody, { borderColor: tokens.colors.borderLight }]}>
            {orderBrands.length === 0 ? (
              <Text style={[styles.emptyText, { color: tokens.colors.text.dim }]}>
                {t('company.hub.noOrders')}
              </Text>
            ) : (
              <View style={{ gap: 12 }}>
                {orderBrands.map((group) => (
                  <BrandOrdersRow
                    key={group.brand}
                    brand={group.brand}
                    orders={group.orders}
                    giftableIds={giftableIds}
                    expanded={expandedBrands.has(group.brand)}
                    expandedOrders={expandedOrders}
                    onToggle={() => toggleBrand(group.brand)}
                    onToggleOrder={toggleOrder}
                    onIssue={onIssue}
                    pulseAnim={pulseAnim}
                  />
                ))}
              </View>
            )}
          </View>
        )}
      </Card>
    </View>
  );
}
