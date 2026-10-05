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
import type { RosterMember, WorkerRosterEntry } from '../lib/stock';

import type { Voucher } from '../../../core/types/api';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Card, ListItem, SectionHeader } from '../../../core/ui';
import { Haptics } from '../../../core/utils/haptics';
import { formatExpirationDate } from '../../../core/utils/formatters';
import { buildWorkerRoster, filterRoster, ownerActionsForVoucher } from '../lib/stock';
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
> & {
  memberName: (m: CompanyMemberDto) => string;
  openGift: (m: CompanyMemberDto) => void;
  confirmFire: (m: CompanyMemberDto) => void;
  confirmRecall: (v: Voucher) => void;
  confirmBlock: (v: Voucher) => void;
};

// Distinguishes a synthesized roster row from a real membership (see `rosterMembers`).
// No member id can contain a colon, so the two namespaces cannot collide.
const FORMER_WORKER_ID_PREFIX = 'former-worker:';

/**
 * The rows the roster is built from: current members, plus one synthesized row per worker
 * who still holds company fuel without being a member. Firing removes the membership and
 * blocks whatever was still assigned, and the server still hands those frozen vouchers to
 * the owner — so without a row for them the roster would be the one place the owner
 * cannot thaw them.
 */
export function rosterMembers(members: CompanyMemberDto[], vouchers: Voucher[]): RosterMember[] {
  const rows: RosterMember[] = [...members];
  const known = new Set(members.map((m) => m.workerUserId));
  for (const v of vouchers) {
    const workerUserId = v.workerUserId;
    if (!workerUserId || known.has(workerUserId)) continue;
    known.add(workerUserId);
    rows.push({
      id: `${FORMER_WORKER_ID_PREFIX}${workerUserId}`,
      workerUserId,
      workerFirstName: v.workerFirstName,
      workerLastName: v.workerLastName,
    });
  }
  return rows;
}

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
  isBlocking: boolean;
  isRecalling: boolean;
  isUnblocking: boolean;
  onFreeze: (v: Voucher) => void;
  onRecall: (v: Voucher) => void;
  onUnfreeze: (v: Voucher) => void;
}

/**
 * One voucher inside a worker's drill-down: what it is, whose it is, and only the owner
 * actions its status allows. `ownerActionsForVoucher` is the gate — freezing and recalling
 * both need an assigned voucher on the server, and only a frozen one can be thawed back,
 * so a spent voucher explains itself rather than offering buttons that could only fail.
 */
