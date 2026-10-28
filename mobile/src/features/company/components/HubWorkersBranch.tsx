import { useMemo, useState } from 'react';
import { Pressable, Text, TextInput, View } from 'react-native';
import {
  Ban,
  ChevronDown,
  ChevronRight,
  RotateCcw,
  Search,
  Ticket,
  Unlock,
  UserMinus,
  Users,
} from 'lucide-react-native';
import type { useCompany } from '../hooks/useCompany';
import type { CompanyMemberDto } from '../types';
import type { OrphanedCompanyFuel, WorkerRosterEntry } from '../lib/stock';

import type { Voucher } from '../../../core/types/api';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Button, Card, ListItem, SectionHeader } from '../../../core/ui';
import { Haptics } from '../../../core/utils/haptics';
import { formatExpirationDate } from '../../../core/utils/formatters';
import {
  buildWorkerRoster,
  filterRoster,
  orphanedCompanyFuel,
  ownerActionsForVoucher,
} from '../lib/stock';
import { styles } from './styles';

type Data = ReturnType<typeof useCompany>;

type HubWorkersBranchProps = Pick<
  Data,
  | 'members'
  | 'gifted'
  | 'blocked'
  | 'unblock'
  | 'isFiring'
  | 'isBlocking'
  | 'isRecalling'
  | 'isUnblocking'
  | 'isReturningToPool'
> & {
  memberName: (m: CompanyMemberDto) => string;
  openGift: (m: CompanyMemberDto) => void;
  confirmFire: (m: CompanyMemberDto) => void;
  confirmRecall: (v: Voucher) => void;
  confirmBlock: (v: Voucher) => void;
  confirmReturnToPool: (v: Voucher) => void;
};

/**
 * The holder a voucher carries with it. Attribution lives on the voucher itself, not on
 * the membership: firing deletes the member row but leaves the vouchers pointing at the
 * worker they went to, which is exactly when the owner most needs to be told whose fuel
 * they are looking at.
 */
function voucherWorkerName(v: Voucher): string {
  return [v.workerFirstName, v.workerLastName].filter(Boolean).join(' ').trim();
}

interface VoucherActionRowProps {
  voucher: Voucher;
  /** The holder as carried on the voucher, for the "→ name" attribution line. */
  holder: string;
  /**
   * Whether the voucher belongs to no current member — the fuel of somebody who no
   * longer works here. It withdraws the thaw: a thaw hands the paper back to its
   * holder, and there is nobody to hand it to.
   */
  orphaned: boolean;
  isBlocking: boolean;
  isRecalling: boolean;
  isUnblocking: boolean;
  isReturningToPool: boolean;
  onFreeze: (v: Voucher) => void;
  onRecall: (v: Voucher) => void;
  onUnfreeze: (v: Voucher) => void;
  onReturnToPool: (v: Voucher) => void;
}

/**
 * One voucher inside a worker's drill-down: what it is, whose it is, and only the owner
 * actions its status allows. `ownerActionsForVoucher` is the gate — freezing and recalling
 * both need an assigned voucher on the server, and only a frozen one can be thawed back,
 * so a spent voucher explains itself rather than offering buttons that could only fail.
 *
 * A frozen voucher has two ways out and the row offers both: the thaw hands it back to
 * whoever holds it, and the return puts it back into the company pool. The second one is
 * the only road the fuel of a fired worker has — the thaw would hand it to the person who
 * was just fired.
 */
