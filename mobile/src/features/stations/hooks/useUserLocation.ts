import React from 'react';
import * as Location from 'expo-location';

export type LocationStatus = 'idle' | 'requesting' | 'granted' | 'denied' | 'error';

export interface UserLocation {
  lat: number;
  lng: number;
}

/**
 * Foreground geolocation for the map price radar. Lazy by design: nothing is requested
 * until `request()` runs (tapping the locate-me control), so the OS permission prompt
 * only appears on an explicit user action. If permission was already granted in a
 * previous session we surface the current position on mount without prompting. All
 * failures degrade to a status the UI can show — the radar still ranks by price without
 * a location, just with no distance or centring.
 */
export function useUserLocation() {
  const [location, setLocation] = React.useState<UserLocation | null>(null);
  const [status, setStatus] = React.useState<LocationStatus>('idle');

  React.useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const { status: perm } = await Location.getForegroundPermissionsAsync();
        if (cancelled || perm !== 'granted') return;
        const pos = await Location.getCurrentPositionAsync({});
        if (cancelled) return;
        setLocation({ lat: pos.coords.latitude, lng: pos.coords.longitude });
        setStatus('granted');
      } catch {
        // No stored grant, or location unavailable — stay idle; request() will prompt.
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const request = React.useCallback(async () => {
    setStatus('requesting');
    try {
      const { status: perm } = await Location.requestForegroundPermissionsAsync();
      if (perm !== 'granted') {
        setStatus('denied');
        return;
      }
      const pos = await Location.getCurrentPositionAsync({});
      setLocation({ lat: pos.coords.latitude, lng: pos.coords.longitude });
      setStatus('granted');
    } catch {
      setStatus('error');
    }
  }, []);

  return { location, status, request };
}
