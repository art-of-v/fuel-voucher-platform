import { StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useStations } from '../../src/features/stations/hooks/useStations';
import { PageLayout } from '../../src/components/page-layout';
import { ScreenHeader } from '../../src/core/ui';
import { useDesignTokens } from '../../src/core/hooks/useTheme';
import { FuelCard } from '../../src/features/stations/components/FuelCard';
import { useCartStore } from '../../src/features/cart/store/cartStore';

export default function StationDetailScreen() {
  const { id } = useLocalSearchParams();
  const router = useRouter();
  const tokens = useDesignTokens();
  const { data: stations } = useStations();
  const { selectStation, selectFuel } = useCartStore();

  const station = stations?.find(s => s.id === id);
  if (!station) return null;

  const handleFuelPress = (station: any, fuel: any) => {
    selectStation(station);
    selectFuel(fuel);
    setTimeout(() => router.push('/packages'), 100);
  };

  const sortedFuels = ((station as any).fuels || []).slice().sort((a: any, b: any) => {
    const getPriority = (name: string) => {
      const n = name.toUpperCase();
      if (n.includes('ДП')) return 1;
      if (n.includes('ГАЗ') || n.includes('LPG')) return 3;
      if (n.includes('ADBLUE')) return 4;
      return 2;
    };
    return getPriority(a.name) - getPriority(b.name);
  });

  return (
    <PageLayout
      header={
        <ScreenHeader title={station.logoText || station.name || ''} />
      }
    >
      <View style={{ paddingHorizontal: tokens.spacing.containerPadding }}>
        <View style={styles.content}>
          <View style={styles.fuelGrid}>
            {sortedFuels.map((fuel: any, index: number) => (
              <FuelCard
                key={fuel.id}
                fuel={fuel}
                station={station}
                index={index}
                onPress={handleFuelPress}
              />
            ))}
          </View>
        </View>
      </View>
    </PageLayout>
  );
}

const styles = StyleSheet.create({
  content: { paddingTop: 10 },
  // `FuelCard` already carries its own `marginBottom: 16`, so this `gap` doubles
  // the spacing between fuels. Left as-is: correcting it changes the stations
  // flow's appearance, which is Phase 3.
  fuelGrid: { gap: 16 },
});