function VoucherActionRow({
  voucher,
  holder,
  orphaned,
  isBlocking,
  isRecalling,
  isUnblocking,
  isReturningToPool,
  onFreeze,
  onRecall,
  onUnfreeze,
  onReturnToPool,
}: VoucherActionRowProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const actions = ownerActionsForVoucher(voucher.status, orphaned);

  return (
    <View style={[styles.row, { borderColor: tokens.colors.borderLight }]}>
      <View style={{ flex: 1 }}>
        <Text style={[styles.rowTitle, { color: tokens.colors.text.primary }]} numberOfLines={1}>
          {voucher.brand} · {voucher.amount} {voucher.unit || t('common.liter')}
        </Text>
        <Text style={[styles.metaText, { color: tokens.colors.text.dim }]} numberOfLines={1}>
          {voucher.fuelName || voucher.fuelType}
          {holder ? ` → ${holder}` : ''}
        </Text>
      </View>
      {actions.canUnblock ? (
        <Button
          variant="secondary"
          size="sm"
          icon={<Unlock />}
          label={t('company.block.unblockAction')}
          disabled={isUnblocking}
          onPress={() => onUnfreeze(voucher)}
        />
      ) : null}
      {actions.canReturnToPool ? (
        <Button
          variant="secondary"
          size="sm"
          icon={<RotateCcw />}
          label={t('company.recall.action')}
          disabled={isReturningToPool}
          onPress={() => onReturnToPool(voucher)}
        />
      ) : actions.canFreezeOrRecall ? (
        <>
          <Button
            variant="destructive"
            size="sm"
            icon={<Ban />}
            label={t('company.block.action')}
            disabled={isBlocking}
            onPress={() => onFreeze(voucher)}
          />
          <Button
            variant="secondary"
            size="sm"
            icon={<RotateCcw />}
            label={t('company.recall.action')}
            disabled={isRecalling}
            onPress={() => onRecall(voucher)}
          />
        </>
      ) : (
        // Keep the row honest about why there is nothing to press.
        <Text style={[styles.metaText, { color: tokens.colors.text.dim }]}>
          {t('company.recall.spentAction')}
        </Text>
      )}
    </View>
  );
}

interface WorkerRosterRowProps {
  entry: WorkerRosterEntry;
  /** The membership behind the row. Every roster row has one: a fired worker is not a row. */
  member: CompanyMemberDto;
  expanded: boolean;
  /** The mutations' pending flags, shared by every row on the screen. */
  flags: {
    isFiring: boolean;
    isBlocking: boolean;
    isRecalling: boolean;
    isUnblocking: boolean;
    isReturningToPool: boolean;
  };
  memberName: (m: CompanyMemberDto) => string;
  onToggle: () => void;
  onGift: (m: CompanyMemberDto) => void;
  onFire: (m: CompanyMemberDto) => void;
  onFreeze: (v: Voucher) => void;
  onRecall: (v: Voucher) => void;
  onUnfreeze: (v: Voucher) => void;
  onReturnToPool: (v: Voucher) => void;
}

/**
 * One worker in the roster: who they are and what they hold, with the per-worker actions
 * (hand over fuel, let them go) and — expanded — everything they were given, by brand.
 *
 * The counters are the point of the roster: they come from the vouchers themselves rather
 * than from the API's `giftedVoucherCount`, which counts only active vouchers and cannot
 * tell the owner how much is spent, frozen or left. Frozen fuel is listed only when there
 * is some, since a zero on every row is noise.
 */
