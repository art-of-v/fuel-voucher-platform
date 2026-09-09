import { View, Text, StyleSheet, Image, Animated } from 'react-native';
import { useRouter } from 'expo-router';
import { useStations } from '../src/features/stations/hooks/useStations';
import { PageLayout } from '../src/components/page-layout';
import { EmptyState, ErrorState, LoadingState } from '../src/core/ui';
import { GlowText } from '../src/components/glow-text';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useStore } from '../src/core/state/appStore';
import { useCartStore } from '../src/features/cart/store/cartStore';
import { useI18n } from '../src/core/i18n';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { usePulseAnimation } from '../src/core/hooks/usePulseAnimation';
import { StationCard } from '../src/features/stations/components/StationCard';
import { useMemo } from 'react';
import { Fuel } from 'lucide-react-native';

const GLOBAL_PADDING = 24;

export default function HomeScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const { data: stations, isLoading: stationsLoading, error, refetch } = useStations();
  const storeAuth = useStore(state => state.isAuthenticated);
  const { isLoading: authLoading } = useAuth();
  const pulseAnim = usePulseAnimation();
  const { t } = useI18n();
  const { selectStation } = useCartStore();

  const sortedStations = useMemo(() => {
    if (!stations) return [];
    /*
     * Every provider the admin creates is rendered — the old hardcoded
     * ['okko','wog','upg','klo'] allowlist silently dropped anything else.
     * Order comes from the server: stations.sort_order (admin-managed
     * priority), name as the tiebreaker. The client re-sorts defensively in
     * case a cached payload predates the sortOrder field.
     */
    return [...stations].sort((a, b) => {
      const orderA = typeof a.sortOrder === 'number' ? a.sortOrder : 999;
      const orderB = typeof b.sortOrder === 'number' ? b.sortOrder : 999;
      if (orderA !== orderB) return orderA - orderB;
      return a.name.localeCompare(b.name);
    });
  }, [stations]);

  const handleStationPress = (station: any) => {
    selectStation(station);
    router.push(`/station/${station.id}`);
  };

  if (authLoading && !storeAuth) {
    /*
     * The cold-start auth gate. It used to be a bare `View`, which meant the very
     * first thing the app painted had no safe-area insets — the spinner was
     * centred in the physical screen rather than in the content area. Wrapping it
     * in the layout also means the backdrop matches the home screen that follows,
     * so the first frame does not flash a different canvas. No `header`: the
     * LEMBERG banner needs data this branch does not have yet.
     */
    return (
      <PageLayout disableScroll>
        <LoadingState fullScreen />
      </PageLayout>
    );
  }

  const headerComponent = (
    <View style={[styles.header, { paddingHorizontal: GLOBAL_PADDING }]}>
      <View style={styles.brandMain}>
        <View style={styles.topRow}>
          <View style={styles.logoContainer}>
            <Animated.View style={[styles.logoSlot, { opacity: pulseAnim }]}>
              {/*
                The reticle ring. Its dark-theme value was a raw `rgba(0, 255, 102, …)`
                — the `lemberg` green hardcoded, so the ring stayed green on the blue,
                violet and cyan themes. `borderAccent` is the brand-tinted outline role.
              */}
              <View style={[styles.reticleBase, { borderColor: tokens.colors.borderAccent }]} />
              <View style={[styles.corner, styles.topLeft, { borderColor: tokens.colors.primary, shadowColor: tokens.colors.primary }]} />
              <View style={[styles.corner, styles.topRight, { borderColor: tokens.colors.primary, shadowColor: tokens.colors.primary }]} />
              <View style={[styles.corner, styles.bottomLeft, { borderColor: tokens.colors.primary, shadowColor: tokens.colors.primary }]} />
              <View style={[styles.corner, styles.bottomRight, { borderColor: tokens.colors.primary, shadowColor: tokens.colors.primary }]} />
              {/*
                The plate behind the app icon. It is the screen canvas showing
                through the reticle, so it is `background` — not a fourth
                hand-rolled `isDark ? '#000' : '#F0F0F0'` pair that only knew
                about two of the eight themes.
              */}
              <View style={[styles.logoInner, { backgroundColor: tokens.colors.background }]}>
                <Image
                  source={require('../assets/adaptive-icon.png')}
                  style={[styles.logoImg, { shadowColor: tokens.colors.primary }]}
                  resizeMode="contain"
                />
              </View>
            </Animated.View>
          </View>

          <View style={styles.brandTitle}>
            <GlowText intensity="high" align="left" animation="pulse" animatedValue={pulseAnim} style={styles.lembergText}>
              LEMBERG
            </GlowText>
            <Text allowFontScaling={false} style={[styles.subtitleText, { color: tokens.colors.text.primary }]}>
              FUEL CORP.
            </Text>
          </View>
        </View>

        <View style={{ width: '100%', alignItems: 'flex-start', marginBottom: 16, paddingLeft: 0 }}>
          <GlowText intensity="none" align="left" animation="pulse" animatedValue={pulseAnim} color={tokens.colors.text.primary} style={styles.bannerLabel}>
            {t('stations.title')}
          </GlowText>
          <GlowText intensity="high" align="left" animation="pulse" animatedValue={pulseAnim} style={styles.bannerLabel}>
            {t('stations.title2')}
          </GlowText>
        </View>
      </View>
    </View>
  );

  return (
    <PageLayout header={headerComponent}>
      <View style={[styles.container, { paddingHorizontal: GLOBAL_PADDING }]}>
        {error && !stationsLoading && (
          <ErrorState
            onRetry={() => refetch()}
            detail={error instanceof Error ? error.message : undefined}
          />
        )}

        {!stationsLoading && !error && (!sortedStations || sortedStations.length === 0) && (
          <EmptyState
            title={t('stations.empty')}
            description={t('stations.emptyHint')}
            icon={<Fuel />}
          />
        )}

        <View style={styles.stationGrid}>
          {sortedStations.map((station, index) => (
            <StationCard
              key={station.id}
              station={station}
              index={index}
              onPress={handleStationPress}
            />
          ))}
        </View>
      </View>
    </PageLayout>
  );
}

