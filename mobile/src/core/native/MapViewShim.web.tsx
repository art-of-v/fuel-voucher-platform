/**
 * Web-side map shim. `react-native-maps` is native-only — Metro must never see
 * it on the web platform. This stub keeps the map screen importable (and the
 * rest of the app renderable) in a browser; the map surface itself renders as
 * an empty View there. Real maps still render in the iOS/Android app via
 * MapViewShim.native.tsx.
 */
import { View } from 'react-native';

export const MapView = View;
export const UrlTile = View;
export const Marker = View;
export const Callout = View;