function WorkerRosterRow({
  entry,
  member,
  expanded,
  flags,
  memberName,
  onToggle,
  onGift,
  onFire,
  onFreeze,
  onRecall,
  onUnfreeze,
  onReturnToPool,
}: WorkerRosterRowProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  const counters = [
    `${t('codes.stock.issuedShort')} ${entry.activeCount}`,
    `${t('codes.stock.leftShort')} ${entry.activeLiters} ${t('common.liter')}`,
    `${t('codes.stock.usedShort')} ${entry.usedCount}`,
  ];
  if (entry.frozenCount > 0) {
    counters.push(`${t('company.block.section')} ${entry.frozenCount}`);
  }

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
            {memberName(member)}
          </Text>
          <Text style={[styles.metaText, { color: tokens.colors.text.dim }]}>
            {counters.join(' · ')}
          </Text>
        </View>
        {expanded ? (
          <ChevronDown size={18} color={tokens.colors.primary} />
        ) : (
          <ChevronRight size={18} color={tokens.colors.text.dim} />
        )}
      </Pressable>

      {/* Handing over fuel and letting a worker go are membership actions: the server
          rejects both for someone who is not a member. */}
      <View style={styles.actionRow}>
        <Button
          variant="secondary"
          size="sm"
          icon={<Ticket />}
          label={t('company.members.gift')}
          onPress={() => onGift(member)}
        />
        <Button
          variant="destructive"
          size="sm"
          icon={<UserMinus />}
          label={t('company.members.fire')}
          disabled={flags.isFiring}
          onPress={() => onFire(member)}
        />
      </View>

      {expanded && (
        <View style={[styles.drillDown, { borderColor: tokens.colors.borderLight }]}>
          <Text style={[styles.metaText, { color: tokens.colors.text.dim }]}>
            {t('company.members.joined', formatExpirationDate(member.joinedAtUtc))}
          </Text>
          {entry.brands.length === 0 ? (
            <Text style={[styles.metaText, { color: tokens.colors.text.dim }]}>
              {t('company.recall.empty')}
            </Text>
          ) : (
            entry.brands.map((group) => (
              <View key={group.brand} style={{ gap: 10 }}>
                <Text style={[styles.groupHeader, { color: tokens.colors.text.dim }]}>
                  {group.brand} · {group.liters} {t('common.liter')}
                </Text>
                {group.items.map((v) => (
                  <VoucherActionRow
                    key={v.id}
                    voucher={v}
                    holder={voucherWorkerName(v)}
                    // A current member holds it, so the thaw is on the table: the fuel can go
                    // back to the worker it was frozen for.
                    orphaned={false}
                    isBlocking={flags.isBlocking}
                    isRecalling={flags.isRecalling}
                    isUnblocking={flags.isUnblocking}
                    isReturningToPool={flags.isReturningToPool}
                    onFreeze={onFreeze}
                    onRecall={onRecall}
                    onUnfreeze={onUnfreeze}
                    onReturnToPool={onReturnToPool}
                  />
                ))}
              </View>
            ))
          )}
        </View>
      )}
    </View>
  );
}

interface CompanyFuelRowProps {
  fuel: OrphanedCompanyFuel;
  expanded: boolean;
  /** The mutations' pending flags, shared by every row on the screen. */
  flags: {
    isBlocking: boolean;
    isRecalling: boolean;
    isUnblocking: boolean;
    isReturningToPool: boolean;
  };
  onToggle: () => void;
  onFreeze: (v: Voucher) => void;
  onRecall: (v: Voucher) => void;
  onUnfreeze: (v: Voucher) => void;
  onReturnToPool: (v: Voucher) => void;
}

/**
 * The fuel the company holds that no worker holds any more — a fired worker's frozen
 * vouchers, filed at the company's level rather than under the former worker's name
 * (the owner's decision, planning #294, 2026-10-21).
 *
 * It is a group of its own rather than a row per former worker, because a former worker
 * is not a worker: the membership is gone, and a roster that still carries a line for
 * somebody who no longer works here reads as if they did. The name that travels with
 * a voucher stays on the voucher — it says whose fuel it was, not who holds it now.
 *
 * The group offers the fuel back to the company, and it does not offer the thaw: a thaw
 * would hand the paper to the worker who was just fired (planning #558).
 */