function VoucherActionRow({
  voucher,
  holder,
  isBlocking,
  isRecalling,
  isUnblocking,
  onFreeze,
  onRecall,
  onUnfreeze,
}: VoucherActionRowProps) {
  const tokens = useDesignTokens();
  const { t } = useI18n();
  const actions = ownerActionsForVoucher(voucher.status);

  return (
    <View style={[styles.row, { borderColor: tokens.colors.borderLight }]}>
      <View style={{ flex: 1 }}>
        <Text style={[styles.rowTitle, { color: tokens.colors.text.primary }]} numberOfLines={1}>
          {voucher.provider} · {voucher.amount} {voucher.unit || t('common.liter')}
        </Text>
        <Text style={[styles.metaText, { color: tokens.colors.text.dim }]} numberOfLines={1}>
          {voucher.fuelName || voucher.fuelType}
          {holder ? ` → ${holder}` : ''}
        </Text>
      </View>
      {actions.canUnblock ? (
        <Pressable
          disabled={isUnblocking}
          onPress={() => onUnfreeze(voucher)}
          style={[
            styles.smallBtn,
            { borderColor: tokens.colors.primary },
            isUnblocking && { opacity: 0.5 },
          ]}
        >
          <Unlock size={14} color={tokens.colors.primary} />
          <Text style={[styles.btnLabel, { color: tokens.colors.primary }]}>
            {t('company.block.unblockAction')}
          </Text>
        </Pressable>
      ) : actions.canFreezeOrRecall ? (
        <>
          <Pressable
            disabled={isBlocking}
            onPress={() => onFreeze(voucher)}
            style={[
              styles.smallBtn,
              { borderColor: tokens.colors.error },
              isBlocking && { opacity: 0.5 },
            ]}
          >
            <Ban size={14} color={tokens.colors.error} />
            <Text style={[styles.btnLabel, { color: tokens.colors.error }]}>
              {t('company.block.action')}
            </Text>
          </Pressable>
          <Pressable
            disabled={isRecalling}
            onPress={() => onRecall(voucher)}
            style={[
              styles.smallBtn,
              { borderColor: tokens.colors.primary },
              isRecalling && { opacity: 0.5 },
            ]}
          >
            <RotateCcw size={14} color={tokens.colors.primary} />
            <Text style={[styles.btnLabel, { color: tokens.colors.primary }]}>
              {t('company.recall.action')}
            </Text>
          </Pressable>
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
  /** `null` for a worker who still holds fuel but is no longer a member. */
  member: CompanyMemberDto | null;
  expanded: boolean;
  /** The mutations' pending flags, shared by every row on the screen. */
  flags: {
    isFiring: boolean;
    isBlocking: boolean;
    isRecalling: boolean;
    isUnblocking: boolean;
  };
  memberName: (m: CompanyMemberDto) => string;
  onToggle: () => void;
  onGift: (m: CompanyMemberDto) => void;
  onFire: (m: CompanyMemberDto) => void;
  onFreeze: (v: Voucher) => void;
  onRecall: (v: Voucher) => void;
  onUnfreeze: (v: Voucher) => void;
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
            {member ? memberName(member) : entry.workerName || t('company.recall.formerWorker')}
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
          rejects both for someone who is not a member, so a former worker's row carries
          the voucher actions alone. */}
      {member && (
        <View style={styles.actionRow}>
          <Pressable
            onPress={() => onGift(member)}
            style={[styles.smallBtn, { borderColor: tokens.colors.primary }]}
          >
            <Ticket size={14} color={tokens.colors.primary} />
            <Text style={[styles.btnLabel, { color: tokens.colors.primary }]}>
              {t('company.members.gift')}
            </Text>
          </Pressable>
          <Pressable
            disabled={flags.isFiring}
            onPress={() => onFire(member)}
            style={[
              styles.smallBtn,
              { borderColor: tokens.colors.error },
              flags.isFiring && { opacity: 0.5 },
            ]}
          >
            <UserMinus size={14} color={tokens.colors.error} />
            <Text style={[styles.btnLabel, { color: tokens.colors.error }]}>
              {t('company.members.fire')}
            </Text>
          </Pressable>
        </View>
      )}

      {expanded && (
        <View style={[styles.drillDown, { borderColor: tokens.colors.borderLight }]}>
          <Text style={[styles.metaText, { color: tokens.colors.text.dim }]}>
            {member
              ? t('company.members.joined', formatExpirationDate(member.joinedAtUtc))
              : t('company.recall.formerWorker')}
          </Text>
          {entry.providers.length === 0 ? (
            <Text style={[styles.metaText, { color: tokens.colors.text.dim }]}>
              {t('company.recall.empty')}
            </Text>
          ) : (
            entry.providers.map((group) => (
              <View key={group.provider} style={{ gap: 10 }}>
                <Text style={[styles.groupHeader, { color: tokens.colors.text.dim }]}>
                  {group.provider} · {group.liters} {t('common.liter')}
                </Text>
                {group.items.map((v) => (
                  <VoucherActionRow
                    key={v.id}
                    voucher={v}
                    holder={voucherWorkerName(v)}
                    isBlocking={flags.isBlocking}
                    isRecalling={flags.isRecalling}
                    isUnblocking={flags.isUnblocking}
                    onFreeze={onFreeze}
                    onRecall={onRecall}
                    onUnfreeze={onUnfreeze}
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

/**
 * The hub's workers branch: one entry point instead of a flat wall of vouchers. It opens a
 * searchable roster, each row drilling down into that worker's fuel by brand, with freeze,
 * recall and unfreeze inside it.
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
    memberName,
    openGift,
    confirmFire,
    confirmRecall,
    confirmBlock,
  } = props;
  const tokens = useDesignTokens();
  const { t } = useI18n();
  // Which branch is open, what the owner is searching for inside it, and which roster
  // rows are drilled into. Expansion is keyed by member id, so collapsing one row never
  // disturbs the next refetch's state (mirrors `my-codes`' expanded orders).
  const [rosterOpen, setRosterOpen] = useState(false);
  const [rosterQuery, setRosterQuery] = useState('');
  const [expandedWorkers, setExpandedWorkers] = useState<Set<string>>(new Set());

  const toggleRoster = () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    setRosterOpen((prev) => !prev);
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
  const roster = useMemo(
    () => buildWorkerRoster(rosterMembers(members, heldVouchers), heldVouchers),
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

            {visibleRoster.length === 0 ? (
              <Text style={[styles.emptyText, { color: tokens.colors.text.dim }]}>
                {rosterQuery.trim() ? t('company.hub.noWorkerMatches') : t('company.members.empty')}
              </Text>
            ) : (
              <View style={{ gap: 12 }}>
                {visibleRoster.map((entry) => (
                  <WorkerRosterRow
                    key={entry.memberId}
                    entry={entry}
                    member={memberById.get(entry.memberId) ?? null}
                    expanded={expandedWorkers.has(entry.memberId)}
                    flags={{ isFiring, isBlocking, isRecalling, isUnblocking }}
                    memberName={memberName}
                    onToggle={() => toggleWorker(entry.memberId)}
                    onGift={openGift}
                    onFire={confirmFire}
                    onFreeze={confirmBlock}
                    onRecall={confirmRecall}
                    onUnfreeze={unfreeze}
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
