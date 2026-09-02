import { View, Text, StyleSheet, Image, Pressable, Animated, ActivityIndicator } from 'react-native';
import { useRouter } from 'expo-router';
import { useStations } from '../src/features/stations/hooks/useStations';
import { PageLayout } from '../src/components/page-layout';
import { GlowText } from '../src/components/glow-text';
import { useDesignTokens } from '../src/core/hooks/useTheme';
import { useStore } from '../src/core/state/appStore';
import { useCartStore } from '../src/features/cart/store/cartStore';
import { useI18n } from '../src/core/i18n';
import { useAuth } from '../src/features/auth/hooks/useAuth';
import { usePulseAnimation } from '../src/core/hooks/usePulseAnimation';
import { StationCard } from '../src/features/stations/components/StationCard';
import { useMemo } from 'react';
import { Fuel, AlertTriangle } from 'lucide-react-native';

const GLOBAL_PADDING = 24;

const STATION_PRIORITY = ['okko', 'wog', 'upg', 'klo'];

export default function HomeScreen() {
  const router = useRouter();
  const tokens = useDesignTokens();
  const { data: stations, isLoading: stationsLoading, error, refetch } = useStations();
  const storeAuth = useStore(state => state.isAuthenticated);
  const { isAuthenticated: hookAuth, isLoading: authLoading } = useAuth();
  const pulseAnim = usePulseAnimation();
  const { t } = useI18n();
  const { selectStation } = useCartStore();

  const isAuthenticated = storeAuth || hookAuth;

  const sortedStations = useMemo(() => {
    if (!stations) return [];
    return stations
      .filter(s => STATION_PRIORITY.includes(s.id.toLowerCase()))
      .sort((a, b) => {
        const indexA = STATION_PRIORITY.indexOf(a.id.toLowerCase());
        const indexB = STATION_PRIORITY.indexOf(b.id.toLowerCase());
        return indexA - indexB;
      });
  }, [stations]);

  const handleStationPress = (station: any) => {
    selectStation(station);
    router.push(`/station/${station.id}`);
  };

  if (authLoading && !storeAuth) {
    return (
      <View style={{ flex: 1, backgroundColor: tokens.colors.background, justifyContent: 'center', alignItems: 'center' }}>
        <ActivityIndicator color={tokens.colors.primary} />
      </View>
    );
  }

  const headerComponent = (
    <View style={[styles.header, { paddingHorizontal: GLOBAL_PADDING }]}>
      <View style={styles.brandMain}>
        <View style={styles.topRow}>
          <View style={styles.logoContainer}>
            <Animated.View style={[styles.logoSlot, { opacity: pulseAnim }]}>
              <View style={[styles.reticleBase, { borderColor: tokens.colors.isDark ? 'rgba(0, 255, 102, 0.3)' : 'rgba(0, 0, 0, 0.1)' }]} />
              <View style={[styles.corner, styles.topLeft, { borderColor: tokens.colors.primary, shadowColor: tokens.colors.primary }]} />
              <View style={[styles.corner, styles.topRight, { borderColor: tokens.colors.primary, shadowColor: tokens.colors.primary }]} />
              <View style={[styles.corner, styles.bottomLeft, { borderColor: tokens.colors.primary, shadowColor: tokens.colors.primary }]} />
              <View style={[styles.corner, styles.bottomRight, { borderColor: tokens.colors.primary, shadowColor: tokens.colors.primary }]} />
              <View style={[styles.logoInner, { backgroundColor: tokens.colors.isDark ? '#000' : '#F0F0F0' }]}>
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
          <View style={{ alignItems: 'center', justifyContent: 'center', paddingVertical: 60 }}>
            <AlertTriangle size={48} color="#EF4444" />
            <Text style={{ color: tokens.colors.text.primary, marginTop: 12, fontSize: 16, textAlign: 'center' }}>
              {error instanceof Error ? error.message : 'An error occurred'}
            </Text>
            <Pressable
              onPress={() => refetch()}
              style={{ marginTop: 16, paddingHorizontal: 32, paddingVertical: 12, backgroundColor: tokens.colors.primary, borderRadius: 8 }}
            >
              <Text style={{ color: tokens.colors.isDark ? '#000' : '#FFF', fontFamily: 'Rajdhani-Bold', fontSize: 14, letterSpacing: 1 }}>
                RETRY
              </Text>
            </Pressable>
          </View>
        )}

        {!stationsLoading && !error && (!stations || stations.length === 0) && (
          <View style={{ alignItems: 'center', justifyContent: 'center', paddingVertical: 60 }}>
            <Fuel size={48} color={tokens.colors.text.muted} />
            <Text style={{ color: tokens.colors.text.primary, marginTop: 12, fontSize: 16, fontFamily: 'Rajdhani-Bold' }}>
              {t('stations.empty') || t('stations.title')}
            </Text>
            <Text style={{ color: tokens.colors.text.muted, marginTop: 4, fontSize: 13 }}>
              No stations available in your area
            </Text>
          </View>
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