function CompanyFuelRow({
  fuel,
  expanded,
  flags,
  onToggle,
  onFreeze,
  onRecall,
  onUnfreeze,
  onReturnToPool,
}: CompanyFuelRowProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();

  const counters = [`${t('company.block.section')} ${fuel.frozenCount}`];
  if (fuel.usedCount > 0) {
    counters.push(`${t('codes.stock.usedShort')} ${fuel.usedCount}`);
  }

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
            {t('company.hub.companyFuel')}
          </Text>
          <Text style={[styles.metaText, { color: tokens.colors.text.dim }]}>
            {counters.join(' · ')}
          </Text>
        </View>
        {expanded ? (
          <ChevronDown size={18} color={tokens.colors.primary} />
        ) : (
          <ChevronRight size={18} color={tokens.colors.text.dim} />
        )}
      </Pressable>

      {expanded && (
        <View style={[styles.drillDown, { borderColor: tokens.colors.borderLight }]}>
          {fuel.brands.map((group) => (
            <View key={group.brand} style={{ gap: 10 }}>
              <Text style={[styles.groupHeader, { color: tokens.colors.text.dim }]}>
                {group.brand} · {group.liters} {t('common.liter')}
              </Text>
              {group.items.map((v) => (
                <VoucherActionRow
                  key={v.id}
                  voucher={v}
                  holder={voucherWorkerName(v)}
                  // No current member holds it, so there is nobody to thaw it for: the way
                  // out is back to the company pool.
                  orphaned
                  isBlocking={flags.isBlocking}
                  isRecalling={flags.isRecalling}
                  isUnblocking={flags.isUnblocking}
                  isReturningToPool={flags.isReturningToPool}
                  onFreeze={onFreeze}
                  onRecall={onRecall}
                  onUnfreeze={onUnfreeze}
                  onReturnToPool={onReturnToPool}
                />
              ))}
            </View>
          ))}
        </View>
      )}
    </View>
  );
}

/**
 * The hub's workers branch: one entry point instead of a flat wall of vouchers. It opens a
 * searchable roster, each row drilling down into that worker's fuel by brand, with freeze,
 * recall and unfreeze inside it — and, on the company's own fuel, the way back to the pool
 * (planning #558).
 *
 * The roster replaces the workers table and both voucher lists, so this component owns
 * everything those three showed — including the counter source, which is the vouchers
 * themselves rather than the API's `giftedVoucherCount` (see `buildWorkerRoster`). It holds
 * only its own open/expanded/search state; the owner actions and their confirmations stay
 * in the screen, which is where the issue-voucher modal lives.
 */
