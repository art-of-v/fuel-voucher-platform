import { ComponentType } from 'react';

/**
 * Platform-agnostic contract for the map shim pair (MapViewShim.native.tsx /
 * MapViewShim.web.tsx). Props mirror the react-native-maps surface that
 * app/map.tsx actually uses (initialRegion, urlTemplate, coordinate,
 * tracksViewChanges, tooltip, ...). The web stubs ignore them (inert Views);
 * the native shim forwards them to the real components, whose prop types are a
 * superset — so a loose record keeps both platforms type-compatible without
 * re-declaring the entire react-native-maps API here.
 */
export type MapViewComponent = ComponentType<Record<string, unknown>>;

export declare const MapView: MapViewComponent;
export declare const UrlTile: MapViewComponent;
export declare const Marker: MapViewComponent;
export declare const Callout: MapViewComponent;
