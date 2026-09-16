/**
 * Native-side map shim. Metro resolves this file only for native platforms
 * (ios/android), so requiring the native-only `react-native-maps` here is safe.
 * The web counterpart (MapViewShim.web.tsx) provides inert View stubs, which is
 * what unblocks `expo export/start --web` — before this pair existed, Metro
 * crashed on web with:
 *   Importing native-only module "react-native/Libraries/Utilities/codegenNativeCommands"
 *   on web from: node_modules\react-native-maps\lib\MapMarkerNativeComponent.js
 */
/* eslint-disable @typescript-eslint/no-var-requires */
const maps = require('react-native-maps');

export const MapView = maps.default;
export const UrlTile = maps.UrlTile;
export const Marker = maps.Marker;
export const Callout = maps.Callout;