export function HubWorkersBranch(props: HubWorkersBranchProps) {
  const {
    members,
    gifted,
    blocked,
    unblock,
    isFiring,
    isBlocking,
    isRecalling,
    isUnblocking,
    isReturningToPool,
    memberName,
    openGift,
    confirmFire,
    confirmRecall,
    confirmBlock,
    confirmReturnToPool,
  } = props;
  const tokens = useDesignTokens();
  const { t } = useI18n();
  // Which branch is open, what the owner is searching for inside it, and which roster
  // rows are drilled into. Expansion is keyed by member id, so collapsing one row never
  // disturbs the next refetch's state (mirrors `my-codes`' expanded orders).
  const [rosterOpen, setRosterOpen] = useState(false);
  const [rosterQuery, setRosterQuery] = useState('');
  const [expandedWorkers, setExpandedWorkers] = useState<Set<string>>(new Set());
  // The company's own fuel is one group, not one row per former worker, so it has its
  // own expansion rather than a key in the workers' set.
  const [companyFuelOpen, setCompanyFuelOpen] = useState(false);

  const toggleRoster = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    setRosterOpen((prev) => !prev);
  };

  const toggleCompanyFuel = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    setCompanyFuelOpen((prev) => !prev);
  };

  const toggleWorker = (memberId: string) => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    setExpandedWorkers((prev) => {
      const next = new Set(prev);
      if (next.has(memberId)) next.delete(memberId);
      else next.add(memberId);
      return next;
    });
  };

  // Everything the company has handed out and not taken back: the issued vouchers plus
  // the frozen ones, which `classifyVoucher` keeps in a separate bucket. The roster is
  // built from both so a worker's counters and drill-down cover their whole holding.
  const heldVouchers = useMemo(() => [...gifted, ...blocked], [gifted, blocked]);
  const roster = useMemo(() => buildWorkerRoster(members, heldVouchers), [members, heldVouchers]);
  // The fuel that belongs to no member any more — a fired worker's frozen vouchers. It
  // is the company's, so it is grouped at the company's level and not under the name of
  // somebody who no longer works here (planning #294).
  const companyFuel = useMemo(
    () => orphanedCompanyFuel(members, heldVouchers),
    [members, heldVouchers],
  );
  const memberById = useMemo(() => new Map(members.map((m) => [m.id, m])), [members]);
  // `filterRoster` looks a phone number up by member id, not by worker id.
  const phonesByMemberId = useMemo(
    () => Object.fromEntries(members.map((m) => [m.id, m.workerPhoneNumber])),
    [members],
  );
  const visibleRoster = useMemo(
    () => filterRoster(roster, rosterQuery, phonesByMemberId),
    [roster, rosterQuery, phonesByMemberId],
  );

  // Thawing is a plain restore, so — unlike freeze and recall — it needs no prompt and
  // takes the voucher directly.
  const unfreeze = (v: Voucher) => unblock(v.id);

  return (
    <View style={{ gap: tokens.spacing.xs }}>
      <SectionHeader title={t('company.managementTitle')} />
      <Card padding="none" style={{ backgroundColor: tokens.colors.surface }}>
        <ListItem
          leading={<Users size={20} color={tokens.colors.primary} />}
          title={t('company.members.section')}
          subtitle={String(members.length)}
          onPress={toggleRoster}
          showChevron={false}
          trailing={
            rosterOpen ? (
              <ChevronDown size={20} color={tokens.colors.primary} />
            ) : (
              <ChevronRight size={20} color={tokens.colors.text.muted} />
            )
          }
        />

        {rosterOpen && (
          <View style={[styles.rosterBody, { borderColor: tokens.colors.borderLight }]}>
            <View
              style={[
                styles.searchField,
                {
                  backgroundColor: tokens.colors.background,
                  borderColor: tokens.colors.borderLight,
                },
              ]}
            >
              <Search size={16} color={tokens.colors.text.dim} />
              <TextInput
                value={rosterQuery}
                onChangeText={setRosterQuery}
                placeholder={t('company.hub.searchPlaceholder')}
                placeholderTextColor={tokens.colors.text.dim}
                autoCapitalize="none"
                autoCorrect={false}
                style={[styles.searchInput, { color: tokens.colors.text.primary }]}
              />
            </View>

            {/* The company's own fuel sits above the roster and outside the search: it is a
                fact about the company, not a row about a person, so typing a name in the
                search must not hide the fuel the owner still has to act on. */}
            {companyFuel.vouchers.length > 0 && (
              <CompanyFuelRow
                fuel={companyFuel}
                expanded={companyFuelOpen}
                flags={{ isBlocking, isRecalling, isUnblocking, isReturningToPool }}
                onToggle={toggleCompanyFuel}
                onFreeze={confirmBlock}
                onRecall={confirmRecall}
                onUnfreeze={unfreeze}
                onReturnToPool={confirmReturnToPool}
              />
            )}

            {visibleRoster.length === 0 ? (
              <Text style={[styles.emptyText, { color: tokens.colors.text.dim }]}>
                {rosterQuery.trim() ? t('company.hub.noWorkerMatches') : t('company.members.empty')}
              </Text>
            ) : (
              <View style={{ gap: 12 }}>
                {visibleRoster.map((entry) => {
                  const member = memberById.get(entry.memberId);
                  // Every roster row comes from a membership, so the row is always there.
                  if (!member) return null;
                  return (
                    <WorkerRosterRow
                      key={entry.memberId}
                      entry={entry}
                      member={member}
                      expanded={expandedWorkers.has(entry.memberId)}
                      flags={{ isFiring, isBlocking, isRecalling, isUnblocking, isReturningToPool }}
                      memberName={memberName}
                      onToggle={() => toggleWorker(entry.memberId)}
                      onGift={openGift}
                      onFire={confirmFire}
                      onFreeze={confirmBlock}
                      onRecall={confirmRecall}
                      onUnfreeze={unfreeze}
                      onReturnToPool={confirmReturnToPool}
                    />
                  );
                })}
              </View>
            )}
          </View>
        )}
      </Card>
    </View>
  );
}
