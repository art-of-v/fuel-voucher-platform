import { StyleSheet, View } from 'react-native';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useStations } from '../../src/features/stations/hooks/useStations';
import { PageLayout } from '../../src/components/page-layout';
import { ScreenHeader } from '../../src/core/ui';
import { FuelCard } from '../../src/features/stations/components/FuelCard';
import { useCartStore } from '../../src/features/cart/store/cartStore';
import { BRAND_COLORS } from '../../src/core/design/tokens';

export default function StationDetailScreen() {
  const { id } = useLocalSearchParams();
  const router = useRouter();
  const { data: stations } = useStations();
  const { selectStation, selectFuel } = useCartStore();

  const station = stations?.find(s => s.id === id);
  if (!station) return null;

  const brandColor = BRAND_COLORS[station.id] || '';

  const handleFuelPress = (station: any, fuel: any) => {
    selectStation(station);
    selectFuel(fuel);
    setTimeout(() => router.push('/packages'), 100);
  };

  const sortedFuels = (station.fuels || []).slice().sort((a, b) => {
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
      <View style={{ paddingHorizontal: 24 }}>
        <View style={styles.content}>
          <View style={styles.fuelGrid}>
            {sortedFuels.map((fuel, index) => (
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
  fuelGrid: { gap: 16 },
});