const styles = StyleSheet.create({
  container: { paddingTop: 0, width: '100%' },
  header: { width: '100%', marginBottom: 12, alignItems: 'flex-start' },
  brandMain: { width: '100%', flexDirection: 'column', alignItems: 'flex-start', marginBottom: 10 },
  topRow: { flexDirection: 'row', alignItems: 'flex-start', justifyContent: 'flex-start', width: '100%', marginBottom: 12 },
  logoContainer: { width: 64, height: 64, marginRight: 12 },
  logoSlot: { width: '100%', height: '100%', padding: 6, justifyContent: 'center', alignItems: 'center', position: 'relative' },
  reticleBase: { position: 'absolute', top: 0, left: 0, right: 0, bottom: 0, borderWidth: 1 },
  logoInner: { width: '100%', height: '100%', padding: 4 },
  logoImg: { width: '100%', height: '100%', shadowOffset: { width: 0, height: 0 }, shadowOpacity: 1, shadowRadius: 20 },
  corner: { position: 'absolute', width: 12, height: 12, borderWidth: 3, zIndex: 10, shadowOffset: { width: 0, height: 0 }, shadowOpacity: 0.9, shadowRadius: 8, elevation: 8 },
  topLeft: { top: 0, left: 0, borderRightWidth: 0, borderBottomWidth: 0 },
  topRight: { top: 0, right: 0, borderLeftWidth: 0, borderBottomWidth: 0 },
  bottomLeft: { bottom: 0, left: 0, borderRightWidth: 0, borderTopWidth: 0 },
  bottomRight: { bottom: 0, right: 0, borderLeftWidth: 0, borderTopWidth: 0 },
  brandTitle: { justifyContent: 'flex-start', alignItems: 'flex-start', flex: 1, paddingTop: 8 },
  lembergText: { fontFamily: 'Rajdhani-Bold', fontSize: 26, letterSpacing: 4, lineHeight: 30, marginBottom: -2 },
  subtitleText: { fontFamily: 'Rajdhani', fontSize: 12, letterSpacing: 15, opacity: 0.9, textAlign: 'left', marginBottom: 4 },
  bannerLabel: { fontSize: 28, letterSpacing: 0, textAlign: 'left', lineHeight: 30, marginBottom: -2 },
  stationGrid: { width: '100%', marginBottom: 20 },
});