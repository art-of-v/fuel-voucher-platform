import React from 'react';
import type MapView from 'react-native-maps';

import type { StationNode } from '../../../core/types/api';
import {
  assignToSlots,
  isRegionCoveredByBox,
  regionFromBoundaries,
  type MapRegion,
} from '../lib/viewport';

/** The region the map opens on. The screen replaces it with where the user is, when it knows. */
export const KYIV_REGION: MapRegion = {
  latitude: 50.4501,
  longitude: 30.5234,
  latitudeDelta: 0.15,
  longitudeDelta: 0.15,
};

/**
 * Where a tapped pin has to be on screen, as a fraction of the map's height, for the station
 * card to leave it visible. The card sits at the bottom and covers roughly the lower 40% of the
 * map, so a pin below `REVEAL_MAX_FRACTION` is behind it: the card opens over the pin the person
 * just tapped and there is no way to tell which АЗК it describes - or whether the tap landed at
 * all. Such a pin is moved to `REVEAL_TARGET_FRACTION`, comfortably above the card.
 */
const REVEAL_MAX_FRACTION = 0.55;
const REVEAL_TARGET_FRACTION = 0.35;

/**
 * The pins the map mounts, and what the camera owes a tap.
 *
 * Two rules live here, both about the same fixed set of markers:
 *
 * - the set is chosen for the region the map *reports*, not the one it was opened on, and it is
 *   recomputed only once the screen genuinely leaves that region;
 * - nothing is mounted once the set exists — a slot is re-pointed at another station instead —
 *   because a `Marker` is a legacy view manager under the Fabric interop layer, and adding or
 *   removing one inside a pan is a fatal, native, no-JS-error crash. See `assignToSlots`.
 *
 * The screen keeps the data and the sheets; it hands this hook the ref and the filtered stations.
 */
export function useStationPins(mapRef: React.RefObject<MapView | null>, points: StationNode[]) {
  // Which pins to show, and how often to work that out. The count tracks the *area* on screen —
  // which is what a pan changes — so it is bounded to MAX_MOUNTED_MARKERS, and `markersForRegion`
  // keeps a spread of that area rather than the first `limit` stations the API listed, on a grid
  // anchored to the ground so a pan cannot re-shuffle it. The box is frozen at whatever region it
  // was last computed for and recomputed only once the screen genuinely leaves it, so the settles
  // in the middle of a long pan change nothing at all.
  const [box, setBox] = React.useState<MapRegion>(KYIV_REGION);

  // The other half is that nothing is ever mounted after the pool exists. The screen waits for the
  // map's own first render, then builds `MARKER_POOL_SIZE` markers already carrying the pins of the
  // region on screen, and afterwards only changes which АЗК each one shows.
  //
  // `null` is "the pool does not exist yet". The wait is not ceremony, but it is not what puts a brand
  // badge on a pin either: under Fabric a marker is childless when the map registers it, so which view
  // MapKit displays is settled by props — the `image` on `StationMarker` — not by which commit the pool
  // arrives in. Deferring keeps a hundred and twenty markers, and the subtrees inside them, out of the
  // commit that creates the map; the second half of the gate keeps a pool that could show nothing off
  // the screen altogether.
  const [mapIsReady, setMapIsReady] = React.useState(false);
  const [slots, setSlots] = React.useState<(StationNode | null)[] | null>(null);

  // The box above starts as a guess — the region the map is *opened* on — and the map is under no
  // obligation to stay there: the screen asks it to centre on the user as soon as a position
  // arrives, which commonly happens before the map has drawn. MapKit applies that silently (the
  // native module drops every region change until it has started rendering), so the screen would
  // keep choosing pins for Kyiv while the camera sits in another city: an empty map whose pins
  // appear only once a gesture finally reports a region. The map is asked what it is showing
  // instead, at the moment it is about to draw, and the pool is built for that.
  const handleMapReady = React.useCallback(async () => {
    const map = mapRef.current;
    if (map) {
      try {
        const viewport = regionFromBoundaries(await map.getMapBoundaries());
        if (viewport) setBox(viewport);
      } catch {
        // A map that cannot describe itself keeps the region it was opened on, which is what the
        // box already holds.
      }
    }
    setMapIsReady(true);
  }, [mapRef]);

  React.useEffect(() => {
    // Both halves of the gate, and the second is not redundant: an all-empty pool would be a hundred
    // and twenty markers mounted only to be updated one frame later.
    if (!mapIsReady || points.length === 0) return;
    setSlots((previous) => {
      const next = assignToSlots(points, box, previous ?? []);
      // Every station still in the same slot means every marker gets the props it already has, so
      // bail out instead of re-rendering a hundred and twenty pins for a search keystroke.
      const unchanged =
        previous != null && next.every((point, index) => point?.id === previous[index]?.id);
      return unchanged ? previous : next;
    });
  }, [mapIsReady, points, box]);

  const handleRegionChange = React.useCallback((region: MapRegion) => {
    setBox((previous) => (isRegionCoveredByBox(region, previous) ? previous : region));
  }, []);

  // Lift the map when the tapped pin would end up behind the card, and only then: a pin that is
  // already in the visible part of the map is where the person left it, and moving the ground
  // under their finger for no reason is worse than no move at all. The pan keeps the current
  // zoom - only the centre changes - so a tap does not reset how far the map is zoomed out.
  const reveal = React.useCallback(
    async (node: StationNode) => {
      const map = mapRef.current;
      if (!map || node.lat == null || node.lng == null) return;
      try {
        const [camera, bounds] = await Promise.all([map.getCamera(), map.getMapBoundaries()]);
        const viewport = regionFromBoundaries(bounds);
        if (!viewport) return;
        const span = viewport.latitudeDelta;
        const fraction = (viewport.latitude + span / 2 - node.lat) / span;
        if (fraction <= REVEAL_MAX_FRACTION) return;
        map.animateCamera(
          {
            ...camera,
            center: {
              latitude: node.lat + span * (REVEAL_TARGET_FRACTION - fraction),
              longitude: node.lng,
            },
          },
          { duration: 300 },
        );
      } catch {
        // The card is already opening; a camera this map will not report is not a reason to
        // fail the tap, and the pin stays where it was.
      }
    },
    [mapRef],
  );

  return { slots, handleMapReady, handleRegionChange, reveal };
}
